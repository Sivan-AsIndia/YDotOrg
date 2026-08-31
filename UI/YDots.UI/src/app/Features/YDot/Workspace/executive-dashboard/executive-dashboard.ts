import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * SCR-UX-002 — Executive dashboard.
 *
 * Faithful implementation of section 4.2 of the YDot Practical UI/UX Generation
 * Specification. Every region (4.2.1), field (4.2.2), action (4.2.3), UI state
 * (4.2.4) and validation/confirmation pattern (4.2.6) below maps directly to the
 * controlled contract. No content or behaviour outside that contract is added.
 *
 *  Route            : /workspace/executive-dashboard
 *  Purpose          : Outcome-level trends with drill-down and freshness evidence.
 *  Primary user     : Executive Sponsor
 *  View permission  : ux.executive-dashboard.view
 *  Primary action   : Filter
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.2.4, plus the settled "ready" surface. */
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

/** Effective permission set for the acting Executive Sponsor (4.2.3). */
interface EffectivePermissions {
  readonly view: boolean; // ux.executive-dashboard.view
  readonly exportAuthorisedSummary: boolean; // ux.executive-dashboard.export-authorised-summary
}

/** A scope-aware selector option with a stable reference and disambiguating context (4.2.2). */
interface ScopeOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

/** A read-only, server-derived outcome metric with its own definition and drill-down (4.2.2 + 4.2.1 Main work). */
interface OutcomeMetric {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  readonly qualifier: string;
  readonly definition: string;
  readonly drillLabel: string;
}

/** A single day on the outcome-level trend line, with a drill-down affordance. */
interface TrendPoint {
  readonly date: string;
  readonly donations: number; // ₹ Lakh, left axis 0–10
  readonly beneficiaries: number; // count, right axis 0–5000
}

/** A tab in the Related and history region; each carries its own permission/scope flag (4.2.1). */
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
  selector: 'app-executive-dashboard',
  imports: [CommonModule, FormsModule],
  templateUrl: './executive-dashboard.html',
  styleUrl: './executive-dashboard.css',
})
export class ExecutiveDashboardComponent {
  // ----- Task header (4.2.1) -----
  protected readonly pageTitle = 'Executive dashboard';
  protected readonly stableReference = 'EXEC-DASH-2026-0001';
  protected readonly lifecycleState = 'Published';
  protected readonly owner = 'Priya Nair · Executive Sponsor';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Effective permissions decided server-side; the client mirrors the same decision (4.2.3, 4.2.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    exportAuthorisedSummary: true,
  };

  /** Lifecycle states in which the protected export is permitted (4.2.3). */
  private readonly exportPermittedStates = ['Published'];

  // ----- Context and filters (4.2.1 + 4.2.2) -----

  /** Date range — operating time zone; impossible ranges rejected before submit (4.2.2). */
  protected readonly rangeStart = signal('2026-07-19');
  protected readonly rangeEnd = signal('2026-07-29');

  /** Organisation or campaign scope — scope-aware searchable selector with identity preview (4.2.2). */
  protected readonly scopeOptions: readonly ScopeOption[] = [
    { reference: 'ORG-0001', name: 'YDot Foundation', context: 'All organisations · National' },
    { reference: 'CAMP-2026-0178', name: 'Blind-Stick Distribution Drive', context: 'Campaign · Maharashtra' },
    { reference: 'CAMP-2026-0134', name: 'Education Support Program', context: 'Campaign · Tamil Nadu' },
    { reference: 'CAMP-2026-0091', name: 'Healthcare Aid Initiative', context: 'Campaign · Karnataka' },
  ];
  protected readonly scopeQuery = signal('');
  protected readonly activeScope = signal<ScopeOption>(this.scopeOptions[0]);
  protected readonly scopePickerOpen = signal(false);

  /** Eligible scope options filtered by the search query (server-side in production; 4.2.2). */
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

  /** Comparison period — search / filter control (4.2.2). */
  protected readonly comparisonOptions = [
    'Previous 10 days',
    'Previous period',
    'Same period last year',
    'No comparison',
  ];
  protected readonly comparisonPeriod = signal(this.comparisonOptions[0]);

  /** Saved filters and the active-filter summary (4.2.1 Context and filters). */
  protected readonly savedFilters = ['My default view', 'Board pack — monthly', 'Exceptions focus'];
  protected readonly savedFilter = signal(this.savedFilters[0]);

  /** Free-text search across the outcome scope (4.2.1 Context and filters). */
  protected readonly searchTerm = signal('');

