import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorCode, apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  CreateMenuDefinitionRequest,
  MenuDefinitionResponse,
  MenuLevel,
  MenuStatus,
} from '../../../../Shared/models/iam-contract.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { NavigationService } from '../../../../Shared/services/navigation.service';
import { ToastService } from '../../../../Shared/services/toast.service';

/** The editor's working copy of one node. Strings throughout, because that is what inputs give. */
interface CatalogueForm {
  code: string;
  name: string;
  description: string;
  level: MenuLevel;
  moduleCode: string;
  parentMenuId: string;
  route: string;
  icon: string;
  requiredPermissionCode: string;
  displayOrder: string;
  badgeKey: string;
  status: MenuStatus;
  isPlatformOnly: boolean;
  isEnabledByDefault: boolean;
  isMandatory: boolean;
  opensInNewTab: boolean;
}

/**
 * The platform navigation catalogue: every screen the product has, across every Organisation.
 *
 * WHAT THIS IS, AND WHAT IT IS NOT
 * ---------------------------------
 * This is the PRODUCT'S menu — the full set of screens that exist, the routes they live at, and
 * the permission each one requires. It is the top of the four-way intersection that decides what
 * anybody sees:
 *
 *     this catalogue        ← here
 *   ∩ organisation config   ← Administration → Menu and navigation
 *   ∩ role mapping          ← the same screen, second tab
 *   ∩ held permissions
 *
 * Turning something on for ONE Organisation is not done here. A change here reaches every
 * customer at once, which is why the whole screen is behind `platform.menu-catalogue.manage`
 * and why the warning at the top of it is not decoration.
 *
 * WHY IT WAS READ-ONLY, AND WHAT CHANGED
 * --------------------------------------
 * It used to be a table you could only look at, on the argument that a catalogue entry names a
 * route that has to exist in the Angular bundle and a permission the API has to enforce — so a
 * form could only produce a menu item leading to a blank page, or one guarded by a permission
 * nothing checks.
 *
 * Both halves of that are real. Neither is a reason to have no editor; they are a specification
 * for what the editor has to check:
 *
 *   THE ROUTE is validated against the routes this build actually registers. Type one that does
 *   not exist and the form says so before you save, naming it. A node with NO route is a
 *   heading, needs no code at all, and is accepted without complaint.
 *
 *   THE PERMISSION is validated by the server against the permission catalogue — an unknown code
 *   is refused — and offered here as a picker so it rarely has to be typed.
 *
 * The result is that authoring is possible and a mistake is caught at the point it is made,
 * rather than being impossible or, worse, silently shipped.
 *
 * DELETING IS DELIBERATELY NARROW. A node any Organisation has configured, or any role has been
 * mapped against, has rows pointing at it; removing it would orphan them. The server refuses,
 * and the answer is Retire — which hides it everywhere, keeps the history, and can be undone.
 */
@Component({
  selector: 'app-menu-catalogue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu-catalogue.html',
  styleUrl: './menu-catalogue.css',
})
export class MenuCatalogueComponent implements OnInit, OnDestroy {
  private readonly api = inject(IamAdminApiService);
  private readonly navigation = inject(NavigationService);
  private readonly tokens = inject(AuthTokenService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroy$ = new Subject<void>();

  readonly nodes = signal<MenuDefinitionResponse[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');
  readonly search = signal('');
  readonly includeRetired = signal(false);

  readonly saving = signal(false);
  readonly formError = signal('');

  /** Null when closed; a node id when editing; the empty string when creating. */
  readonly editingId = signal<string | null>(null);
  readonly form = signal<CatalogueForm>(this.blankForm());
  private editingVersion = 0;

  /** The node awaiting a delete confirmation. */
  readonly confirmingDelete = signal<MenuDefinitionResponse | null>(null);

  readonly permissions = signal<{ code: string; name: string; moduleCode: string }[]>([]);

  readonly canManage = computed(
    () => this.tokens.hasPermission('platform.menu-catalogue.manage'));

  // =========================================================================================
  // The tree, flattened
  // =========================================================================================

  readonly rows = computed(() => {
    const flat: (MenuDefinitionResponse & { depth: number })[] = [];

    const walk = (list: MenuDefinitionResponse[], depth: number): void => {
      for (const node of list) {
        flat.push({ ...node, depth });
        walk(node.children ?? [], depth + 1);
      }
    };

    walk(this.nodes(), 0);
    return flat;
  });

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (!term) {
      return this.rows();
    }

    // Matching the route and the permission as well as the name, because somebody arriving here
    // usually holds one of those rather than the label.
    return this.rows().filter((node) =>
      (node.name ?? '').toLowerCase().includes(term)
      || (node.code ?? '').toLowerCase().includes(term)
      || (node.route ?? '').toLowerCase().includes(term)
      || (node.requiredPermissionCode ?? '').toLowerCase().includes(term)
      || (node.moduleCode ?? '').toLowerCase().includes(term));
  });

