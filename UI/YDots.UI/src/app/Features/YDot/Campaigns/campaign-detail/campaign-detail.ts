import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  UiState,
  CampaignStatus,
  DetailTab,
  HistoryRow,
  ActivityItem,
} from '../../../../Shared/models/campaign.model';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { TrackingAssetStoreService } from '../../../../Shared/services/tracking-asset-store.service';
import { BudgetTargetStoreService } from '../../../../Shared/services/budget-target-store.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PauseResumeCloseCampaignComponent } from '../pause-resume-and-close-campaign/pause-resume-and-close-campaign';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { CampaignApiService } from '../../../../Service/campaign-api.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { MoneyResponse } from '../../../../Shared/models/payment.model';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';

/** One named lifecycle transition offered from a given state. */
interface LifecycleTransition {
  readonly key: string;
  readonly label: string;
  readonly target: CampaignStatus;
}

/**
 * The primary lifecycle transition offered from each state — the verb the header
 * button runs: Draft→Submit, Submitted→Approve, Approved→Schedule,
 * Scheduled→Activate, Active→Pause, Paused→Resume and Closing→Complete closure.
 * Together these make the state machine fully traversable.
 */
const PRIMARY_TRANSITION: Partial<Record<CampaignStatus, LifecycleTransition>> = {
  Draft: { key: 'primary', label: 'Submit', target: 'Submitted' },
  Submitted: { key: 'primary', label: 'Approve', target: 'Approved' },

  // APPROVED GOES TO ACTIVE, NOT TO SCHEDULED, and that is the shape of the server's state
  // machine rather than a preference. `Scheduled` is where a campaign set to AUTO activation
  // waits for its own start date, and approval is what puts it there — there is no separate
  // "schedule" step for anybody to run. This offered one: it was labelled Schedule, it routed
  // to the approve endpoint, and approve refuses anything that is not Submitted, so pressing it
  // on an Approved campaign answered 409 every time. An approved campaign's permitted action on
  // the server is Activate, which is what this now offers.
  Approved: { key: 'primary', label: 'Activate', target: 'Active' },
  Scheduled: { key: 'primary', label: 'Activate', target: 'Active' },
  Active: { key: 'primary', label: 'Pause', target: 'Paused' },
  Paused: { key: 'primary', label: 'Resume', target: 'Active' },
  Closing: { key: 'primary', label: 'Complete closure', target: 'Closed' },
};
const SECONDARY_TRANSITIONS: Partial<Record<CampaignStatus, readonly LifecycleTransition[]>> = {
  Scheduled: [{ key: 'pause', label: 'Pause', target: 'Paused' }],
  Active: [{ key: 'close', label: 'Close', target: 'Closing' }],
  Paused: [{ key: 'close', label: 'Close', target: 'Closing' }],
};
/** States from which Cancel is offered "if permitted" (non-terminal, pre-closing). */
const CANCELLABLE_STATES: readonly CampaignStatus[] = [
  'Draft', 'Submitted', 'Approved', 'Scheduled', 'Active', 'Paused',
];
const CANCEL_TRANSITION: LifecycleTransition = { key: 'cancel', label: 'Cancel campaign', target: 'Cancelled' };

/**
 * States whose lifecycle primary action is a Pause / Resume / Close operation.
 * For these the header button is a doorway to the dedicated Pause / Resume /
 * Close screen rather than resolving in place — that screen is the only one that
 * runs Activate / Pause / Resume / Request close / Approve close. The earlier
 * transitions (Draft→Submit … Closing→Complete) keep their in-place confirm dialog.
 *
 * `Scheduled` is included so activation (Scheduled → Active) also happens on that
 * screen; the detail page never opens an in-place activation dialog.
 */
const LIFECYCLE_PAGE_STATES: readonly CampaignStatus[] = ['Scheduled', 'Active', 'Paused'];

/**
 * Campaign detail.
 *
 * A read-only view of a single campaign — summary, targets, budget, tracking and
 * payments — plus the lifecycle actions available from its current state.
 */
