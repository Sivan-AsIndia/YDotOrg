import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * UX-UI-07 — Role-aware application shell.
 *
 * Faithful implementation of section 4.7 of the YDot Practical UI/UX Generation
 * Specification. Every region (4.7.1), field (4.7.2), action (4.7.3), UI state
 * (4.7.4) and validation/confirmation pattern (4.7.6) below maps directly to the
 * controlled contract. No content or behaviour outside that contract is added.
 *
 *  Route            : /ux/role-aware-application-shell
 *  Purpose          : Present only the menu groups, scope selector and commands
 *                     permitted for the signed-in person.
 *  Primary users    : Operational users; Executive Sponsor; All users; Module users
 *  View permission  : ux.role-aware-application-shell.view
 *  Primary action   : Switch permitted role
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.7.4, plus the settled "ready" surface. */
type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Effective permission set for the signed-in person (4.7.3). */
interface EffectivePermissions {
  readonly view: boolean; // ux.role-aware-application-shell.view
  readonly switchPermittedRole: boolean; // ...switch-permitted-role
  readonly switchPermittedScope: boolean; // ...switch-permitted-scope
  readonly openCommandPalette: boolean; // ...open-command-palette
  readonly signOut: boolean; // ...sign-out
}

/** A scope-aware selector option with a stable reference and disambiguating context (4.7.2). */
interface ScopeOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

/** A single destination inside a role-aware menu group; unavailable ones are omitted (4.7.1). */
interface MenuDestination {
  readonly label: string;
  readonly route: string;
  readonly permitted: boolean;
}

/** A task-oriented menu group presented in the shell (4.7.1 Main work). */
interface MenuGroup {
  readonly key: string;
  readonly label: string;
  readonly destinations: readonly MenuDestination[];
}

/** A command the signed-in person is permitted to run (4.7.1 Main work / command palette). */
interface ShellCommand {
  readonly label: string;
  readonly permission: string;
  readonly permitted: boolean;
  readonly icon: 'edit' | 'people' | 'check' | 'lock' | 'megaphone';
}

/** A tab in the Related and history region; each carries its own permission/scope flag (4.7.1). */
interface RelatedTab {
  readonly key: string;
  readonly label: string;
  readonly permitted: boolean;
  readonly rows: readonly RelatedRow[];
}

interface RelatedRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}

@Component({
  selector: 'app-role-aware-application-shell',
  imports: [CommonModule, FormsModule],
  templateUrl: './role-aware-application-shell.html',
  styleUrl: './role-aware-application-shell.css',
})
export class RoleAwareApplicationShellComponent {
  // ----- Task header (4.7.1) -----
  protected readonly pageTitle = 'Role-aware application shell';
  protected readonly stableReference = 'UX-SHELL-2026-0001';
  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'Anita Rao · Access Administrator';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Effective permissions decided server-side; the client mirrors the same decision (4.7.3, 4.7.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    switchPermittedRole: true,
    switchPermittedScope: true,
    openCommandPalette: true,
    signOut: true,
  };

  // ----- Field and control contract (4.7.2) -----

  /** Signed-in display name — read-only text in this page mode (4.7.2). */
  protected readonly signedInName = signal('Priya Nair');

  /** Active role — read-only text; the primary action switches it (4.7.2, 4.7.3). */
  protected readonly activeRole = signal('Programme Manager');

  /** Roles the signed-in person is permitted to assume (4.7.3 eligible roles). */
  protected readonly permittedRoles = [
    'Programme Manager',
    'Executive Sponsor',
    'Finance Manager',
    'Operations Lead',
    'Programme Officer',
  ];

  /** Active organisation scope — scope-aware searchable selector with identity preview (4.7.2). */
  protected readonly scopeOptions: readonly ScopeOption[] = [
    { reference: 'ORG-0001', name: 'YDot Foundation', context: 'All organisations · National' },
    { reference: 'PROG-2026-0007', name: 'Blind-Stick Programme', context: 'Programme · Maharashtra' },
    { reference: 'CAMP-2026-0134', name: 'Education Support Program', context: 'Campaign · Tamil Nadu' },
    { reference: 'WARE-2026-0003', name: 'Pune Distribution Warehouse', context: 'Warehouse · Maharashtra' },
  ];
  protected readonly scopeQuery = signal('');
  protected readonly activeScope = signal<ScopeOption>(this.scopeOptions[0]);
  protected readonly scopePickerOpen = signal(false);

