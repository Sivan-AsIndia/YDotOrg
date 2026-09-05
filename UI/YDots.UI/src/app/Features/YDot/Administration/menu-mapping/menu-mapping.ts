import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, forkJoin, takeUntil } from 'rxjs';
import { IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorCode, apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  RoleLookupResponse,
  RoleMenuNodeResponse,
  TenantMenuItemRequest,
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
 * THREE RULES THE SERVER ENFORCES, AND THIS SCREEN MAKES VISIBLE
 * -------------------------------------------------------------
 * Disabling a parent disables its children. A reachable child under a hidden parent is a hole in
 * the navigation, not a convenience, so the toggles cascade downwards.
 *
 * ENABLING A CHILD ENABLES ITS PARENTS, and this is the half that was missing. The tree is only
 * ever walked from the top, so a node whose parent is off is not rendered however it is set — it
 * is not indented under a greyed-out heading, it is absent. Ticking one child on and saving
 * therefore reported success and changed nothing anybody could see, which reads exactly like a
 * save that was thrown away. Turning a node on now turns on the section it lives in.
 *
 * A node the organisation has not enabled can still be mapped to a role, but it shows nobody
 * anything until the organisation switches it on — so the row says so, rather than leaving
 * somebody to wonder why a saved mapping had no effect.
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

  /** Set when the server refused a save because somebody else got there first. */
  readonly staleData = signal(false);

  // ---- Organisation configuration -------------------------------------------------------------
  readonly configuredNodes = signal<TenantMenuNodeResponse[]>([]);

  /** Node id → enabled. Held separately so Cancel can restore what the server last confirmed. */
  readonly enabled = signal<Record<string, boolean>>({});

  /**
   * What the server last confirmed.
   *
   * A SIGNAL, NOT A PLAIN FIELD, because `hasConfigurationChanges` reads it. A computed only
   * recomputes when a signal it read has changed, so with a plain field the Save and Cancel
   * buttons went on being offered after a successful save until something else happened to
   * touch `enabled` — the screen said there was unsaved work when there was none.
   */
  private readonly savedEnabled = signal<Record<string, boolean>>({});

  /**
   * Node id → this organisation's label, icon and order overrides.
   *
   * A SIGNAL, because these are now EDITABLE. The three columns have existed on TenantMenu since
   * the beginning and the save has always carried them, but no screen ever offered a way to set
   * one - so an organisation could not rename "Donors" to "Supporters", swap an icon or move a
   * section, despite the entity, the request DTO, the command and the query all supporting it
   * end to end. The capability was built and then left unreachable.
   */
  private readonly configurationOverrides = signal<Record<string, TenantMenuItemRequest>>({});

  /** What the server last confirmed for those overrides, so Discard can put them back. */
  private readonly savedOverrides = signal<Record<string, TenantMenuItemRequest>>({});

  /** The node whose label, icon and order are open for editing, if any. */
  readonly editingNodeId = signal<string | null>(null);

  /** The unsaved contents of that editor. Applied to the overrides only when Apply is pressed. */
  readonly editorDraft = signal<{ name: string; icon: string; order: string }>(
    { name: '', icon: '', order: '' });

  /** Node id → its parent's id, so a toggle can walk up the tree as well as down it. */
  private configurationParents: Record<string, string> = {};

  // ---- Role mapping ---------------------------------------------------------------------------
  readonly roles = signal<RoleLookupResponse[]>([]);
  readonly selectedRoleId = signal('');
  readonly roleNodes = signal<RoleMenuNodeResponse[]>([]);
  readonly mapped = signal<Record<string, boolean>>({});
  private readonly savedMapped = signal<Record<string, boolean>>({});
  readonly loadingRoleMapping = signal(false);

  private mappingParents: Record<string, string> = {};
  private mappingNodes: Record<string, RoleMenuNodeResponse> = {};

  /**
   * Which node this role lands on after sign-in.
   *
   * CARRIED THROUGH THE SAVE, and it was not. The server writes the landing flag from what the
   * request names, so a request that named nothing cleared it: every save of this screen quietly
   * dropped the role's landing page and everybody holding it started landing on whatever node
   * happened to come first. It is not editable here yet, which is precisely why it has to be
   * sent back untouched rather than omitted.
   */
  readonly landingMenuId = signal<string | null>(null);

  /**
   * The role's optimistic-concurrency stamp, fetched with the role and sent back on save.
   *
   * Two administrators editing the same role's menu means the second is told to reload rather
   * than silently overwriting the first — the same rule every other write in this system follows.
   */
  readonly roleVersion = signal(0);

  readonly hasConfigurationChanges = computed(
    () => JSON.stringify(this.enabled()) !== JSON.stringify(this.savedEnabled())
          || JSON.stringify(this.configurationOverrides()) !== JSON.stringify(this.savedOverrides()));

  readonly hasMappingChanges = computed(
    () => JSON.stringify(this.mapped()) !== JSON.stringify(this.savedMapped()));

  readonly enabledCount = computed(
    () => Object.values(this.enabled()).filter(Boolean).length);

  readonly mappedCount = computed(
    () => Object.values(this.mapped()).filter(Boolean).length);

  /**
   * How many switches are pending, so the save bar can say so.
   *
   * "Unsaved changes" alone is not enough on a list this long: somebody who has been up and
   * down sixty rows wants to know whether they are about to save the one thing they meant to
   * change or six things they do not remember touching.
   */
  readonly configurationChangeCount = computed(() => {
    const switches = this.countDifferences(this.enabled(), this.savedEnabled());

    // A renamed node counts once however many of its three fields moved: the person made one
    // edit and expects the bar to say one.
    const current = this.configurationOverrides();
    const saved = this.savedOverrides();

    const edited = Object.keys(current).filter((id) =>
      JSON.stringify(this.presentationOf(current[id]))
      !== JSON.stringify(this.presentationOf(saved[id]))).length;

    return switches + edited;
  });

  /** Just the three editable fields, for comparing one row's overrides against another. */
  private presentationOf(item: TenantMenuItemRequest | undefined): [string, string, number | null] {
    return [
      item?.displayNameOverride ?? '',
      item?.iconOverride ?? '',
      item?.displayOrderOverride ?? null,
    ];
  }

  readonly mappingChangeCount = computed(
    () => this.countDifferences(this.mapped(), this.savedMapped()));

  /** The name of the role being edited, for the save bar to name what is about to change. */
  readonly selectedRoleName = computed(() => {
    const id = this.selectedRoleId();

    return this.roles().find((role) => role.id === id)?.name ?? 'this role';
  });

  private countDifferences(
    current: Record<string, boolean>, saved: Record<string, boolean>): number {
    return Object.keys(current).filter((key) => current[key] !== saved[key]).length;
  }

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
          this.readConfigurationTree(nodes);

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

  /**
   * Walks the configuration tree once and keeps the three things the screen needs from it: the
   * flat id → enabled map the toggles bind to, each node's parent, and the organisation's own
   * label/icon/order overrides.
   *
   * THE OVERRIDES ARE KEPT BECAUSE THE SAVE HAS TO SEND THEM BACK. The request replaces a node's
   * whole row, so a save that carried only the on/off flag wiped every rename, custom icon and
   * custom ordering the organisation had — silently, on a screen that never showed them.
   */
  private readConfigurationTree(nodes: TenantMenuNodeResponse[]): void {
    const enabled: Record<string, boolean> = {};
    const overrides: Record<string, TenantMenuItemRequest> = {};
    const parents: Record<string, string> = {};

    const walk = (list: TenantMenuNodeResponse[], parentId: string | null): void => {
      for (const node of list) {
        const id = node.menuDefinitionId;

        if (id) {
          enabled[id] = node.isEnabled === true;

          overrides[id] = {
            menuDefinitionId: id,
            isEnabled: node.isEnabled === true,
            displayNameOverride: node.displayNameOverride ?? null,
            iconOverride: node.iconOverride ?? null,
            displayOrderOverride: node.displayOrderOverride ?? null,
          };

          if (parentId) {
            parents[id] = parentId;
          }
        }

        walk(node.children ?? [], id ?? parentId);
      }
    };

    walk(nodes, null);

    this.configurationParents = parents;
    this.savedEnabled.set(enabled);
    this.enabled.set({ ...enabled });
    this.savedOverrides.set(overrides);
    this.configurationOverrides.set(structuredClone(overrides));
    this.editingNodeId.set(null);
  }

  // =========================================================================================
  // Organisation configuration
  // =========================================================================================

  isEnabled(node: TenantMenuNodeResponse): boolean {
    return node.menuDefinitionId ? this.enabled()[node.menuDefinitionId] === true : false;
  }

  /**
   * Toggles a node, everything beneath it, and — when switching on — everything above it.
   *
   * DOWNWARDS, because enabling a child of a disabled parent would produce a link nobody can
   * reach, and disabling a parent while leaving children on would leave those links reachable by
   * URL with no way to find them.
   *
   * UPWARDS, because the navigation is built by walking down from the top level. A node whose
   * parent is off is not drawn greyed-out; it is not drawn at all. Turning one on and leaving its
   * section off was a save that succeeded and showed nothing, which is the worst kind of failure
   * — the screen agreed with the administrator and the product did not.
   */
  toggleNode(node: TenantMenuNodeResponse): void {
    if (!node.menuDefinitionId) {
      return;
    }

    const next = !this.isEnabled(node);
    const updates: Record<string, boolean> = {};

    const cascadeDown = (target: TenantMenuNodeResponse): void => {
      if (target.menuDefinitionId) {
        updates[target.menuDefinitionId] = next;
      }

      for (const child of target.children ?? []) {
        cascadeDown(child);
      }
    };

    cascadeDown(node);

    if (next) {
      for (
        let parentId: string | undefined = this.configurationParents[node.menuDefinitionId];
        parentId;
        parentId = this.configurationParents[parentId]
      ) {
        updates[parentId] = true;
      }
    }

    this.enabled.update((current) => ({ ...current, ...updates }));
  }

  saveConfiguration(): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.staleData.set(false);

    // Every field of the row, not just the switch: see readConfigurationTree.
    const overrides = this.configurationOverrides();

    const items: TenantMenuItemRequest[] = Object.entries(this.enabled()).map(
      ([menuDefinitionId, isEnabled]) => ({
        ...(overrides[menuDefinitionId] ?? { menuDefinitionId }),
        menuDefinitionId,
        isEnabled,
      }));

    this.api
      .configureMenu({ items })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.savedEnabled.set({ ...this.enabled() });
          this.savedOverrides.set(structuredClone(this.configurationOverrides()));

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
          this.reportSaveFailure(error, 'The menu could not be saved.');
        },
      });
  }

  cancelConfiguration(): void {
    this.enabled.set({ ...this.savedEnabled() });
    this.configurationOverrides.set(structuredClone(this.savedOverrides()));
    this.editingNodeId.set(null);
    this.errorMessage.set('');
    this.staleData.set(false);
  }

  // -------------------------------------------------------------------------------------
  // Renaming, re-iconing and re-ordering, for this organisation only
  // -------------------------------------------------------------------------------------

  /**
   * Opens the editor for one node.
   *
   * The boxes start EMPTY when nothing has been overridden, with the catalogue value shown as a
   * placeholder. Pre-filling them with the catalogue value would turn "inherit whatever the
   * product calls this" into a copy taken on the day somebody happened to open the editor - and
   * a later improvement to the product's own label would then never reach this organisation.
   */
  openEditor(node: TenantMenuNodeResponse): void {
    const id = node.menuDefinitionId;

    if (!id) {
      return;
    }

    const override = this.configurationOverrides()[id];

    this.editorDraft.set({
      name: override?.displayNameOverride ?? '',
      icon: override?.iconOverride ?? '',
      order: override?.displayOrderOverride == null ? '' : String(override.displayOrderOverride),
    });

    this.editingNodeId.set(id);
  }

  closeEditor(): void {
    this.editingNodeId.set(null);
  }

  /** Whether this row's editor is the one currently open. */
  isEditing(node: TenantMenuNodeResponse): boolean {
    return !!node.menuDefinitionId && this.editingNodeId() === node.menuDefinitionId;
  }

  /** True when this organisation has overridden anything about the node. */
  hasOverride(node: TenantMenuNodeResponse): boolean {
    const override = node.menuDefinitionId
      ? this.configurationOverrides()[node.menuDefinitionId]
      : undefined;

    return !!override
      && (!!override.displayNameOverride
          || !!override.iconOverride
          || override.displayOrderOverride != null);
  }

  /**
   * Takes what is in the editor into the pending changes.
   *
   * BLANK MEANS INHERIT, not "named empty string". A person clearing the box is asking for the
   * product's own label back, so the field is stored as null and the server drops the override.
   * A non-numeric order is treated the same way rather than being coerced to zero, which would
   * silently move the item to the top of its section.
   */
  applyEditor(): void {
    const id = this.editingNodeId();

    if (!id) {
      return;
    }

    const draft = this.editorDraft();
    const parsedOrder = Number.parseInt(draft.order.trim(), 10);

    this.configurationOverrides.update((current) => ({
      ...current,
      [id]: {
        ...(current[id] ?? { menuDefinitionId: id }),
        menuDefinitionId: id,
        displayNameOverride: draft.name.trim() || null,
        iconOverride: draft.icon.trim() || null,
        displayOrderOverride: Number.isFinite(parsedOrder) ? parsedOrder : null,
      },
    }));

    this.editingNodeId.set(null);
  }

  /** Clears every override on the open node, putting it back to what the product ships. */
  resetEditor(): void {
    this.editorDraft.set({ name: '', icon: '', order: '' });
  }

  /**
   * What the row should read as right now, including an edit that has not been saved yet.
   *
   * The list has to show pending renames or the editor would appear to have done nothing until
   * the page was reloaded - the same class of confusion the save bar was added to solve.
   */
  pendingName(node: TenantMenuNodeResponse): string {
    const override = node.menuDefinitionId
      ? this.configurationOverrides()[node.menuDefinitionId]
      : undefined;

    return override?.displayNameOverride?.trim() || this.catalogueName(node);
  }

  pendingIcon(node: TenantMenuNodeResponse): string {
    const override = node.menuDefinitionId
      ? this.configurationOverrides()[node.menuDefinitionId]
      : undefined;

    return this.navigation.iconClass(override?.iconOverride?.trim() || node.resolvedIcon);
  }

  /** The product's own name for a node, which is what an override replaces. */
  catalogueName(node: TenantMenuNodeResponse): string {
    return node.catalogueName || node.code || '';
  }

  // =========================================================================================
  // Role mapping
  // =========================================================================================

  onRoleSelected(roleId: string): void {
    this.selectedRoleId.set(roleId);
    this.roleNodes.set([]);
    this.mapped.set({});
    this.savedMapped.set({});
    this.mappingParents = {};
    this.mappingNodes = {};
    this.landingMenuId.set(null);
    this.errorMessage.set('');
    this.staleData.set(false);

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
          this.readMappingTree(nodes);
          this.landingMenuId.set(mapping.landingMenuId ?? null);
          this.roleVersion.set(role.version ?? 0);
          this.loadingRoleMapping.set(false);
        },
        error: (error: unknown) => {
          this.loadingRoleMapping.set(false);
          this.errorMessage.set(apiErrorMessage(error, 'That mapping could not be loaded.'));
        },
      });
  }

  private readMappingTree(nodes: RoleMenuNodeResponse[]): void {
    const visible: Record<string, boolean> = {};
    const parents: Record<string, string> = {};
    const byId: Record<string, RoleMenuNodeResponse> = {};

    const walk = (list: RoleMenuNodeResponse[], parentId: string | null): void => {
      for (const node of list) {
        const id = node.menuDefinitionId;

        if (id) {
          visible[id] = node.isVisible === true;
          byId[id] = node;

          if (parentId) {
            parents[id] = parentId;
          }
        }

        walk(node.children ?? [], id ?? parentId);
      }
    };

    walk(nodes, null);

    this.mappingParents = parents;
    this.mappingNodes = byId;
    this.savedMapped.set(visible);
    this.mapped.set({ ...visible });
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

  /**
   * Whether the organisation offers this node at all.
   *
   * A node switched off on the other tab is removed from everybody's navigation before role
   * mapping is even consulted, so a mapping against it is stored faithfully and shows nobody
   * anything. The mapping is still allowed — it takes effect the moment the organisation
   * switches the node on — but the row has to say so, or the save looks like it was ignored.
   */
  isEnabledForOrganisation(node: RoleMenuNodeResponse): boolean {
    return node.isEnabledForOrganisation !== false;
  }

  toggleMapping(node: RoleMenuNodeResponse): void {
    if (!node.menuDefinitionId || !this.isAvailable(node)) {
      return;
    }

    const next = !this.isMapped(node);
    const updates: Record<string, boolean> = { [node.menuDefinitionId]: next };

    // Mapping a parent maps its children; unmapping it unmaps them. Same reasoning as the
    // configuration cascade above.
    const cascadeDown = (target: RoleMenuNodeResponse): void => {
      for (const child of target.children ?? []) {
        if (child.menuDefinitionId && this.isAvailable(child)) {
          updates[child.menuDefinitionId] = next;
        }

        cascadeDown(child);
      }
    };

    cascadeDown(node);

    // And mapping a child maps the section it lives in, for the same reason the organisation
    // toggles cascade upwards: an orphan under a hidden heading is never rendered.
    if (next) {
      for (
        let parentId: string | undefined = this.mappingParents[node.menuDefinitionId];
        parentId;
        parentId = this.mappingParents[parentId]
      ) {
        const parent = this.mappingNodes[parentId];

        if (parent && this.isAvailable(parent)) {
          updates[parentId] = true;
        }
      }
    }

    this.mapped.update((current) => ({ ...current, ...updates }));
  }

  saveMapping(): void {
    const roleId = this.selectedRoleId();

    if (!roleId || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.staleData.set(false);

    const visibleMenuIds = Object.entries(this.mapped())
      .filter(([, isVisible]) => isVisible)
      .map(([menuDefinitionId]) => menuDefinitionId);

    this.api
      .mapRoleMenus(roleId, {
        visibleMenuIds,
        expectedVersion: this.roleVersion(),
        // Sent back untouched: omitting it cleared the role's landing page on every save.
        landingMenuId: this.landingMenuId(),
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.savedMapped.set({ ...this.mapped() });

          this.toast.show(
            'Mapping saved',
            outcome.message ?? 'The role now sees this navigation.',
            'success');

          // The same courtesy the organisation tab already extended, and for a better reason:
          // an administrator editing their OWN role's mapping is changing their own sidebar.
          // Without this the menu they are looking at disagreed with the screen that had just
          // told them it was saved, until they happened to reload the whole application.
          this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({
            error: () => undefined,
          });

          this.onRoleSelected(roleId);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.reportSaveFailure(error, 'The mapping could not be saved.');
        },
      });
  }

  cancelMapping(): void {
    this.mapped.set({ ...this.savedMapped() });
    this.errorMessage.set('');
    this.staleData.set(false);
  }

  /**
   * Turns a failed save into something the administrator can act on.
   *
   * A 409 means somebody else saved while this screen was open, and the only useful next step is
   * to reload and redo the change — so it is named as that rather than shown as the same flat
   * "could not be saved" as a network error, which invites people to press Save again and again.
   */
  private reportSaveFailure(error: unknown, fallback: string): void {
    const stale = apiErrorCode(error) === 'CONCURRENCY_CONFLICT';

    this.staleData.set(stale);
    this.errorMessage.set(apiErrorMessage(error, fallback));
  }

  /** Reloads whichever half of the screen the person is looking at, after a stale-data refusal. */
  reloadAfterConflict(): void {
    this.staleData.set(false);
    this.errorMessage.set('');

    if (this.tab() === 'roles' && this.selectedRoleId()) {
      this.onRoleSelected(this.selectedRoleId());
      return;
    }

    this.load();
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