  readonly totalNodes = computed(() => this.rows().length);

  readonly moduleCount = computed(
    () => new Set(this.rows().map((node) => node.moduleCode).filter(Boolean)).size);

  /** Module codes already in use, so the form suggests rather than invents. */
  readonly moduleCodes = computed(() => [...new Set(
    this.rows().map((node) => (node.moduleCode ?? '').trim()).filter(Boolean))].sort());

  /**
   * Nodes that may be a parent: anything not already at the deepest level.
   *
   * The tree is three deep by design and the server refuses a fourth, so a child submenu is
   * never offered as a parent rather than being offered and then rejected.
   */
  readonly parentOptions = computed(() => this.rows()
    .filter((node) => node.level !== 'childSubMenu')
    .map((node) => ({
      id: node.id ?? '',
      label: `${'— '.repeat(node.depth)}${node.name ?? node.code ?? ''}`,
      level: node.level,
    })));

  // =========================================================================================
  // Route validation
  // =========================================================================================

  /**
   * Every path this build registers, taken from the router itself.
   *
   * THIS IS THE CHECK THAT MAKES AUTHORING SAFE. A catalogue route is a promise that a screen
   * exists; asking the router what it actually has turns "you will find out when somebody
   * clicks it" into "this route does not exist in this build" while the form is still open.
   */
  private readonly knownRoutes = computed(() => {
    const paths = new Set<string>();

    const walk = (config: readonly { path?: string; children?: unknown[] }[], prefix: string): void => {
      for (const entry of config) {
        const segment = entry.path ?? '';
        const full = [prefix, segment].filter(Boolean).join('/');

        if (segment !== '**') {
          paths.add('/' + full);
        }

        if (Array.isArray(entry.children)) {
          walk(entry.children as { path?: string }[], full);
        }
      }
    };

    walk(this.router.config as { path?: string; children?: unknown[] }[], '');
    return paths;
  });

  /**
   * Whether a route names a screen this build has.
   *
   * Parameterised segments are matched positionally, so `/app/users/:id` in the router accepts
   * `/app/users/42` in the catalogue. An empty route is a heading and is always fine.
   */
  readonly routeWarning = computed(() => {
    const route = this.form().route.trim();

    if (!route) {
      return '';
    }

    if (!route.startsWith('/')) {
      return 'A route has to begin with "/" — for example /app/administration/access/user-directory.';
    }

    const wanted = route.split('?')[0].split('#')[0].replace(/\/+$/, '').split('/').filter(Boolean);

    for (const known of this.knownRoutes()) {
      const parts = known.split('/').filter(Boolean);

      if (parts.length !== wanted.length) {
        continue;
      }

      if (parts.every((part, index) => part.startsWith(':') || part === wanted[index])) {
        return '';
      }
    }

    return `No screen is registered at ${route} in this build. Save it and the menu item will `
      + 'lead to a "page not found". Leave the route empty if this is meant to be a heading.';
  });

  readonly isHeading = computed(() => !this.form().route.trim());

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    this.load();

