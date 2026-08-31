  import { Component, computed, effect, inject, signal } from '@angular/core';
  import { CommonModule } from '@angular/common';
  import { FormsModule } from '@angular/forms';
  import { RouterModule, Router } from '@angular/router';
  import { forkJoin } from 'rxjs';
  import { ToastService } from '../../../../Shared/services/toast.service';
  import { RoleCatalogueApiService } from '../../../../Service/role-catalogue-api.service';
  import { PermissionMatrixResponse } from '../../../../Shared/models/iam-contract.model';
  import {
    CreateRoleRequest,
    RoleCatalogueResponse,
    RoleDetail,
    RoleListItem,
    RolePermission,
    RoleSearchFilter,
  } from '../../../../Shared/models/role-catalogue-api.model';

  /** One permission, as the detail panel lists it. */
  interface RolePermissionView {
    code: string;
    name: string;
    isSensitive: boolean;
  }

  /** One segregation-of-duties rule, as the detail panel lists it. */
  interface RoleConflictView {
    name: string;
    reason: string;
    isBlocking: boolean;
  }

  /**
   * The open role, as the detail panel shows it.
   *
   * Deeper than the row: the permission and conflict lists stay lists, because the panel renders
   * one item each rather than a sentence.
   */
  interface RoleDetailView {
    id: string;
    name: string;
    code: string;
    purpose: string;
    roleType: string;
    owningFunction: string;
    approvalState: string;
    privilegeLevel: string;
    isPrivileged: boolean;
    isDefaultRole: boolean;
    isSystemRole: boolean;
    grantsAllPermissions: boolean;
    priority: number;
    assignedUserCount: number;
    permissionBundle: RolePermissionView[];
    excludedPermissions: RolePermissionView[];
    incompatibleRoles: RoleConflictView[];
    visibleMenuCount: number;
    roleVersion: number;
    createdAt: string;
    updatedAt: string;
    canActivate: boolean;
    canRetire: boolean;
    version: number;
  }

  interface RoleItemView {
    status: string;
    canActivate: boolean;
    canRetire: boolean;
    canDelete: boolean;
    id: string;
    reference: string;
    roleName: string;
    roleCode: string;
    purpose: string;
    roleType: string;
    owningFunction: string;
    permissionBundle: string;
    excludedPermissions: string;
    defaultScopeType: string;
    incompatibleRoles: string;
    assignmentPrerequisites: string;
    maximumDuration: string;
    reviewInterval: string;
    privilegeClassification: string;
    roleVersion: string;
    approvalState: string;
    approvalStateClass: string;
    assignedUserCount: number;
    effectiveDate: string;
    retirementReason: string;
    isSystemRole: boolean;
    version: number;
  }

  @Component({
    selector: 'app-role-catalogue',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterModule],
    templateUrl: './role-catalogue.html',
    styleUrl: './role-catalogue.css',
  })
  export class RoleCatalogueComponent {
    private readonly router = inject(Router);
    private readonly toast = inject(ToastService);
    private readonly api = inject(RoleCatalogueApiService);

    data = signal<RoleCatalogueResponse | null>(null);
    detailCache = new Map<string, RoleDetail>();
    loading = signal(true);
    loadFailed = signal(false);
    submitting = signal(false);
    errorMessage = signal('');

    searchQuery = signal('');
    filterStatus = signal('');
    filterType = signal('');
    filterFunction = signal('');

    filteredRoles = signal<RoleItemView[]>([]);

    // ===== NEW: Stats used by the top summary cards + status tabs =====
    // Total count only — the per-status split below is fully dynamic and
    // reflects whatever approvalState values actually exist in the data
    // (Active/Draft/Retired/Pending/Approved/... — whatever comes back).
    roleCounts = computed(() => ({
      total: (this.data()?.roles ?? []).length,
    }));

    // ===== NEW: extra widget card — count of built-in/system roles =====
    systemRoleCount = computed(() => (this.data()?.roles ?? []).filter(r => r.isSystemRole).length);

    statusBreakdown = computed(() => {
      const items = this.data()?.roles ?? [];
      const counts = new Map<string, number>();
      for (const r of items) {
        const state = r.statusDisplay ?? r.status ?? 'Unknown';
        counts.set(state, (counts.get(state) ?? 0) + 1);
      }
      // Stable, readable order: the states people care about first, then anything else.
      const priority: Record<string, number> = { 'In use': 0, Draft: 1, Retired: 2 };
      return Array.from(counts.entries())
        .map(([status, count]) => ({ status, count }))
        .sort((a, b) => (priority[a.status] ?? 99) - (priority[b.status] ?? 99) || a.status.localeCompare(b.status));
    });

    // Maps any approvalState string to a consistent visual bucket so new/unexpected
    // states (Pending, Approved, etc.) still render sensibly instead of falling
    // back to nothing.
    statusBucket(status: string): 'active' | 'draft' | 'retired' | 'default' {
      const s = (status ?? '').toLowerCase();
      if (s === 'active' || s === 'approved') return 'active';
      if (s === 'draft' || s === 'pending') return 'draft';
      if (s === 'retired' || s === 'rejected') return 'retired';
      return 'default';
    }

    statCardClass(status: string): string {
      return `stat-${this.statusBucket(status)}`;
    }

    statusTabClass(status: string): string {
      return `tab-${this.statusBucket(status)}-state`;
    }

    statusBadgeClass(status: string): string {
      return `status-${this.statusBucket(status)}`;
    }

    statusIcon(status: string): string {
      switch (this.statusBucket(status)) {
        case 'active': return 'ri-shield-check-line';
        case 'draft': return 'ri-draft-line';
        case 'retired': return 'ri-archive-line';
        default: return 'ri-shield-line';
      }
    }

    // ===== NEW: deterministic light-color avatar per role name =====
    private readonly avatarPalette: { background: string; color: string }[] = [
      { background: '#E9FBF3', color: '#12946A' }, // green
      { background: '#EAF1FF', color: '#3157C7' }, // blue
      { background: '#FFF3E0', color: '#B9711F' }, // orange
      { background: '#F1F0FF', color: '#6A5ACD' }, // purple
      { background: '#FDEEEE', color: '#C1443A' }, // red
      { background: '#E6FAF8', color: '#0E8074' }, // teal
      { background: '#FFF8E6', color: '#B08A1E' }, // yellow
      { background: '#FCE9F5', color: '#B23D82' }, // pink
    ];

    avatarStyle(name: string): { background: string; color: string } {
      const idx = this.hashString(name ?? '') % this.avatarPalette.length;
      return this.avatarPalette[idx];
    }

    private hashString(value: string): number {
      let hash = 0;
      for (let i = 0; i < value.length; i++) {
        hash = (hash * 31 + value.charCodeAt(i)) >>> 0;
      }
      return hash;
    }

    // ===== Create Role Modal =====
    showCreateModal = signal(false);
    createRoleForm = signal({
      name: '',
      code: '',
      description: '',
      displayTag: '',
      priority: 100,
      isPrivileged: false,
      isDefaultRole: false,
    });

    /**
     * The permissions ticked, BY CODE rather than by id.
     *
     * The API assigns permissions by code, and a code is stable across environments in a way an
     * id is not: the same role definition exported from one Organisation and imported into
     * another keeps meaning the same thing.
     */
    selectedPermissionCodes = signal<string[]>([]);

    /** Codes ticked as explicit denials. Deny beats allow wherever the two overlap. */
    selectedDeniedCodes = signal<string[]>([]);

    /**
     * The permission matrix, loaded when the editor opens.
     *
     * Grouped by module and group, because a flat list of a hundred and thirty codes cannot be
     * reasoned about — and the whole point of the editor is to let somebody decide what a role
     * should be able to do.
     */
    readonly permissionMatrix = signal<PermissionMatrixResponse | null>(null);
    selectedIncompatibleRoleIds = signal<string[]>([]);
    permissionDropdownOpen = signal(false);
    incompatibleDropdownOpen = signal(false);

    // ===== Role Detail Modal =====
    showDetailModal = signal(false);
    detailRole = signal<RoleDetail | null>(null);

    /**
     * The open role, shaped for the detail panel.
     *
     * Built from the record the server sends and nothing else. Fields the panel used to show —
     * a maximum duration, a review interval, an assignment prerequisite — are not columns on a
     * role here, and rendering empty rows for them read as data that had gone missing rather
     * than as facts nobody records.
     */
    readonly detailView = computed<RoleDetailView | null>(() => {
      const detail = this.detailRole();

      if (!detail) {
        return null;
      }

      const permissions = detail.permissions ?? [];

      const toPermissionView = (permission: RolePermission): RolePermissionView => ({
        code: permission.permissionCode ?? '',
        name: permission.permissionName ?? permission.permissionCode ?? '',
        isSensitive: permission.isSensitive === true,
      });

      const status = detail.status ?? '';

      return {
        id: detail.id ?? '',
        name: detail.name ?? '',
        code: detail.code ?? '',
        purpose: detail.description ?? '',
        roleType: this.roleTypeLabel(detail.roleType),
        owningFunction: detail.displayTag ?? '',
        approvalState: detail.statusDisplay ?? status,
        privilegeLevel: detail.isPrivileged ? 'Privileged' : 'Standard',
        isPrivileged: detail.isPrivileged === true,
        isDefaultRole: detail.isDefaultRole === true,
        isSystemRole: detail.isSystemRole === true,
        grantsAllPermissions: detail.grantsAllTenantPermissions === true,
        priority: detail.priority ?? 0,
        assignedUserCount: detail.memberCount ?? 0,

        permissionBundle: permissions
          .filter((permission) => permission.isDenied !== true)
          .map(toPermissionView),

        // Deny beats allow, so an excluded permission is worth its own list: a role can grant a
        // broad set and carve one thing out of it, and somebody reading the panel needs to see
        // the carve-out rather than infer it from an absence.
        excludedPermissions: permissions
          .filter((permission) => permission.isDenied === true)
          .map(toPermissionView),

        incompatibleRoles: (detail.incompatibilities ?? []).map((conflict) => ({
          name: conflict.conflictingRoleName ?? '',
          reason: conflict.reason ?? '',
          isBlocking: conflict.isBlocking === true,
        })),

        visibleMenuCount: (detail.visibleMenuIds ?? []).length,
        roleVersion: detail.version ?? 0,
        createdAt: detail.createdAtUtc ? this.formatDate(detail.createdAtUtc) : '—',
        updatedAt: detail.updatedAtUtc ? this.formatDate(detail.updatedAtUtc) : '—',

        // A system role is the platform's own and is never retired from here; a draft is put
        // into use, and a role in use is retired. Reading these off the raw status rather than
        // the display text keeps the buttons correct when the wording changes.
        canActivate: status === 'draft' && detail.isSystemRole !== true,
        canRetire: status === 'active' && detail.isSystemRole !== true,
        version: detail.version ?? 0,
      };
    });

    /** The role type in the words the screen uses. */
    private roleTypeLabel(roleType: string | undefined): string {
      switch (roleType) {
        case 'platform': return 'Platform';
        case 'tenant': return 'Organisation';
        case 'template': return 'Template';
        default: return roleType ?? '';
      }
    }

    // ===== Compare & Delete Modal =====
    showCompareModal = signal(false);
    showDeleteRoleModal = signal(false);
    actionRole = signal<RoleItemView | null>(null);
    deleteReason = signal('');
    deleteError = signal('');
    compareRoleId = signal('');
    comparing = signal(false);
    compareResult = signal<{ onlyInLeft: string[]; onlyInRight: string[]; inBoth: string[] } | null>(null);

    constructor() {
      this.loadData();
      effect(() => { this.applyFilters(); });
    }

    private loadData(): void {
      this.loading.set(true);
      this.loadFailed.set(false);

      const filter: RoleSearchFilter = { page: 1, pageSize: 100 };

      this.api.getCatalogue(filter).subscribe({
        next: (res) => {
          this.data.set(res);
          this.loading.set(false);
          this.applyFilters();
        },
        error: (error: Error) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(error.message);
          this.toast.show('Error', 'Failed to load role catalogue.', 'error');
        },
      });
    }

    retry(): void { this.loadData(); }

    applyFilters(): void {
      const all = this.data()?.roles ?? [];
      const q = this.searchQuery().toLowerCase();
      const s = this.filterStatus();
      const t = this.filterType();
      const f = this.filterFunction();
      let result = all;
      if (q) {
        result = result.filter((r) =>
          (r.name ?? '').toLowerCase().includes(q)
          || (r.code ?? '').toLowerCase().includes(q));
      }
      if (s) result = result.filter((r) => r.status === s);
      if (t) result = result.filter((r) => r.roleType === t);

      // There is no owning-function on a role: the department that owns a role is a matter of
      // convention rather than a column, and inventing one here would produce a filter that
      // silently matched nothing.
      void f;
      this.filteredRoles.set(result.map(r => this.toRoleItemView(r)));
    }

    clearFilters(): void {
      this.searchQuery.set(''); this.filterStatus.set(''); this.filterType.set(''); this.filterFunction.set('');
    }

    private toRoleItemView(r: RoleListItem): RoleItemView {
      // The detail is used when it happens to be cached from a previous open; the row alone is
      // enough to render the table, which is why the catalogue does not fetch every role.
      const detail = this.detailCache.get(r.id ?? '');

      const permissions = (detail?.permissions ?? []);
      const granted = permissions.filter((permission) => permission.isDenied !== true);
      const denied = permissions.filter((permission) => permission.isDenied === true);

      const status = r.status ?? '';

      return {
        status,
        canActivate: status === 'draft' && r.isSystemRole !== true,
        canRetire: status === 'active' && r.isSystemRole !== true,

        // Only a draft can be removed outright. Once a role has been in use its assignment
        // history has to stay explicable, and a dangling role id in an audit row explains
        // nothing — retiring is what applies then.
        canDelete: status === 'draft' && r.isSystemRole !== true,

        id: r.id ?? '',
        reference: r.code ?? '',
        roleName: r.name ?? '',
        roleCode: r.code ?? '',
        purpose: r.description ?? detail?.description ?? '',
        roleType: this.roleTypeLabel(r.roleType),
        owningFunction: r.displayTag ?? '',

        permissionBundle: granted.length > 0
          ? granted.map((permission) => permission.permissionCode).join(', ')
          : `${r.permissionCount ?? 0} permission(s)`,

        // Deny beats allow, so an excluded permission is worth showing separately: a role can
        // grant a broad set and carve one thing out of it.
        excludedPermissions: denied.length > 0
          ? denied.map((permission) => permission.permissionCode).join(', ')
          : 'None',

        defaultScopeType: 'Whole organisation',

        incompatibleRoles: (detail?.incompatibilities ?? []).length > 0
          ? (detail?.incompatibilities ?? [])
              .map((item) => item.conflictingRoleName)
              .join(', ')
          : 'None',

        assignmentPrerequisites: 'None',
        maximumDuration: '',
        reviewInterval: '',
        privilegeClassification: r.isPrivileged ? 'Privileged' : 'Standard',
        roleVersion: `v${r.version ?? 0}`,
        approvalState: r.statusDisplay ?? r.status ?? '',
        approvalStateClass: this.approvalStateClass(r.status ?? ''),
        assignedUserCount: r.memberCount ?? 0,
        effectiveDate: r.updatedAtUtc ? this.formatDate(r.updatedAtUtc) : '—',
        retirementReason: '',
        isSystemRole: r.isSystemRole === true,
        version: r.version ?? 0,
      };
    }

    private approvalStateClass(state: string): string {
      switch (state) {
        case 'active': return 'bg-success-subtle text-success';
        case 'draft': return 'bg-warning-subtle text-warning';
        case 'inactive': return 'bg-secondary-subtle text-secondary';
        default: return 'bg-secondary-subtle text-secondary';
      }
    }

    private formatDate(value: string): string {
      try {
        return new Date(value).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
      } catch {
        return value;
      }
    }

    // ===== CREATE ROLE =====
    openCreateModal(): void {
      this.createRoleForm.set({
        name: '',
        code: '',
        description: '',
        displayTag: '',
        priority: 100,
        isPrivileged: false,
        isDefaultRole: false,
      });
      this.selectedPermissionCodes.set([]);
      this.selectedDeniedCodes.set([]);
      this.selectedIncompatibleRoleIds.set([]);
      this.permissionDropdownOpen.set(false);
      this.incompatibleDropdownOpen.set(false);
      this.errorMessage.set('');
      this.showCreateModal.set(true);

      // The matrix is fetched when the editor opens rather than with the catalogue: it is a
      // hundred and thirty rows that most visits to this screen never look at.
      if (!this.permissionMatrix()) {
        this.api.getPermissionMatrix().subscribe({
          next: (matrix) => this.permissionMatrix.set(matrix),
          error: (error: Error) =>
            this.toast.show('Permissions unavailable', error.message, 'error'),
        });
      }
    }

    closeCreateModal(): void {
      this.showCreateModal.set(false);
    }

    createDraftRole(): void {
      this.openCreateModal();
    }

    togglePermissionDropdown(): void {
      this.permissionDropdownOpen.set(!this.permissionDropdownOpen());
    }

    toggleIncompatibleDropdown(): void {
      this.incompatibleDropdownOpen.set(!this.incompatibleDropdownOpen());
    }

    togglePermission(code: string): void {
      const current = this.selectedPermissionCodes();
      this.selectedPermissionCodes.set(
        current.includes(code) ? current.filter(x => x !== code) : [...current, code]
      );

      // Granting a permission clears any denial of the same code. Holding both would be a rule
      // that argues with itself, and the server would resolve it as a denial — which is not
      // what somebody who has just ticked the box is asking for.
      if (this.selectedPermissionCodes().includes(code)) {
        this.selectedDeniedCodes.set(this.selectedDeniedCodes().filter(x => x !== code));
      }
    }

    toggleDeniedPermission(code: string): void {
      const current = this.selectedDeniedCodes();
      this.selectedDeniedCodes.set(
        current.includes(code) ? current.filter(x => x !== code) : [...current, code]
      );

      if (this.selectedDeniedCodes().includes(code)) {
        this.selectedPermissionCodes.set(
          this.selectedPermissionCodes().filter(x => x !== code));
      }
    }

    isPermissionDenied(code: string): boolean {
      return this.selectedDeniedCodes().includes(code);
    }

    toggleIncompatibleRole(id: string): void {
      const current = this.selectedIncompatibleRoleIds();
      this.selectedIncompatibleRoleIds.set(
        current.includes(id) ? current.filter(x => x !== id) : [...current, id]
      );
    }

    isPermissionSelected(code: string): boolean {
      return this.selectedPermissionCodes().includes(code);
    }

    isIncompatibleRoleSelected(id: string): boolean {
      return this.selectedIncompatibleRoleIds().includes(id);
    }

    /**
     * The permissions currently ticked, by name.
     *
     * Read from the matrix the editor loads rather than from the catalogue list: the catalogue
     * carries roles, and a permission's name lives with the permission.
     */
    selectedPermissionLabels(): string {
      const byCode = new Map<string, string>();

      for (const module of this.permissionMatrix()?.modules ?? []) {
        for (const group of module.groups ?? []) {
          for (const permission of group.permissions ?? []) {
            byCode.set(permission.code ?? '', permission.name ?? permission.code ?? '');
          }
        }
      }

      return this.selectedPermissionCodes()
        .map((code) => byCode.get(code) ?? code)
        .join(', ');
    }

    /**
     * Every permission in the matrix, flattened.
     *
     * The picker is a searchable list rather than the grouped grid the matrix screen renders:
     * somebody creating a role usually knows roughly what they are looking for, and a flat list
     * they can type into finds it in one keystroke.
     */
    readonly permissionChoices = computed(() => {
      const choices: { code: string; name: string; description: string; isSensitive: boolean }[] = [];

      for (const module of this.permissionMatrix()?.modules ?? []) {
        for (const group of module.groups ?? []) {
          for (const permission of group.permissions ?? []) {
            choices.push({
              code: permission.code ?? '',
              name: permission.name ?? permission.code ?? '',
              description: permission.description
                ?? `${module.moduleName ?? ''} · ${group.groupName ?? ''}`,
              isSensitive: permission.isSensitive === true,
            });
          }
        }
      }

      return choices;
    });

    selectedIncompatibleRoleLabels(): string {
      const roles = this.data()?.roles ?? [];
      return this.selectedIncompatibleRoleIds()
        .map(id => roles.find(r => r.id === id)?.name ?? id)
        .join(', ');
    }

    confirmCreateRole(): void {
      const form = this.createRoleForm();

      if (!form.name.trim() || !form.code.trim()) {
        this.toast.show('Check the form', 'A role needs a name and a code.', 'warning');
        return;
      }

      this.submitting.set(true);
      this.errorMessage.set('');

      const request: CreateRoleRequest = {
        code: form.code.trim(),
        name: form.name.trim(),
        description: form.description.trim() || null,
        displayTag: form.displayTag.trim() || null,

        // Created as a draft, always. A role arrives with no permissions on it and is put into
        // use once somebody has decided what it grants.
        status: 'draft',
        priority: form.priority,
        isPrivileged: form.isPrivileged,
        isDefaultRole: form.isDefaultRole,
        permissionCodes: this.selectedPermissionCodes(),
        visibleMenuIds: [],
      };

      this.api.createRole(request).subscribe({
        next: (role) => {
          this.detailCache.set(role.id ?? '', role);
          this.recordConflicts(role.id ?? '', role.name ?? form.name);
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.errorMessage.set(error.message);
          this.toast.show('Could not create the role', error.message, 'error');
        },
      });
    }

    /**
     * Records the roles the new one must not be held alongside.
     *
     * Separate calls because the endpoint needs the id the create call has only just returned.
     * A failure here is reported as exactly what it is — the role exists, the rule did not
     * record — rather than as a failed creation, because the role really is there.
     */
    private recordConflicts(roleId: string, roleName: string): void {
      const conflictIds = this.selectedIncompatibleRoleIds();

      const finish = (conflictError?: string) => {
        this.submitting.set(false);
        this.showCreateModal.set(false);

        if (conflictError) {
          this.toast.show(
            'Role created, rules not recorded',
            `${roleName} was created. The segregation-of-duties rules could not be saved: ${conflictError}`,
            'warning');
        } else {
          this.toast.show('Role created', `${roleName} has been created as a draft.`, 'success');
        }

        this.loadData();
      };

      if (!roleId || conflictIds.length === 0) {
        finish();
        return;
      }

      forkJoin(
        conflictIds.map((conflictingRoleId) =>
          this.api.addIncompatibility({
            roleId,
            conflictingRoleId,
            reason: 'Recorded when the role was created.',
            isBlocking: true,
          })),
      ).subscribe({
        next: () => finish(),
        error: (error: Error) => finish(error.message),
      });
    }

    // ===== ROLE DETAIL =====
    openDetail(role: RoleItemView): void {
      const cached = this.detailCache.get((role.id ?? ''));
      if (cached) {
        this.detailRole.set(cached);
        this.showDetailModal.set(true);
        return;
      }

      this.api.getRole((role.id ?? '')).subscribe({
        next: (detail) => {
          this.detailCache.set(detail.id ?? '', detail);
          this.detailRole.set(detail);
          this.showDetailModal.set(true);
        },
        error: (error: Error) => {
          this.toast.show('Load Failed', error.message, 'error');
        },
      });
    }

    closeDetail(): void { this.showDetailModal.set(false); this.detailRole.set(null); }

    // ===== COMPARE =====
    openCompareModal(role: RoleItemView): void {
      this.actionRole.set(role);
      this.compareRoleId.set('');
      this.compareResult.set(null);
      this.showCompareModal.set(true);
    }

    closeCompareModal(): void {
      this.showCompareModal.set(false);
      this.actionRole.set(null);
      this.compareRoleId.set('');
      this.compareResult.set(null);
    }

    runCompare(): void {
      const base = this.actionRole();
      const otherId = this.compareRoleId();
      if (!base || !otherId) return;

      this.comparing.set(true);
      this.api.compareRoles(base.id, otherId).subscribe({
        next: (result) => {
          this.comparing.set(false);
          this.compareResult.set({
            onlyInLeft: result.onlyInLeft,
            onlyInRight: result.onlyInRight,
            inBoth: result.inBoth,
          });
        },
        error: (error: Error) => {
          this.comparing.set(false);
          this.toast.show('Compare Failed', error.message, 'error');
        },
      });
    }

    // ===== DELETE DRAFT =====
    openDeleteRoleModal(role: RoleItemView): void {
      if (!role.canDelete) return;
      this.actionRole.set(role);
      this.deleteReason.set('');
      this.deleteError.set('');
      this.showDeleteRoleModal.set(true);
    }

    closeDeleteRoleModal(): void {
      this.showDeleteRoleModal.set(false);
      this.actionRole.set(null);
      this.deleteReason.set('');
      this.deleteError.set('');
    }

    confirmDeleteRole(): void {
      const target = this.actionRole();
      if (!target) return;

      const reason = this.deleteReason().trim();
      if (reason.length < 10) {
        this.deleteError.set('Deletion reason must be at least 10 characters.');
        return;
      }

      this.submitting.set(true);
      this.errorMessage.set('');

      this.api.deleteDraftRole(target.id, target.version, reason).subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.closeDeleteRoleModal();
          this.toast.show('Role Deleted', `Role ${target.roleCode} was permanently deleted.`, 'success');
          this.loadData();
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.deleteError.set(error.message);
        },
      });
    }

    // ===== ROLE ACTIONS =====
    submitRole(role: RoleItemView | RoleDetailView): void {
      this.submitting.set(true);
      this.api.submitRole((role.id ?? ''), (role.version ?? 0), 'Ready for use.').subscribe({
        next: () => {
          this.submitting.set(false);

          // Drop the cached copy rather than patching it: the server decides the new state, and
          // a cache one save out of date is worse than no cache at all.
          this.detailCache.delete((role.id ?? ''));

          this.toast.show(
            'Role activated',
            `${this.roleDisplayName(role)} is now in use.`,
            'success');
          this.closeDetail();
          this.loadData();
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.toast.show('Submit Failed', error.message, 'error');
        },
      });
    }

    retireRole(role: RoleItemView | RoleDetailView): void {
      this.submitting.set(true);
      this.api.retireRole((role.id ?? ''), (role.version ?? 0), 'Role is no longer required.').subscribe({
        next: () => {
          this.submitting.set(false);
          this.detailCache.delete((role.id ?? ''));
          this.toast.show('Role retired', `${this.roleDisplayName(role)} has been retired.`, 'info');
          this.closeDetail();
          this.loadData();
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.toast.show('Retire Failed', error.message, 'error');
        },
      });
    }

    private roleDisplayName(role: RoleItemView | RoleDetailView): string {
      return 'roleName' in role ? role.roleName : role.name;
    }

    goBack(): void {
      this.router.navigate(['/app/administration/access/user-directory']);
    }
  }