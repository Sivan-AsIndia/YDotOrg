import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, forkJoin, takeUntil } from 'rxjs';
import { IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  RoleLookupResponse,
  RoleMenuNodeResponse,
  TenantMenuNodeResponse,
} from '../../../../Shared/models/iam-contract.model';
import { NavigationService } from '../../../../Shared/services/navigation.service';
import { ToastService } from '../../../../Shared/services/toast.service';

type Tab = 'organisation' | 'roles';

/**
 * Deciding what appears in the menu, and for whom.
 *
 * THE MENU A PERSON SEES IS THE INTERSECTION OF FOUR THINGS:
 *
 *     platform catalogue      what the product HAS
 *   ∩ organisation config     what this organisation TURNED ON   ← the first tab
 *   ∩ role mapping            what this role was GIVEN           ← the second tab
 *   ∩ held permissions        what this person may actually DO
 *
 * This screen owns the two middle steps. The first is fixed by the product; the last is decided
 * by the role's permissions and is not editable here — a menu item mapped to a role whose holder
 * lacks the permission simply never appears, which is why the mapping tab shows the permission
 * each node needs.
 *
 * TWO RULES THE SERVER ENFORCES, AND THIS SCREEN MAKES VISIBLE
 * ------------------------------------------------------------
 * Disabling a parent disables its children. A reachable child under a hidden parent is a hole in
 * the navigation, not a convenience, so the toggles cascade.
 *
 * A node the organisation has not enabled cannot be mapped to a role. The mapping would be dead
 * weight and would confuse whoever read it next, so those rows are shown as unavailable rather
 * than being silently accepted and then ignored.
 *
 * AND NONE OF IT IS A SECURITY BOUNDARY. Hiding a link is a courtesy; every endpoint behind it
 * re-checks its own permission on every request.
 */
@Component({
  selector: 'app-menu-mapping',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu-mapping.html',
  styleUrl: './menu-mapping.css',
})
export class MenuMappingComponent implements OnInit, OnDestroy {
  private readonly api = inject(IamAdminApiService);
  private readonly navigation = inject(NavigationService);
  private readonly toast = inject(ToastService);

  private readonly destroy$ = new Subject<void>();

  readonly tab = signal<Tab>('organisation');

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly errorMessage = signal('');

  // ---- Organisation configuration -------------------------------------------------------------
  readonly configuredNodes = signal<TenantMenuNodeResponse[]>([]);

  /** Node id → enabled. Held separately so Cancel can restore what the server last confirmed. */
  readonly enabled = signal<Record<string, boolean>>({});
  private savedEnabled: Record<string, boolean> = {};

  // ---- Role mapping ---------------------------------------------------------------------------
  readonly roles = signal<RoleLookupResponse[]>([]);
  readonly selectedRoleId = signal('');
  readonly roleNodes = signal<RoleMenuNodeResponse[]>([]);
  readonly mapped = signal<Record<string, boolean>>({});
  private savedMapped: Record<string, boolean> = {};
  readonly loadingRoleMapping = signal(false);

  /**
   * The role's optimistic-concurrency stamp, fetched with the role and sent back on save.
   *
   * Two administrators editing the same role's menu means the second is told to reload rather
   * than silently overwriting the first — the same rule every other write in this system follows.
   */
  readonly roleVersion = signal(0);

  readonly hasConfigurationChanges = computed(
    () => JSON.stringify(this.enabled()) !== JSON.stringify(this.savedEnabled));

  readonly hasMappingChanges = computed(
    () => JSON.stringify(this.mapped()) !== JSON.stringify(this.savedMapped));

  readonly enabledCount = computed(
    () => Object.values(this.enabled()).filter(Boolean).length);

