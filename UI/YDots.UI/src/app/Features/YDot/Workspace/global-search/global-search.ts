import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * SCR-UX-003 — Global search.
 *
 * Faithful implementation of section 4.3 of the YDot Practical UI/UX Generation
 * Specification. Every region (4.3.1), field (4.3.2), action (4.3.3), UI state
 * (4.3.4), responsive / accessibility / privacy rule (4.3.5) and validation /
 * confirmation pattern (4.3.6) below maps directly to the controlled contract.
 * Nothing outside that contract is added and nothing listed is left out.
 *
 *  Route            : /workspace/global-search
 *  Purpose          : Locate permitted people and business records across modules.
 *  Primary users    : All users
 *  View permission  : ux.global-search.view
 *  Data scope       : Only records inside the actor's active organisation,
 *                     campaign, geography, warehouse, queue, assignment or
 *                     explicit record scope.
 *  Primary action   : Search
 *  History rule     : Delete is available only for an unused draft with no
 *                     downstream reference; otherwise use the domain lifecycle
 *                     action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows;
 *                     calm-blue information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.3.4, plus the settled "ready" surface. */
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

/** Effective permission set decided server-side; the client mirrors it (4.3.3, 4.3.7). */
interface EffectivePermissions {
  readonly view: boolean; // ux.global-search.view
  readonly search: boolean; // ux.global-search.search
  readonly filter: boolean; // ux.global-search.filter
  readonly openResult: boolean; // ux.global-search.open-result
}

/** A Record-type facet — the "Record type" searchable controlled choice (4.3.2). */
interface RecordTypeTab {
  readonly key: string;
  readonly label: string;
  readonly count: number | null; // null = the Modules menu entry (no single count)
  readonly isMenu?: boolean;
}

/** A Module facet — the "Module" searchable controlled choice (4.3.2). */
interface ModuleFacet {
  readonly value: string;
  readonly label: string;
  readonly count: number;
}

/** A "Created by" refine facet (part of Context and filters, 4.3.1). */
interface CreatedByFacet {
  readonly value: string;
  readonly count: number;
}

/**
 * A record result row. Every visible cell is a read-only field from 4.3.2:
 * Business reference, Primary label, Owner or scope, Result type, plus an
 * optional Masked secondary information value.
 */
interface RecordResult {
  readonly reference: string; // Business reference
  readonly primaryLabel: string; // Primary label
  readonly ownerScope: string; // Owner or scope
  readonly amount: string; // business value shown on the row
  readonly resultType: string; // Result type (badge text)
  readonly tone: 'success' | 'info' | 'accent'; // Result type badge tone
  readonly maskedSecondary?: string; // Masked secondary information (privacy)
}

/** A task result row. resultType is the status/priority badge (4.3.2). */
interface TaskResult {
  readonly primaryLabel: string;
  readonly ownerScope: string;
  readonly resultType: string;
  readonly tone: 'high' | 'medium' | 'low';
}

/** A document result row. */
interface DocumentResult {
  readonly primaryLabel: string;
  readonly ownerScope: string;
  readonly kind: 'pdf' | 'psd';
}

/** A person result card — Primary label, Owner or scope, masked contact (4.3.2). */
interface PersonResult {
  readonly name: string;
  readonly role: string;
  readonly scope: string;
  readonly initials: string;
  readonly maskedContact: string; // Masked secondary information (privacy)
}

/** A search-help tip — the shell "help" content (4.3.1 Application shell). */
interface HelpTip {
  readonly icon: string;
  readonly title: string;
  readonly detail: string;
}

/** A recent search — Related and history chronology (4.3.1). */
interface RecentSearch {
  readonly term: string;
  readonly when: string;
}