@Component({
  selector: 'app-campaign-detail',
  imports: [CommonModule, FormsModule, PauseResumeCloseCampaignComponent],
  templateUrl: './campaign-detail.html',
  styleUrl: './campaign-detail.css',
})
export class CampaignDetailComponent {
  /**
   * Reads the campaign reference passed by the register's Open action and pulls
   * the record from the single shared CampaignStoreService. Falls back to a
   * built-in mock when opened directly without a reference.
   */
  private readonly campaignApi = inject(CampaignApiService);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly store = inject(CampaignStoreService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly trackingStore = inject(TrackingAssetStoreService);
  private readonly budgetStore = inject(BudgetTargetStoreService);
  private readonly toast = inject(ToastService);

  /** The payments service. This campaign's Payments tab reads its donations from it. */
  private readonly payments = inject(PaymentApiService);
  /**
   * Owner names, resolved from IAM rather than from a map in this file.
   *
   * THE FOUR NAMES HERE WERE INVENTED, and the fallbacks below made that invisible: an unknown
   * owner reference rendered as 'Arun Kumar' and an absent one as 'USR-0114'. Somebody reading
   * this screen to find out who is accountable for a campaign got a confident answer that was not
   * connected to anything.
   */
  private readonly people = inject(PeopleDirectoryService);
  private readonly initialRecord = this.store.get(this.route.snapshot.queryParamMap.get('ref') ?? '');

  constructor() {
    this.loadActivity();
    // Donations-in-scope is modelled as a backend fetch: stamp a freshly-fetched time on load,
    // the same as the manual refresh action does.
    this.refreshDonationsScope();
  }

  // ================= Task header =================
  /** Campaign reference — server-derived, immutable in this view. */
  protected readonly reference = this.initialRecord?.code ?? 'EDU-2025-001';
  /**
   * Live re-read of this campaign's record from the shared store on every access
   * NOT a one-time snapshot — so a change made on another page (Wizard edit, Operate
   * on this page, etc.) is reflected here without a reload.
   */
  private readonly liveRecord = computed(() => this.store.get(this.reference));
  /** Campaign name — read-only. */
  protected readonly campaignName = computed(() => this.liveRecord()?.name ?? 'Educate a Child 2025');
  /** Status — server-derived current state. */
  protected readonly status = signal<CampaignStatus>(this.initialRecord?.status ?? 'Active');
  /** Owner — read-only. */
  protected readonly owner = computed(() => {
    const rec = this.liveRecord();

    // 'Unassigned' rather than a name. A campaign whose owner has not loaded, or which has no
    // owner at all, must not read as one belonging to a particular person.
    return rec ? this.people.name(rec.ownerReference) : 'Unassigned';
  });
  /** Owner reference/id — shown directly below the campaign name, alongside the owner's role. */
  protected readonly ownerRef = computed(() => this.liveRecord()?.ownerReference ?? '');
  /** Owner role — from the mock session profile when the owner is one of the seeded users; a plain
   *  campaign owner otherwise (this app models every non-staff campaign owner as that role). */
  /**
   * The owner's role.
   *
   * FROM THE DIRECTORY'S OWN CONTEXT - designation and unit - rather than from a table of mock
   * profiles. 'Campaign Owner' remains the fallback because that IS this person's role in relation
   * to this campaign, whatever their job title says.
   */
  protected readonly ownerRole = computed(
    () => this.people.get(this.ownerRef())?.context || 'Campaign Owner',
  );
  /** Every accountable owner's name — a campaign may have more than one (Wizard "+ Add owner"). */
  protected readonly ownersLabel = computed(() => {
    const rec = this.liveRecord();
    const refs = rec?.ownerReferences?.length ? rec.ownerReferences : rec ? [rec.ownerReference] : [];
    return refs.length ? refs.map((r) => this.people.name(r)).join(', ') : this.owner();
  });
  /** Freshness — server-derived last refresh. */
  protected readonly lastRefresh = signal('12 May 2025, 02:15 PM · IST');
  private nowLabel(): string {
    return (
      new Date().toLocaleString('en-GB', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) +
      ' · IST'
    );
  }
  /** "Created" for a freshly created campaign with no content edits since; "Updated" once it has
   *  been edited (record.wasEdited) — never both fixed labels regardless of record state. */
  protected readonly createdOrUpdatedLabel = computed(() => (this.liveRecord()?.wasEdited ? 'Updated' : 'Created'));
  protected readonly createdOrUpdatedAt = computed(() => {
    const rec = this.liveRecord();
    if (!rec) return this.lastRefresh();
    const iso = rec.wasEdited ? rec.updatedAt ?? rec.createdAt : rec.createdAt;
    return iso ? this.formatDate(iso) : this.lastRefresh();
  });

  /**
   * Effective permissions — sourced from the shared mock session
   * (CurrentUserService), the same authority Campaign Register reads, so the two
   * screens share one session context rather than holding a local permission object.
   */
  protected readonly permissions = computed(() => ({
    view: this.currentUser.hasPermission('cam.campaigns.view'),

    // "OPERATE ACCORDING TO LIFECYCLE" IS NOT ONE PERMISSION. The CAM service enforces a separate
    // code per transition, so the control is offered when the caller holds ANY of them and the
    // server decides which transition the click actually is. The single page-named code this used
    // to check existed in no catalogue at all, so it was false for everybody - including the
    // organisation's own administrator - and every lifecycle action on this screen was inert.
    operate: this.currentUser.hasAnyPermission(
      'cam.campaigns.submit',
      'cam.campaigns.approve',
      'cam.campaigns.activate',
      'cam.campaigns.pause',
      'cam.campaigns.resume',
      'cam.campaigns.request-close',
      'cam.campaigns.close',
    ),
    // Reuses the Campaign Register's export permission key — same export capability,
    // now also offered from this campaign's own detail page.
    export: this.currentUser.hasPermission('cam.campaigns.export'),
    // Reuses the Campaign Register's delete-draft permission — the same capability, now also
    // offered from a draft campaign's own detail page next to Submit.
    deleteDraft: this.currentUser.hasPermission('cam.campaigns.delete-draft'),
  }));
  protected readonly exportAllowed = computed(() => this.permissions().export && this.uiState() !== 'no-access');
  /** Edit is offered only for a Draft campaign — opens the Campaign Wizard pre-filled with this record. */
  protected readonly canEdit = computed(() => this.status() === 'Draft' && this.uiState() !== 'no-access');
  protected openEdit(): void {
    if (!this.canEdit()) {
      return;
    }
    this.router.navigate(['/app/fundraising/campaigns/campaign-wizard'], { queryParams: { ref: this.reference } });
  }
  /** Delete draft is offered only for a Draft campaign with no downstream reference, to a permitted
   *  user — a permanent delete of an unused draft, distinct from the lifecycle Cancel. */
  protected readonly canDeleteDraft = computed(
    () =>
      this.permissions().deleteDraft &&
      this.status() === 'Draft' &&
      !this.liveRecord()?.hasDownstreamReference &&
      this.uiState() !== 'no-access',
  );

  // ================= Read-only fields =================
  /** Purpose — read-only. */
  protected readonly purpose = computed(
    () =>
      this.liveRecord()?.purpose ??
      'Support underprivileged children by providing access to quality education, learning resources and school essentials.',
  );

  /** Date range — read-only. */
  protected readonly launchDate = computed(() => this.liveRecord()?.startDate || '2025-05-01');
  protected readonly endDate = computed(() => this.liveRecord()?.endDate || '2025-12-31');

  /** Target and reconciled progress — server-derived amounts. */
  protected readonly targetAmount = computed(() => this.liveRecord()?.targetAmount ?? 2500000);
  protected readonly reconciledAmount = computed(() => this.liveRecord()?.reconciledAmount ?? 1275000);
  protected readonly progressPercent = computed(() =>
    Math.round((this.reconciledAmount() / this.targetAmount()) * 100),
  );

  /** Targets — server-derived plan amount. */
  protected readonly targetsAmount = computed(() => this.targetAmount());

  /**
   * Budget summary — live from the shared BudgetTargetStoreService,
   * not a page-local mock.
   * Reads ONLY each plan's current Approved version (`approvedForCampaign`), never
   * summing across Draft/Submitted/Superseded versions — this is what prevents the
   * double-counting the screen exists to avoid.
   */
  protected readonly budgetPlans = computed(() => this.budgetStore.approvedForCampaign(this.campaignName()));
  protected readonly budgetAmount = computed(() =>
    this.budgetPlans().reduce((sum, v) => sum + v.budgetAmount, 0),
  );
  /** Each approved allocation's share of the total budget — how the total is split
   *  across categories. */
  protected budgetShare(planBudget: number): number {
    const total = this.budgetAmount();
    return total ? Math.round((planBudget / total) * 1000) / 10 : 0;
  }

  /**
   * Tracking assets — read-only. Read live from the single
   * shared TrackingAssetStoreService — not a
   * page-local copy — so a Generate on the Tracking Asset Manager appears here
   * immediately, without a refresh. Falls back to a built-in mock when
   * opened directly without a reference (no real campaign to filter by).
   */
  protected readonly trackingAssets = computed<readonly HistoryRow[]>(() => {
    if (!this.initialRecord) {
      return [
        { primary: 'education-2025-link-07', secondary: 'UTM tracking link · Website', meta: 'Created 12 May 2025, 10:45 AM' },
        { primary: 'QR-EDU-2025-03', secondary: 'QR destination · Print flyer', meta: 'Active · 214 scans' },
      ];
    }
    return this.trackingStore.forCampaign(this.reference).map((a) => ({
      primary: a.trackingReference,
      secondary: `${a.assetType} · ${a.channel}`,
      meta: `${a.assetStatus} · ${a.usageCount.toLocaleString('en-IN')} uses`,
    }));
  });

  /**
   * This campaign's donations — THE REAL ONES, from the payments service.
   *
   * WHAT WAS HERE: three hard-coded rows compiled into the bundle. "₹5,000 · Ramesh Kumar",
   * "₹2,500 · Anitha S", "₹10,000 · Corporate — Zentra", dated May 2025, with a headline
   * count of 1,248 donations. Every campaign, in every organisation, showed the same three
   * donations from the same three people and the same total — including a campaign created a
   * minute earlier that had received nothing at all.
   *
   * That is worse than an empty tab in a way worth being exact about: a named donor and an
   * amount on a campaign's Payments tab reads as a financial record. Somebody reconciling that
   * campaign would be reading three invented transactions attributed to three invented people,
   * and a fourth number, 1,248, attributed to nobody.
   *
   * It is now GET /donations?campaignId={id} — the payments register's own endpoint, filtered to
   * this campaign, permission-checked and organisation-scoped server-side like every other read.
   * A campaign with no donations shows the empty state, which is the truth.
   */
  protected readonly donations = signal<readonly HistoryRow[]>([]);
  protected readonly donationsCount = signal(0);
  protected readonly donationsLoading = signal(false);
  protected readonly donationsError = signal<string | null>(null);

  /**
   * Reads this campaign's donations.
   *
   * A PAGE OF TWENTY, ordered by the API. The tab is a summary beside the campaign, not the
   * payments register — the full ledger is that screen's job — so it shows the most recent and
   * reports the true total beside them.
   */
  private loadDonations(): void {
    const campaignId = this.store.apiId(this.reference);

    if (!campaignId) {
      this.donations.set([]);
      this.donationsCount.set(0);
      return;
    }

    this.donationsLoading.set(true);
    this.donationsError.set(null);

    this.payments.searchDonations({ campaignId, page: 1, pageSize: 20 }).subscribe({
      next: (page) => {
        this.donations.set(
          (page.items ?? []).map((donation) => ({
            primary: `${this.money(donation.amount)} · ${donation.donorName || 'Anonymous donor'}`,
            secondary: [donation.sourceType as string | null, donation.methodType as string | null]
              .filter((part): part is string => !!part)
              .join(' · ') || donation.statusDescription,
            meta: this.donationWhen(donation.donatedAtUtc),
          })),
        );

        this.donationsCount.set(page.totalCount ?? 0);
        this.donationsLoading.set(false);
      },

      // REPORTED, NOT SWALLOWED INTO AN EMPTY LIST. "No donations" and "the payments service did
      // not answer" are different facts, and only one of them is a reason to stop chasing a
      // missing donation.
      error: (error: unknown) => {
        this.donations.set([]);
        this.donationsCount.set(0);
        this.donationsLoading.set(false);
        this.donationsError.set(
          apiErrorMessage(error, 'This campaign\u2019s donations could not be loaded.'));
      },
    });
  }

  /** An amount as its own currency prints it, rather than as a bare number. */
  private money(amount: MoneyResponse | null | undefined): string {
    if (!amount) {
      return '—';
    }

    try {
      return new Intl.NumberFormat('en-IN', {
        style: 'currency',
        currency: amount.currencyCode || 'INR',
        maximumFractionDigits: 2,
      }).format(amount.amount ?? 0);
    } catch {
      return `${amount.currencyCode ?? ''} ${amount.amount ?? 0}`.trim();
    }
  }

  private donationWhen(value: string | null | undefined): string {
    if (!value) {
      return '—';
    }

    const when = new Date(value);

    return Number.isNaN(when.getTime())
      ? '—'
      : when.toLocaleString('en-IN', {
          day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
        });
  }

  /** Documents — Confidential; visible only within record scope and purpose. */
  protected readonly documents: readonly HistoryRow[] = [
    { primary: 'Campaign brief — Educate a Child 2025', secondary: 'PDF · 320 KB', meta: 'Confidential · record scope' },
    { primary: 'May 2025 donation statement', secondary: 'CSV · 88 KB', meta: 'Confidential · record scope' },
  ];
  /**
   * Documents carries its own, independent permission/scope check rather than only relying on the page-level view gate.
   * No doc-listed sub-permission exists for this field, so eligibility is
   * modelled the same way the rest of this session's mock session objects
   * model role scope: the narrower Campaign Manager role is in the record's
   * accountable scope, Executive users are not.
   */
  protected readonly documentsAllowed = computed(
    () => this.permissions().view && this.currentUser.role() === 'Campaign Manager',
  );

  /**
   * The campaign's activity chronology.
   *
   * WHAT THIS REPLACES. Five fixed entries, identical on every campaign, dated May 2025 - one of
   * them "Donation received: ₹5,000 from Ramesh Kumar". A named donor and an amount, on a screen
   * a fundraiser reads to see what has been happening, against a gift nobody made.
   *
   * IT NOW READS THE CAMPAIGN'S OWN LIFECYCLE HISTORY - the server's append-only trail of who did
   * what to this campaign and whether it was allowed.
   */
  protected readonly recentActivity = signal<readonly ActivityItem[]>([]);

  /** Loads the history for the campaign on screen. */
  private loadActivity(): void {
    const campaignId = this.store.apiId(this.reference);

    if (!campaignId) {
      this.recentActivity.set([]);
      return;
    }

    this.campaignApi.getCampaignHistory(campaignId).subscribe({
      next: (entries) =>
        this.recentActivity.set(
          entries.map((entry) => {
            const record = entry as unknown as Record<string, unknown>;
            const action = String(record['actionTypeDescription'] ?? record['actionType'] ?? '');
            const reason = String(record['detailedReason'] ?? record['reasonCategory'] ?? '');
            const at = String(record['effectiveAtUtc'] ?? record['createdAtUtc'] ?? '');
            const status = String(record['actionStatus'] ?? '');

            return {
              title: action,
              detail: reason || status,
              time: at ? new Date(at).toLocaleString('en-IN') : '',

              // A REFUSED ACTION IS DRAWN DIFFERENTLY. The history records attempts that were not
              // allowed as well as ones that were, and a trail that showed both the same way would
              // hide the more interesting half.
              tone: status.toLowerCase().includes('reject') || status.toLowerCase().includes('refus')
                ? 'plum'
                : 'good',
            } as ActivityItem;
          }),
        ),
      error: () => this.recentActivity.set([]),
    });
  }

  // ================= Campaign Overview summary card content =================
  // Campaign reference / name / status / owner are already shown in the task header directly
  // above this card, so the summary card itself surfaces the fields that aren't shown there:
  // Purpose (as the lead paragraph), Target & Budget, Channel, Public description, Terms & notice.

  // THE `*Name` FIELDS, NOT THE ID FIELDS. `channels`, `currency`, `country`, `region` and
  // `city` on a CampaignRecord hold the API's Guids — that is what the create and update
  // bodies require — so printing them directly would put raw identifiers on the summary card.
  protected readonly channelsLabel = computed(() => {
    const list = this.liveRecord()?.channelNames;
    return list && list.length ? list.join(', ') : '—';
  });
  protected readonly targetBudgetLabel = computed(() => {
    const rec = this.liveRecord();
    const target = this.formatAmount(this.targetAmount());
    return rec?.budgetAmount ? `${target} target · ${this.formatAmount(rec.budgetAmount)} budget` : `${target} target`;
  });
  /** Fund or programme — captured on Wizard step 1. */
  protected readonly fundProgramme = computed(() => this.liveRecord()?.fundProgramme || '—');
  /** Currency — captured on Wizard step 2. */
  protected readonly currencyLabel = computed(() => this.liveRecord()?.currencyName || '—');
  /** Country / State / City / Zip code — captured on Wizard step 3. */
  protected readonly locationLabel = computed(() => {
    const rec = this.liveRecord();
    const parts = [rec?.countryName, rec?.regionName, rec?.cityName, rec?.pincode].filter(
      (v): v is string => !!v,
    );
    return parts.length ? parts.join(' · ') : '—';
  });
  /** Lifecycle activation + reminder — captured on Wizard step 3. */
  protected readonly activationLabel = computed(() => {
    const rec = this.liveRecord();
    if (!rec) return '—';
    const mode = rec.activationMode === 'auto' ? 'Auto-activate on the start date' : 'Manual activate';
    const reminder =
      rec.reminderDaysBefore != null
        ? ` · Reminder ${rec.reminderDaysBefore} day(s) before at ${rec.reminderTime || '—'}`
        : '';
    return `${mode}${reminder}`;
  });

  private richOrPlain(html: string | undefined, plain: string | undefined, emptyText: string): string {
    if (html && html.trim()) return html;
    if (plain && plain.trim()) return `<p>${plain}</p>`;
    return `<p>${emptyText}</p>`;
  }
  private previewOf(plain: string | undefined, emptyText: string): string {
    const t = (plain ?? '').trim();
    if (!t) return emptyText;
    return t.length > 140 ? `${t.slice(0, 140)}…` : t;
  }
  protected readonly publicDescriptionHtml = computed(() =>
    this.richOrPlain(this.liveRecord()?.publicDescriptionHtml, this.liveRecord()?.publicDescription, 'No public description provided.'),
  );
  protected readonly publicDescriptionPreview = computed(() =>
    this.previewOf(this.liveRecord()?.publicDescription, 'No public description provided.'),
  );
  protected readonly termsNoticeHtml = computed(() =>
    this.richOrPlain(this.liveRecord()?.termsNoticeHtml, this.liveRecord()?.termsNotice, 'No terms and notice provided.'),
  );
  protected readonly termsNoticePreview = computed(() =>
    this.previewOf(this.liveRecord()?.termsNotice, 'No terms and notice provided.'),
  );
  /** Whether a value was actually entered — drives whether "Read more" is shown at all. */
  protected readonly hasPublicDescription = computed(() =>
    !!(this.liveRecord()?.publicDescription ?? '').trim() || !!(this.liveRecord()?.publicDescriptionHtml ?? '').trim(),
  );
  protected readonly hasTermsNotice = computed(() =>
    !!(this.liveRecord()?.termsNotice ?? '').trim() || !!(this.liveRecord()?.termsNoticeHtml ?? '').trim(),
  );

  /** "Read more" popup — Public description and Terms & notice open the full rendered content. */
  protected readonly readMoreField = signal<'description' | 'terms' | null>(null);
  protected openReadMore(which: 'description' | 'terms'): void {
    // Do not open the popup when there is no value to show.
    if (which === 'terms' ? !this.hasTermsNotice() : !this.hasPublicDescription()) return;
    this.readMoreField.set(which);
  }
  protected closeReadMore(): void {
    this.readMoreField.set(null);
  }
  protected readonly readMoreTitle = computed(() => (this.readMoreField() === 'terms' ? 'Terms and notice' : 'Public description'));
  protected readonly readMoreHtml = computed(() => (this.readMoreField() === 'terms' ? this.termsNoticeHtml() : this.publicDescriptionHtml()));

  // ================= Main work: tabs =================
  /** History & Sources removed per user request; Targets/Budget/Tracking/Payments remain. */
  protected readonly tabs: readonly DetailTab[] = [
    { key: 'targets', label: 'Targets' },
    { key: 'budget', label: 'Budget' },
    { key: 'tracking', label: 'Tracking' },
    { key: 'payments', label: 'Payments' },
  ];
  protected readonly activeTab = signal<string>('targets');
  protected selectTab(key: string): void {
    this.activeTab.set(key);
  }

  // ================= Context and filters =================
  /** Active data scope — this campaign's own country/region, not a fixed org name. */
  protected readonly scope = computed(() => {
    const rec = this.liveRecord();
    const parts = [rec?.country, rec?.region].filter((v): v is string => !!v);
    return parts.length ? `${parts.join(' · ')} · This campaign` : 'This campaign';
  });

  /** Active-filter summary chips — kept for a possible future filter, empty today (no search/saved view). */
  protected readonly activeFilterSummary = computed<readonly { key: string; label: string }[]>(() => []);

  /** Donations in scope — modelled as backend-fetched: re-reading it stamps a fresh refreshed time. */
  protected readonly donationsScopeFetching = signal(false);
  protected readonly scopedTotals = computed(
    () => `${this.donationsCount().toLocaleString('en-IN')} donations in scope · refreshed ${this.lastRefresh()}`,
  );

  /**
   * Re-reads this campaign's donations.
   *
   * IT ACTUALLY RE-READS THEM NOW. This was a 400 ms `setTimeout` that stamped a fresh
   * "refreshed" time onto a list that had not changed and could not change, because the list was
   * a constant — so pressing Refresh made the invented figures look freshly confirmed.
   */
  protected refreshDonationsScope(): void {
    this.donationsScopeFetching.set(true);
    this.loadDonations();
    this.lastRefresh.set(this.nowLabel());
    this.donationsScopeFetching.set(false);
  }

  protected clearFilters(): void {
    /* No search/saved-view filters remain on this page — kept as a no-op for the state-panel Reset button. */
  }

  // ================= Related and history =================
  // Only Activity is shown for now — Linked records / Documents / Integration status /
  // Support correlation / Audit chronology are hidden (not deleted) below, kept for
  // later re-enabling.
  protected readonly linkedRecords: readonly HistoryRow[] = [
    { primary: 'BUD-2025-0031', secondary: 'FY25 campaign budget', meta: 'Budget · Approved' },
    { primary: 'PLAN-2025-0007', secondary: 'Budget and target plan v3', meta: 'Plan · Current' },
    { primary: 'CAMP-2025-0011', secondary: 'Educate a Child 2025', meta: 'Campaign · Active' },
  ];
  protected readonly integrationRows: readonly HistoryRow[] = [
    { primary: 'Settlement feed', secondary: 'Healthy', meta: 'Last sync 12 May 2025, 02:10 PM' },
    { primary: 'Payment provider', secondary: 'Healthy', meta: 'Last sync 12 May 2025, 02:08 PM' },
  ];
  protected readonly supportRows: readonly HistoryRow[] = [
    { primary: 'INT-88421', secondary: 'Receipt email retry', meta: 'Resolved · correlation kept' },
  ];
  protected readonly auditRows: readonly HistoryRow[] = [
    { primary: 'Campaign opened', secondary: '', meta: 'A. Kumar · 12 May 2025, 02:15 PM' },
    { primary: 'Budget updated', secondary: 'Marketing budget increased', meta: 'N. Patel · 11 May 2025, 04:30 PM' },
  ];

  // ================= Actions, eligibility and result =================
  /** The named transition this state's header button performs. */
  protected readonly primaryTransition = computed(() => PRIMARY_TRANSITION[this.status()]);
  /** Other eligible transitions for this state, offered inside the same confirm dialog
   *  rather than as extra header buttons. */
  protected readonly alternateTransitions = computed<readonly LifecycleTransition[]>(() => {
    const alts = [...(SECONDARY_TRANSITIONS[this.status()] ?? [])];
    if (CANCELLABLE_STATES.includes(this.status())) {
      alts.push(CANCEL_TRANSITION);
    }
    return alts;
  });
  /** The single primary action: appears only when role, permission, scope, state and dependencies allow. */
  protected readonly operateAllowed = computed(
    () => this.permissions().operate && !!this.primaryTransition() && this.uiState() !== 'no-access',
  );
  /** True when this state's lifecycle primary action is run on the dedicated Pause / Resume /
   *  Close screen rather than the in-place confirm dialog. */
  protected readonly lifecycleUsesDedicatedPage = computed(() => LIFECYCLE_PAGE_STATES.includes(this.status()));
  /** Header lifecycle-button label: a single "Manage lifecycle" doorway for the states operated on
   *  the dedicated screen (Scheduled activation, Active pause, Paused resume/close); the specific
   *  maker verb (Submit / Approve / Schedule …) for the in-place confirm states. */
  protected readonly primaryActionLabel = computed(() =>
    this.lifecycleUsesDedicatedPage() ? 'Manage lifecycle' : (this.primaryTransition()?.label ?? ''),
  );

  /** Lifecycle popup — Pause / Resume / Close is a modal launched from this page, not a
   *  separate route. */
  protected readonly lifecyclePopupOpen = signal(false);
  /** True while the embedded pause/resume/close action off-canvas is open — the host hides its
   *  own lifecycle popup beneath it, and restores it when the action panel closes. */
  protected readonly lifecyclePanelOpen = signal(false);
  protected closeLifecyclePopup(): void {
    this.lifecyclePopupOpen.set(false);
    this.lifecyclePanelOpen.set(false);
  }
  /** The campaign reference to hand to the popup — this record's own reference when it's
   *  backed by a real shared-store record; otherwise the popup's own seeded demo campaign
   *  (matching the sample fallback this page used before it carried a real ?ref). */
  protected readonly lifecyclePopupRef = computed(() => (this.liveRecord() ? this.reference : 'CAMP-2025-0011'));

  /**
   * The single header lifecycle action. For a live campaign (Active / Paused) it opens
   * the Pause / Resume / Close actions as a popup, carrying this campaign's reference — the
   * "lifecycle primary action → Pause / Resume / Close" flow. For the maker states it opens the
   * in-place high-risk confirm dialog.
   */
  protected runPrimaryLifecycle(): void {
    if (!this.operateAllowed()) {
      return;
    }
    if (this.lifecycleUsesDedicatedPage()) {
      this.lifecyclePopupOpen.set(true);
      return;
    }
    this.openOperate();
  }

  // ----- Operate according to lifecycle: high-risk confirm -----
  protected readonly operateDialogOpen = signal(false);
  protected readonly operateReason = signal('');
  protected readonly operateReasonMin = 10;
  protected readonly operateReasonMax = 2000;
  protected readonly operateReasonCount = computed(() => this.operateReason().trim().length);
  protected readonly operateReasonValid = computed(() => {
    const len = this.operateReason().trim().length;
    return len >= this.operateReasonMin && len <= this.operateReasonMax;
  });
  protected readonly operateReasonTouched = signal(false);

  /** Which of primaryTransition / alternateTransitions the dialog currently proposes. */
  protected readonly selectedTransitionKey = signal('primary');
  protected readonly dialogOptions = computed<readonly LifecycleTransition[]>(() => {
    const primary = this.primaryTransition();
    return primary ? [primary, ...this.alternateTransitions()] : this.alternateTransitions();
  });
  // Annotated with the null it really can be: with no transitions available, `[0]` is
  // undefined and the `?? null` is what runs. Left to inference, TypeScript reads the index as
  // always present, concludes the fallback is dead, and the template's `?.` looks redundant.
  protected readonly proposedTransition = computed<LifecycleTransition | null>(
    () =>
      this.dialogOptions().find((t) => t.key === this.selectedTransitionKey()) ?? this.dialogOptions()[0] ?? null,
  );
  /** Before-and-after values for the decision/review region. */
  protected readonly proposedState = computed<CampaignStatus>(
    () => this.proposedTransition()?.target ?? this.status(),
  );
  protected readonly effectiveTime = '12 May 2025, 02:20 PM · IST';

  protected selectTransition(key: string): void {
    this.selectedTransitionKey.set(key);
  }

  protected openOperate(): void {
    if (!this.operateAllowed()) {
      return;
    }
    this.operateReason.set('');
    this.operateReasonTouched.set(false);
    this.selectedTransitionKey.set('primary');
    this.operateDialogOpen.set(true);
  }
  protected cancelOperate(): void {
    this.operateDialogOpen.set(false);
  }
  /** Require explicit confirmation; change only the authorised record and show a persistent result. */
  protected confirmOperate(): void {
    this.operateReasonTouched.set(true);
    if (!this.operateReasonValid()) {
      return;
    }
    const target = this.proposedState();
    this.status.set(target);
    // Write back to the shared store so every other screen reflects the change immediately.
    if (this.store.get(this.reference)) {
      this.store.update(this.reference, { status: target });
    }
    this.operateDialogOpen.set(false);
    this.toast.show('Lifecycle updated', `${this.reference} is now ${target}.`, 'success');
    this.uiState.set('ready');
  }

  // ================= UI states =================
  protected readonly uiState = signal<UiState>('ready');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  // ----- Export this campaign (per-detail-page export; reuses the register's CSV shape) -----
  protected exportThisCampaign(): void {
    if (!this.exportAllowed()) {
      return;
    }
    const header = [
      'Campaign Code', 'Campaign Name', 'Status', 'Owner', 'Launch Date', 'End Date',
      'Target Amount', 'Reconciled Amount', 'Progress %',
    ];
    const csvField = (value: string | number): string => {
      const s = String(value);
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const row = [
      this.reference, this.campaignName(), this.status(), this.owner(),
      this.launchDate(), this.endDate(), this.targetAmount(), this.reconciledAmount(),
      this.progressPercent(),
    ].map(csvField).join(',');
    const csv = [header.join(','), row].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `campaign-${this.reference}-${Date.now()}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  // ----- Delete draft (permanent removal of an unused draft) -----
  protected readonly deleteDraftDialogOpen = signal(false);
  protected openDeleteDraft(): void {
    if (!this.canDeleteDraft()) {
      return;
    }
    this.deleteDraftDialogOpen.set(true);
  }
  protected cancelDeleteDraft(): void {
    this.deleteDraftDialogOpen.set(false);
  }
  /** Permanently remove the draft from the shared store, then return to the campaign register. */
  protected confirmDeleteDraft(): void {
    if (!this.canDeleteDraft()) {
      return;
    }
    this.store.delete(this.reference);
    this.deleteDraftDialogOpen.set(false);
    this.router.navigate(['/app/fundraising/campaigns/campaign-register']);
  }

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return '₹' + value.toLocaleString('en-IN');
  }
  protected formatDate(iso: string): string {
    if (!iso) {
      return '—';
    }
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
  protected statusClass(status: CampaignStatus): string {
    switch (status) {
      case 'Draft':
        return 'cd-badge-draft';
      case 'Submitted':
        return 'cd-badge-submitted';
      case 'Approved':
        return 'cd-badge-approved';
      case 'Scheduled':
        return 'cd-badge-scheduled';
      case 'Active':
        return 'cd-badge-active';
      case 'Paused':
        return 'cd-badge-paused';
      case 'Closing':
        return 'cd-badge-closing';
      case 'Closed':
        return 'cd-badge-closed';
      case 'Cancelled':
        return 'cd-badge-cancelled';
    }
  }
}