  readonly mappedCount = computed(
    () => Object.values(this.mapped()).filter(Boolean).length);

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    forkJoin({
      configuration: this.api.getMenuConfiguration(),
      roles: this.api.getRoleLookup(),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ configuration, roles }) => {
          const nodes = configuration.nodes ?? [];

          this.configuredNodes.set(nodes);
          this.savedEnabled = this.flattenEnabled(nodes);
          this.enabled.set({ ...this.savedEnabled });

          this.roles.set(roles);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The menu could not be loaded.'));
        },
      });
  }

  /** Walks the tree once and returns a flat id → enabled map, which is what the toggles bind to. */
  private flattenEnabled(nodes: TenantMenuNodeResponse[]): Record<string, boolean> {
    const map: Record<string, boolean> = {};

    const walk = (list: TenantMenuNodeResponse[]): void => {
      for (const node of list) {
        if (node.menuDefinitionId) {
          map[node.menuDefinitionId] = node.isEnabled === true;
        }

        walk(node.children ?? []);
      }
    };

    walk(nodes);
    return map;
  }

  // =========================================================================================
  // Organisation configuration
  // =========================================================================================

  isEnabled(node: TenantMenuNodeResponse): boolean {
    return node.menuDefinitionId ? this.enabled()[node.menuDefinitionId] === true : false;
  }

  /**
   * Toggles a node, and everything beneath it.
   *
   * The cascade matches the server: enabling a child of a disabled parent would produce a link
   * nobody can reach, and disabling a parent while leaving children on would leave those links
   * reachable by URL with no way to find them.
   */
  toggleNode(node: TenantMenuNodeResponse): void {
    if (!node.menuDefinitionId) {
      return;
    }

    const next = !this.isEnabled(node);
    const updates: Record<string, boolean> = {};

    const cascade = (target: TenantMenuNodeResponse): void => {
      if (target.menuDefinitionId) {
        updates[target.menuDefinitionId] = next;
      }

      for (const child of target.children ?? []) {
        cascade(child);
      }
    };

    cascade(node);

    this.enabled.update((current) => ({ ...current, ...updates }));
  }

  saveConfiguration(): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');

    const items = Object.entries(this.enabled()).map(([menuDefinitionId, isEnabled]) => ({
      menuDefinitionId,
      isEnabled,
    }));

    this.api
      .configureMenu({ items })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.savedEnabled = { ...this.enabled() };

          this.toast.show(
            'Menu updated',
            outcome.message ?? 'The navigation has been saved.',
            'success');

          // Reloaded because the person editing the menu is also using it: their own sidebar
          // should reflect what they just turned on or off, immediately.
          this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({
            error: () => undefined,
          });

          this.load();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(apiErrorMessage(error, 'The menu could not be saved.'));
        },
      });
  }

  cancelConfiguration(): void {
    this.enabled.set({ ...this.savedEnabled });
    this.errorMessage.set('');
  }

  // =========================================================================================
  // Role mapping
  // =========================================================================================

  onRoleSelected(roleId: string): void {
    this.selectedRoleId.set(roleId);
    this.roleNodes.set([]);
    this.mapped.set({});
    this.savedMapped = {};
    this.errorMessage.set('');

    if (!roleId) {
      return;
    }

    this.loadingRoleMapping.set(true);

    // Both together: the mapping is what the screen renders, and the role carries the version
    // the save has to quote. Fetching them separately would leave a window where one arrived
    // and the other did not.
    forkJoin({
      mapping: this.api.getRoleMenuMapping(roleId),
      role: this.api.getRole(roleId),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ mapping, role }) => {
          const nodes = mapping.nodes ?? [];

          this.roleNodes.set(nodes);
          this.savedMapped = this.flattenMapped(nodes);
          this.mapped.set({ ...this.savedMapped });
          this.roleVersion.set(role.version ?? 0);
          this.loadingRoleMapping.set(false);
        },
        error: (error: unknown) => {
          this.loadingRoleMapping.set(false);
          this.errorMessage.set(apiErrorMessage(error, 'That mapping could not be loaded.'));
        },
      });
  }

  private flattenMapped(nodes: RoleMenuNodeResponse[]): Record<string, boolean> {
    const map: Record<string, boolean> = {};

    const walk = (list: RoleMenuNodeResponse[]): void => {
      for (const node of list) {
        if (node.menuDefinitionId) {
          map[node.menuDefinitionId] = node.isVisible === true;
        }

        walk(node.children ?? []);
      }
    };

    walk(nodes);
    return map;
  }

  isMapped(node: RoleMenuNodeResponse): boolean {
    return node.menuDefinitionId ? this.mapped()[node.menuDefinitionId] === true : false;
  }

  /**
   * Whether this node can usefully be mapped to this role.
   *
   * `isPermitted` is the SERVER'S judgement about the ROLE: does it hold the permission the node
   * requires? A node whose permission the role lacks is shown disabled rather than accepted and
   * then quietly ignored - mapping it would achieve nothing, because the endpoint behind the
   * screen would still answer 403 and the link would never appear.
   *
   * It says nothing about what the organisation has switched on. That is a separate setting on a
   * separate screen, and the badge used to confuse the two.
   */
  isAvailable(node: RoleMenuNodeResponse): boolean {
    return node.isPermitted !== false;
  }

  toggleMapping(node: RoleMenuNodeResponse): void {
    if (!node.menuDefinitionId || !this.isAvailable(node)) {
      return;
    }

    const next = !this.isMapped(node);
    const updates: Record<string, boolean> = { [node.menuDefinitionId]: next };

    // Mapping a parent maps its children; unmapping it unmaps them. Same reasoning as the
    // configuration cascade above.
    const cascade = (target: RoleMenuNodeResponse): void => {
      for (const child of target.children ?? []) {
        if (child.menuDefinitionId && this.isAvailable(child)) {
          updates[child.menuDefinitionId] = next;
        }

        cascade(child);
      }
    };

    cascade(node);

    this.mapped.update((current) => ({ ...current, ...updates }));
  }

  saveMapping(): void {
    const roleId = this.selectedRoleId();

    if (!roleId || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');

    const visibleMenuIds = Object.entries(this.mapped())
      .filter(([, isVisible]) => isVisible)
      .map(([menuDefinitionId]) => menuDefinitionId);

    this.api
      .mapRoleMenus(roleId, { visibleMenuIds, expectedVersion: this.roleVersion() })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.savedMapped = { ...this.mapped() };

          this.toast.show(
            'Mapping saved',
            outcome.message ?? 'The role now sees this navigation.',
            'success');

          this.onRoleSelected(roleId);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(apiErrorMessage(error, 'The mapping could not be saved.'));
        },
      });
  }

  cancelMapping(): void {
    this.mapped.set({ ...this.savedMapped });
    this.errorMessage.set('');
  }

  // =========================================================================================
  // Display helpers
  // =========================================================================================

  /** Indents a row by its depth, so three levels read as three levels. */
  indent(level: string | undefined): string {
    switch (level) {
      case 'subMenu': return '1.75rem';
      case 'childSubMenu': return '3.5rem';
      default: return '0';
    }
  }

  /** The name to show for a configuration node: the organisation's override, or the catalogue's. */
  configuredName(node: TenantMenuNodeResponse): string {
    return node.resolvedName || node.catalogueName || node.code || '';
  }

  levelLabel(level: string | undefined): string {
    switch (level) {
      case 'menu': return 'Menu';
      case 'subMenu': return 'Submenu';
      case 'childSubMenu': return 'Child submenu';
      default: return '';
    }
  }

  iconClass(icon: string | null | undefined): string {
    return this.navigation.iconClass(icon);
  }
}