    // For the permission picker. A failure costs the picker and nothing else — the code can
    // still be typed, and the server validates it either way.
    this.api
      .getMenuPermissionCodes()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (options) => this.permissions.set(options),
        error: () => this.permissions.set([]),
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    this.api
      .getMenuDefinitions(this.includeRetired())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (nodes) => {
          this.nodes.set(nodes);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The catalogue could not be loaded.'));
        },
      });
  }

  toggleRetired(): void {
    this.includeRetired.update((value) => !value);
    this.load();
  }

  // =========================================================================================
  // The editor
  // =========================================================================================

  private blankForm(): CatalogueForm {
    return {
      code: '', name: '', description: '', level: 'menu', moduleCode: '',
      parentMenuId: '', route: '', icon: '', requiredPermissionCode: '',
      displayOrder: '0', badgeKey: '', status: 'active',
      isPlatformOnly: false, isEnabledByDefault: true, isMandatory: false, opensInNewTab: false,
    };
  }

  startCreate(parent?: MenuDefinitionResponse): void {
    const form = this.blankForm();

    // Adding "beneath" a node pre-selects it and the level that follows from it, because those
    // two have to agree and the server refuses the pair when they do not.
    if (parent?.id) {
      form.parentMenuId = parent.id;
      form.level = parent.level === 'menu' ? 'subMenu' : 'childSubMenu';
      form.moduleCode = parent.moduleCode ?? '';
      form.isPlatformOnly = parent.isPlatformOnly === true;
    }

    this.editingVersion = 0;
    this.form.set(form);
    this.formError.set('');
    this.editingId.set('');
  }

  startEdit(node: MenuDefinitionResponse): void {
    this.editingVersion = node.version ?? 0;

    this.form.set({
      code: node.code ?? '',
      name: node.name ?? '',
      description: node.description ?? '',
      level: node.level ?? 'menu',
      moduleCode: node.moduleCode ?? '',
      parentMenuId: node.parentMenuId ?? '',
      route: node.route ?? '',
      icon: node.icon ?? '',
      requiredPermissionCode: node.requiredPermissionCode ?? '',
      displayOrder: String(node.displayOrder ?? 0),
      badgeKey: node.badgeKey ?? '',
      status: node.status ?? 'active',
      isPlatformOnly: node.isPlatformOnly === true,
      isEnabledByDefault: node.isEnabledByDefault !== false,
      isMandatory: node.isMandatory === true,
      opensInNewTab: node.opensInNewTab === true,
    });

    this.formError.set('');
    this.editingId.set(node.id ?? '');
  }

  closeEditor(): void {
    this.editingId.set(null);
    this.formError.set('');
  }

  readonly isCreating = computed(() => this.editingId() === '');

  patch<K extends keyof CatalogueForm>(key: K, value: CatalogueForm[K]): void {
    this.form.update((current) => ({ ...current, [key]: value }));
  }

  /**
   * What the form still needs before it can be sent.
   *
   * Only the things the SERVER will refuse. The route warning above is advice, not a block: a
   * route may legitimately be added a moment before the screen behind it ships, and refusing
   * would make the catalogue impossible to prepare.
   */
  readonly validationError = computed(() => {
    const form = this.form();

    if (!form.name.trim()) {
      return 'Give it a name.';
    }

    if (this.isCreating()) {
      if (!/^[A-Z0-9_-]+$/.test(form.code.trim())) {
        return 'The code must be upper-case letters, digits, underscores or hyphens.';
      }

      if (!form.moduleCode.trim()) {
        return 'Choose a module.';
      }

      if (form.level !== 'menu' && !form.parentMenuId) {
        return 'A submenu or child submenu needs a parent.';
      }

      if (form.level === 'menu' && form.parentMenuId) {
        return 'A top-level menu cannot have a parent.';
      }
    }

    return '';
  });

  save(): void {
    if (this.saving() || this.validationError()) {
      return;
    }

    const form = this.form();
    const id = this.editingId();

    this.saving.set(true);
    this.formError.set('');

    const order = Number.parseInt(form.displayOrder.trim(), 10);
    const displayOrder = Number.isFinite(order) ? order : 0;

    if (this.isCreating()) {
      const request: CreateMenuDefinitionRequest = {
        code: form.code.trim().toUpperCase(),
        name: form.name.trim(),
        level: form.level,
        moduleCode: form.moduleCode.trim().toUpperCase(),
        parentMenuId: form.parentMenuId || null,
        route: form.route.trim() || null,
        icon: form.icon.trim() || null,
        requiredPermissionCode: form.requiredPermissionCode.trim() || null,
        description: form.description.trim() || null,
        displayOrder,
        isPlatformOnly: form.isPlatformOnly,
        isEnabledByDefault: form.isEnabledByDefault,
        isMandatory: form.isMandatory,
        opensInNewTab: form.opensInNewTab,
        badgeKey: form.badgeKey.trim() || null,
      };

      this.api.createMenuDefinition(request).pipe(takeUntil(this.destroy$)).subscribe({
        next: (created) => this.afterWrite(`${created.name} was added to the catalogue.`),
        error: (error: unknown) => this.afterFailure(error, 'The menu node could not be added.'),
      });

      return;
    }

    // UPDATE SENDS EVERY EDITABLE FIELD, including the ones left blank. The request treats null
    // as "leave alone", so omitting a cleared field would silently keep the old value and the
    // form would appear to have lost the edit. An empty string is what clears one.
    this.api.updateMenuDefinition(id!, {
      expectedVersion: this.editingVersion,
      name: form.name.trim(),
      description: form.description.trim(),
      route: form.route.trim(),
      icon: form.icon.trim(),
      requiredPermissionCode: form.requiredPermissionCode.trim(),
      displayOrder,
      status: form.status,
      isEnabledByDefault: form.isEnabledByDefault,
      opensInNewTab: form.opensInNewTab,
      badgeKey: form.badgeKey.trim(),
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => this.afterWrite(`${form.name.trim()} was saved.`),
      error: (error: unknown) => this.afterFailure(error, 'The menu node could not be saved.'),
    });
  }

  private afterWrite(message: string): void {
    this.saving.set(false);
    this.editingId.set(null);
    this.toast.show('Catalogue updated', message, 'success');

    // The catalogue decides the sidebar, and whoever just edited it is looking at that sidebar.
    this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({ error: () => undefined });
    this.load();
  }

  private afterFailure(error: unknown, fallback: string): void {
    this.saving.set(false);

    const message = apiErrorMessage(error, fallback);

    this.formError.set(apiErrorCode(error) === 'CONCURRENCY_CONFLICT'
      ? `${message} Close this and open it again to pick up the newer version.`
      : message);
  }

  // =========================================================================================
  // Retire and delete
  // =========================================================================================

  /**
   * Hides a node everywhere without losing it.
   *
   * This is the answer for a node people are using. The catalogue and mapping queries both drop
   * a retired node, so it disappears from every Organisation — and it can be brought back,
   * which a delete cannot.
   */
  retire(node: MenuDefinitionResponse): void {
    if (this.saving() || !node.id) {
      return;
    }

    this.saving.set(true);

    this.api.updateMenuDefinition(node.id, {
      expectedVersion: node.version ?? 0,
      status: 'retired',
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => this.afterWrite(`${node.name} was retired.`),
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show('Could not retire', apiErrorMessage(error), 'error');
      },
    });
  }

  restore(node: MenuDefinitionResponse): void {
    if (this.saving() || !node.id) {
      return;
    }

    this.saving.set(true);

    this.api.updateMenuDefinition(node.id, {
      expectedVersion: node.version ?? 0,
      status: 'active',
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => this.afterWrite(`${node.name} is active again.`),
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show('Could not restore', apiErrorMessage(error), 'error');
      },
    });
  }

  askDelete(node: MenuDefinitionResponse): void {
    this.confirmingDelete.set(node);
  }

  cancelDelete(): void {
    this.confirmingDelete.set(null);
  }

  /**
   * Removes a node outright.
   *
   * The server refuses if it has children, is mandatory, or anything anywhere references it —
   * and says which. That refusal is shown as it arrives rather than being second-guessed here,
   * because the client cannot see the other Organisations that make it true.
   */
  confirmDelete(): void {
    const node = this.confirmingDelete();

    if (!node?.id || this.saving()) {
      return;
    }

    this.saving.set(true);

    this.api.deleteMenuDefinition(node.id, node.version ?? 0)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.confirmingDelete.set(null);
          this.afterWrite(`${node.name} was removed from the catalogue.`);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.confirmingDelete.set(null);
          this.toast.show('Could not delete', apiErrorMessage(error), 'error');
        },
      });
  }

  // =========================================================================================
  // Display helpers
  // =========================================================================================

  iconClass(icon: string | null | undefined): string {
    return this.navigation.iconClass(icon);
  }

  indent(depth: number): string {
    return `${depth * 1.5}rem`;
  }

  levelLabel(level: string | undefined): string {
    switch (level) {
      case 'menu': return 'Menu';
      case 'subMenu': return 'Submenu';
      case 'childSubMenu': return 'Child submenu';
      default: return '';
    }
  }

  levelClass(level: string | undefined): string {
    switch (level) {
      case 'menu': return 'bg-primary-subtle text-primary';
      case 'subMenu': return 'bg-info-subtle text-info';
      default: return 'bg-secondary-subtle text-secondary';
    }
  }

  statusClass(status: string | undefined): string {
    switch (status) {
      case 'active': return 'bg-success-subtle text-success';
      case 'draft': return 'bg-warning-subtle text-warning-emphasis';
      case 'retired': return 'bg-danger-subtle text-danger';
      default: return 'bg-secondary-subtle text-secondary';
    }
  }

  /** Whether a node may be given children, which is what "Add beneath" needs to know. */
  canHaveChildren(node: MenuDefinitionResponse): boolean {
    return node.level !== 'childSubMenu';
  }
}
