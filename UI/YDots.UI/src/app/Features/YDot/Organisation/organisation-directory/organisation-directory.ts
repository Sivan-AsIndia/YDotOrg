import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import {
  OrganisationApiService,
  OrganisationSearchFilter,
} from '../../../../Service/organisation-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  OrganisationListItemResponse,
  OrganisationStatisticsResponse,
  TenantStatus,
} from '../../../../Shared/models/iam-contract.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { OrganisationContextService } from '../../../../Shared/services/organisation-context.service';
import { ToastService } from '../../../../Shared/services/toast.service';

/**
 * The SuperAdmin Organisation directory.
 *
 * EVERY ROW COMES FROM THE API. There is no seed data and no browser cache behind this screen:
 * an earlier version kept Organisations in localStorage, which meant two administrators saw two
 * different platforms and neither matched the database.
 *
 * WHAT "STATUS" MEANS HERE, AND WHY IT IS NOT A TICK BOX
 * ------------------------------------------------------
 * An Organisation moves through a real lifecycle — invited, invitation accepted, profile
 * incomplete, submitted, under review, approved or rejected, active, suspended, archived — and
 * the actions available at each stage differ. The server sends `permittedActions` with the
 * detail, and the buttons on the next screen are rendered from it rather than from a guess made
 * here, so what a person is offered and what the API will allow cannot drift apart.
 */
