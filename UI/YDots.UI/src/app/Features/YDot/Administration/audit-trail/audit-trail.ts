import { CommonModule } from '@angular/common';
import {
  Component,
  OnDestroy,
  OnInit,
  afterRenderEffect,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { AuditSearchFilter, IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { AuditEventResponse } from '../../../../Shared/models/iam-contract.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { ToastService } from '../../../../Shared/services/toast.service';

declare var ApexCharts: any;

/**
 * The server clamps every list endpoint to this many rows per page (see the IAM API's
 * `PaginationRequest`). The summary cards, donut and heatmap are computed from one page fetched
 * at this size rather than a dedicated aggregate endpoint, so on an organisation with more than
 * this many matching events they describe the most recent `STATS_PAGE_SIZE` rather than the
 * organisation's entire history — `isTruncatedForStats` is how the UI stays honest about that.
 */
const STATS_PAGE_SIZE = 100;
const INITIAL_VISIBLE = 5;
const LOAD_MORE_STEP = 5;

/** One cell of the Mon–Sun x time-of-day grid. */
type HeatmapGrid = number[][];

/**
 * The audit trail.
 *
 * READ-ONLY, AND THAT IS THE WHOLE POINT. There is no endpoint behind this screen that writes,
 * edits or deletes an audit event, because a trail that can be corrected is not evidence of
 * anything. Events are written by the handlers themselves, in the same transaction as the change
 * they describe, so an action cannot succeed without leaving a record.
 *
 * TWO THINGS ARE WORTH KNOWING ABOUT WHAT APPEARS HERE
 * ----------------------------------------------------
 * Payloads are REDACTED ON WRITE — password hashes, tokens, secrets and recovery codes never
 * reach the table, so no permission can reveal them on this screen.
 *
 * Detail is GRADED. Without `iam.audit.view-sensitive` the before and after payloads are withheld
 * and only the event envelope is shown. Knowing that a colleague's password was reset is routine;
 * reading the contents of the change is not.
 *
 * EXPORTING IS ITSELF AUDITED, filter included. An unusual export is exactly the kind of thing a
 * later investigation needs to be able to see.
 */
@Component({
  selector: 'app-audit-trail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './audit-trail.html',
  styleUrl: './audit-trail.css',
})
export class AuditTrailComponent implements OnInit, OnDestroy {
  private readonly api = inject(IamAdminApiService);
  private readonly tokens = inject(AuthTokenService);
  private readonly toast = inject(ToastService);

  private readonly destroy$ = new Subject<void>();
  private readonly searchInput$ = new Subject<string>();

  /** The events fetched so far, for the Recent Activity list. Grows as "Load more" is used. */
  readonly events = signal<AuditEventResponse[]>([]);

  /**
   * A frozen snapshot of the first page fetched for the current filter set. The summary cards,
   * donut and heatmap always read from this — not from `events` — so paging further through the
   * list for browsing does not shift numbers that were already shown.
   */
  readonly statsSource = signal<AuditEventResponse[]>([]);

  readonly totalCount = signal(0);
  readonly hasNextPage = signal(false);
  readonly page = signal(1);
  readonly visibleCount = signal(INITIAL_VISIBLE);

  readonly loading = signal(true);
  readonly loadingMore = signal(false);
  readonly loadFailed = signal(false);
  readonly exporting = signal(false);
  readonly errorMessage = signal('');

  /** The row expanded to show its payload, if any. */
  readonly expandedId = signal<string | null>(null);

  // ---- Filters --------------------------------------------------------------------------------
  readonly search = signal('');
  readonly targetType = signal('');
  readonly result = signal('');
  readonly fromDate = signal('');
  readonly toDate = signal('');

  readonly hasFilters = computed(
    () => this.search().trim().length > 0
      || this.targetType() !== ''
      || this.result() !== ''
      || this.fromDate() !== ''
      || this.toDate() !== '');

  readonly canExport = computed(() => this.tokens.hasPermission('iam.audit.export'));
  readonly canSeeDetail = computed(() => this.tokens.hasPermission('iam.audit.view-sensitive'));
  readonly organisationName = computed(() => this.tokens.organisationName());

  /**
   * The record types to offer, fetched from the trail itself.
   *
   * THIS USED TO BE A LITERAL LIST of eleven entity names typed into the component. A hardcoded
   * filter list can only be wrong two ways and is silent in both: a type the platform began
   * writing later could never be filtered for, and a type that had never once occurred was
   * offered as a filter that quietly returns nothing. The server answers from DISTINCT over the
   * caller's own Organisation, so the dropdown always matches what is actually there.
   */
  readonly targetTypes = signal<string[]>([]);

  readonly results = [
    { value: 'succeeded', label: 'Succeeded' },
    { value: 'denied', label: 'Denied' },
    { value: 'failed', label: 'Failed' },
  ];

  // ---- Recent Activity (visible slice + "load more") -------------------------------------------

  readonly visibleEvents = computed(() => this.events().slice(0, this.visibleCount()));

  readonly canLoadMore = computed(
    () => this.visibleCount() < this.events().length || this.hasNextPage());

  // ---- Summary cards, computed from `statsSource` ----------------------------------------------

  /**
   * True once the fetched batch stops short of the trail's real size — the point at which the
   * cards below describe "the most recent N events" rather than the organisation's whole history.
   */
  readonly isTruncatedForStats = computed(() => this.totalCount() > this.statsSource().length);

  readonly statsTotal = computed(() => this.statsSource().length);

  readonly succeededCount = computed(
    () => this.statsSource().filter((e) => e.result === 'succeeded').length);

  /**
   * Everything that isn't a clean success — "failed" and "denied" alike — bucketed together for
   * the summary cards and the donut, which (like the reference layout) show only two outcomes.
   * The event list below keeps the finer-grained three-way badge, since a denial reaching that
   * far still deserves its own colour.
   */
  readonly failedCount = computed(() => this.statsTotal() - this.succeededCount());

  readonly succeededPct = computed(
    () => (this.statsTotal() ? Math.round((this.succeededCount() / this.statsTotal()) * 100) : 0));

  readonly failedPct = computed(
    () => (this.statsTotal() ? 100 - this.succeededPct() : 0));

  readonly uniqueUsersCount = computed(
    () => new Set(this.statsSource().map((e) => this.actorKey(e))).size);

  readonly mostActiveUser = computed(() => {
    const counts = new Map<string, { name: string; count: number }>();

    for (const event of this.statsSource()) {
      const key = this.actorKey(event);
      const existing = counts.get(key);

      if (existing) {
        existing.count++;
      } else {
        counts.set(key, { name: event.actorDisplayName || 'System', count: 1 });
      }
    }

    let best: { name: string; count: number } | null = null;

    for (const entry of counts.values()) {
      if (!best || entry.count > best.count) {
        best = entry;
      }
    }

    return best;
  });

  // ---- Activity heatmap (day-of-week x 6-hour bucket), computed from `statsSource` -------------

  readonly dayLabels = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  readonly timeLabels = ['12 AM', '6 AM', '12 PM', '6 PM'];
  private readonly timeRanges = ['12 AM – 6 AM', '6 AM – 12 PM', '12 PM – 6 PM', '6 PM – 12 AM'];

  /** Rows are the four 6-hour buckets (12am/6am/12pm/6pm); columns are Mon(0)..Sun(6). */
  readonly heatmap = computed<HeatmapGrid>(() => {
    const grid: HeatmapGrid = Array.from({ length: 4 }, () => Array(7).fill(0));

    for (const event of this.statsSource()) {
      if (!event.occurredAtUtc) {
        continue;
      }

      const occurred = new Date(event.occurredAtUtc);
      const mondayFirstDay = (occurred.getDay() + 6) % 7;
      const bucket = Math.min(3, Math.floor(occurred.getHours() / 6));

      grid[bucket][mondayFirstDay]++;
    }

    return grid;
  });

  readonly heatmapMax = computed(
    () => Math.max(1, ...this.heatmap().flatMap((row) => row)));

  /** The busiest 6-hour window across the whole week, for the Quick Insights card. */
  readonly busiestTimeRange = computed(() => {
    const bucketTotals = this.heatmap().map((row) => row.reduce((sum, count) => sum + count, 0));
    let bestIndex = 0;

    for (let i = 1; i < bucketTotals.length; i++) {
      if (bucketTotals[i] > bucketTotals[bestIndex]) {
        bestIndex = i;
      }
    }

    return bucketTotals[bestIndex] > 0 ? this.timeRanges[bestIndex] : null;
  });

  // ---- Outcome donut (ApexCharts, loaded globally via a <script> tag like the dashboard) -------

  private chart: any = null;

  constructor() {
    // `afterRenderEffect` (not a plain `effect`) is what makes this reliable: a plain effect can
    // flush BEFORE Angular has finished patching the DOM for the same signal change, so on the
    // very first load it was finding `succeededCount`/`failedCount` already updated but the
    // template's `#auditOutcomeChart` div not yet attached — the chart was silently never
    // created. `afterRenderEffect` is specifically the reactive primitive that is guaranteed to
    // run after rendering, so the container is always there by the time this runs.
    afterRenderEffect(() => {
      const succeeded = this.succeededCount();
      const failed = this.failedCount();
      this.updateChart(succeeded, failed);
    });
  }

  ngOnInit(): void {
    // Fetched once. A failure here costs the dropdown its options and nothing else, so it must
    // not take the page down with it - the trail itself is the thing somebody came for.
    this.api.getAuditTargetTypes()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (types) => this.targetTypes.set(types),
        error: () => this.targetTypes.set([]),
      });

    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe((term) => {
        this.search.set(term);
        this.load();
      });

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.chart?.destroy();
  }

  /** A fresh load for the current filters: page 1, and a new frozen snapshot for the cards. */
  load(): void {
    this.page.set(1);
    this.visibleCount.set(INITIAL_VISIBLE);
    this.fetchPage(true);
  }

  private fetchPage(reset: boolean): void {
    this.loading.set(reset);
    this.loadingMore.set(!reset);
    this.loadFailed.set(false);

    this.api
      .searchAuditEvents(this.buildFilter())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          const items = result.items ?? [];

          if (reset) {
            this.events.set(items);
            this.statsSource.set(items);
          } else {
            this.events.update((current) => [...current, ...items]);
          }

          this.totalCount.set(result.totalCount ?? 0);
          this.hasNextPage.set(result.hasNextPage ?? false);
          this.loading.set(false);
          this.loadingMore.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadingMore.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The audit trail could not be loaded.'));
        },
      });
  }

  private buildFilter(): AuditSearchFilter {
    return {
      search: this.search().trim() || undefined,
      targetType: this.targetType() || undefined,
      result: this.result() || undefined,

      // A date input gives a local calendar day; the API wants an instant. Taking the start of
      // the chosen day and the END of the chosen day is what makes "from the 1st to the 1st"
      // return that day's events rather than none.
      fromUtc: this.fromDate() ? new Date(this.fromDate() + 'T00:00:00').toISOString() : undefined,
      toUtc: this.toDate() ? new Date(this.toDate() + 'T23:59:59.999').toISOString() : undefined,

      page: this.page(),
      pageSize: STATS_PAGE_SIZE,
    };
  }

  onSearchInput(value: string): void {
    this.searchInput$.next(value);
  }

  applyFilters(): void {
    this.load();
  }

  clearFilters(): void {
    this.search.set('');
    this.targetType.set('');
    this.result.set('');
    this.fromDate.set('');
    this.toDate.set('');
    this.load();
  }

  /**
   * Reveals more of what's already been fetched first; only reaches for the next page over the
   * network once the visible slice has caught up with it.
   */
  loadMore(): void {
    if (this.visibleCount() < this.events().length) {
      this.visibleCount.update((count) => count + LOAD_MORE_STEP);
      return;
    }

    if (this.hasNextPage()) {
      this.page.update((p) => p + 1);
      this.visibleCount.update((count) => count + LOAD_MORE_STEP);
      this.fetchPage(false);
    }
  }

  toggleDetail(id: string | undefined): void {
    if (!id) {
      return;
    }

    this.expandedId.update((current) => (current === id ? null : id));
  }

  export(): void {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);

    this.api
      .exportAuditEvents(this.buildFilter())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);

          const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '');
          this.api.saveBlob(blob, `audit-trail-${stamp}.csv`);

          this.toast.show(
            'Export ready',
            'The file has been downloaded. This export has been recorded in the trail.',
            'success');
        },
        error: (error: unknown) => {
          this.exporting.set(false);
          this.toast.show('Export failed', apiErrorMessage(error), 'error');
        },
      });
  }

  /**
   * The badge colour for an outcome.
   *
   * A denial is not a failure: it is the system working. They are coloured differently because
   * scanning for "something broke" and scanning for "somebody was refused" are different jobs.
   */
  resultClass(result: string | undefined): string {
    switch (result) {
      case 'succeeded': return 'bg-success-subtle text-success';
      case 'denied': return 'bg-warning-subtle text-warning';
      case 'failed': return 'bg-danger-subtle text-danger';
      default: return 'bg-secondary-subtle text-secondary';
    }
  }

  /**
   * The row icon's colour: each action category gets its own colour when it succeeded (a
   * fingerprint is green, a gear is blue, a lock is amber, and so on — the icon says what kind
   * of thing happened), but the moment something didn't go cleanly, the outcome takes over and
   * every icon turns red (failed) or amber (denied) regardless of category — the icon then says
   * something went wrong before it says what kind of thing it was.
   */
  iconTint(actionCode: string | null | undefined, result: string | undefined): string {
    if (result === 'failed') return 'danger';
    if (result === 'denied') return 'warning';
    return this.actionTint(actionCode);
  }

  /** The per-category colour `iconTint` falls back to once an event has succeeded. */
  private actionTint(actionCode: string | null | undefined): string {
    const code = (actionCode || '').toLowerCase();

    if (code.includes('password')) return 'warning';
    if (code.includes('token')) return 'info';
    if (code.includes('reauthenticat') || code.includes('login') || code.includes('auth')) return 'success';
    if (code.includes('role')) return 'primary';
    if (code.includes('export')) return 'primary';
    if (code.includes('delete') || code.includes('remove')) return 'danger';
    if (code.includes('user')) return 'info';
    return 'secondary';
  }

  /** Pretty-prints a payload, falling back to the raw string when it is not JSON. */
  formatPayload(payload: string | null | undefined): string {
    if (!payload) {
      return '';
    }

    try {
      return JSON.stringify(JSON.parse(payload), null, 2);
    } catch {
      return payload;
    }
  }

  /** Icon for an event's action category, read from its machine-readable action code. */
  actionIcon(actionCode: string | null | undefined): string {
    const code = (actionCode || '').toLowerCase();

    if (code.includes('password')) return 'ri-lock-2-line';
    if (code.includes('token')) return 'ri-settings-3-line';
    if (code.includes('reauthenticat') || code.includes('login') || code.includes('auth')) return 'ri-fingerprint-line';
    if (code.includes('role')) return 'ri-shield-user-line';
    if (code.includes('export')) return 'ri-download-2-line';
    if (code.includes('delete') || code.includes('remove')) return 'ri-delete-bin-line';
    if (code.includes('user')) return 'ri-user-add-line';
    return 'ri-history-line';
  }

  /** "Today" / "Yesterday" / a short date, in the viewer's own timezone. */
  eventDayLabel(occurredAtUtc: string | undefined): string {
    if (!occurredAtUtc) {
      return '—';
    }

    const occurred = new Date(occurredAtUtc);
    const now = new Date();
    const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();

    if (sameDay(occurred, now)) return 'Today';

    const yesterday = new Date(now);
    yesterday.setDate(now.getDate() - 1);
    if (sameDay(occurred, yesterday)) return 'Yesterday';

    return occurred.toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }

  eventTimeLabel(occurredAtUtc: string | undefined): string {
    if (!occurredAtUtc) {
      return '';
    }

    return new Date(occurredAtUtc).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
  }

  /** The opacity for one heatmap cell, scaled to the busiest cell in the grid. */
  /** Every cell reads as a shade of green — even an empty one gets a faint tint rather than
   *  switching to a neutral grey, so the grid reads as one continuous scale. */
  heatCellOpacity(count: number): number {
    if (count === 0) {
      return 0.06;
    }

    return 0.18 + 0.82 * (count / this.heatmapMax());
  }

  private actorKey(event: AuditEventResponse): string {
    return event.actorUserId || event.actorDisplayName || 'system';
  }

  /** Creates the donut the first time its container is found in the DOM, then just updates its
   *  series after that. See the note on the `afterRenderEffect` call in the constructor for why
   *  this can't be a plain `effect()`. */
  private updateChart(succeeded: number, failed: number): void {
    if (this.chart) {
      this.chart.updateSeries([succeeded, failed]);
      return;
    }

    const el = document.getElementById('auditOutcomeChart');

    if (!el || typeof ApexCharts === 'undefined') {
      return;
    }

    const options = {
      chart: { type: 'donut', height: 190, sparkline: { enabled: false } },
      series: [succeeded, failed],
      labels: ['Succeeded', 'Failed'],
      colors: ['#37c37e', '#f55b5b'],
      stroke: { width: 0 },
      dataLabels: { enabled: false },
      legend: { show: false },
      tooltip: { y: { formatter: (value: number) => `${value} event${value === 1 ? '' : 's'}` } },
      plotOptions: { pie: { donut: { size: '72%', labels: { show: false } } } },
    };

    this.chart = new ApexCharts(el, options);
    this.chart.render();
  }
}