@Component({
  selector: 'app-global-search',
  imports: [CommonModule, FormsModule],
  templateUrl: './global-search.html',
  styleUrl: './global-search.css',
})
export class GlobalSearchComponent {
  // ===================================================================
  // Task header (4.3.1) — Global search title, stable reference where
  // applicable, lifecycle state, owner, freshness and one primary action.
  // ===================================================================
  protected readonly pageTitle = 'Global Search';
  protected readonly pageSubtitle =
    'Search across YDot to find people, records, documents, tasks and more.';
  protected readonly stableReference = signal('GS-2026-0087');
  protected readonly lifecycleState = signal('Active');
  protected readonly owner = signal('Sophie Bennett · Executive Sponsor');
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('29 Jul 2026, 09:30 IST');

  /**
   * Active data scope (4.3.1 Application shell / Context and filters). Totals
   * are qualified by this scope and the last refresh time.
   */
  protected readonly activeScope = signal('South Zone · All Modules · FY 2026-27');

  /** Effective permissions; every action is gated by these (4.3.3, 4.3.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    search: true,
    filter: true,
    openResult: true,
  };

  // ===================================================================
  // Context and filters (4.3.1) — Search text field (4.3.2).
  // ===================================================================
  protected readonly searchText = signal('donation drive');

  /** Saved filter (4.3.1 Context and filters). */
  protected readonly savedFilters = signal<readonly string[]>([
    'None',
    'My donation drives',
    'Open high-value records',
    'This week across modules',
  ]);
  protected readonly savedFilter = signal('None');
  /** Save the current refine selection as a reusable saved filter (4.3.1). */
  protected saveCurrentFilter(): void {
    const name = `Saved filter ${this.savedFilters().length}`;
    this.savedFilters.update((list) => (list.includes(name) ? list : [...list, name]));
    this.savedFilter.set(name);
  }

  // ===================================================================
  // Record type (4.3.2) — searchable controlled choice, shown as the tabs.
  // ===================================================================
  protected readonly recordTypeTabs: readonly RecordTypeTab[] = [
    { key: 'all', label: 'All', count: null },
    { key: 'records', label: 'Records', count: 128 },
    { key: 'people', label: 'People', count: 24 },
    { key: 'tasks', label: 'Tasks', count: 36 },
    { key: 'documents', label: 'Documents', count: 56 },
    { key: 'campaigns', label: 'Campaigns', count: 14 },
    { key: 'announcements', label: 'Announcements', count: 8 },
    { key: 'modules', label: 'Modules', count: null, isMenu: true },
  ];
  protected readonly activeTab = signal('all');

  // ===================================================================
  // Module (4.3.2) — searchable controlled choice, shown in Refine results.
  // ===================================================================
  protected readonly moduleFacets: readonly ModuleFacet[] = [
    { value: 'Donations', label: 'Donations', count: 128 },
    { value: 'Fundraising', label: 'Fundraising', count: 96 },
    { value: 'Beneficiaries', label: 'Beneficiaries', count: 74 },
    { value: 'Inventory', label: 'Inventory', count: 18 },
    { value: 'Expenses', label: 'Expenses', count: 12 },
  ];
  protected readonly allModulesSelected = signal(true);
  protected readonly selectedModules = signal<Set<string>>(new Set());

  // ===================================================================
  // Created by (Context and filters refine facet, 4.3.1).
  // ===================================================================
  protected readonly createdByQuery = signal('');
  protected readonly createdByFacets: readonly CreatedByFacet[] = [
    { value: 'Sophie Bennett', count: 48 },
    { value: 'Arun Kumar', count: 32 },
    { value: 'Meera Nair', count: 21 },
    { value: 'John Paul', count: 16 },
  ];
  protected readonly allUsersSelected = signal(true);
  protected readonly selectedUsers = signal<Set<string>>(new Set());

  /** Date range refine facet (Context and filters, 4.3.1). */
  protected readonly dateRanges: readonly string[] = [
    'Last 30 days',
    'Last 7 days',
    'Last 90 days',
    'This financial year',
    'All time',
  ];
  protected readonly dateRange = signal('Last 30 days');