  /** Eligible scope options filtered by the search query (server-side in production; 4.7.2). */
  protected readonly scopeResults = computed(() => {
    const q = this.scopeQuery().trim().toLowerCase();
    if (!q) {
      return this.scopeOptions;
    }
    return this.scopeOptions.filter(
      (o) =>
        o.name.toLowerCase().includes(q) ||
        o.reference.toLowerCase().includes(q) ||
        o.context.toLowerCase().includes(q),
    );
  });

  /** Unread task count — read-only measure with unit (4.7.2). */
  protected readonly unreadTaskCount = signal(12);
  /** Notification count — read-only measure with unit (4.7.2). */
  protected readonly notificationCount = signal(12);

  /** Help context — read-only text (4.7.2). */
  protected readonly helpContext = signal('Workspace · Role-aware shell');

  /** Session expiry warning — date-time in the operating time zone (4.7.2). */
  protected readonly sessionExpiry = signal('2026-07-29T10:15');
  protected readonly sessionExpiryInterpreted = computed(
    () => `${this.formatDateTime(this.sessionExpiry())} · ${this.operatingTimeZone}`,
  );

  // ----- Main work: permitted menu groups, scope selector and commands (4.7.1) -----

  /** Role-aware menu groups; only permitted destinations are rendered, unavailable omitted (4.7.1). */
  protected readonly menuGroups: readonly MenuGroup[] = [
    {
      key: 'main',
      label: 'Main',
      destinations: [{ label: 'Dashboard', route: '/app/dashboard', permitted: true }],
    },
    {
      key: 'workspace',
      label: 'Workspace',
      destinations: [
        { label: 'My Workspace', route: '/app/workspace/my-workspace', permitted: true },
        { label: 'Executive dashboard', route: '/app/workspace/executive-dashboard', permitted: true },
        { label: 'Notification centre', route: '/app/workspace/notifications', permitted: true },
      ],
    },
    {
      key: 'administration',
      label: 'Administration',
      destinations: [
        { label: 'User directory', route: '/app/administration/access/user-directory', permitted: true },
        {
          label: 'Role and permission catalogue',
          route: '/app/administration/access/role-and-permission-catalogue',
          permitted: true,
        },
        { label: 'Access review campaign', route: '/app/administration/access/access-review-campaign', permitted: false },
      ],
    },
    {
      key: 'reports',
      label: 'Reports',
      destinations: [{ label: 'Impact reports', route: '/app/reports/impact', permitted: true }],
    },
  ];

  /** Groups reduced to only their permitted destinations; empty groups are dropped (4.7.1). */
  protected readonly visibleMenuGroups = computed(() =>
    this.menuGroups
      .map((g) => ({ ...g, destinations: g.destinations.filter((d) => d.permitted) }))
      .filter((g) => g.destinations.length > 0),
  );

  /** Available menu groups — read-only summary field (4.7.2). */
  protected readonly availableMenuGroupsLabel = computed(() => {
    const names = this.visibleMenuGroups().map((g) => g.label);
    return `${names.join(', ')} (${names.length} groups)`;
  });

  /** The currently expanded menu group (progressive disclosure; 4.7.1 Main work). */
  protected readonly expandedGroup = signal<string | null>('workspace');

  /** Commands permitted for the signed-in person; only permitted ones are shown (4.7.1). */
  protected readonly commands: readonly ShellCommand[] = [
    { label: 'Create donation', permission: 'donation.create', permitted: true, icon: 'edit' },
    { label: 'Add beneficiary', permission: 'beneficiary.create', permitted: true, icon: 'people' },
    { label: 'Schedule follow up', permission: 'followup.create', permitted: true, icon: 'check' },
    { label: 'Raise access request', permission: 'access-request.create', permitted: true, icon: 'lock' },
    { label: 'Create announcement', permission: 'announcement.create', permitted: false, icon: 'megaphone' },
  ];
  protected readonly permittedCommands = computed(() => this.commands.filter((c) => c.permitted));