  /** Human-readable, interpreted date shown before submit (4.2.2 Date range). */
  protected readonly interpretedRange = computed(
    () => `${this.formatDate(this.rangeStart())} – ${this.formatDate(this.rangeEnd())} · ${this.operatingTimeZone}`,
  );

  /** True when the range is impossible (end before start); blocks submit (4.2.2, 4.2.6 invalid value). */
  protected readonly rangeInvalid = computed(() => new Date(this.rangeEnd()) < new Date(this.rangeStart()));

  /** Default range values — used to tell whether the range filter is non-default. */
  private readonly defaultRangeStart = '2026-07-19';
  private readonly defaultRangeEnd = '2026-07-29';

  /**
   * Active-filter summary chips — only filters that differ from their default are
   * shown, so "Clear filters" visibly removes every chip (4.2.1).
   */
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.activeScope().reference !== this.scopeOptions[0].reference) {
      chips.push({ key: 'scope', label: `Scope: ${this.activeScope().name}` });
    }
    if (this.rangeStart() !== this.defaultRangeStart || this.rangeEnd() !== this.defaultRangeEnd) {
      chips.push({ key: 'range', label: `Range: ${this.formatDate(this.rangeStart())} – ${this.formatDate(this.rangeEnd())}` });
    }
    if (this.comparisonPeriod() !== this.comparisonOptions[0]) {
      chips.push({ key: 'comparison', label: `Compare: ${this.comparisonPeriod()}` });
    }
    const term = this.appliedSearchTerm();
    if (term) {
      chips.push({ key: 'search', label: `Search: “${term}”` });
    }
    return chips;
  });

  // ----- Main work: outcome-level trends + freshness evidence (4.2.1 + 4.2.2) -----

  /** Last refresh time — server-derived, read-only freshness evidence (4.2.2). */
  protected readonly lastRefresh = signal('29 Jul 2026, 09:30 IST');

  /** Totals qualified by scope and last refresh (4.2.1 Context and filters). */
  protected readonly totalsCaption = computed(
    () => `Totals for ${this.activeScope().name} · as of ${this.lastRefresh()}`,
  );

  /** The read-only outcome metrics (4.2.2). All values are server-derived and immutable in this view. */
  protected readonly metrics: readonly OutcomeMetric[] = [
    {
      key: 'reconciled-collection',
      label: 'Reconciled collection',
      value: '₹48.72 L',
      qualifier: 'Donations reconciled against settlement in scope',
      definition: 'Sum of donations reconciled against bank settlement within the selected range and effective scope.',
      drillLabel: 'Drill into reconciled donations',
    },
    {
      key: 'active-campaigns',
      label: 'Active campaigns',
      value: '18',
      qualifier: 'Campaigns in an active lifecycle state',
      definition: 'Count of campaigns currently in an Active or In-Progress lifecycle state within effective scope.',
      drillLabel: 'Drill into active campaigns',
    },
    {
      key: 'eligible-beneficiaries',
      label: 'Eligible beneficiaries',
      value: '24,582',
      qualifier: 'Verified and eligible in scope',
      definition: 'Distinct beneficiaries holding a verified, eligible status within the selected effective scope.',
      drillLabel: 'Drill into eligible beneficiaries',
    },
    {
      key: 'available-stock',
      label: 'Available stock',
      value: '3,842',
      qualifier: 'Blind sticks available to distribute',
      definition: 'On-hand stock units available for allocation across warehouses inside effective scope.',
      drillLabel: 'Drill into available stock',
    },
    {
      key: 'acknowledged-distributions',
      label: 'Acknowledged distributions',
      value: '2,914',
      qualifier: 'Acknowledged by beneficiaries in range',
      definition: 'Distributions carrying a signed or OTP-confirmed acknowledgement within the selected range.',
      drillLabel: 'Drill into acknowledged distributions',
    },
    {
      key: 'open-exceptions',
      label: 'Open exceptions',
      value: '7',
      qualifier: 'Unresolved and awaiting review',
      definition: 'Reconciliation, stock and verification exceptions still open at the last refresh in scope.',
      drillLabel: 'Drill into open exceptions',
    },
  ];

  /** The currently expanded metric definition (progressive disclosure; 4.2.1 Main work). */
  protected readonly openDefinition = signal<string | null>(null);

  /**
   * Search term as of the last time Filter was actually applied (4.2.3). Kept
   * separate from `searchTerm` so typing alone never changes the result set —
   * only the explicit primary action does.
   */
  protected readonly appliedSearchTerm = signal('');

  /** Outcome metrics narrowed by the applied search term (4.2.1 Main work + 4.2.3 Filter). */
  protected readonly filteredMetrics = computed(() => {
    const term = this.appliedSearchTerm().trim().toLowerCase();
    if (!term) {
      return this.metrics;
    }
    return this.metrics.filter(
      (m) => m.label.toLowerCase().includes(term) || m.qualifier.toLowerCase().includes(term),
    );
  });

  /** Outcome-level trend series over the selected range (4.2.1 Main work). */
  protected readonly trend: readonly TrendPoint[] = [
    { date: '19 Jul', donations: 3.1, beneficiaries: 1550 },
    { date: '20 Jul', donations: 4.6, beneficiaries: 1900 },
    { date: '21 Jul', donations: 5.4, beneficiaries: 2100 },
    { date: '22 Jul', donations: 5.9, beneficiaries: 2050 },
    { date: '23 Jul', donations: 7.0, beneficiaries: 2200 },
    { date: '24 Jul', donations: 7.82, beneficiaries: 3120 },
    { date: '25 Jul', donations: 6.5, beneficiaries: 2050 },
    { date: '26 Jul', donations: 6.1, beneficiaries: 2150 },
    { date: '27 Jul', donations: 7.0, beneficiaries: 2300 },
    { date: '28 Jul', donations: 6.6, beneficiaries: 2250 },
    { date: '29 Jul', donations: 7.7, beneficiaries: 2750 },
  ];

  /** Whether the comparison series is overlaid on the trend (toggled by the Compare action; 4.2.3). */
  protected readonly comparisonShown = signal(false);

  /** Index of the point currently focused for drill-down / freshness read-out. */
  protected readonly focusedPoint = signal(5);

  // ----- Related and history (4.2.1). Each tab is independently permission/scope gated. -----
  protected readonly relatedTabs: readonly RelatedTab[] = [
    {
      key: 'linked',
      label: 'Linked records',
      permitted: true,
      rows: [
        { primary: 'CAMP-2026-0178', secondary: 'Blind-Stick Distribution Drive', meta: 'Campaign · Active' },
        { primary: 'STOCK-2026-0267', secondary: 'Blind Stick Stock', meta: 'Warehouse · Maharashtra' },
      ],
    },
    {
      key: 'documents',
      label: 'Documents',
      permitted: true,
      rows: [
        { primary: 'Board pack — July 2026', secondary: 'PDF · 2.1 MB', meta: 'Generated 29 Jul 2026' },
        { primary: 'Reconciliation summary', secondary: 'PDF · 640 KB', meta: 'Generated 29 Jul 2026' },
      ],
    },
    {
      key: 'activity',
      label: 'Activity',
      permitted: true,
      rows: [
        { primary: 'Filter applied', secondary: 'Scope narrowed to Maharashtra', meta: 'P. Nair · 09:32 IST' },
        { primary: 'Data refreshed', secondary: 'Outcome metrics recomputed', meta: 'System · 09:30 IST' },
      ],
    },
    {
      key: 'integration',
      label: 'Integration status',
      permitted: true,
      rows: [
        { primary: 'Settlement feed', secondary: 'Healthy', meta: 'Last sync 09:28 IST' },
        { primary: 'Warehouse stock feed', secondary: 'Degraded', meta: 'Retrying · corr. INT-77213' },
      ],
    },
    {
      key: 'support',
      label: 'Support correlation',
      permitted: true,
      rows: [{ primary: 'INT-77213', secondary: 'Stock feed retry', meta: 'Open · linked to dependency failure' }],
    },
    {
      key: 'audit',
      label: 'Audit chronology',
      permitted: true,
      rows: [
        { primary: 'View opened', secondary: 'ux.executive-dashboard.view granted', meta: 'P. Nair · 09:31 IST' },
        { primary: 'Export requested', secondary: 'Pending confirmation', meta: 'P. Nair · 09:34 IST' },
      ],
    },
  ];

  /** Only tabs the actor is permitted to see are offered; unavailable ones are omitted (4.2.1). */
  protected readonly visibleRelatedTabs = computed(() => this.relatedTabs.filter((t) => t.permitted));
  protected readonly activeRelatedTab = signal<string>('linked');
  protected readonly activeRelatedRows = computed(
    () => this.visibleRelatedTabs().find((t) => t.key === this.activeRelatedTab())?.rows ?? [],
  );

  // ----- Persistent outcome (4.2.1). A toast may support but never replace this. -----
  // A signal (not a static object) so the export and filter actions can update it
  // with a genuine, observable result instead of only logging to the console.
  protected readonly persistentOutcome = signal({
    reference: 'EXEC-DASH-2026-0001',
    state: 'Published',
    effectiveTime: '29 Jul 2026, 09:30 IST',
    downstreamStatus: 'No protected export pending',
    owner: 'Priya Nair · Executive Sponsor',
    nextAction: 'Review 7 open exceptions',
  });

  // ----- UI state demonstrability (4.2.4 / 4.2.7) -----
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

  // ----- Decision / review + Export authorised summary confirmation (4.2.1, 4.2.3, 4.2.6) -----
  protected readonly exportDialogOpen = signal(false);
  protected readonly exportConfirmation = {
    classification: 'Official — Sensitive',
    purpose: 'Board reporting pack',
    scope: 'YDot Foundation · 19–29 Jul 2026',
    rowFileCount: '6 metrics · 1 file (PDF)',
    expiry: 'Link expires in 24 hours',
    auditReference: 'AUD-EXP-2026-0442',
  };

  /** Handle for the in-flight simulated filter request, so it can be cancelled. */
  private filterTimeoutId: number | null = null;

  /** Whether the primary action (Filter) is currently allowed (role, permission, scope, state, dependencies; 4.2.1). */
  protected readonly filterAllowed = computed(
    () =>
      this.permissions.view &&
      !this.rangeInvalid() &&
      this.uiState() !== 'no-access' &&
      this.uiState() !== 'loading',
  );

  /** Whether the protected export action is offered (permission + permitted lifecycle state; 4.2.3). */
  protected readonly exportAllowed = computed(
    () => this.permissions.exportAuthorisedSummary && this.exportPermittedStates.includes(this.lifecycleState),
  );

  // ===== Behaviour =====

  protected selectScope(option: ScopeOption): void {
    this.activeScope.set(option);
    this.scopePickerOpen.set(false);
    this.scopeQuery.set('');
  }

  protected toggleScopePicker(): void {
    this.scopePickerOpen.update((v) => !v);
  }

  protected setComparison(period: string): void {
    this.comparisonPeriod.set(period);
  }

  /** Primary action — Filter. Server-side filtering; refreshes only the authorised records in scope (4.2.3). */
  protected applyFilter(): void {
    if (!this.filterAllowed()) {
      return;
    }
    // Close any open pickers so the settled result is what the actor sees next.
    this.scopePickerOpen.set(false);

    // Show that a query round-trip is genuinely happening (4.2.4 loading state).
    this.uiState.set('loading');

    // Wire to the outcome query service when available. In the meantime this
    // applies the filter locally so the region visibly reflects the actor's
    // scope, range, comparison and search choices rather than doing nothing.
    if (this.filterTimeoutId !== null) {
      window.clearTimeout(this.filterTimeoutId);
    }
    this.filterTimeoutId = window.setTimeout(() => {
      this.filterTimeoutId = null;
      const appliedSearch = this.searchTerm().trim();
      this.appliedSearchTerm.set(appliedSearch);
      this.lastRefresh.set(this.formatNow());

      console.info('Filter applied', {
        scope: this.activeScope().reference,
        start: this.rangeStart(),
        end: this.rangeEnd(),
        comparison: this.comparisonPeriod(),
        search: appliedSearch,
      });

      // Zero results from *this* filter/scope combination surface the empty
      // state rather than silently showing a shorter, unexplained grid.
      this.uiState.set(this.filteredMetrics().length === 0 ? 'empty' : 'ready');
    }, 400);
  }

  /** Compare — overlays the comparison period on the outcome trend (4.2.3). */
  protected toggleCompare(): void {
    this.comparisonShown.update((v) => !v);
  }

  /** Drill down — opens the authorised detail for a metric or trend point (4.2.3). */
  protected drillDown(target: string): void {
    console.info('Drill down requested', target);
  }

  protected toggleDefinition(metricKey: string): void {
    this.openDefinition.update((cur) => (cur === metricKey ? null : metricKey));
  }

  /** Clearing a filter is explicit and returns focus predictably (4.2.1 Context and filters). */
  protected clearFilters(): void {
    this.rangeStart.set(this.defaultRangeStart);
    this.rangeEnd.set(this.defaultRangeEnd);
    this.activeScope.set(this.scopeOptions[0]);
    this.comparisonPeriod.set(this.comparisonOptions[0]);
    this.savedFilter.set(this.savedFilters[0]);
    this.searchTerm.set('');
    this.appliedSearchTerm.set('');
    this.comparisonShown.set(false);
    if (this.uiState() === 'empty') {
      this.uiState.set('ready');
    }
  }

  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  protected focusPoint(index: number): void {
    this.focusedPoint.set(index);
  }

  protected setUiState(state: UiState): void {
    if (this.uiState() === 'loading' && state !== 'loading' && this.filterTimeoutId !== null) {
      window.clearTimeout(this.filterTimeoutId);
      this.filterTimeoutId = null;
    }
    this.uiState.set(state);
  }

  /** Open the Export authorised summary review/confirmation (4.2.3 Export → Decision/review). */
  protected openExportDialog(): void {
    if (!this.exportAllowed()) {
      return;
    }
    this.exportDialogOpen.set(true);
  }

  protected cancelExport(): void {
    this.exportDialogOpen.set(false);
  }

  /** Explicit confirmation before the protected output is made available (4.2.3, 4.2.6 high-risk). */
  protected confirmExport(): void {
    console.info('Export authorised summary confirmed', this.exportConfirmation);

    // Produce the actual protected output. Wire this to the export service when
    // available; until then, generate the authorised summary file client-side so
    // the action has a real, verifiable result instead of only a console log.
    const now = this.formatNow();
    const lines = [
      'YDot Foundation — Authorised summary (Board reporting pack)',
      `Classification: ${this.exportConfirmation.classification}`,
      `Scope: ${this.exportConfirmation.scope}`,
      `Generated: ${now}`,
      `Audit reference: ${this.exportConfirmation.auditReference}`,
      '',
      ...this.filteredMetrics().map((m) => `${m.label}: ${m.value} (${m.qualifier})`),
    ];
    this.downloadTextFile(`${this.exportConfirmation.auditReference}.txt`, lines.join('\n'));

    this.persistentOutcome.update((outcome) => ({
      ...outcome,
      effectiveTime: now,
      downstreamStatus: `Authorised summary exported · ${this.exportConfirmation.auditReference}`,
      nextAction: 'Share the authorised summary with the Board',
    }));

    this.exportDialogOpen.set(false);
    this.uiState.set('success');
  }

  /** Triggers a browser download for the generated authorised-summary file. */
  private downloadTextFile(filename: string, content: string): void {
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  /** Current timestamp formatted the same way as the rest of the freshness evidence. */
  private formatNow(): string {
    const d = new Date();
    const datePart = d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
    const timePart = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
    return `${datePart}, ${timePart} IST`;
  }

  // ===== Chart geometry (SVG viewBox 0 0 900 300) =====
  private readonly chartW = 900;
  private readonly chartH = 300;
  private readonly pad = { top: 12, right: 12, bottom: 28, left: 12 };

  private scaleX(i: number): number {
    const usable = this.chartW - this.pad.left - this.pad.right;
    return this.pad.left + (usable * i) / (this.trend.length - 1);
  }

  private scaleDonations(v: number): number {
    const usable = this.chartH - this.pad.top - this.pad.bottom;
    return this.chartH - this.pad.bottom - (usable * v) / 10;
  }

  private scaleBeneficiaries(v: number): number {
    const usable = this.chartH - this.pad.top - this.pad.bottom;
    return this.chartH - this.pad.bottom - (usable * v) / 5000;
  }

  protected readonly donationsPath = computed(() =>
    this.trend
      .map((p, i) => `${i === 0 ? 'M' : 'L'} ${this.scaleX(i).toFixed(1)} ${this.scaleDonations(p.donations).toFixed(1)}`)
      .join(' '),
  );

  protected readonly beneficiariesPath = computed(() =>
    this.trend
      .map(
        (p, i) =>
          `${i === 0 ? 'M' : 'L'} ${this.scaleX(i).toFixed(1)} ${this.scaleBeneficiaries(p.beneficiaries).toFixed(1)}`,
      )
      .join(' '),
  );

  protected donationPoints = computed(() =>
    this.trend.map((p, i) => ({ x: this.scaleX(i), y: this.scaleDonations(p.donations), point: p, index: i })),
  );

  protected beneficiaryPoints = computed(() =>
    this.trend.map((p, i) => ({ x: this.scaleX(i), y: this.scaleBeneficiaries(p.beneficiaries), point: p, index: i })),
  );

  protected readonly focusX = computed(() => this.scaleX(this.focusedPoint()));
  protected readonly focusedTrend = computed(() => this.trend[this.focusedPoint()]);

  private formatDate(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