  // ===================================================================
  // Status (4.3.2) — search-select / radio decision / status badge.
  // Collapsible refine section, matching the supplied design.
  // ===================================================================
  protected readonly statusOptions: readonly string[] = [
    'Any status',
    'Completed',
    'In Progress',
    'Received',
  ];
  protected readonly statusFilter = signal('Any status');
  protected readonly statusOpen = signal(false);

  /** Tags refine facet (Context and filters). */
  protected readonly tagOptions: readonly string[] = ['#donation-drive', '#education', '#corporate', '#july'];
  protected readonly selectedTags = signal<Set<string>>(new Set());
  protected readonly tagsOpen = signal(false);

  // ===================================================================
  // Results sort + view mode (Main work presentation, 4.3.1).
  // ===================================================================
  protected readonly sortOptions: readonly string[] = ['Relevance', 'Last updated', 'Reference'];
  protected readonly sortBy = signal('Relevance');
  protected readonly viewMode = signal<'list' | 'grid'>('list');

  // ===================================================================
  // Main work (4.3.1) — permitted results grouped by record type.
  // Each cell is a read-only field from 4.3.2.
  // ===================================================================
  protected readonly recordResults: readonly RecordResult[] = [
    {
      reference: 'DONATION-2026-0921',
      primaryLabel: 'Corporate Donation from GreenSol India Pvt Ltd',
      ownerScope: 'Donations · Created 18 Jul 2026 · Arun Kumar',
      amount: '₹12,36,500',
      resultType: 'Completed',
      tone: 'success',
      maskedSecondary: 'Donor PAN ••••••••',
    },
    {
      reference: 'DONATION-DRIVE-2026-0456',
      primaryLabel: 'Education Support Donation Drive',
      ownerScope: 'Donations · Created 15 Jul 2026 · Meera Nair',
      amount: '₹3,45,000',
      resultType: 'In Progress',
      tone: 'info',
    },
    {
      reference: 'DONATION-2026-0765',
      primaryLabel: 'Individual Donation Drive - July',
      ownerScope: 'Donations · Created 12 Jul 2026 · John Paul',
      amount: '₹75,600',
      resultType: 'Received',
      tone: 'accent',
    },
  ];

  protected readonly taskResults: readonly TaskResult[] = [
    {
      primaryLabel: 'Verify documents for donation drive DONATION-DRIVE-2026-0456',
      ownerScope: 'Created by Meera Nair · Due 24 Jul 2026',
      resultType: 'High Priority',
      tone: 'high',
    },
    {
      primaryLabel: 'Approve donation drive expense reimbursement',
      ownerScope: 'Created by Arun Kumar · Due 22 Jul 2026',
      resultType: 'Medium Priority',
      tone: 'medium',
    },
    {
      primaryLabel: 'Follow up with donor for donation drive - July',
      ownerScope: 'Created by John Paul · Due 20 Jul 2026',
      resultType: 'Low Priority',
      tone: 'low',
    },
  ];

  protected readonly documentResults: readonly DocumentResult[] = [
    {
      primaryLabel: 'Donation Drive Guidelines 2026.pdf',
      ownerScope: 'Documents · Updated 17 Jul 2026 · 1.2 MB',
      kind: 'pdf',
    },
    {
      primaryLabel: 'Donation Drive Impact Report - Q2 2026.pdf',
      ownerScope: 'Documents · Updated 16 Jul 2026 · 2.4 MB',
      kind: 'pdf',
    },
    {
      primaryLabel: 'Donation Drive Banner - Editable.psd',
      ownerScope: 'Documents · Updated 12 Jul 2026 · 18.7 MB',
      kind: 'psd',
    },
  ];

  protected readonly peopleResults: readonly PersonResult[] = [
    { name: 'Arun Kumar', role: 'Campaign Manager', scope: 'Donations', initials: 'AK', maskedContact: '+91 ••••• ••210' },
    { name: 'Meera Nair', role: 'Fundraising Lead', scope: 'Fundraising', initials: 'MN', maskedContact: '+91 ••••• ••884' },
    { name: 'John Paul', role: 'Operations Executive', scope: 'Donations', initials: 'JP', maskedContact: '+91 ••••• ••037' },
    { name: 'Ritika Sharma', role: 'Volunteer Coordinator', scope: 'Community', initials: 'RS', maskedContact: '+91 ••••• ••156' },
  ];

