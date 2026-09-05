import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { AuditSearchFilter, IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { AuditEventResponse } from '../../../../Shared/models/iam-contract.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { ToastService } from '../../../../Shared/services/toast.service';

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

  readonly events = signal<AuditEventResponse[]>([]);
  readonly totalCount = signal(0);
  readonly totalPages = signal(1);

  readonly loading = signal(true);
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
  readonly page = signal(1);
  readonly pageSize = signal(25);

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
        this.page.set(1);
        this.load();
      });

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    this.api
      .searchAuditEvents(this.buildFilter())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.events.set(result.items ?? []);
          this.totalCount.set(result.totalCount ?? 0);
          this.totalPages.set(Math.max(1, result.totalPages ?? 1));
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
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
      pageSize: this.pageSize(),
    };
  }

  onSearchInput(value: string): void {
    this.searchInput$.next(value);
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  clearFilters(): void {
    this.search.set('');
    this.targetType.set('');
    this.result.set('');
    this.fromDate.set('');
    this.toDate.set('');
    this.page.set(1);
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) {
      return;
    }

    this.page.set(page);
    this.load();
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
}
