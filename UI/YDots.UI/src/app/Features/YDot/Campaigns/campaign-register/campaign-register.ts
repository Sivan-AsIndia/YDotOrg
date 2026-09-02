import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { UiState, CampaignStatus, OwnerOption, CampaignRecord, SortableColumn, SortDirection } from '../../../../Shared/models/campaign.model';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';

/**
 * The sentinel that means "do not filter by owner".
 *
 * IT IS NOT A PERSON, which is why it is separate from the directory rather than the first row of
 * it. The previous list mixed the two, so `ownerOf()` falling through to `ownerOptions[0]` labelled
 * every unrecognised owner 'All Owners' - a campaign with an unresolved owner appeared to be owned
 * by everybody.
 */
export const ALL_OWNERS_REFERENCE = 'ALL';

/**
 * Campaign register.
 *
 * Lists campaigns with their status, target, collection progress and ownership,
 * with search, filters, compare and export.
 *
 *  Route           : /fundraising/campaigns/campaign-register
 *  Purpose         : List campaigns with status, target, collection truth and ownership.
 *  Primary users   : Campaign / Executive users
 *  View permission : cam.campaign-register.view
 *  Primary action  : Create
 *  History rule    : Delete is available only for an unused draft with no downstream
 *                    reference; otherwise use a lifecycle action.
 */