  // ----- Context and filters (4.7.1) -----
  protected readonly searchTerm = signal('');
  protected readonly savedFilters = ['Default shell view', 'Administration focus', 'Field role view'];
  protected readonly savedFilter = signal(this.savedFilters[0]);
  protected readonly lastRefresh = signal('29 Jul 2026, 09:30 IST');

  protected readonly totalsCaption = computed(
    () => `Scope ${this.activeScope().name} · as of ${this.lastRefresh()}`,
  );

  /** Active-filter summary chips, qualified by scope (4.7.1). */
  protected readonly activeFilterSummary = computed(() => {
    const chips = [
      { key: 'scope', label: `Scope: ${this.activeScope().name}` },
      { key: 'role', label: `Role: ${this.activeRole()}` },
      { key: 'saved', label: `View: ${this.savedFilter()}` },
    ];
    const term = this.searchTerm().trim();
    if (term) {
      chips.push({ key: 'search', label: `Search: “${term}”` });
    }
    return chips;
  });

  // ----- Related and history (4.7.1). Each tab is independently permission/scope gated. -----
  protected readonly relatedTabs: readonly RelatedTab[] = [
    {
      key: 'linked',
      label: 'Linked records',
      permitted: true,
      rows: [
        { primary: 'ROLE-PM-014', secondary: 'Programme Manager assignment', meta: 'Access · Active' },
        { primary: 'ORG-0001', secondary: 'YDot Foundation', meta: 'Organisation · National' },
      ],
    },
    {
      key: 'documents',
      label: 'Documents',
      permitted: true,
      rows: [{ primary: 'Access policy — 2026', secondary: 'PDF · 480 KB', meta: 'Effective 20 Jul 2026' }],
    },
    {
      key: 'activity',
      label: 'Activity',
      permitted: true,
      rows: [
        { primary: 'Scope switched', secondary: 'Narrowed to Maharashtra', meta: 'P. Nair · 09:32 IST' },
        { primary: 'Shell loaded', secondary: 'Role-aware menu resolved', meta: 'System · 09:30 IST' },
      ],
    },
    {
      key: 'integration',
      label: 'Integration status',
      permitted: true,
      rows: [
        { primary: 'Identity provider', secondary: 'Healthy', meta: 'Last sync 09:28 IST' },
        { primary: 'Permission service', secondary: 'Healthy', meta: 'Last sync 09:29 IST' },
      ],
    },
    {
      key: 'support',
      label: 'Support correlation',
      permitted: true,
      rows: [{ primary: 'SESS-88431', secondary: 'Active session correlation', meta: 'Open · session trace' }],
    },
    {
      key: 'audit',
      label: 'Audit chronology',
      permitted: true,
      rows: [
        { primary: 'View opened', secondary: 'ux.role-aware-application-shell.view granted', meta: 'P. Nair · 09:31 IST' },
        { primary: 'Role switch requested', secondary: 'Pending confirmation', meta: 'P. Nair · 09:34 IST' },
      ],
    },
  ];

  /** Only tabs the actor is permitted to see are offered; unavailable ones are omitted (4.7.1). */
  protected readonly visibleRelatedTabs = computed(() => this.relatedTabs.filter((t) => t.permitted));
  protected readonly activeRelatedTab = signal<string>('linked');
  protected readonly activeRelatedRows = computed(
    () => this.visibleRelatedTabs().find((t) => t.key === this.activeRelatedTab())?.rows ?? [],
  );

  // ----- Persistent outcome (4.7.1). A toast may support but never replace this. -----
  protected readonly persistentOutcome = {
    reference: 'UX-SHELL-2026-0001',
    state: 'Active',
    effectiveTime: '29 Jul 2026, 09:30 IST',
    downstreamStatus: 'No role switch pending',
    owner: 'Anita Rao · Access Administrator',
    nextAction: 'Review active scope and permitted commands',
  };