  // ===================================================================
  // Application shell help (4.3.1) — Search help tips.
  // ===================================================================
  protected readonly helpTips: readonly HelpTip[] = [
    { icon: 'quote', title: 'Use quotes for exact match', detail: '"donation drive"' },
    { icon: 'star', title: 'Use * for wildcard search', detail: 'donation*' },
    { icon: 'filter', title: 'Use filters to narrow results', detail: 'Select module, date, status' },
    { icon: 'enter', title: 'Press Enter to search', detail: 'Quick and easy' },
  ];

  // ===================================================================
  // Related and history (4.3.1) — recent searches chronology.
  // ===================================================================
  protected readonly recentSearches = signal<RecentSearch[]>([
    { term: 'donation drive', when: 'Just now' },
    { term: 'beneficiary verification', when: '2 hours ago' },
    { term: 'stock allocation', when: 'Yesterday' },
    { term: 'campaign report', when: '2 days ago' },
    { term: 'expense reimbursement', when: '3 days ago' },
  ]);

  // ===================================================================
  // UI state demonstrability (4.3.4 / 4.3.7).
  // ===================================================================
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

  /** Validation summary entries shown in the Validation state (4.3.4 / 4.3.6). */
  protected readonly validationErrors = signal<Array<{ field: string; message: string }>>([]);

  // ===================================================================
  // High-risk confirmation (4.3.6) — Confirm Search dialog.
  // ===================================================================
  protected readonly confirmSearchOpen = signal(false);

  // ===================================================================
  // Decision / review (4.3.1) — populated when a result is opened (4.3.3).
  // Before-and-after values, warnings, effective permission, evidence,
  // reason and resulting state.
  // ===================================================================
  protected readonly openedResult = signal<{
    reference: string;
    label: string;
    effectivePermission: string;
    beforeValue: string;
    afterValue: string;
    warning: string;
    evidence: string;
    reason: string;
    resultingState: string;
  } | null>(null);

  // ===================================================================
  // Persistent outcome (4.3.1) — reference, state, effective time,
  // downstream status, accountable owner and next action.
  // ===================================================================
  protected readonly persistentOutcome = signal<{
    reference: string;
    state: string;
    effectiveTime: string;
    downstreamStatus: string;
    accountableOwner: string;
    nextAction: string;
  } | null>(null);

  // ===================================================================
  // Derived — totals qualified by scope and last refresh (4.3.1).
  // ===================================================================
  protected readonly totalResults = computed(() =>
    this.recordTypeTabs.reduce((sum, t) => sum + (t.count ?? 0), 0),
  );

  /** Active-filter summary chips (4.3.1 Context and filters). */
  protected readonly activeFilters = computed<Array<{ key: string; label: string }>>(() => {
    const chips: Array<{ key: string; label: string }> = [];
    if (this.searchText().trim()) {
      chips.push({ key: 'q', label: `"${this.searchText().trim()}"` });
    }
    if (this.activeTab() !== 'all') {
      const tab = this.recordTypeTabs.find((t) => t.key === this.activeTab());
      if (tab) {
        chips.push({ key: 'type', label: `Type: ${tab.label}` });
      }
    }
    if (!this.allModulesSelected()) {
      for (const m of this.selectedModules()) {
        chips.push({ key: `mod:${m}`, label: `Module: ${m}` });
      }
    }
    if (this.dateRange() !== 'All time') {
      chips.push({ key: 'date', label: this.dateRange() });
    }
    if (!this.allUsersSelected()) {
      for (const u of this.selectedUsers()) {
        chips.push({ key: `user:${u}`, label: `Created by: ${u}` });
      }
    }
    if (this.statusFilter() !== 'Any status') {
      chips.push({ key: 'status', label: `Status: ${this.statusFilter()}` });
    }
    for (const t of this.selectedTags()) {
      chips.push({ key: `tag:${t}`, label: t });
    }
    return chips;
  });

