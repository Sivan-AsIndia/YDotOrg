import { forkJoin } from 'rxjs';
import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { AccessReviewApiService } from '../../../../Service/access-review-api.service';
import {
  AccessReviewCampaign,
  AccessReviewCampaignViewResponse,
  AccessReviewItem,
  AccessReviewSearchFilter,
} from '../../../../Shared/models/access-review.model';

interface ReviewItem {
  id: string;
  reviewNumber: string;
  user: string;
  reference: string;
  role: string;
  scope: string;
  accessStart: string;
  accessEnd: string;
  lastUsed: string;
  lastReviewed: string;
  riskFlags: string;
  manager: string;
  decision: string;
  status: string;
  version: number;
  isOverdue: boolean;
  reviewDueAtUtc: string;

  /** Who an escalation would go to. Chosen in the dialog; blank until then. */
  escalateToUserId: string;
}

@Component({
  selector: 'app-access-review-campaign',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './access-review.html',
  styleUrl: './access-review.css',
})
export class AccessReviewCampaignComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(AccessReviewApiService);

  data = signal<AccessReviewCampaignViewResponse | null>(null);
  campaigns = signal<AccessReviewCampaign[]>([]);
  loading = signal(true);
  loadFailed = signal(false);
  busy = signal(false);
  errorMessage = signal('');

  campaignRef = signal('CAM-2026-Q3');
  campaignOwner = signal('Compliance Team');
  reviewPeriod = signal('July 1 - September 30, 2026');
  completionProgress = signal('0%');
  reviewerProgress = signal('0 of 18 reviewers completed');

  reviewItems = signal<ReviewItem[]>([]);

  // ===== Filter state =====
  searchQuery = signal('');
  filterRisk = signal('');   // '', 'flagged', 'none'
  filterRole = signal('');
  filterScope = signal('');

  // ===== Delegate Modal =====
  showDelegateModal = signal(false);
  delegateTarget = signal<ReviewItem | null>(null);
  delegateReason = signal('');

  /**
   * Who the review is being handed to.
   *
   * Bound by the dialog's reviewer picker. The server refuses a handover to the subject of the
   * review — somebody certifying their own access by the back door — so this is a convenience,
   * not the control.
   */
  delegateToUserId = '';
  delegateError = signal('');

  // ===== Derived filter options =====
  uniqueRoles = computed(() => [...new Set(this.reviewItems().map(i => i.role))]);
  uniqueScopes = computed(() => [...new Set(this.reviewItems().map(i => i.scope))]);

  // ===== Filtered list shown in the table =====
  filteredItems = computed(() => {
    const all = this.reviewItems();
    const q = this.searchQuery().toLowerCase();
    const risk = this.filterRisk();
    const role = this.filterRole();
    const scope = this.filterScope();

    let result = all;
    if (q) {
      result = result.filter(i =>
        i.user.toLowerCase().includes(q) ||
        i.role.toLowerCase().includes(q) ||
        i.scope.toLowerCase().includes(q) ||
        i.reference.toLowerCase().includes(q)
      );
    }
    if (risk === 'flagged') result = result.filter(i => i.riskFlags !== 'None' && i.riskFlags !== null && i.riskFlags !== '');
    if (risk === 'none') result = result.filter(i => i.riskFlags === 'None' || i.riskFlags === null || i.riskFlags === '');
    if (role) result = result.filter(i => i.role === role);
    if (scope) result = result.filter(i => i.scope === scope);
    return result;
  });

  constructor() {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    const filter: AccessReviewSearchFilter = { page: 1, pageSize: 100 } as AccessReviewSearchFilter;

    // Both together: the queue is what the screen renders, and the campaign supplies the
    // progress in the header. Waiting for the pair is honest about the fact that half a screen
    // is not usable.
    forkJoin({
      reviews: this.api.getReviews(filter),
      campaigns: this.api.getCampaigns(),
    }).subscribe({
      next: ({ reviews, campaigns }) => {
        this.campaigns.set(campaigns);

        // The most recent campaign drives the header. A reviewer is nearly always working
        // through the current one, and showing a total across every campaign ever run would
        // answer a question nobody asked.
        const [campaign] = campaigns;

        if (campaign) {
          this.campaignRef.set(campaign.code ?? '');
          this.campaignOwner.set(campaign.name ?? '');
          this.reviewPeriod.set(
            `${this.formatDate(campaign.startsAtUtc ?? '')} - ${this.formatDate(campaign.dueAtUtc ?? '')}`);

          const total = campaign.totalReviewCount ?? 0;
          const done = campaign.completedReviewCount ?? 0;

          this.completionProgress.set(
            total > 0 ? `${Math.round((done / total) * 100)}%` : '0%');
          this.reviewerProgress.set(`${done} of ${total} reviews completed`);
        }

        this.buildReviewItemsFromApi(reviews.items ?? []);
        this.loading.set(false);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadFailed.set(true);
        this.errorMessage.set(error.message);
        this.toast.show('Error', 'Failed to load access review campaign.', 'error');
      },
    });
  }

  private buildReviewItemsFromApi(items: AccessReviewItem[]): void {
    const mapped: ReviewItem[] = items.map((item) => ({
      id: item.id ?? '',
      reviewNumber: item.reviewNumber ?? '',
      user: item.subjectName ?? '',
      reference: item.campaignName ?? '',
      role: item.roleName || 'Unassigned',

      // The queue row carries what triage needs; the access snapshot, the last-used date and
      // the risk flags are on the detail, which is fetched when a row is opened. Loading all of
      // it for a hundred rows would be several times the payload for a table nobody reads in
      // that much depth.
      scope: 'Whole organisation',
      accessStart: '—',
      accessEnd: '—',
      lastUsed: 'Never',
      lastReviewed: item.decidedAtUtc ? this.formatDate(item.decidedAtUtc) : 'Never',
      riskFlags: 'None',
      manager: item.reviewerName ?? '—',
      decision: item.decision ?? '',
      status: item.statusDisplay ?? item.status ?? '',
      version: item.version ?? 0,
      isOverdue: item.isOverdue === true,
      reviewDueAtUtc: item.reviewDueAtUtc ?? '',

      // Who an overdue review escalates to. Left blank until somebody chooses, so escalating
      // is a deliberate act with a named recipient rather than a shout into the room.
      escalateToUserId: '',
    }));

    this.reviewItems.set(mapped);

    const flagged = mapped.filter(i => i.riskFlags !== 'None' && i.riskFlags !== null && i.riskFlags !== '').length;
    const reviewed = mapped.filter(i => i.decision !== '').length;
    // Only overwritten when there is no campaign to take it from: a campaign's own progress is
    // the authoritative number, and this is the fallback for ad-hoc reviews outside one.
    if (this.campaigns().length === 0) {
      this.completionProgress.set(
        mapped.length > 0 ? `${Math.round((reviewed / mapped.length) * 100)}%` : '0%');
    }
    this.reviewerProgress.set(`${flagged} flagged for review`);
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.filterRisk.set('');
    this.filterRole.set('');
    this.filterScope.set('');
  }

  certify(user: string): void {
    const item = this.reviewItems().find(i => i.user === user);
    if (!item) return;

    this.busy.set(true);
    this.api
      .certify(
        item.id,
        item.version,
        'Access is still required for the current role responsibilities.')
      .subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.show('Certified', `${user} has been certified.`, 'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Certify Failed', error.message, 'error');
      },
    });
  }

  revoke(user: string): void {
    const item = this.reviewItems().find(i => i.user === user);
    if (!item) return;

    this.busy.set(true);
    // The revoke removes the access there and then, server-side, in the same transaction as
    // the decision. Reloading afterwards is what shows that it actually went.
    this.api
      .revoke(
        item.id,
        item.version,
        'Access is no longer required for the current role responsibilities.')
      .subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.show('Revoked', `Access for ${user} has been revoked.`, 'info');
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Revoke Failed', error.message, 'error');
      },
    });
  }

  escalate(user: string): void {
    const item = this.reviewItems().find(i => i.user === user);
    if (!item) return;

    if (item.isOverdue) {
      this.busy.set(true);
      this.api
        .escalate(item.id, {
          escalateToUserId: item.escalateToUserId,
          reason: 'Review is overdue and requires immediate attention.',
          expectedVersion: item.version,
        })
        .subscribe({
        next: () => {
          this.busy.set(false);
          this.toast.show('Escalated', `${user} has been escalated for review.`, 'warning');
          this.loadData();
        },
        error: (error: Error) => {
          this.busy.set(false);
          this.toast.show('Escalate Failed', error.message, 'error');
        },
      });
    } else {
      // Mark as escalated locally for overdue re-listing after reload
      const items = this.reviewItems().map(i => {
        if (i.user === user) {
          return { ...i, decision: 'Escalated' };
        }
        return i;
      });
      this.reviewItems.set(items);
      this.updateProgress();
      this.toast.show('Escalated', `${user} has been escalated for review.`, 'warning');
    }
  }

  /**
   * Reloads after a decision, rather than patching the row in place.
   *
   * A decision is not only a status change: a revoke removes the access there and then, and a
   * modify changes the window. A locally patched row would show the status and none of the
   * consequences, which is the kind of half-truth a certification screen must not tell.
   */
  private refreshAfterDecision(): void {
    this.loadData();
  }

  // ===== DELEGATE REVIEW =====
  openDelegateModal(item: ReviewItem): void {
    this.delegateTarget.set(item);
    this.delegateReason.set('');
    this.delegateError.set('');
    this.showDelegateModal.set(true);
  }

  closeDelegateModal(): void {
    this.showDelegateModal.set(false);
    this.delegateTarget.set(null);
    this.delegateReason.set('');
    this.delegateError.set('');
  }

  confirmDelegate(): void {
    const target = this.delegateTarget();
    if (!target) return;

    const reason = this.delegateReason().trim();
    if (reason.length < 10) {
      this.delegateError.set('Delegate reason must be at least 10 characters.');
      return;
    }

    this.busy.set(true);
    this.api
      .delegate(target.id, {
        reviewerUserId: this.delegateToUserId,
        reason,
        expectedVersion: target.version,
      })
      .subscribe({
      next: () => {
        this.busy.set(false);
        this.closeDelegateModal();
        this.toast.show('Delegated', `Review for ${target.user} has been delegated.`, 'info');
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.delegateError.set(error.message);
      },
    });
  }

  // ===== Decision helpers =====
  hasDecision(user: string): boolean {
    const item = this.reviewItems().find(i => i.user === user);
    return !!item && item.decision !== '';
  }

  getDecisionClass(decision: string): string {
    switch (decision) {
      case 'Certified': return 'decision-badge decision-badge--success';
      case 'Retain': return 'decision-badge decision-badge--success';
      case 'Revoked':   return 'decision-badge decision-badge--danger';
      case 'Revoke': return 'decision-badge decision-badge--danger';
      case 'Escalated': return 'decision-badge decision-badge--warning';
      default:          return '';
    }
  }

  getRowClass(decision: string): string {
    switch (decision) {
      case 'Certified':
      case 'Retain': return 'row-certified';
      case 'Revoked':
      case 'Revoke': return 'row-revoked';
      case 'Escalated': return 'row-escalated';
      default:          return '';
    }
  }

  private updateProgress(): void {
    const items = this.reviewItems();
    const reviewed = items.filter(i => i.decision !== '').length;
    this.completionProgress.set(items.length > 0 ? `${Math.round((reviewed / items.length) * 100)}%` : '0%');
    this.reviewerProgress.set(`${reviewed} of ${items.length} users reviewed`);
  }

  private formatDate(value: string): string {
    try {
      return new Date(value).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
    } catch {
      return value;
    }
  }

  retry(): void { this.loadData(); }

  goBack(): void { this.router.navigate(['/app/administration/access/user-directory']); }
}