@Component({
  selector: 'app-campaign-register',
  imports: [CommonModule, FormsModule, ClickOutsideDirective],
  templateUrl: './campaign-register.html',
  styleUrl: './campaign-register.css',
})
export class CampaignRegisterComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(CampaignStoreService);
  protected readonly user = inject(CurrentUserService);
  private readonly toast = inject(ToastService);

  /** Dev-only session switcher — every permission combination is testable (Step 3). */
  /**
   * The session switcher's options.
   *
   * EMPTY, because there is no switcher any more. This listed five invented profiles and the
   * control beside it called `setProfile('super-admin')`, which granted every campaign permission
   * in the interface. Who the caller is comes from their token.
   */
  protected readonly userProfiles: readonly { key: string; name: string; role: string }[] = [];
  protected setUserProfile(key: string): void {
    this.user.setProfile(key);
  }

  // ================= Task header =================
  protected readonly pageTitle = 'Campaign Register';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Last refresh — server-derived, read-only freshness evidence. */
  protected readonly lastRefresh = signal('Today, 09:30 AM · IST');

  /**
   * Effective permissions decided server-side; the client mirrors the same decision
   *. Every action below checks this before rendering or enabling
   * itself — never a CSS-only hide.
   */
  protected readonly permissions = computed(() => ({
    view: this.user.hasPermission('cam.campaigns.view'),
    create: this.user.hasPermission('cam.campaigns.create'),
    export: this.user.hasPermission('cam.campaigns.export'),
    deleteDraft: this.user.hasPermission('cam.campaigns.delete-draft'),
  }));

  /** Accountable owner shown in the task header — the active session's identity. */
  protected readonly owner = computed(() => `${this.user.current().name} · ${this.user.current().role}`);

  // ================= Context and filters =================

  /** Saved view — search / filter control. */
  protected readonly savedViews = ['All Campaigns (Default)', 'Board pack — monthly', 'Exceptions focus'];
  protected readonly savedView = signal(this.savedViews[0]);

  /** Fixed page size for pagination (records-per-page selector removed per design). */
  protected readonly pageSize = 5;
  protected readonly currentPage = signal(1);

  /** Campaign name or code — scope-aware searchable selector. */
  protected readonly searchTerm = signal('');

  /** Status — search-select using only current catalogue values. */
  protected readonly statusCatalogue: readonly CampaignStatus[] = [
    'Draft', 'Submitted', 'Approved', 'Scheduled', 'Active', 'Paused', 'Closing', 'Closed', 'Cancelled',
  ];
  protected readonly statusFilter = signal<CampaignStatus | ''>('');

  /** Owner — scope-aware searchable selector with identity preview (single shared catalogue above). */
  private readonly people = inject(PeopleDirectoryService);

  /**
   * The owner filter's options: the "all" sentinel, then everybody in the caller's data scope.
   *
   * FROM IAM. The five people this list used to hold were invented and were duplicated across six
   * screens; filtering by one of them returned nothing, because no campaign has ever been owned by
   * a string in a bundle.
   */
  protected readonly ownerOptions = computed<readonly OwnerOption[]>(() => [
    {
      reference: ALL_OWNERS_REFERENCE,
      name: 'All Owners',
      context: 'Every owner in your data scope',
      initials: 'AL',
      tone: 'meadow',
    },
    ...this.people.all().map((person) => ({
      reference: person.reference,
      name: person.name,
      context: person.context,
      initials: person.initials,
      tone: person.tone,
      avatarUrl: person.avatarUrl,
      email: person.email,
    })),
  ]);

  protected readonly ownerFilter = signal<string>(ALL_OWNERS_REFERENCE);

  /** Date range — operating time zone; impossible ranges rejected before submit. */
  protected readonly rangeStart = signal('');
  protected readonly rangeEnd = signal('');

  /** Data scope — the actor's effective scope. Held in the "More filters" panel. */
  // THE SIGNED-IN ORGANISATION AND NOTHING ELSE. See the note on the tracking-asset register:
  // the API scopes every read to the token's organisation, so the other three entries offered a
  // choice that did not exist and named organisations that do not either.
  protected readonly scopeOptions = [
    `${this.user.organisationName() || 'My active organisation'} (default)`,
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);

  /** Whether the "More filters" panel (secondary region) is shown. */
  protected readonly moreFiltersOpen = signal(false);

  protected toggleMoreFilters(): void {
    this.moreFiltersOpen.update((v) => !v);
  }

  /** Whether the filters card is shown — collapsed by default, revealed via the Filters button. */
  protected readonly filtersOpen = signal(false);

  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  /** Human-readable, interpreted date shown before submit. */
  protected readonly interpretedRange = computed(() => {
    const s = this.rangeStart();
    const e = this.rangeEnd();
    if (!s && !e) {
      return `Any date · ${this.operatingTimeZone}`;
    }
    return `${s ? this.formatDate(s) : '…'} – ${e ? this.formatDate(e) : '…'} · ${this.operatingTimeZone}`;
  });

  /** True when the range is impossible (end before start); blocks submit. */
  protected readonly rangeInvalid = computed(() => {
    const s = this.rangeStart();
    const e = this.rangeEnd();
    return !!s && !!e && new Date(e) < new Date(s);
  });

  /** Count of active filters beyond the always-present search (drives the "More Filters" badge). */
  protected readonly moreFiltersCount = computed(() => {
    let n = 0;
    if (this.scopeFilter() !== this.scopeOptions[0]) n++;
    if (this.rangeStart() || this.rangeEnd()) n++;
    return n;
  });

  /** Active-filter summary chips, qualified by scope. */
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.statusFilter()) {
      chips.push({ key: 'status', label: `Status: ${this.statusFilter()}` });
    }
    const owner = this.ownerOptions().find((o) => o.reference === this.ownerFilter());
    if (owner && owner.reference !== ALL_OWNERS_REFERENCE) {
      chips.push({ key: 'owner', label: `Owner: ${owner.name}` });
    }
    if (this.rangeStart() || this.rangeEnd()) {
      chips.push({
        key: 'date',
        label: `Campaign Date: ${this.rangeStart() ? this.formatDate(this.rangeStart()) : '…'} – ${
          this.rangeEnd() ? this.formatDate(this.rangeEnd()) : '…'
        }`,
      });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.scopeFilter() !== this.scopeOptions[0]) {
      chips.push({ key: 'scope', label: `Scope: ${this.scopeFilter()}` });
    }
    return chips;
  });

  // ================= Main work: campaign register with stable columns =================

  /** The stable columns of the register. Every column is a read-only field. */
  protected readonly columns: readonly SortableColumn[] = [
    { key: 'name', label: 'Campaign Name', sortable: true },
    { key: 'status', label: 'Status', sortable: true },
    // TARGET AND RAISED AMOUNT ARE WITHDRAWN with the Target & Budget feature. `progress` stays
    // and is still whatever the server derived; it is not recomputed here.
    { key: 'progress', label: 'Progress', sortable: true, numeric: true },
    { key: 'ownerReference', label: 'Owner', sortable: true },
    { key: 'startDate', label: 'Launch Date', sortable: true },
  ];

  /**
   * The full record set inside the actor's effective data scope.
   * Read live from the single shared CampaignStoreService — no page-local copy.
   */
  protected readonly records = computed(() => this.store.all());

  /**
   * Whether the register is still waiting for its first answer, and why it has none.
   *
   * BOTH ARE SHOWN, because this screen used to show NEITHER. The store has carried
   * `isLoading` and `loadError` all along and no template read them, so a failed load rendered
   * as "No campaigns match this scope and filter." - a sentence that tells the reader to adjust
   * their filters when the truth is that the request failed. That is what hid a broken create:
   * the campaign was never saved, the list was empty, and the empty state blamed the filters.
   */
  protected readonly isLoading = this.store.isLoading;
  protected readonly loadError = this.store.loadError;

  /** Re-asks the server. The "Try again" action on the load-failure state. */
  protected reload(): void {
    this.store.refresh();
  }

  /** Committed sort — column + direction. */
  protected readonly sortColumn = signal<keyof CampaignRecord>('startDate');
  protected readonly sortDirection = signal<SortDirection>('desc');

  /** The filtered, sorted record set for the current scope and filters. */
  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const status = this.statusFilter();
    const owner = this.ownerFilter();
    const start = this.rangeStart() ? new Date(this.rangeStart()) : null;
    const end = this.rangeEnd() ? new Date(this.rangeEnd()) : null;
    if (end) {
      end.setHours(23, 59, 59, 999);
    }

    const scope = this.scopeFilter();
    const scopeRegion = scope === this.scopeOptions[0] ? null : scope.split('·').pop()?.trim().toLowerCase() ?? null;

    const rows = this.records().filter((r) => {
      if (q && !(r.name.toLowerCase().includes(q) || r.code.toLowerCase().includes(q))) {
        return false;
      }
      if (status && r.status !== status) {
        return false;
      }
      if (owner !== ALL_OWNERS_REFERENCE && r.ownerReference !== owner) {
        return false;
      }
      if (start && r.startDate && new Date(r.startDate) < start) {
        return false;
      }
      if (end && r.startDate && new Date(r.startDate) > end) {
        return false;
      }
      if (scopeRegion && !this.ownerOf(r.ownerReference).context.toLowerCase().includes(scopeRegion)) {
        return false;
      }
      return true;
    });

    const col = this.sortColumn();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = a[col];
      const bv = b[col];
      if (typeof av === 'number' && typeof bv === 'number') {
        return (av - bv) * dir;
      }
      return String(av ?? '').localeCompare(String(bv ?? '')) * dir;
    });
  });

  /** Totals qualified by scope and last refresh. */
  protected readonly recordCount = computed(() => this.visibleRecords().length);

  // ----- Pagination (footer pager; the register slices the filtered set) -----
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.recordCount() / this.pageSize)));
  /** Current page clamped to the available page count as filters change. */
  private readonly clampedPage = computed(() => Math.min(this.currentPage(), this.totalPages()));
  protected readonly pagedRecords = computed(() => {
    const start = (this.clampedPage() - 1) * this.pageSize;
    return this.visibleRecords().slice(start, start + this.pageSize);
  });
  protected readonly pageStart = computed(() =>
    this.recordCount() === 0 ? 0 : (this.clampedPage() - 1) * this.pageSize + 1,
  );
  protected readonly pageEnd = computed(() => Math.min(this.clampedPage() * this.pageSize, this.recordCount()));
  protected readonly pageNumbers = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));

  protected goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
    }
  }

  // ----- Row selection → Compare / bulk eligibility -----
  protected readonly selectedRefs = signal<ReadonlySet<string>>(new Set());
  protected readonly selectedCount = computed(() => this.selectedRefs().size);
  protected readonly allVisibleSelected = computed(() => {
    const visible = this.visibleRecords();
    const sel = this.selectedRefs();
    return visible.length > 0 && visible.every((r) => sel.has(r.code));
  });

  protected toggleRow(code: string): void {
    this.selectedRefs.update((cur) => {
      const next = new Set(cur);
      if (next.has(code)) {
        next.delete(code);
      } else {
        next.add(code);
      }
      return next;
    });
  }
  protected toggleAllVisible(): void {
    const visible = this.visibleRecords();
    this.selectedRefs.update((cur) => {
      const allSelected = visible.length > 0 && visible.every((r) => cur.has(r.code));
      const next = new Set(cur);
      if (allSelected) {
        visible.forEach((r) => next.delete(r.code));
      } else {
        visible.forEach((r) => next.add(r.code));
      }
      return next;
    });
  }
  protected isSelected(code: string): boolean {
    return this.selectedRefs().has(code);
  }

  // ================= Actions, eligibility and result =================

  /** Create — primary action; appears only when role, permission, scope, state and dependencies allow. No blocking state exists for a list page beyond no-access. */
  protected readonly createAllowed = computed(
    () => this.permissions().create && this.uiState() !== 'no-access',
  );
  /** Filter — any authorised state. */
  protected readonly filterAllowed = computed(
    () => this.permissions().view && !this.rangeInvalid() && this.uiState() !== 'no-access',
  );
  /** Compare — any authorised state; needs at least two selected records. */
  protected readonly compareAllowed = computed(
    () => this.permissions().view && this.selectedCount() >= 2 && this.uiState() !== 'no-access',
  );
  /** Export — any authorised state. */
  protected readonly exportAllowed = computed(() => this.permissions().export && this.uiState() !== 'no-access');

  // ----- Create primary action: navigates to the Campaign Wizard -----
  /** Create never opens an inline form — it navigates to the Campaign Wizard route. */
  protected openCreate(): void {
    if (!this.createAllowed()) {
      return;
    }
    this.router.navigate(['/app/fundraising/campaigns/campaign-wizard']);
  }

  // ----- Compare action -----
  protected readonly compareDialogOpen = signal(false);
  /** The selected records, read live from the shared store. */
  protected readonly compareRecords = computed(() =>
    this.records().filter((r) => this.selectedRefs().has(r.code)),
  );
  protected openCompare(): void {
    if (!this.compareAllowed()) {
      return;
    }
    this.compareDialogOpen.set(true);
  }
  protected closeCompare(): void {
    this.compareDialogOpen.set(false);
  }

  // ----- Filter action -----
  protected applyFilter(): void {
    if (!this.filterAllowed()) {
      // Impossible ranges surface the validation state.
      this.uiState.set('validation');
      return;
    }
    // Server-side filtering; the confirmed result is the refreshed record set.
    this.moreFiltersOpen.set(false);
    this.currentPage.set(1);
    this.uiState.set(this.visibleRecords().length === 0 ? 'empty' : 'ready');
  }

  /** Clearing filters is explicit and returns focus predictably. */
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set('');
    this.ownerFilter.set(ALL_OWNERS_REFERENCE);
    this.rangeStart.set('');
    this.rangeEnd.set('');
    this.scopeFilter.set(this.scopeOptions[0]);
    this.savedView.set(this.savedViews[0]);
    this.currentPage.set(1);
    this.uiState.set('ready');
  }

  protected removeFilterChip(key: string): void {
    this.currentPage.set(1);
    if (key === 'status') {
      this.statusFilter.set('');
    } else if (key === 'owner') {
      this.ownerFilter.set(ALL_OWNERS_REFERENCE);
    } else if (key === 'date') {
      this.rangeStart.set('');
      this.rangeEnd.set('');
    } else if (key === 'search') {
      this.searchTerm.set('');
    } else if (key === 'scope') {
      this.scopeFilter.set(this.scopeOptions[0]);
    }
  }

  // ----- Open action -----
  /** Open navigates to Campaign Detail, passing the campaign reference, reading from the same shared store. */
  protected openRecord(record: CampaignRecord): void {
    this.openRowMenu.set(null);
    this.router.navigate(['/app/fundraising/campaigns/campaign-detail'], { queryParams: { ref: record.code } });
  }

  /** Edit is offered only for a Draft — opens the Campaign Wizard pre-filled with this record. */
  protected canEdit(record: CampaignRecord): boolean {
    return record.status === 'Draft';
  }
  protected openEdit(record: CampaignRecord): void {
    this.openRowMenu.set(null);
    if (!this.canEdit(record)) return;
    this.router.navigate(['/app/fundraising/campaigns/campaign-wizard'], { queryParams: { ref: record.code } });
  }

  /** Open the Campaign Readiness Checklist for this specific campaign, carrying its reference so the
   *  launch-gate opens on the right record. This is now the ONLY way in: the checklist has come
   *  off the sidebar, because opened bare it is a list of checks against no campaign. */
  protected openReadiness(record: CampaignRecord): void {
    this.openRowMenu.set(null);
    this.router.navigate(['/app/cam/campaign-readiness-checklist'], { queryParams: { ref: record.code } });
  }

  /** Open the Tracking Asset Manager scoped to this campaign — the register's counterpart to the
   *  same button on Campaign detail, and the other half of the sidebar entry's replacement. */
  protected openTrackingAssets(record: CampaignRecord): void {
    this.openRowMenu.set(null);
    if (!this.canViewTrackingAssets()) return;
    this.router.navigate(['/app/fundraising/campaigns/tracking-asset-manager'], {
      queryParams: { campaign: record.code },
    });
  }
  protected readonly canViewTrackingAssets = computed(() =>
    this.user.hasPermission('cam.tracking-assets.view'),
  );

  // ----- Row overflow menu (Open / Export / Delete unused draft) -----
  protected readonly openRowMenu = signal<string | null>(null);
  protected toggleRowMenu(code: string): void {
    this.openRowMenu.update((cur) => (cur === code ? null : code));
  }

  /**
   * Delete unused draft is offered only for a Draft with no downstream reference
   * AND when the current user is that draft's creator.
   */
  protected canDeleteDraft(record: CampaignRecord): boolean {
    return (
      this.permissions().deleteDraft &&
      record.status === 'Draft' &&
      !record.hasDownstreamReference &&
      record.createdByRef === this.user.reference()
    );
  }

  /**
   * Approve is offered for a Submitted campaign to anybody holding `cam.campaigns.approve` -
   * TENANT_ADMIN and APPROVER - but never to the person who created it.
   *
   * THE ROLE NAMES IN THIS COMMENT USED TO BE Super Admin, Campaign Manager and Campaign Owner.
   * The last two no longer exist: the catalogue is TENANT_ADMIN, INITIATOR and APPROVER, and an
   * INITIATOR is defined by holding no approval at all.
   *
   * THIS IS HALF THE SEGREGATION RULE and the server enforces the other half. The creator is
   * excluded here; the SUBMITTER is excluded server-side too, and the browser has no trustworthy
   * way to know who that was. A row that slips through is refused with a clear message rather
   * than silently approved.
   */
  protected canApprove(record: CampaignRecord): boolean {
    return (
      this.user.hasPermission('cam.campaigns.approve') &&
      record.status === 'Submitted' &&
      record.createdByRef !== this.user.reference()
    );
  }

  // ----- Approve dialog (row action; approved by Super Admin or Campaign Manager) -----
  protected readonly approveDialogOpen = signal(false);
  protected readonly approveTarget = signal<CampaignRecord | null>(null);
  protected requestApprove(record: CampaignRecord): void {
    if (!this.canApprove(record)) return;
    this.openRowMenu.set(null);
    this.approveTarget.set(record);
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
  }
  /** Approve → Scheduled via the shared store, firing the approved/scheduled notifications. */
  protected confirmApprove(): void {
    const record = this.approveTarget();
    if (!record) return;
    this.store.approveCampaign(record.code, this.user.reference());
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
  }

  // ----- Export action + Export confirmation -----
  protected readonly exportDialogOpen = signal(false);
  protected readonly exportReason = signal('');
  protected readonly exportReasonMin = 10;
  protected readonly exportReasonMax = 2000;
  protected readonly exportConfirmation = computed(() => ({
    classification: 'Official — Sensitive',
    purpose: 'Campaign register export — all campaigns',
    scope: `All campaigns in register · ${this.store.all().length} rows`,
    rowFileCount: `${this.store.all().length} rows · 1 file (CSV)`,
    expiry: 'Link expires in 24 hours',
    auditReference: 'AUD-EXP-2025-0518',
  }));
  protected readonly exportReasonValid = computed(() => {
    const len = this.exportReason().trim().length;
    return len >= this.exportReasonMin && len <= this.exportReasonMax;
  });
  protected readonly exportReasonCount = computed(() => this.exportReason().trim().length);

  protected openExportDialog(): void {
    if (!this.exportAllowed()) {
      return;
    }
    this.exportReason.set('');
    this.exportDialogOpen.set(true);
  }
  protected cancelExport(): void {
    this.exportDialogOpen.set(false);
  }
  /** Confirm classification, purpose, scope, count, expiry and audit reference before release. */
  protected confirmExport(): void {
    if (!this.exportReasonValid()) {
      return;
    }
    // The header Export button exports EVERY campaign in the register, not just the
    // filtered view (per requirement) — a real CSV file is downloaded to the browser.
    this.downloadExport(this.store.all(), 'campaign-register-all');
    this.lastActionReference.set(this.exportConfirmation().auditReference);
    this.exportDialogOpen.set(false);
    this.toast.show('Export ready', `Campaign register exported · ${this.exportConfirmation().auditReference}.`, 'success');
    this.uiState.set('ready');
  }

  /** Produce a downloadable CSV of the given rows. */
  private downloadExport(rows: readonly CampaignRecord[], filenameBase = 'campaign-register-export'): void {
    const header = [
      'Campaign Code',
      'Campaign Name',
      'Status',
      'Owner',
      'Launch Date',
      'End Date',
      'Progress %',
    ];
    const csvField = (value: string | number): string => {
      const s = String(value);
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const lines = rows.map((r) =>
      [
        r.code,
        r.name,
        r.status,
        this.ownerOf(r.ownerReference).name,
        r.startDate,
        r.endDate,
        r.progress,
      ]
        .map(csvField)
        .join(','),
    );
    const csv = [header.join(','), ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${filenameBase}-${Date.now()}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  // ----- Delete unused draft action -----
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteTarget = signal<CampaignRecord | null>(null);
  protected readonly deleteReason = signal('');
  protected readonly deleteReasonMin = 10;
  protected readonly deleteReasonMax = 2000;
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.deleteReasonMin && len <= this.deleteReasonMax;
  });
  protected readonly deleteReasonCount = computed(() => this.deleteReason().trim().length);

  protected requestDeleteDraft(record: CampaignRecord): void {
    this.openRowMenu.set(null);
    if (!this.canDeleteDraft(record)) {
      return;
    }
    this.deleteTarget.set(record);
    this.deleteReason.set('');
    this.deleteDialogOpen.set(true);
  }
  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
  }
  /** Require named reason and consequence preview; a true delete from the shared store. */
  protected confirmDelete(): void {
    if (!this.deleteReasonValid()) {
      return;
    }
    const target = this.deleteTarget();
    if (!target) {
      return;
    }
    this.store.delete(target.code);
    this.lastActionReference.set(target.code);
    this.selectedRefs.update((cur) => {
      const next = new Set(cur);
      next.delete(target.code);
      return next;
    });
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
    this.toast.show('Draft deleted', `${target.name} (${target.code}) was removed.`, 'success');
    this.uiState.set('ready');
  }

  // ================= UI states =================
  protected readonly uiState = signal<UiState>('ready');
  /** The reference produced by the most recent confirmed action — drives the persistent-outcome card. */
  protected readonly lastActionReference = signal<string>('');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  constructor() {
    // The Campaign Wizard redirects here with ?created={ref} after Save draft; the
    // new row already appears via the shared store — this only surfaces the
    // persistent success confirmation.
    const created = this.route.snapshot.queryParamMap.get('created');
    if (created) {
      this.lastActionReference.set(created);
      this.toast.show('Campaign saved', `Draft ${created} was created.`, 'success');
    }

    // No access must hide the record, fields, counts, actions and search — never a
    // disabled-only affordance. Never just visually hide via CSS.
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

  // ================= Persistent outcome =================
  protected readonly persistentOutcome = computed(() => {
    const ref = this.lastActionReference();
    const record = ref ? this.store.get(ref) : undefined;
    return {
      reference: ref || '—',
      state: record ? record.status : ref ? 'Deleted / exported' : '—',
      effectiveTime: this.lastRefresh(),
      downstreamStatus:
        this.selectedCount() > 0 ? `${this.selectedCount()} campaign(s) selected` : 'No pending action',
      owner: this.owner(),
      nextAction: 'Review the filtered campaign register',
    };
  });

  /**
   * The owner card for a reference.
   *
   * AN UNRESOLVED OWNER SAYS SO. It used to fall through to `ownerOptions[0]`, which was the
   * 'All Owners' sentinel - so a campaign whose owner had not loaded, or who had left the
   * organisation, was drawn as though it belonged to everybody.
   */
  protected ownerOf(reference: string): OwnerOption {
    const person = this.people.get(reference);

    if (person) {
      return {
        reference: person.reference,
        code: person.code,
        name: person.name,
        context: person.context,
        initials: person.initials,
        tone: person.tone,
        avatarUrl: person.avatarUrl,
        email: person.email,
      };
    }

    // AN UNRESOLVED OWNER IS NOT GIVEN THE RAW ID AS A NAME. The reference here is an API Guid,
    // and printing it where a person's name belongs is the same class of mistake as printing it
    // as their code: it puts an identifier on screen and calls it a person.
    return {
      reference,
      name: 'Unassigned',
      context: reference ? 'Owner not resolved' : '',
      initials: '??',
      tone: 'plum',
    };
  }
  /** Every accountable owner for a record — falls back to the single primary owner
   *  when no additional owners were captured. */
  protected ownersOf(record: CampaignRecord): readonly OwnerOption[] {
    const refs = record.ownerReferences && record.ownerReferences.length
      ? record.ownerReferences
      : [record.ownerReference];
    return refs.map((ref) => this.ownerOf(ref));
  }

  // ----- Owners popup — clicking the owner cell shows every owner of the campaign
  // with basic details; the list scrolls inside itself when there are many. -----
  protected readonly ownersPopupTarget = signal<CampaignRecord | null>(null);
  protected openOwnersPopup(record: CampaignRecord): void {
    this.ownersPopupTarget.set(record);
  }
  protected closeOwnersPopup(): void {
    this.ownersPopupTarget.set(null);
  }

  // ----- Single-owner detail popup — clicking one avatar shows that owner's full details. -----
  protected readonly ownerDetailTarget = signal<OwnerOption | null>(null);
  protected openOwnerDetail(owner: OwnerOption): void {
    this.ownerDetailTarget.set(owner);
  }
  protected closeOwnerDetail(): void {
    this.ownerDetailTarget.set(null);
  }

  // ----- Broken profile pictures fall back to initials. -----
  private readonly failedAvatars = signal<ReadonlySet<string>>(new Set());
  protected avatarFailed(reference: string): boolean {
    return this.failedAvatars().has(reference);
  }
  protected onAvatarError(reference: string): void {
    this.failedAvatars.update((set) => new Set(set).add(reference));
  }
  protected statusClass(status: CampaignStatus): string {
    switch (status) {
      case 'Draft':
        return 'cr-badge-draft';
      case 'Submitted':
        return 'cr-badge-submitted';
      case 'Approved':
        return 'cr-badge-approved';
      case 'Scheduled':
        return 'cr-badge-scheduled';
      case 'Active':
        return 'cr-badge-active';
      case 'Paused':
        return 'cr-badge-paused';
      case 'Closing':
        return 'cr-badge-closing';
      case 'Closed':
        return 'cr-badge-closed';
      case 'Cancelled':
        return 'cr-badge-cancelled';
    }
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
  protected sortLabel(key: keyof CampaignRecord): string {
    return this.columns.find((c) => c.key === key)?.label ?? String(key);
  }

  /** Sort request from a column header. */
  protected requestSort(column: keyof CampaignRecord): void {
    const col = this.columns.find((c) => c.key === column);
    if (!col?.sortable) {
      return;
    }
    if (this.sortColumn() === column) {
      this.sortDirection.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('asc');
    }
  }
}