  /** Whether the primary Search action is currently allowed (4.3.1, 4.3.3). */
  protected readonly searchAllowed = computed(
    () => this.permissions.search && this.uiState() !== 'no-access',
  );

  protected readonly filteredCreatedBy = computed(() => {
    const q = this.createdByQuery().trim().toLowerCase();
    if (!q) {
      return this.createdByFacets;
    }
    return this.createdByFacets.filter((f) => f.value.toLowerCase().includes(q));
  });

  // ===================================================================
  // Behaviour — UI state switcher (4.3.4 / 4.3.7)
  // ===================================================================
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
    // The Success state must always present the persistent outcome (4.3.4); seed
    // a default when it is reached from the state switcher without a prior action.
    if (state === 'success' && !this.persistentOutcome()) {
      this.persistentOutcome.set({
        reference: this.stableReference(),
        state: 'Search completed',
        effectiveTime: this.lastRefresh(),
        downstreamStatus: `${this.totalResults()} results within ${this.activeScope()}`,
        accountableOwner: this.owner(),
        nextAction: 'Open a result or refine the filters',
      });
    }
  }

  // ----- Search text -----
  protected updateSearchText(value: string): void {
    this.searchText.set(value);
  }

  protected clearSearchText(): void {
    this.searchText.set('');
  }

  // ===================================================================
  // Actions, eligibility and result (4.3.3)
  // ===================================================================

  /**
   * Search — Primary action (4.3.3). Treated as a high-risk action (4.3.6):
   * the actor confirms the affected record, consequence, reason and effective
   * time before the search commits.
   */
  protected search(): void {
    if (!this.searchAllowed()) {
      return;
    }
    // Required-field validation before committing (4.3.6).
    if (!this.searchText().trim()) {
      this.validationErrors.set([{ field: 'Search text', message: 'Enter Search text.' }]);
      this.uiState.set('validation');
      return;
    }
    this.validationErrors.set([]);
    this.confirmSearchOpen.set(true);
  }

  protected cancelSearch(): void {
    this.confirmSearchOpen.set(false);
  }

  /** Commit the confirmed Search — produces the persistent success outcome (4.3.4 / 4.3.6). */
  protected confirmSearch(): void {
    this.confirmSearchOpen.set(false);
    this.persistentOutcome.set({
      reference: this.stableReference(),
      state: 'Search completed',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: `${this.totalResults()} results within ${this.activeScope()}`,
      accountableOwner: this.owner(),
      nextAction: 'Open a result or refine the filters',
    });
    this.uiState.set('success');
  }

  /** Filter type — Filter action (4.3.3). Re-qualifies totals by scope. */
  protected selectTab(key: string): void {
    if (!this.permissions.filter) {
      return;
    }
    this.activeTab.set(key);
  }

  protected toggleAllModules(): void {
    this.allModulesSelected.set(true);
    this.selectedModules.set(new Set());
  }

  protected toggleModule(value: string): void {
    if (!this.permissions.filter) {
      return;
    }
    const next = new Set(this.selectedModules());
    if (next.has(value)) {
      next.delete(value);
    } else {
      next.add(value);
    }
    this.selectedModules.set(next);
    this.allModulesSelected.set(next.size === 0);
  }

  protected toggleAllUsers(): void {
    this.allUsersSelected.set(true);
    this.selectedUsers.set(new Set());
  }

  protected toggleUser(value: string): void {
    if (!this.permissions.filter) {
      return;
    }
    const next = new Set(this.selectedUsers());
    if (next.has(value)) {
      next.delete(value);
    } else {
      next.add(value);
    }
    this.selectedUsers.set(next);
    this.allUsersSelected.set(next.size === 0);
  }

  protected toggleTag(value: string): void {
    if (!this.permissions.filter) {
      return;
    }
    const next = new Set(this.selectedTags());
    if (next.has(value)) {
      next.delete(value);
    } else {
      next.add(value);
    }
    this.selectedTags.set(next);
  }

  protected removeActiveFilter(key: string): void {
    if (key === 'q') {
      this.searchText.set('');
    } else if (key === 'type') {
      this.activeTab.set('all');
    } else if (key === 'date') {
      this.dateRange.set('All time');
    } else if (key === 'status') {
      this.statusFilter.set('Any status');
    } else if (key.startsWith('mod:')) {
      this.toggleModule(key.slice(4));
    } else if (key.startsWith('user:')) {
      this.toggleUser(key.slice(5));
    } else if (key.startsWith('tag:')) {
      this.toggleTag(key.slice(4));
    }
  }

  /** Clear every active filter (4.3.1 — clearing is explicit and predictable). */
  protected clearAllFilters(): void {
    this.activeTab.set('all');
    this.allModulesSelected.set(true);
    this.selectedModules.set(new Set());
    this.dateRange.set('All time');
    this.createdByQuery.set('');
    this.allUsersSelected.set(true);
    this.selectedUsers.set(new Set());
    this.statusFilter.set('Any status');
    this.selectedTags.set(new Set());
  }

  protected toggleStatusSection(): void {
    this.statusOpen.update((v) => !v);
  }

  protected toggleTagsSection(): void {
    this.tagsOpen.update((v) => !v);
  }

  // ----- Refine facet section collapse (Context and filters, 4.3.1) -----
  protected readonly moduleOpen = signal(true);
  protected readonly dateOpen = signal(true);
  protected readonly createdByOpen = signal(true);
  protected toggleModuleSection(): void {
    this.moduleOpen.update((v) => !v);
  }
  protected toggleDateSection(): void {
    this.dateOpen.update((v) => !v);
  }
  protected toggleCreatedBySection(): void {
    this.createdByOpen.update((v) => !v);
  }

  // ----- Show more / less within a facet -----
  private readonly facetPreview = 3;
  protected readonly modulesExpanded = signal(false);
  protected readonly usersExpanded = signal(false);
  protected readonly visibleModuleFacets = computed(() =>
    this.modulesExpanded() ? this.moduleFacets : this.moduleFacets.slice(0, this.facetPreview),
  );
  protected readonly visibleCreatedBy = computed(() => {
    const list = this.filteredCreatedBy();
    return this.usersExpanded() ? list : list.slice(0, this.facetPreview);
  });
  protected toggleModulesExpanded(): void {
    this.modulesExpanded.update((v) => !v);
  }
  protected toggleUsersExpanded(): void {
    this.usersExpanded.update((v) => !v);
  }

  protected setViewMode(mode: 'list' | 'grid'): void {
    this.viewMode.set(mode);
  }

  /**
   * Open result — Open result action (4.3.3). Opens the Decision / review
   * region (before-and-after, effective permission, evidence, reason,
   * resulting state) and records the persistent outcome (4.3.1).
   */
  protected openRecord(result: RecordResult): void {
    if (!this.permissions.openResult) {
      return;
    }
    this.openedResult.set({
      reference: result.reference,
      label: result.primaryLabel,
      effectivePermission: 'View · within your active scope',
      beforeValue: '—',
      afterValue: result.resultType,
      warning: 'Opening a record is read-only; no downstream effect is committed.',
      evidence: `${result.ownerScope} · Amount ${result.amount}`,
      reason: 'Actor opened the result from Global search',
      resultingState: result.resultType,
    });
    this.persistentOutcome.set({
      reference: result.reference,
      state: result.resultType,
      effectiveTime: this.lastRefresh(),
      downstreamStatus: 'No downstream change; record opened read-only',
      accountableOwner: result.ownerScope,
      nextAction: 'Continue in the record or return to results',
    });
  }

  protected closeOpenedResult(): void {
    this.openedResult.set(null);
  }

  // ----- Recent searches (Related and history, 4.3.1) -----
  protected applyRecentSearch(term: string): void {
    this.searchText.set(term);
  }

  protected clearRecentSearches(): void {
    this.recentSearches.set([]);
  }
}