@Component({
  selector: 'app-organisation-directory',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './organisation-directory.html',
  styleUrl: './organisation-directory.css',
})
export class OrganisationDirectoryComponent implements OnInit, OnDestroy {
  private readonly api = inject(OrganisationApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly tokens = inject(AuthTokenService);
  private readonly organisationContext = inject(OrganisationContextService);

  private readonly destroy$ = new Subject<void>();
  private readonly searchInput$ = new Subject<string>();

  // ---- Data -------------------------------------------------------------------------------
  readonly organisations = signal<OrganisationListItemResponse[]>([]);
  readonly statistics = signal<OrganisationStatisticsResponse | null>(null);
  readonly totalCount = signal(0);
  readonly totalPages = signal(1);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');
  readonly switching = signal<string | null>(null);

  // ---- Filters ----------------------------------------------------------------------------
  readonly searchTerm = signal('');
  readonly statusFilter = signal<TenantStatus | ''>('');
  readonly awaitingReviewOnly = signal(false);
  readonly page = signal(1);
  readonly pageSize = signal(10);

  readonly hasActiveFilters = computed(
    () => this.searchTerm().trim().length > 0 || this.statusFilter() !== '' || this.awaitingReviewOnly());

  readonly isSuperAdmin = computed(() => this.tokens.isSuperAdmin());

  /**
   * The statuses to offer in the filter.
   *
   * Written out rather than fetched because they are the lifecycle itself, and the lifecycle is
   * fixed by the domain — a new status means new server behaviour, not new configuration.
   */
  readonly statusOptions: { value: TenantStatus; label: string }[] = [
    { value: 'invited', label: 'Invitation sent' },
    { value: 'invitationAccepted', label: 'Invitation accepted' },
    { value: 'profileIncomplete', label: 'Profile incomplete' },
    { value: 'submitted', label: 'Submitted' },
    { value: 'underReview', label: 'Under review' },
    { value: 'rejected', label: 'Rejected' },
    { value: 'resubmitted', label: 'Resubmitted' },
    { value: 'approved', label: 'Approved' },
    { value: 'active', label: 'Active' },
    { value: 'suspended', label: 'Suspended' },
    { value: 'archived', label: 'Archived' },
  ];

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    // Typing in the search box should not fire a request per keystroke. Three hundred
    // milliseconds is long enough to finish a word and short enough not to feel laggy.
    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe((term) => {
        this.searchTerm.set(term);
        this.page.set(1);
        this.load();
      });

    this.load();
    this.loadStatistics();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // =========================================================================================
  // Loading
  // =========================================================================================

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    const filter: OrganisationSearchFilter = {
      search: this.searchTerm().trim() || undefined,
      status: this.statusFilter() || undefined,
      awaitingReviewOnly: this.awaitingReviewOnly() || undefined,
      page: this.page(),
      pageSize: this.pageSize(),
    };

    this.api
      .search(filter)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.organisations.set(result.items ?? []);
          this.totalCount.set(result.totalCount ?? 0);
          this.totalPages.set(Math.max(1, result.totalPages ?? 1));
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The organisations could not be loaded.'));
        },
      });
  }

  /**
   * The counters across the top.
   *
   * Loaded separately and allowed to fail quietly: a broken counter is a cosmetic problem, and
   * failing the whole screen over it would hide the table that actually matters.
   */
  private loadStatistics(): void {
    this.api
      .getStatistics()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (stats) => this.statistics.set(stats),
        error: () => this.statistics.set(null),
      });
  }

  // =========================================================================================
  // Filters and paging
  // =========================================================================================

  onSearchInput(value: string): void {
    this.searchInput$.next(value);
  }

  onStatusChange(value: string): void {
    this.statusFilter.set((value as TenantStatus) || '');
    this.page.set(1);
    this.load();
  }

  toggleAwaitingReview(): void {
    this.awaitingReviewOnly.update((only) => !only);
    this.page.set(1);
    this.load();
  }

  clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set('');
    this.awaitingReviewOnly.set(false);
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

  // =========================================================================================
  // Actions
  // =========================================================================================

  view(id: string): void {
    void this.router.navigate(['/app/administration/organisation/details', id]);
  }

  review(id: string): void {
    void this.router.navigate(['/app/administration/organisation/registration-verification', id]);
  }

  /**
   * Steps into an Organisation to work inside it.
   *
   * The server issues a NEW ACCESS TOKEN scoped to that Organisation on the same session. The
   * caller's own user record is untouched — a root user has no Organisation of their own and
   * never acquires one by looking at somebody's data. After this the whole app is operating
   * inside that Organisation, which is why the shell shows an "acting as" banner.
   */
  enter(organisation: OrganisationListItemResponse): void {
    if (!organisation.id) {
      return;
    }

    this.switching.set(organisation.id);

    this.organisationContext
      .select(organisation.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.switching.set(null);
          this.toast.show(
            'Organisation selected',
            `You are now working inside ${organisation.name}.`,
            'success');
          void this.router.navigate(['/app/dashboard']);
        },
        error: (error: unknown) => {
          this.switching.set(null);
          this.toast.show('Could not switch', apiErrorMessage(error), 'error');
        },
      });
  }

  resendInvitation(organisation: OrganisationListItemResponse): void {
    if (!organisation.id) {
      return;
    }

    this.api
      .resendInvitation(organisation.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.toast.show(
            'Invitation re-sent',
            result.invitationSent
              ? `A new link has been e-mailed to ${result.adminEmail}.`
              : 'The invitation was renewed, but the e-mail could not be sent.',
            result.invitationSent ? 'success' : 'warning');
          this.load();
        },
        error: (error: unknown) =>
          this.toast.show('Could not re-send', apiErrorMessage(error), 'error'),
      });
  }

  // =========================================================================================
  // Display helpers
  // =========================================================================================

  /**
   * The badge colour for a status.
   *
   * Grouped by what the status MEANS to somebody scanning the table — settled, in progress,
   * needs attention, stopped — rather than one colour per status, which produces a rainbow
   * nobody can read at a glance.
   */
  statusClass(status: TenantStatus | undefined): string {
    switch (status) {
      case 'active':
      case 'approved':
        return 'is-good';

      case 'submitted':
      case 'underReview':
      case 'resubmitted':
        return 'is-warn';

      case 'rejected':
      case 'suspended':
        return 'is-error';

      case 'archived':
        return 'is-muted';

      default:
        return 'is-info';
    }
  }

  /** Whether an invitation is still outstanding, and therefore worth offering to re-send. */
  awaitingInvitation(organisation: OrganisationListItemResponse): boolean {
    return organisation.status === 'invited';
  }

  trackById(_index: number, organisation: OrganisationListItemResponse): string {
    return organisation.id ?? '';
  }
}