  // ----- UI state demonstrability (4.7.4 / 4.7.7) -----
  protected readonly uiState = signal<UiState>('ready');
  protected readonly uiStates: readonly UiState[] = [
    'ready',
    'loading',
    'empty',
    'validation',
    'duplicate',
    'no-access',
    'conflict',
    'dependency-failure',
    'success',
  ];

  // ----- Actions: dialogs, overflow, command palette (4.7.3) -----
  protected readonly overflowOpen = signal(false);
  protected readonly roleDialogOpen = signal(false);
  protected readonly commandPaletteOpen = signal(false);
  protected readonly commandQuery = signal('');
  /** Candidate role selected inside the switch dialog (required; 4.7.6). */
  protected readonly selectedRole = signal('');

  /** Whether the primary action (Switch permitted role) is currently allowed (4.7.1, 4.7.3). */
  protected readonly switchRoleAllowed = computed(
    () => this.permissions.switchPermittedRole && this.uiState() !== 'no-access',
  );

  /** Commands filtered inside the palette by query. */
  protected readonly paletteResults = computed(() => {
    const q = this.commandQuery().trim().toLowerCase();
    const list = this.permittedCommands();
    return q ? list.filter((c) => c.label.toLowerCase().includes(q)) : list;
  });

  // ===== Behaviour =====

  protected selectScope(option: ScopeOption): void {
    this.activeScope.set(option);
    this.scopePickerOpen.set(false);
    this.scopeQuery.set('');
  }

  protected toggleScopePicker(): void {
    this.scopePickerOpen.update((v) => !v);
  }

  protected toggleGroup(key: string): void {
    this.expandedGroup.update((cur) => (cur === key ? null : key));
  }

  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected toggleOverflow(): void {
    this.overflowOpen.update((v) => !v);
  }

  /** Clearing a filter is explicit and returns focus predictably (4.7.1 Context and filters). */
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.savedFilter.set(this.savedFilters[0]);
    this.activeScope.set(this.scopeOptions[0]);
  }

  // --- Switch permitted role (Primary; high-risk confirm — 4.7.3 / 4.7.6) ---
  protected openRoleDialog(): void {
    if (!this.switchRoleAllowed()) {
      return;
    }
    this.selectedRole.set(this.activeRole());
    this.overflowOpen.set(false);
    this.roleDialogOpen.set(true);
  }

  protected cancelRoleSwitch(): void {
    this.roleDialogOpen.set(false);
  }

  /** Explicit confirmation before the role is switched (4.7.3, 4.7.6 high-risk). */
  protected confirmRoleSwitch(): void {
    const next = this.selectedRole().trim();
    if (!next) {
      // Required field — keep the dialog open and surface validation (4.7.6).
      this.uiState.set('validation');
      return;
    }
    console.info('Switch permitted role confirmed', { from: this.activeRole(), to: next });
    this.activeRole.set(next);
    this.roleDialogOpen.set(false);
    this.uiState.set('success');
  }

  // --- Switch permitted scope (Secondary / overflow — 4.7.3) ---
  protected switchPermittedScope(): void {
    if (!this.permissions.switchPermittedScope) {
      return;
    }
    this.overflowOpen.set(false);
    this.scopePickerOpen.set(true);
  }

  // --- Open command palette (Secondary / overflow — 4.7.3) ---
  protected openCommandPalette(): void {
    if (!this.permissions.openCommandPalette) {
      return;
    }
    this.overflowOpen.set(false);
    this.commandQuery.set('');
    this.commandPaletteOpen.set(true);
  }

  protected closeCommandPalette(): void {
    this.commandPaletteOpen.set(false);
  }

  protected runCommand(command: ShellCommand): void {
    console.info('Command invoked', command.label);
    this.commandPaletteOpen.set(false);
  }

  // --- Sign out (Secondary / overflow — 4.7.3) ---
  protected signOut(): void {
    if (!this.permissions.signOut) {
      return;
    }
    this.overflowOpen.set(false);
    console.info('Sign out requested');
  }

  private formatDateTime(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return d.toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
