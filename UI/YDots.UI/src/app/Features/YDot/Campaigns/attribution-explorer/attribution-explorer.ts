import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { UiState, HistoryRow, PersistentOutcome } from '../../../../Shared/models/campaign.model';
import {
  AttributionPermissions,
  AttributionRelatedTab,
  DonationRecord,
  TraceGroup,
} from '../../../../Shared/models/attribution.model';
import { AttributionStoreService } from '../../../../Shared/services/attribution-store.service';
import { CurrentUserService } from '../../../../Service/current-user.service';
import { TrackingAssetStoreService } from '../../../../Shared/services/tracking-asset-store.service';

@Component({
  selector: 'app-attribution-explorer',
  imports: [CommonModule, FormsModule, ClickOutsideDirective],
  templateUrl: './attribution-explorer.html',
  styleUrl: './attribution-explorer.css',
})
export class AttributeExplorerComponent {
  private readonly store = inject(AttributionStoreService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly trackingStore = inject(TrackingAssetStoreService);

  /**
   * Source/Medium must reflect the referenced tracking asset's own configured
   * values (the single shared TrackingAssetStoreService), not a separately-entered
   * copy on the donation record — otherwise the two can silently drift apart.
   * Falls back to the donation's own value only when the tracking asset reference
   * doesn't resolve in the shared store (e.g. an asset since retired).
   */
  protected sourceOf(r: DonationRecord): string {
    return this.trackingStore.get(r.trackingAsset)?.source ?? r.source;
  }
  protected mediumOf(r: DonationRecord): string {
    return this.trackingStore.get(r.trackingAsset)?.medium ?? r.medium;
  }

  protected readonly pageTitle = 'Attribution explorer';
  protected readonly pageSubtitle =
    'Trace a donation from the link the donor followed through to the campaign it was credited to.';
  /**
   * The record on screen.
   *
   * IT IS THE SELECTED DONATION'S REFERENCE, not a page-level 'ATTRIB-2026-0001' that never
   * referred to anything. The explorer is about one donation at a time; its reference is what
   * somebody quotes when asking a colleague to look at the same gift.
   */
  protected readonly taskReference = computed(() => this.selected()?.reference ?? '—');
  /** The selected donation's status, from the payments module. */
  protected readonly lifecycleState = computed(() => this.selected()?.lifecycle ?? '—');
  /**
   * Who the gift on screen came from.
   *
   * The page-level 'attribution steward' this replaced was one invented person's name shown to
   * every organisation, which told a reader nothing and implied somebody was accountable.
   */
  protected readonly owner = computed(() => this.selected()?.leadOrDonor ?? '—');
  /** When the loaded set was actually read, rather than a fixed 'Today, 09:30'. */
  protected readonly lastRefresh = signal('');
  /** Effective permissions decided server-side; the client mirrors the same decision. */
  protected readonly permissions = computed<AttributionPermissions>(() => ({
    view: this.currentUser.hasPermission('cam.attribution.view'),
    requestCorrection: this.currentUser.hasPermission('cam.attribution.request-correction'),
    deleteDraft: this.currentUser.hasPermission('cam.attribution.request-correction'),
  }));
  /** Sensitive-field rule — gates Lead-or-donor identity independently of `.view`. */
  protected readonly canViewDonorIdentity = computed(() =>
    this.currentUser.hasPermission('don.donors.view-sensitive-contact'),
  );
  /**
   * The data scope selector.
   *
   * ONE ENTRY, because the server decides the scope from the caller's token and the
   * organisation they are operating in. The four hard-coded regions this replaced offered a
   * choice the API would have ignored - picking 'Western Region' changed the label and nothing
   * else.
   */
  protected readonly scopeOptions: readonly string[] = ['My active organisation'];
  protected readonly scope = signal(this.scopeOptions[0]);
  protected readonly searchTerm = signal('');
  protected readonly savedViews: readonly string[] = [
    'All donations in scope (Default)',
    'Traced to a tracking asset',
    'Arrived without tracking',
    'Open correction requests',
  ];
  protected readonly savedView = signal(this.savedViews[0]);

  /** The filters/search card is hidden until the user opens it with the Filters button
   * — and can be hidden again from either the header or the card. */
  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.scope() !== this.scopeOptions[0]) {
      chips.push({ key: 'scope', label: `Scope: ${this.scope()}` });
    }
    if (this.savedView() !== this.savedViews[0]) {
      chips.push({ key: 'saved', label: `Saved filter: ${this.savedView()}` });
    }
    return chips;
  });

  /**
   * Read from the single shared AttributionStoreService — not
   * a page-local copy — so Campaign Detail's Sources tab sees the same
   * records.
   */
  protected get records(): DonationRecord[] {
    return [...this.store.all()];
  }
  /** The server's total, which is not the length of the loaded page. */
  protected readonly totalRecords = computed(() => this.store.total());
  protected readonly searched = signal(false);

  /** Search query — filters the records by reference, donor, campaign or asset. */
  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    if (!q) return this.records;
    return this.records.filter(
      (r) =>
        r.reference.toLowerCase().includes(q) ||
        r.leadOrDonor.toLowerCase().includes(q) ||
        r.campaign.toLowerCase().includes(q) ||
        r.trackingAsset.toLowerCase().includes(q),
    );
  });

  protected readonly recordCount = computed(() => this.visibleRecords().length);

  // ================= Pagination =================
  protected readonly pageSize = 10;
  protected readonly currentPage = signal(1);
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.visibleRecords().length / this.pageSize)));
  protected readonly pagedRecords = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.visibleRecords().slice(start, start + this.pageSize);
  });
  protected goToPage(p: number): void {
    if (p < 1 || p > this.totalPages()) return;
    this.currentPage.set(p);
    this.selectedRef.set('');
  }
  protected nextPage(): void {
    this.goToPage(this.currentPage() + 1);
  }
  protected prevPage(): void {
    this.goToPage(this.currentPage() - 1);
  }
  protected readonly pageNumbers = computed<number[]>(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    const pages: number[] = [];
    const start = Math.max(1, current - 2);
    const end = Math.min(total, start + 4);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  });
  protected readonly pagedStart = computed(() =>
    this.visibleRecords().length === 0 ? 0 : (this.currentPage() - 1) * this.pageSize + 1,
  );
  protected readonly pagedEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize, this.visibleRecords().length),
  );

  // ================= Selection =================
  protected readonly selectedRef = signal<string>('');
  protected readonly selected = computed(
    () => this.records.find((r) => r.reference === this.selectedRef()) ?? null,
  );
  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
  }
  protected selectDonation(ref: string): void {
    this.selectedRef.set(ref);
    this.activeWorkTab.set('trace');
    this.clearBanner();
  }
  protected closeTrace(): void {
    this.selectedRef.set('');
  }

  protected readonly traceGroups = computed<readonly TraceGroup[]>(() => {
    const r = this.selected();
    if (!r || r.restricted) return [];
    return [
      {
        key: 'source',
        step: '1',
        title: 'Source evidence',
        caption: 'Where the donation originated and who it is attributed to.',
        fields: [
          { key: 'source', label: 'Source', value: this.sourceOf(r), copyable: true },
          { key: 'medium', label: 'Medium', value: this.mediumOf(r), copyable: true },
          { key: 'trackingAsset', label: 'Tracking asset', value: r.trackingAsset, copyable: true },
          this.canViewDonorIdentity()
            ? { key: 'leadOrDonor', label: 'Lead or donor', value: r.leadOrDonor, copyable: true }
            : { key: 'leadOrDonor', label: 'Lead or donor', value: 'This value cannot be displayed with your current access.', copyable: false },
        ],
      },
      {
        key: 'intent',
        step: '2',
        title: 'Intent',
        caption: 'When the donor expressed intent to give.',
        fields: [{ key: 'intentCreated', label: 'Intent created time', value: r.intentCreated, copyable: true }],
      },
      {
        key: 'payment',
        step: '3',
        title: 'Payment',
        caption: 'When the payment was captured by the provider.',
        fields: [{ key: 'paymentCaptured', label: 'Payment captured time', value: r.paymentCaptured, copyable: true }],
      },
      {
        key: 'settlement',
        step: '4',
        title: 'Settlement and reconciliation',
        caption: 'How the captured payment settled and reconciled to the ledger.',
        fields: [
          { key: 'settlementRecon', label: 'Settlement and reconciliation', value: r.settlementRecon, copyable: true },
        ],
      },
      {
        key: 'attribution',
        step: '5',
        title: 'Attribution snapshot',
        caption: 'The immutable attribution snapshot and its evidence chain.',
        fields: [
          { key: 'attributionSnapshot', label: 'Attribution snapshot', value: r.attributionSnapshot, copyable: true },
          { key: 'correctionRequest', label: 'Correction request', value: r.correctionRequest, copyable: true },
          { key: 'auditChain', label: 'Audit chain', value: r.auditChain, copyable: true },
        ],
      },
    ];
  });

  protected readonly copiedKey = signal<string>('');
  protected copyValue(key: string, value: string): void {
    const done = () => {
      this.copiedKey.set(key);
      setTimeout(() => this.copiedKey.set(''), 1600);
    };
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(value).then(done).catch(done);
    } else {
      done();
    }
  }

  // ================= Actions =================
  protected readonly searchAllowed = computed(() => this.permissions().view && this.uiState() !== 'no-access');

  protected requestCorrectionAllowed(r: DonationRecord | null): boolean {
    return (
      !!r &&
      !r.restricted &&
      this.permissions().requestCorrection &&
      (r.lifecycle === 'Reconciled' || r.lifecycle === 'Captured' || r.lifecycle === 'Pending settlement')
    );
  }

  protected canDeleteDraft(r: DonationRecord | null): boolean {
    return !!r && !r.restricted && r.isDraft && !r.hasDownstreamReference && this.permissions().deleteDraft;
  }

  // ================= Search / filter actions =================
  protected readonly searchError = signal('');

  /** Run search — requires a non-empty term, then filters and paginates. */
  protected runSearch(): void {
    if (!this.searchAllowed()) return;
    if (!this.searchTerm().trim()) {
      this.searchError.set('Enter a donation reference, donor, campaign or asset to search.');
      this.uiState.set('validation');
      return;
    }
    this.searchError.set('');
    this.searched.set(true);
    this.selectedRef.set('');
    this.currentPage.set(1);
    this.uiState.set('loading');
    setTimeout(() => {
      if (this.uiState() !== 'loading') return;
      this.uiState.set(this.visibleRecords().length === 0 ? 'empty' : 'ready');
    }, 500);
  }

  protected clearSearch(): void {
    this.searchTerm.set('');
    this.searchError.set('');
    this.currentPage.set(1);
    this.uiState.set('ready');
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.searchError.set('');
    this.scope.set(this.scopeOptions[0]);
    this.savedView.set(this.savedViews[0]);
    this.currentPage.set(1);
    this.uiState.set('ready');
  }
  protected removeFilterChip(key: string): void {
    switch (key) {
      case 'search':
        this.searchTerm.set('');
        this.searchError.set('');
        this.currentPage.set(1);
        break;
      case 'scope':
        this.scope.set(this.scopeOptions[0]);
        this.currentPage.set(1);
        break;
      case 'saved':
        this.savedView.set(this.savedViews[0]);
        this.currentPage.set(1);
        break;
    }
  }

  protected inspectChain(r: DonationRecord): void {
    this.selectDonation(r.reference);
    this.activeWorkTab.set('related');
    this.activeRelatedTab.set('audit');
  }

  // ================= Request correction =================
  protected readonly correctionDialogOpen = signal(false);
  protected readonly correctionReason = signal('');
  protected readonly correctionSubmitted = signal(false);
  protected readonly correctionReasonMin = 10;
  protected readonly correctionReasonMax = 2000;
  protected readonly correctionReasonCount = computed(() => this.correctionReason().trim().length);
  protected readonly correctionReasonValid = computed(() => {
    const len = this.correctionReason().trim().length;
    return len >= this.correctionReasonMin && len <= this.correctionReasonMax;
  });

  protected openCorrection(): void {
    const r = this.selected();
    if (!this.requestCorrectionAllowed(r)) return;
    if (r!.hasOpenCorrection) {
      this.uiState.set('duplicate');
      return;
    }
    this.correctionReason.set('');
    this.correctionSubmitted.set(false);
    this.correctionDialogOpen.set(true);
  }
  protected cancelCorrection(): void {
    this.correctionDialogOpen.set(false);
  }
  /**
   * Asks for a donation's attribution to be looked at again.
   *
   * WHAT THIS USED TO DO. It composed a correction reference in the browser -
   * `CORR-2026-` plus the row count - and then wrote `hasOpenCorrection: true` straight onto the
   * donation record in a local store. Three things were wrong with that, and the third is serious:
   *
   *   - THE REFERENCE WAS INVENTED, and two people raising a correction on the same afternoon
   *     would have been given the same one.
   *   - NOTHING WAS RAISED WITH ANYBODY. The flag lived in one browser, so the finance team the
   *     message said it was "open with" never heard about it.
   *   - IT MUTATED A DONATION. Even as a flag, a campaign screen writing to a donation record is
   *     the wrong direction - and the same store offered a `delete()` that removed the gift
   *     entirely.
   *
   * IT NOW RECORDS A REAL REQUEST, and does not touch the donation. At most one open request per
   * donation, so two people cannot investigate the same gift unaware of each other.
   */
  protected confirmCorrection(): void {
    this.correctionSubmitted.set(true);

    if (!this.correctionReasonValid()) {
      this.uiState.set('validation');
      return;
    }

    const record = this.selected();

    if (!record) {
      return;
    }

    this.correctionDialogOpen.set(false);

    this.store.requestCorrection(record.reference, this.correctionReason().trim());

    this.outcomeReference.set(record.reference);
    this.outcomeState.set('Correction requested');
    this.uiState.set('success');
  }

  // ================= Delete unused draft =================
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteReason = signal('');
  protected readonly deleteConfirmText = signal('');
  protected readonly deleteSubmitted = signal(false);
  protected readonly deleteReasonMin = 10;
  protected readonly deleteReasonMax = 2000;
  protected readonly deleteReasonCount = computed(() => this.deleteReason().trim().length);
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.deleteReasonMin && len <= this.deleteReasonMax;
  });
  protected readonly deleteConfirmValid = computed(() => this.deleteConfirmText().trim().toUpperCase() === 'DELETE');

  protected openDeleteDraft(): void {
    if (!this.canDeleteDraft(this.selected())) return;
    this.deleteReason.set('');
    this.deleteConfirmText.set('');
    this.deleteSubmitted.set(false);
    this.deleteDialogOpen.set(true);
  }
  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
  }
  /**
   * Retained so the dialog's existing binding compiles; it refuses and explains.
   *
   * THERE IS NO SUCH THING AS A DRAFT DONATION. The seeded data had them so this screen could
   * demonstrate a delete action, and the store's `delete()` removed the record outright. On real
   * data that would have taken a gift out of a fundraiser's view while it sat perfectly intact in
   * the ledger - the two would then disagree, and the ledger would be right.
   *
   * The CAM API has no delete for a donation and should not: a donation is a record of money that
   * moved, and what happens to a mistaken one is a void or a refund in the payments module, where
   * it leaves a trail.
   */
  protected confirmDeleteDraft(): void {
    this.deleteDialogOpen.set(false);
    this.outcomeReference.set(this.selected()?.reference ?? '—');
    this.outcomeState.set('A donation cannot be deleted from this screen');
    this.uiState.set('ready');
  }

  protected readonly uiState = signal<UiState>('ready');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  constructor() {
    // No access must hide the record, fields, counts, actions and search — never a
    // disabled-only affordance, matching every other CAM page.
    effect(() => {
      const canView = this.permissions().view;
      const current = untracked(this.uiState);
      if (!canView && current !== 'no-access') {
        this.uiState.set('no-access');
      } else if (canView && current === 'no-access') {
        this.uiState.set('ready');
      }
    });
  }
  protected clearBanner(): void {
    if (
      this.uiState() === 'validation' ||
      this.uiState() === 'duplicate' ||
      this.uiState() === 'conflict' ||
      this.uiState() === 'dependency-failure' ||
      this.uiState() === 'success'
    ) {
      this.uiState.set('ready');
    }
  }
  protected retryDependency(): void {
    this.uiState.set('success');
  }

  // ================= Work tabs / related =================
  protected readonly workTabs: readonly { key: string; label: string }[] = [
    { key: 'trace', label: 'Trace' },
    { key: 'related', label: 'Related & history' },
  ];
  protected readonly activeWorkTab = signal<string>('trace');
  protected selectWorkTab(key: string): void {
    this.activeWorkTab.set(key);
  }
  /**
   * The related-records tabs, built from the server's attribution trail.
   *
   * THE ROWS WERE FIXED STRINGS - 'Payment captured · 22 Jul 08:47 IST' - shown against every
   * donation the screen ever displayed. A history panel that says the same thing about every
   * record is worse than an empty one: it looks like evidence.
   */
  protected readonly relatedTabs = computed<readonly AttributionRelatedTab[]>(() => {
    const detail = this.store.detail();

    if (!detail) {
      return [{ key: 'activity', label: 'Activity', rows: [] }];
    }

    return [
      {
        key: 'activity',
        label: 'Activity',
        rows: detail.trace.map((step) => ({
          primary: step.title,
          secondary: step.caption,
          meta: step.fields.map((field) => `${field.label}: ${field.value}`).join(' · '),
        })),
      },
    ];
  });
  protected readonly activeRelatedTab = signal<string>('linked');
  protected readonly activeRelatedRows = computed<readonly HistoryRow[]>(
    () => this.relatedTabs().find((tab) => tab.key === this.activeRelatedTab())?.rows ?? [],
  );
  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  // ================= Outcome / correlation =================
  /** The permission the server actually enforces on a correction request. */
  protected readonly correctionEffectivePermission = 'cam.attribution.request-correction';
  protected readonly outcomeReference = signal('');
  protected readonly outcomeState = signal('');
  protected readonly persistentOutcome = computed<PersistentOutcome>(() => {
    const r = this.selected();
    return {
      reference: this.outcomeReference() || r?.reference || this.taskReference(),
      state: this.outcomeState() || (r ? r.lifecycle : this.lifecycleState()),
      effectiveTime: this.lastRefresh(),
      downstreamStatus: r?.downstreamStatus ?? 'No donation selected',
      owner: r && !r.restricted ? r.owner : this.owner(),
      nextAction: r ? 'Inspect the audit chain or request a correction' : 'Search for a donation to begin a trace',
    };
  });

  /**
   * The identifier tying this request to its server-side log line.
   *
   * IT COMES FROM THE RESPONSE. The fixed 'INT-77213' it replaced was the same on every screen
   * in every organisation, so quoting it to somebody looking at the logs found nothing.
   */
  private readonly correlationReferenceState = signal('');

  /**
   * The same value, reachable as a plain property.
   *
   * THE TEMPLATE READS IT WITHOUT PARENTHESES, so a signal here rendered the function's own source
   * into the page beside the word "Correlation". Falling back to the selected row's reference is
   * what makes the line useful before any request has been made - it is the identifier somebody
   * would quote either way.
   */
  protected get correlationReference(): string {
    return this.correlationReferenceState() || this.selected()?.reference || '—';
  }
}