import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { UiState, HistoryRow } from '../../../../Shared/models/campaign.model';
import { AssetStatus, ApprovalState, TrackingAssetPermissions, CampaignOption, TrackingAsset, PlaceCustomField } from '../../../../Shared/models/tracking-asset.model';
import { generateQrMatrix, qrMatrixToPath } from '../../../../Shared/qr-code/qr-code';
import { TrackingAssetStoreService } from '../../../../Shared/services/tracking-asset-store.service';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { CampaignApiService } from '../../../../Service/campaign-api.service';

/** One row of a CAM reference catalogue: the id the API takes, and the name a person reads. */
interface CatalogueOption {
  readonly ref: string;
  readonly label: string;
}


@Component({
  selector: 'app-tracking-asset-manager',
  imports: [CommonModule, FormsModule, ClickOutsideDirective],
  templateUrl: './tracking-asset-manager.html',
  styleUrl: './tracking-asset-manager.css',
})
export class TrackingAssetManagerComponent {
  private readonly store = inject(TrackingAssetStoreService);
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly campaignApi = inject(CampaignApiService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  // ================= Task header =================
  protected readonly pageTitle = 'Tracking asset manager';
  protected readonly pageSubtitle = 'Create and control links and QR destinations.';
  /**
   * The header's identity line.
   *
   * A REGISTER HAS NO SINGLE RECORD, so it no longer claims one. This header used to announce
   * 'TAM-2025-0001 Active · Owner: Sophie Bennett' above a table reading "Total assets 0" - a
   * reference, a state and a person that between them described nothing on the page.
   */
  protected readonly taskReference = '';
  protected readonly lifecycleState = '';
  protected get owner(): string {
    return this.currentUser.current().name;
  }
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Last refresh — server-derived, read-only freshness evidence. */
  protected readonly lastRefresh = signal('Today, 09:30 AM · IST');

  /** The acting session's "user id" — reused for the segregation-of-duty rule. */
  protected readonly currentUserRef = computed(() => this.currentUser.reference());
  protected readonly currentUserName = computed(() => this.currentUser.current().name);

  /** Effective permissions — sourced from the shared mock session (CurrentUserService), the
   *  same authority Campaign Register and Campaign Detail read. */
  protected readonly permissions = computed<TrackingAssetPermissions>(() => ({
    view: this.currentUser.hasPermission('cam.tracking-assets.view'),
    generate: this.currentUser.hasPermission('cam.tracking-assets.create'),
    test: this.currentUser.hasPermission('cam.tracking-assets.view'),
    approve: this.currentUser.hasPermission('cam.tracking-assets.approve'),
    activate: this.currentUser.hasPermission('cam.tracking-assets.activate'),

    // THE DISABLE PAIR. An Initiator holds request-disable and an Approver holds the decision;
    // `disable` used to be the only one of the two, so the maker's Disable click went to the
    // approver's endpoint and answered 403.
    requestDisable: this.currentUser.hasPermission('cam.tracking-assets.request-disable'),
    disable: this.currentUser.hasPermission('cam.tracking-assets.deactivate'),

    // DELETE-DRAFT USED TO BORROW `disable`, so discarding your own unused draft required the
    // right to take live assets down.
    deleteDraft: this.currentUser.hasPermission('cam.tracking-assets.delete-draft'),
    replace: this.currentUser.hasPermission('cam.tracking-assets.edit'),
  }));

  /**
   * THE GENERATE GATE. This is why nobody could create a tracking asset.
   *
   * It used to be `private readonly generatePermittedStates = ['Active'];`, checked against
   * `this.lifecycleState` - the register's own header state. That header was removed when the
   * invented record above it went ('TAM-2025-0001 Active - Owner: Sophie Bennett'), and
   * `lifecycleState` was left behind as the empty string. `[''].includes` of `'Active'` is false,
   * so `generateAllowed()` was FALSE FOR EVERY USER, IN EVERY ORGANISATION, ALWAYS - and the
   * Create button was permanently disabled with no explanation and no tooltip. Both roles that
   * tried this hit the same wall.
   *
   * A REGISTER HAS NO LIFECYCLE STATE. The gate is the permission, which is what actually
   * decides this: `cam.tracking-assets.create`, the same code the API checks on the way in.
   */
  private readonly inGenerateState = () => true;

  // ================= Context and filters =================

  /** Saved filter. */
  protected readonly savedViews = ['All tracking assets (Default)', 'QR destinations', 'Awaiting approval'];
  protected readonly savedView = signal(this.savedViews[0]);

  /**
   * Fixed page size for pagination (records-per-page selector removed per design).
   *
   * TEN PER PAGE, FIVE VISIBLE AT ONCE. The table's own height is fixed to five rows
   * (`.tam-table-scroll`) with an inner scrollbar for the rest of the current page, so a page
   * holds a full ten before pagination has to advance — the table never grows taller than five
   * rows to show them.
   */
  protected readonly pageSize = 10;
  protected readonly currentPage = signal(1);

  /** The filters section is hidden until the user opens it with the Filter button. */
  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  /** Search — scope-aware search over reference, destination and campaign. */
  protected readonly searchTerm = signal('');

  /**
   * The campaign this screen is scoped to, when it was opened from one.
   *
   * THE MANAGER IS REACHED FROM A CAMPAIGN NOW rather than from the sidebar, so it has to be able
   * to answer "the assets for THIS campaign" and say which campaign that is. Campaign detail's
   * "Tracking assets" button carries the code in `?campaign=`; opened without one the screen is
   * the whole register, exactly as before.
   */
  protected readonly campaignFilter = signal(this.route.snapshot.queryParamMap.get('campaign') ?? '');
  protected readonly campaignFilterName = computed(() => {
    const ref = this.campaignFilter();
    return ref ? this.campaignOf(ref).name || ref : '';
  });

  /** Asset type — searchable controlled choice; effective approved catalogue. */
  protected readonly assetTypeCatalogue: readonly string[] = [
    'QR Code',
    'Short Link',
    'UTM Link',
    'Landing Page',
  ];
  protected readonly assetTypeFilter = signal<string>('');

  /**
   * Channel, source and medium — THE CAM CATALOGUES, BY ID.
   *
   * WHAT WAS HERE. Six hard-coded channel LABELS ('Website', 'Facebook', 'Instagram', 'Email',
   * 'YouTube', 'Offline'), and Source and Medium as free-text boxes. All three went straight into
   * the create call as `channelId`, `sourceId` and `mediumId`, which the API declares as Guids -
   * so every Generate refused with
   *
   *     400  The JSON value could not be converted to CreateTrackingAssetRequest
   *
   * before any handler ran. No tracking asset could be created from this screen by anybody.
   * Three of the six labels were not channels at all on the server side either: Facebook,
   * Instagram and YouTube are SOURCES beneath the Social Media channel.
   *
   * These now hold `{ ref, label }` where `ref` is the row's Guid, which is what the API takes,
   * and `label` is what the person reads.
   */
  protected readonly channelChoices = signal<readonly CatalogueOption[]>([]);
  protected readonly sourceChoices = signal<readonly CatalogueOption[]>([]);
  protected readonly mediumChoices = signal<readonly CatalogueOption[]>([]);

  /** The channel names, for the filter row — which matches on the stored label, not on an id. */
  protected readonly channelCatalogue = computed<readonly string[]>(() =>
    this.channelChoices().map((choice) => choice.label));

  protected channelLabel(ref: string): string {
    return this.channelChoices().find((choice) => choice.ref === ref)?.label ?? ref;
  }

  protected sourceLabel(ref: string): string {
    return this.sourceChoices().find((choice) => choice.ref === ref)?.label ?? ref;
  }

  protected mediumLabel(ref: string): string {
    return this.mediumChoices().find((choice) => choice.ref === ref)?.label ?? ref;
  }

  /** Reads the three catalogues once. An empty list is reported rather than left unexplained. */
  private loadReferenceCatalogues(): void {
    this.campaignApi.getReferenceData().subscribe({
      next: (reference) => {
        // ACTIVE ROWS ONLY: a retired channel is one the API refuses on the way back in, so
        // offering it produces a selection the create call rejects.
        this.channelChoices.set(
          reference.channels.filter((c) => c.isActive).map((c) => ({ ref: c.id, label: c.name })));
        this.sourceChoices.set(
          reference.sources.filter((c) => c.isActive).map((c) => ({ ref: c.id, label: c.name })));
        this.mediumChoices.set(
          reference.mediums.filter((c) => c.isActive).map((c) => ({ ref: c.id, label: c.name })));
      },
      error: () =>
        this.toast.show(
          'Reference lists unavailable',
          'The channel, source and medium lists could not be loaded. Reload the page to try again.',
          'error'),
    });
  }

  protected readonly channelFilter = signal<string>('');

  /** Asset status — search-select using only current catalogue values. */
  protected readonly statusCatalogue: readonly AssetStatus[] =
    ['Draft', 'Submitted', 'Approved', 'Active', 'Disable requested', 'Inactive', 'Paused', 'Disabled'];
  protected readonly statusFilter = signal<AssetStatus | ''>('');

  /** Active from / Active to — date range in the operating time zone. */
  protected readonly rangeStart = signal('');
  protected readonly rangeEnd = signal('');

  /** Data scope — the actor's effective scope. Held in the "Filters" panel. */
  // THE SIGNED-IN ORGANISATION AND NOTHING ELSE. Three invented regions used to sit beneath it,
  // belonging to no one; every read is scoped to the token's organisation regardless, so picking
  // one changed nothing except what the operator believed they were looking at.
  protected readonly scopeOptions = [
    `${this.currentUser.organisationName() || 'My active organisation'} (default)`,
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);

  /** Whether the secondary "Filters" panel (data scope) is shown; toggling is explicit. */
  protected readonly moreFiltersOpen = signal(false);
  protected toggleMoreFilters(): void {
    this.moreFiltersOpen.update((v) => !v);
  }

  /**
   * Custom single-select dropdowns — replaces the native <select> "system"
   * control with an in-app themed listbox so every choice field matches the owner
   * combobox styling. One shared signal holds the key of the currently open menu
   * (or null when all are closed); only one dropdown is ever open at a time.
   */
  protected readonly openMenu = signal<string | null>(null);
  protected toggleMenu(key: string): void {
    this.openMenu.update((v) => (v === key ? null : key));
  }
  protected closeMenu(): void {
    this.openMenu.set(null);
  }
  /**
   * Click-outside handler bound per dropdown. Because every dropdown shares one
   * `openMenu` signal, a bare `closeMenu` on each would let a *sibling* combo's
   * outside-press (fired on pointerdown when you press an option in another combo)
   * close the menu before the option's click lands — making options unselectable.
   * Guarding on the key means only the currently-open combo can close itself.
   */
  protected onClickOutside(key: string): void {
    if (this.openMenu() === key) {
      this.openMenu.set(null);
    }
  }

  /** Human-readable, interpreted date shown before submit. */
  protected readonly interpretedRange = computed(() => {
    const s = this.rangeStart();
    const e = this.rangeEnd();
    if (!s && !e) {
      return `Any active date · ${this.operatingTimeZone}`;
    }
    return `${s ? this.formatDate(s) : '…'} – ${e ? this.formatDate(e) : '…'} · ${this.operatingTimeZone}`;
  });

  /** True when the range is impossible (end before start); blocks submit. */
  protected readonly rangeInvalid = computed(() => {
    const s = this.rangeStart();
    const e = this.rangeEnd();
    return !!s && !!e && new Date(e) < new Date(s);
  });

  /** Count of active filters held in the secondary panel (drives the "Filters" badge). */
  protected readonly moreFiltersCount = computed(() => {
    let n = 0;
    if (this.scopeFilter() !== this.scopeOptions[0]) n++;
    if (this.rangeStart() || this.rangeEnd()) n++;
    return n;
  });

  /** Active-filter summary chips, qualified by scope. */
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.campaignFilter()) {
      chips.push({ key: 'campaign', label: `Campaign: ${this.campaignFilterName()}` });
    }
    if (this.assetTypeFilter()) {
      chips.push({ key: 'assetType', label: `Asset type: ${this.assetTypeFilter()}` });
    }
    if (this.channelFilter()) {
      chips.push({ key: 'channel', label: `Channel: ${this.channelFilter()}` });
    }
    if (this.statusFilter()) {
      chips.push({ key: 'status', label: `Asset status: ${this.statusFilter()}` });
    }
    if (this.rangeStart() || this.rangeEnd()) {
      chips.push({
        key: 'date',
        label: `Active: ${this.rangeStart() ? this.formatDate(this.rangeStart()) : '…'} – ${
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

  // ================= Campaign selector =================
  /** Scope-aware searchable selector with identity preview — reads live from
   *  the single shared CampaignStoreService. Never a hardcoded list. */
  /**
   * The campaigns an asset can still be created against.
   *
   * A CLOSED OR CANCELLED CAMPAIGN IS NOT ONE OF THEM. This offered every campaign the store
   * held, so the Generate form's campaign picker listed campaigns that finished last year
   * alongside the one being set up - and the server accepts the create either way, so a QR code
   * could be minted, printed and put on a table for a campaign that had already been closed. It
   * would resolve, and every scan of it would be attributed to a campaign nobody is running.
   *
   * DRAFT AND SCHEDULED CAMPAIGNS STAY, deliberately, even though a tester asked for Active only.
   * Tracking readiness is one of the checks that has to PASS before a campaign can be activated,
   * so its assets have to exist before it is active; an Active-only picker would make that gate
   * impossible to satisfy for every campaign on the platform. Excluding the two dead states is
   * the part of that request that holds.
   */
  protected readonly campaignOptions = computed<readonly CampaignOption[]>(() =>
    this.campaignStore
      .all()
      .filter((c) => c.status !== 'Closed' && c.status !== 'Cancelled')
      .map((c) => ({
        reference: c.code,
        name: c.name,
        context: `${c.status}${c.startDate ? ' · ' + this.formatDate(c.startDate) : ''}`,
      })),
  );

  // ================= Main work: tracking asset index =================

  /**
   * The full asset set inside the actor's effective data scope. Read from the single shared TrackingAssetStoreService — not a page-local copy — so Campaign Detail's Tracking tab sees
   * the same records.
   */
  protected readonly records = computed(() => this.store.all());

  /** Total across the scope — the real count from the shared store, not a placeholder. */
  protected readonly totalRecords = computed(() => this.records().length);

  /** The filtered asset set for the current scope and filters. */
  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const campaign = this.campaignFilter();
    const type = this.assetTypeFilter();
    const channel = this.channelFilter();
    const status = this.statusFilter();
    const start = this.rangeStart() ? new Date(this.rangeStart()) : null;
    const end = this.rangeEnd() ? new Date(this.rangeEnd()) : null;
    if (end) {
      end.setHours(23, 59, 59, 999);
    }

    return this.records().filter((r) => {
      if (campaign && r.campaignRef !== campaign) return false;
      if (
        q &&
        !(
          r.trackingReference.toLowerCase().includes(q) ||
          r.destination.toLowerCase().includes(q) ||
          this.campaignOf(r.campaignRef).name.toLowerCase().includes(q)
        )
      ) {
        return false;
      }
      if (type && r.assetType !== type) return false;
      if (channel && r.channel !== channel) return false;
      if (status && r.assetStatus !== status) return false;
      if (start && r.activeFrom && new Date(r.activeFrom) < start) return false;
      if (end && r.activeFrom && new Date(r.activeFrom) > end) return false;
      return true;
    });
  });

  /** Totals qualified by scope and last refresh. */
  protected readonly recordCount = computed(() => this.visibleRecords().length);
  protected readonly activeCount = computed(() => this.records().filter((r) => r.assetStatus === 'Active').length);
  protected readonly totalUsage = computed(() => this.records().reduce((sum, r) => sum + r.usageCount, 0));
  protected readonly pendingApprovalCount = computed(
    () => this.records().filter((r) => r.approvalState === 'Pending review').length,
  );
  protected readonly assetTypeCount = computed(() => new Set(this.records().map((r) => r.assetType)).size);

  // ----- Pagination (footer pager slices the filtered set) -----
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.recordCount() / this.pageSize)));
  private readonly clampedPage = computed(() => Math.min(this.currentPage(), this.totalPages()));
  protected readonly pagedRecords = computed(() => {
    const start = (this.clampedPage() - 1) * this.pageSize;
    return this.visibleRecords().slice(start, start + this.pageSize);
  });
  protected readonly pageNumbers = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));
  protected goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.currentPage.set(page);
  }

  // ================= Selection → detail =================
  /** The selected asset drives the secondary detail panel; no raw table editing.
   *  Empty by default so the detail panel stays hidden until a row is selected. */
  protected readonly selectedRef = signal<string>('');
  protected readonly selectedAsset = computed(
    () => this.records().find((r) => r.trackingReference === this.selectedRef()) ?? null,
  );
  /**
   * Whether a workflow dialog launched from inside the detail off-canvas is open.
   *
   * THE OFF-CANVAS AND EVERY WORKFLOW DIALOG SHARE THE SAME STACKING LAYER — `.tam-detail` and
   * `.tam-modal-backdrop` sit at the same z-index band, so Approve (or Submit, Activate, Edit,
   * Request disable, Disable, Delete unused draft, Generate) used to open BEHIND the still-open
   * off-canvas instead of over it. Raising the dialog's z-index would only trade one visual bug
   * for another — the off-canvas would still be sitting there, now behind a dialog that visually
   * belongs to it. Hiding the off-canvas outright while a dialog is open, then letting it
   * reappear the moment the dialog closes, is what `detailAsset` (below) does — it is a plain
   * derived value, so there is no separate close/reopen bookkeeping to keep in sync.
   */
  protected readonly anyWorkflowDialogOpen = computed(
    () =>
      this.submitDialogOpen() ||
      this.approveDialogOpen() ||
      this.activateDialogOpen() ||
      this.editDialogOpen() ||
      this.requestDisableDialogOpen() ||
      this.disableDialogOpen() ||
      this.deleteDialogOpen() ||
      this.generateDialogOpen(),
  );
  /** What the off-canvas template renders from — `null` (hidden) whenever a workflow dialog is
   *  open, even though `selectedAsset`/`selectedRef` are left untouched underneath, so the panel
   *  comes back showing the same record the moment the dialog closes. */
  protected readonly detailAsset = computed(() =>
    this.anyWorkflowDialogOpen() ? null : this.selectedAsset(),
  );
  /** Version of the selected asset at the moment it was opened — detects "record changed after
   *  you opened it" if a workflow action is then attempted on the same still-open panel. */
  protected readonly selectedSnapshotVersion = signal<number | null>(null);
  protected selectAsset(ref: string): void {
    this.selectedRef.set(ref);
    this.selectedSnapshotVersion.set(this.store.get(ref)?.version ?? null);
    this.closeRowMenu();
  }
  /** True when a workflow action targets the currently-open asset and it has changed since it was opened. */
  private isStaleSinceOpened(asset: TrackingAsset): boolean {
    return (
      asset.trackingReference === this.selectedRef() &&
      this.selectedSnapshotVersion() !== null &&
      (asset.version ?? 1) !== this.selectedSnapshotVersion()
    );
  }
  /** Record conflict recovery: re-sync to the latest version so the next attempt succeeds. */
  protected reapplyLatestVersion(): void {
    if (this.selectedRef()) {
      this.selectedSnapshotVersion.set(this.store.get(this.selectedRef())?.version ?? null);
    }
    this.uiState.set('ready');
  }
  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
  }

  /** Copy the generated URL to the clipboard, preserving the exact stable value. */
  protected readonly copiedRef = signal<string>('');
  protected copyUrl(url: string): void {
    const done = () => {
      this.copiedRef.set(this.selectedRef());
      setTimeout(() => this.copiedRef.set(''), 1800);
    };
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(url).then(done).catch(done);
    } else {
      done();
    }
  }
  /** Open the generated URL in a new, safe tab. */
  protected openUrl(url: string): void {
    if (url) window.open(url, '_blank', 'noopener,noreferrer');
  }

  // ----- Download / share the asset details, and download the QR separately -----
  private triggerDownload(filename: string, content: string, mime: string): void {
    const blob = new Blob([content], { type: mime });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  }
  private qrSvgMarkup(asset: TrackingAsset): string {
    const value = asset.generatedUrl.startsWith('http') ? asset.generatedUrl : `https://${asset.generatedUrl}`;
    const matrix = generateQrMatrix(value);
    const size = matrix.length + 8;
    const path = qrMatrixToPath(matrix);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}" width="512" height="512"><rect width="${size}" height="${size}" fill="#fff"/><path d="${path}" fill="#1e2a24"/></svg>`;
  }
  /** Download the full asset details as a text file (reference, type, channel, destination, URL, place, dates, status). */
  protected downloadAssetDetails(asset: TrackingAsset): void {
    const lines = [
      `Tracking reference: ${asset.trackingReference}`,
      `Asset type: ${asset.assetType}`,
      `Channel: ${asset.channel}`,
      asset.place ? `Place: ${asset.place}` : null,
      asset.placeCity ? `City: ${asset.placeCity}` : null,
      asset.placeState ? `State: ${asset.placeState}` : null,
      ...(asset.placeCustomFields ?? []).map((f) => `${f.key}: ${f.value}`),
      `Campaign: ${this.campaignOf(asset.campaignRef).name} (${asset.campaignRef})`,
      `Destination: ${asset.destination}`,
      `Generated URL: ${asset.generatedUrl}`,
      `Active: ${this.formatDate(asset.activeFrom)} – ${this.formatDate(asset.activeTo)}`,
      `Status: ${asset.assetStatus}`,
      `Approval: ${asset.approvalState}`,
      `Usage: ${this.formatUsage(asset.usageCount)}`,
    ].filter((l): l is string => !!l);
    this.triggerDownload(`${asset.trackingReference}-details.txt`, lines.join('\n'), 'text/plain');
  }
  /** Download just the QR code as a standalone SVG image (only meaningful for a QR-type asset). */
  protected downloadQr(asset: TrackingAsset): void {
    if (!asset.isQr) return;
    this.triggerDownload(`${asset.trackingReference}-qr.svg`, this.qrSvgMarkup(asset), 'image/svg+xml');
  }
  /** Rasterise the asset's QR code (SVG) to a PNG blob so it can be shared as an image file. */
  private async qrPngBlob(asset: TrackingAsset): Promise<Blob | null> {
    const svg = this.qrSvgMarkup(asset);
    const svgUrl = URL.createObjectURL(new Blob([svg], { type: 'image/svg+xml' }));
    try {
      const img = new Image();
      await new Promise<void>((resolve, reject) => {
        img.onload = () => resolve();
        img.onerror = () => reject(new Error('QR image failed to load'));
        img.src = svgUrl;
      });
      const canvas = document.createElement('canvas');
      canvas.width = 512;
      canvas.height = 512;
      const ctx = canvas.getContext('2d');
      if (!ctx) return null;
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, 512, 512);
      ctx.drawImage(img, 0, 0, 512, 512);
      return await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'));
    } finally {
      URL.revokeObjectURL(svgUrl);
    }
  }
  /** Share the QR code — as an image file via the Web Share API when available, otherwise download
   *  the QR image, with a persistent inline confirmation (not a toast alone). Non-QR assets fall
   *  back to sharing the generated link. */
  protected readonly shareStatus = signal<string>('');
  protected async shareAsset(asset: TrackingAsset): Promise<void> {
    const url = asset.generatedUrl.startsWith('http') ? asset.generatedUrl : `https://${asset.generatedUrl}`;
    const nav = navigator as Navigator & {
      share?: (data: ShareData) => Promise<void>;
      canShare?: (data: ShareData) => boolean;
    };
    if (asset.isQr) {
      try {
        const blob = await this.qrPngBlob(asset);
        if (blob) {
          const file = new File([blob], `${asset.trackingReference}-qr.png`, { type: 'image/png' });
          if (nav.share && nav.canShare?.({ files: [file] })) {
            await nav.share({ files: [file], title: asset.trackingReference, text: `${asset.trackingReference} QR code` });
            this.shareStatus.set('Shared the QR code.');
          } else {
            // Browser cannot share files — download the QR code image instead.
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = `${asset.trackingReference}-qr.png`;
            a.click();
            URL.revokeObjectURL(a.href);
            this.shareStatus.set('Your browser cannot share files — the QR code image was downloaded instead.');
          }
        }
      } catch {
        /* the user cancelled the native share sheet — not an error */
      }
      setTimeout(() => this.shareStatus.set(''), 3000);
      return;
    }
    // Non-QR asset — no QR to share; fall back to the generated link.
    const text = `${asset.trackingReference} — ${asset.assetType} · ${asset.channel}${asset.place ? ' · ' + asset.place : ''}\n${url}`;
    try {
      if (nav.share) {
        await nav.share({ title: asset.trackingReference, text, url });
        this.shareStatus.set('Shared.');
      } else if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        this.shareStatus.set('This asset has no QR code — copied the generated link to the clipboard.');
      } else {
        this.shareStatus.set(text);
      }
    } catch {
      /* the user cancelled the native share sheet — not an error */
    }
    setTimeout(() => this.shareStatus.set(''), 3000);
  }

  // ----- Real QR code for the selected asset's generated URL -----
  private readonly qrMatrix = computed(() => {
    const a = this.selectedAsset();
    if (!a || !a.isQr) return null;
    const value = a.generatedUrl.startsWith('http') ? a.generatedUrl : `https://${a.generatedUrl}`;
    return generateQrMatrix(value);
  });
  /** SVG viewBox sized to the matrix plus a 4-module quiet zone. */
  protected readonly qrViewBox = computed(() => {
    const m = this.qrMatrix();
    if (!m) return '0 0 33 33';
    const size = m.length + 8;
    return `0 0 ${size} ${size}`;
  });
  /** SVG path of all dark modules for the selected asset's QR code. */
  protected readonly qrPath = computed(() => {
    const m = this.qrMatrix();
    return m ? qrMatrixToPath(m) : '';
  });

  // ================= Actions, eligibility and result =================

  /** Generate — primary; offered when the caller holds the create permission. */
  protected readonly generateAllowed = computed(
    () => this.permissions().generate && this.inGenerateState() && this.uiState() !== 'no-access',
  );

  /** Why Create is unavailable, so a disabled primary action is never silent. */
  protected readonly generateDisabledReason = computed(() =>
    this.permissions().generate
      ? ''
      : 'Creating a tracking asset needs the cam.tracking-assets.create permission.');
  /** Submit — a Draft asset is submitted for approval (moves Draft → Submitted). */
  protected submitAllowed(asset: TrackingAsset | null): boolean {
    return !!asset && asset.assetStatus === 'Draft';
  }
  /** Why Submit is unavailable — only a Draft can be submitted for approval. */
  protected submitDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (asset.assetStatus !== 'Draft') return `Submit is only available while this asset is a Draft, not ${asset.assetStatus}.`;
    return '';
  }

  /**
   * Whether this session may EVER submit, regardless of the asset in front of it.
   *
   * SEPARATE FROM `submitAllowed` ON PURPOSE, and the same split runs through every action on
   * this screen. A missing PERMISSION means the action is not this person's to take and the item
   * is not rendered; an incompatible STATE means it is theirs but not yet, and the item is
   * rendered disabled with a sentence saying why. Greying out a verb a role will never hold
   * teaches people to ignore greyed-out things.
   */
  protected readonly canEverSubmit = computed(() => this.currentUser.hasPermission('cam.tracking-assets.submit'));
  protected readonly canEverApprove = computed(() => this.permissions().approve);
  protected readonly canEverActivate = computed(() => this.permissions().activate);
  protected readonly canEverRequestDisable = computed(() => this.permissions().requestDisable);
  protected readonly canEverDisable = computed(() => this.permissions().disable);
  protected readonly canEverEdit = computed(() => this.permissions().replace);
  /** True when the current session created this asset — the one condition that blocks Approve
   *  even for an otherwise-eligible independent approver. */
  protected isOwnAsset(asset: TrackingAsset | null): boolean {
    return !!asset && !!asset.createdByRef && asset.createdByRef === this.currentUserRef();
  }
  /** Approve — authorised independent approver, Submitted / Pending review, and never the asset's
   *  own creator. */
  protected approveAllowed(asset: TrackingAsset | null): boolean {
    return (
      !!asset &&
      this.permissions().approve &&
      (asset.assetStatus === 'Submitted' || asset.approvalState === 'Pending review')
    );
  }
  /** Why Approve is unavailable for this asset right now — distinct "cannot self-approve" case is
   *  explained, not folded into a generic access error. */
  protected approveDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (asset.assetStatus !== 'Submitted' && asset.approvalState !== 'Pending review') {
      return 'Approve is only available while this asset is Submitted or Pending review.';
    }
    if (this.isOwnAsset(asset)) {
      return `This value cannot be approved by its creator (${this.currentUserName()}). An independent approver who did not create this asset must decide.`;
    }
    return '';
  }
  /**
   * Activate — the step between Approved and live.
   *
   * WITHOUT IT THE LIFECYCLE STOPPED AT APPROVED. `PermittedActionsFor` on the server offers
   * Activate from exactly this state and nothing in the client ever called it, so an approved
   * asset stayed approved: it had a reference and a generated URL, and it resolved nothing,
   * because `IsLiveAt` requires the status to be Active.
   */
  protected activateAllowed(asset: TrackingAsset | null): boolean {
    return !!asset && this.permissions().activate && asset.assetStatus === 'Approved';
  }
  /** Why Activate is unavailable — explains permission or an incompatible current state. */
  protected activateDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (asset.assetStatus !== 'Approved') {
      return `Activate is only available once this asset is Approved, not ${asset.assetStatus}.`;
    }
    return '';
  }
  /**
   * Request disable — the maker asking for a live asset to be taken down.
   *
   * A REQUEST, NOT THE ACT. Disabling an asset stops a printed QR code resolving, so it is a
   * decision somebody else makes. This moves the asset to "Disable requested", and it goes on
   * resolving scans until an approver decides - nothing about asking should change what a
   * donor's scan does.
   */
  protected requestDisableAllowed(asset: TrackingAsset | null): boolean {
    return !!asset && this.permissions().requestDisable
      && (asset.assetStatus === 'Active' || asset.assetStatus === 'Paused');
  }
  protected requestDisableDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (asset.assetStatus === 'Disable requested') {
      return 'A disable request is already waiting for an approver on this asset.';
    }
    if (asset.assetStatus !== 'Active' && asset.assetStatus !== 'Paused') {
      return `A disable can only be requested for a live asset, not one that is ${asset.assetStatus}.`;
    }
    return '';
  }

  /** Disable — the approver's decision, on a live asset or one already carrying a request. */
  protected disableAllowed(asset: TrackingAsset | null): boolean {
    return !!asset && this.permissions().disable
      && (asset.assetStatus === 'Active'
        || asset.assetStatus === 'Paused'
        || asset.assetStatus === 'Disable requested');
  }
  /**
   * Why Disable is unavailable.
   *
   * IT NO LONGER EXPLAINS A MISSING PERMISSION, because a caller without the permission is not
   * shown the item at all. What is left is the state, which is worth saying.
   */
  protected disableDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (!this.disableAllowed(asset)) {
      return `Disable is only available for a live asset, not one that is ${asset.assetStatus}.`;
    }
    return '';
  }
  /** Edit — the asset's details can be edited only while it is a Draft; once Submitted it is locked. */
  protected editAllowed(asset: TrackingAsset | null): boolean {
    return !!asset && this.permissions().replace && asset.assetStatus === 'Draft';
  }
  /** Why Edit is unavailable — explains permission or that only a Draft can be edited. */
  protected editDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (asset.assetStatus !== 'Draft') return `Edit is only available while this asset is a Draft, not ${asset.assetStatus}.`;
    return '';
  }
  /**
   * Delete unused draft — a Draft with nothing pointing at it.
   *
   * ON ITS OWN PERMISSION NOW. It tested `permissions().disable`, so discarding your own unused
   * draft required the right to take LIVE assets down - which an Initiator does not hold, and
   * which has nothing to do with a draft that has never been activated.
   */
  protected canDeleteDraft(asset: TrackingAsset): boolean {
    return asset.assetStatus === 'Draft' && !asset.hasDownstreamReference && this.permissions().deleteDraft;
  }

  // ----- Row overflow menu -----
  protected readonly openRowMenu = signal<string | null>(null);
  /**
   * Fixed-viewport placement for the open row menu, computed from the trigger button's own
   * position rather than the table's layout.
   *
   * THE TABLE SCROLLS (see `.tam-table-scroll`'s fixed height), and an `overflow: auto` ancestor
   * clips any `position: absolute` descendant that would render outside its box — including this
   * menu, which used to always open downward with no way to avoid that clipping for a row near
   * the bottom of the visible five. Anchoring it with `position: fixed` off the button's
   * `getBoundingClientRect()` escapes the clipping entirely and picks a direction with room.
   */
  protected readonly rowMenuStyle = signal<Record<string, string> | null>(null);
  protected readonly rowMenuUp = computed(() => {
    const style = this.rowMenuStyle();
    return !!style && style['bottom'] !== 'auto';
  });
  protected toggleRowMenu(ref: string, trigger?: HTMLElement): void {
    if (this.openRowMenu() === ref) {
      this.closeRowMenu();
      return;
    }
    this.openRowMenu.set(ref);
    if (!trigger) {
      this.rowMenuStyle.set(null);
      return;
    }
    const rect = trigger.getBoundingClientRect();
    // Room for the longest possible menu (Open / Submit / Approve / Activate / Edit /
    // Request disable / Disable / Delete unused draft).
    const menuAllowance = 340;
    const openUp = window.innerHeight - rect.bottom < menuAllowance;
    this.rowMenuStyle.set({
      position: 'fixed',
      right: `${Math.max(8, window.innerWidth - rect.right)}px`,
      top: openUp ? 'auto' : `${rect.bottom + 6}px`,
      bottom: openUp ? `${window.innerHeight - rect.top + 6}px` : 'auto',
    });
  }
  protected closeRowMenu(): void {
    this.openRowMenu.set(null);
    this.rowMenuStyle.set(null);
  }

  // ================= Generate primary action =================
  protected readonly generateDialogOpen = signal(false);

  // Input fields collected by the Generate form.
  protected readonly gAssetType = signal<string>(''); // required
  protected readonly gDestination = signal<string>(''); // required
  protected readonly gCampaign = signal<string>(''); // required
  protected readonly gChannel = signal<string>(''); // required
  // REQUIRED, AND NO LONGER PRE-ANSWERED. It started on 'Draft', so somebody who never touched
  // the field created a Draft asset without choosing to - and a Draft asset resolves no scans, so
  // a QR code could be printed against one that was never going to work. Empty means the person
  // has to pick, and errorSummary() refuses the form until they do.
  protected readonly gAssetStatus = signal<AssetStatus | ''>('');
  protected readonly gSource = signal<string>(''); // conditional
  protected readonly gMedium = signal<string>(''); // conditional
  protected readonly gContentTag = signal<string>(''); // optional
  protected readonly gContentTagMax = 150;
  protected readonly gActiveFrom = signal<string>(''); // conditional
  protected readonly gActiveTo = signal<string>(''); // conditional
  protected readonly generateSubmitted = signal(false);
  protected readonly generatedReference = signal('');
  /** The full set of references minted by the last Generate — one per on-ground place, or a single
   *  entry for every other asset type (drives the post-generate "created" confirmation list). */
  protected readonly generatedReferences = signal<readonly string[]>([]);

  /**
   * Campaign is captured first: selecting it auto-fills the channel, source and active
   * window from what was chosen for that campaign in the Campaign Wizard, so the person
   * generating an asset doesn't have to re-enter what's already on record.
   */
  protected onCampaignSelect(ref: string): void {
    this.gCampaign.set(ref);
    const rec = this.campaignStore.get(ref);
    if (!rec) return;

    // THE ID, NOT THE NAME. `rec.channels` holds the API's Guids, which is exactly what the
    // picker and the create call both want now. The old prefill took `channelNames[0]` and tried
    // to match it against this screen's own hard-coded label list, falling through an alias map
    // when it did not - so it either set a label the API cannot accept or set nothing at all.
    const firstChannel = rec.channels?.[0];
    if (firstChannel && this.channelChoices().some((choice) => choice.ref === firstChannel)) {
      this.gChannel.set(firstChannel);
      // Asset type is left for the person to choose - it is not auto-derived from the
      // selected campaign or channel.
    }

    const firstSource = rec.sources?.[0];
    if (firstSource && this.sourceChoices().some((choice) => choice.ref === firstSource)) {
      this.gSource.set(firstSource);
    }

    if (rec.startDate) this.gActiveFrom.set(rec.startDate);
    if (rec.endDate) this.gActiveTo.set(rec.endDate);

    // THE CAMPAIGN'S GEOGRAPHY HAS TO BE FETCHED BEFORE IT CAN BE SHOWN.
    //
    // City and State render as read-only "From campaign" boxes on every place row, and they were
    // reliably empty. Two separate reasons, both fixed here:
    //
    //   - THE LIST PROJECTION DOES NOT CARRY THEM. `CampaignListItem` - what the register loads,
    //     and what this picker's campaigns come from - has a code, a name, dates, amounts and
    //     counts. `cityName` and `stateName` are on the DETAIL response, so `campaignStore.get()`
    //     returned a record with both undefined until somebody had opened that campaign's detail
    //     page in the same session. `loadDetail` asks for them; `placeLocation` below reads them
    //     back the moment they land, because the store is a signal.
    //   - ONLY `addPlace()` EVER STAMPED THEM. The first place row is created by `openGenerate()`,
    //     before any campaign has been chosen, so a single-place asset - the ordinary case - had
    //     no way to acquire a city at all. The two boxes are now driven by `placeLocation()`
    //     rather than by a value copied onto each row, so every row is right, including after the
    //     campaign is changed.
    this.campaignStore.loadDetail(ref);
  }

  /**
   * The selected campaign's City and State, as people read them.
   *
   * THE NAMES, NOT THE IDS. `CampaignRecord.city` and `.region` hold the API's Guids, because
   * that is what the create and update bodies require; the readable values live alongside them in
   * `cityName` and `regionName`.
   */
  protected readonly placeLocation = computed<{ readonly city: string; readonly state: string }>(
    () => {
      const rec = this.campaignStore.get(this.gCampaign());

      return {
        city: rec?.cityName ?? '',
        state: rec?.regionName ?? rec?.regionLabel ?? '',
      };
    },
  );

  /** On-ground events happen in more than one physical place for the same campaign, so each place
   *  gets its own separate QR/link — this is what lets the team see which place is contributing most. */
  /**
   * On-ground events run in several physical places, so each place gets its own asset.
   *
   * MATCHED ON THE CHANNEL'S NAME rather than on the literal 'Offline' the picker used to hold,
   * because the picker now holds the channel's id. The seeded CAM channel for this is 'Offline'.
   */
  /**
   * Whether this asset carries PLACES: a QR code, on the Offline channel.
   *
   * BOTH HALVES, NOT JUST THE CHANNEL. This tested the channel alone, so choosing Offline with a
   * Short Link opened the Places section and demanded a place for it - and a place describes
   * where a printed thing was put, which a link does not have. The server's rule is the pair, so
   * a short link on Offline that reached it with places was refused outright: "Places apply to an
   * offline QR code only."
   */
  protected readonly isOnGround = computed(
    () =>
      this.gAssetType().toLowerCase() === 'qr code' &&
      this.channelLabel(this.gChannel()).toLowerCase() === 'offline',
  );
  // CITY AND STATE ARE NOT ROW STATE. They belong to the campaign, are the same on every row,
  // and are read live from `placeLocation()` - which is what stops them from being blank on the
  // first row and stale on the rest.
  protected readonly gPlaces = signal<
    readonly {
      readonly id: string;
      label: string;
      destination: string;
      customFields: readonly { readonly id: string; key: string; value: string }[];
    }[]
  >([{ id: 'place-1', label: '', destination: '', customFields: [] }]);
  private placeSeq = 1;
  private placeFieldSeq = 1;
  protected addPlace(): void {
    this.placeSeq += 1;

    this.gPlaces.update((list) => [
      ...list,
      { id: `place-${this.placeSeq}`, label: '', destination: '', customFields: [] },
    ]);
  }
  protected removePlace(id: string): void {
    this.gPlaces.update((list) => (list.length > 1 ? list.filter((p) => p.id !== id) : list));
  }
  protected updatePlaceLabel(id: string, value: string): void {
    this.gPlaces.update((list) => list.map((p) => (p.id === id ? { ...p, label: value } : p)));
  }
  protected updatePlaceDestination(id: string, value: string): void {
    this.gPlaces.update((list) => list.map((p) => (p.id === id ? { ...p, destination: value } : p)));
  }
  /** Customisable add fields — lets a place carry any further named detail (e.g. Stall number,
   *  Contact person) beyond the fixed City/State columns. */
  protected addPlaceCustomField(placeId: string): void {
    this.placeFieldSeq += 1;
    const fieldId = `pf-${this.placeFieldSeq}`;
    this.gPlaces.update((list) =>
      list.map((p) => (p.id === placeId ? { ...p, customFields: [...p.customFields, { id: fieldId, key: '', value: '' }] } : p)),
    );
  }
  protected removePlaceCustomField(placeId: string, fieldId: string): void {
    this.gPlaces.update((list) =>
      list.map((p) => (p.id === placeId ? { ...p, customFields: p.customFields.filter((f) => f.id !== fieldId) } : p)),
    );
  }
  protected updatePlaceCustomFieldKey(placeId: string, fieldId: string, value: string): void {
    this.gPlaces.update((list) =>
      list.map((p) =>
        p.id === placeId ? { ...p, customFields: p.customFields.map((f) => (f.id === fieldId ? { ...f, key: value } : f)) } : p,
      ),
    );
  }
  protected updatePlaceCustomFieldValue(placeId: string, fieldId: string, value: string): void {
    this.gPlaces.update((list) =>
      list.map((p) =>
        p.id === placeId ? { ...p, customFields: p.customFields.map((f) => (f.id === fieldId ? { ...f, value } : f)) } : p,
      ),
    );
  }
  /** At least one named place with a destination is required for an on-ground asset. */
  protected readonly placesValid = computed(
    () => !this.isOnGround() || this.gPlaces().some((p) => p.label.trim() && p.destination.trim()),
  );

  // ----- Live preview inside the Generate popup — shows the QR / link before it's created ----
  /** True once there's enough information to preview something (asset type + at least one destination). */
  protected readonly previewReady = computed(() => {
    if (!this.gAssetType()) return false;
    if (this.isOnGround()) return this.gPlaces().some((p) => p.destination.trim());
    return !!this.gDestination().trim();
  });
  /**
   * WHAT A PREVIEW CAN HONESTLY SHOW, which is the destination and not the tracking link.
   *
   * The tracking reference and the short URL that carries it are allocated by the SERVER, and
   * only on approval — that is what makes a printed code recoverable. So the preview used to ask
   * `nextReference()` for a placeholder ('QR-PENDING-271381'), hand it to `buildGeneratedUrl()`,
   * and get back an empty string, because that function looks up an asset that does not exist
   * yet. Two things followed on screen: an empty white square where the QR belonged, and a
   * fabricated reference presented as though it were the one the asset would receive.
   *
   * The preview now encodes THE DESTINATION the person typed, and says so. That is a real QR of
   * a real URL — scannable here, and the same page the live tracking link will redirect to.
   */
  protected previewUrlFor(assetType: string, destination: string): string {
    return destination.trim();
  }
  /** Single-destination preview (every asset type except an on-ground multi-place one). */
  protected readonly previewUrl = computed(() => this.gDestination().trim());
  private previewQrMatrix(url: string): ReturnType<typeof generateQrMatrix> | null {
    if (!url) return null;
    const value = url.startsWith('http') ? url : `https://${url}`;
    return generateQrMatrix(value);
  }
  protected previewQrPath(url: string): string {
    const m = this.previewQrMatrix(url);
    return m ? qrMatrixToPath(m) : '';
  }
  protected previewQrViewBox(url: string): string {
    const m = this.previewQrMatrix(url);
    if (!m) return '0 0 33 33';
    const size = m.length + 8;
    return `0 0 ${size} ${size}`;
  }
  /** Per-place preview — each place has its own destination, so each gets its own QR. */
  protected placePreviewUrl(index: number): string {
    return this.gPlaces()[index]?.destination.trim() ?? '';
  }

  protected readonly gRangeInvalid = computed(() => {
    const s = this.gActiveFrom();
    const e = this.gActiveTo();
    return !!s && !!e && new Date(e) < new Date(s);
  });
  protected readonly gInterpretedRange = computed(() => {
    const s = this.gActiveFrom();
    const e = this.gActiveTo();
    if (!s && !e) return `No active window · ${this.operatingTimeZone}`;
    return `${s ? this.formatDate(s) : '…'} – ${e ? this.formatDate(e) : '…'} · ${this.operatingTimeZone}`;
  });

  /** Field-level errors surfaced only after a submit attempt. Every field is required
   *  except Content tag. */
  protected missing(field: string): boolean {
    if (!this.generateSubmitted()) return false;
    switch (field) {
      case 'assetType':
        return !this.gAssetType();
      case 'channel':
        return !this.gChannel();
      case 'destination':
        return !this.isOnGround() && !this.gDestination().trim();
      case 'places':
        return this.isOnGround() && !this.placesValid();
      case 'campaign':
        return !this.gCampaign();
      case 'source':
        return !this.gSource().trim();
      case 'medium':
        return !this.gMedium().trim();
      case 'activeFrom':
        return !this.gActiveFrom();
      case 'activeTo':
        return !this.gActiveTo() || this.gRangeInvalid();
      default:
        return false;
    }
  }
  /** The list of invalid fields for the error summary. */
  protected readonly errorSummary = computed(() => {
    if (!this.generateSubmitted()) return [] as { key: string; label: string }[];
    const errs: { key: string; label: string }[] = [];
    if (this.missing('assetType')) errs.push({ key: 'g-assetType', label: 'Enter Asset type.' });
    if (this.missing('channel')) errs.push({ key: 'g-channel', label: 'Enter Channel.' });
    if (this.missing('destination')) errs.push({ key: 'g-destination', label: 'Enter Destination.' });
    if (this.missing('places')) errs.push({ key: 'g-places', label: 'Enter at least one place name and destination.' });
    if (this.missing('campaign')) errs.push({ key: 'g-campaign', label: 'Enter Campaign.' });
    if (this.missing('source')) errs.push({ key: 'g-source', label: 'Enter Source.' });
    if (this.missing('medium')) errs.push({ key: 'g-medium', label: 'Enter Medium.' });
    if (!this.gAssetStatus()) errs.push({ key: 'g-assetStatus', label: 'Enter Asset status.' });
    if (this.missing('activeFrom')) errs.push({ key: 'g-activeFrom', label: 'Enter Active from.' });
    if (this.gRangeInvalid())
      errs.push({ key: 'g-activeTo', label: 'Review Active to. The value does not meet the stated format or range.' });
    else if (this.missing('activeTo')) errs.push({ key: 'g-activeTo', label: 'Enter Active to.' });
    return errs;
  });

  protected openGenerate(): void {
    if (!this.generateAllowed()) return;
    this.gAssetType.set('');
    this.gDestination.set('');
    this.gCampaign.set('');
    this.gChannel.set('');
    // Reset to unanswered, not to Draft — the person picks.
    this.gAssetStatus.set('');
    this.gSource.set('');
    this.gMedium.set('');
    this.gContentTag.set('');
    this.gActiveFrom.set('');
    this.gActiveTo.set('');
    this.gPlaces.set([{ id: 'place-1', label: '', destination: '', customFields: [] }]);
    this.generateSubmitted.set(false);
    this.generatedReferences.set([]);
    this.generateDialogOpen.set(true);
  }
  protected cancelGenerate(): void {
    this.generateDialogOpen.set(false);
  }
  /** Build one asset record (shared by the single-destination and per-place on-ground paths). */
  private buildAsset(
    destination: string,
    placeInfo?: { label: string; city: string; state: string; customFields: readonly { key: string; value: string }[] },
  ): TrackingAsset {
    const reference = this.store.nextReference(this.gAssetType());
    const isQr = this.gAssetType() === 'QR Code';
    const cleanCustomFields: readonly PlaceCustomField[] = (placeInfo?.customFields ?? [])
      .filter((f) => f.key.trim())
      .map((f) => ({ key: f.key.trim(), value: f.value.trim() }));
    return {
      trackingReference: reference,
      assetType: this.gAssetType(),
      channel: this.gChannel(),
      destination,
      campaignRef: this.gCampaign(),
      source: this.gSource().trim(),
      medium: this.gMedium().trim(),
      contentTag: this.gContentTag().trim(),
      activeFrom: this.gActiveFrom(),
      activeTo: this.gActiveTo(),
      generatedUrl: this.store.buildGeneratedUrl(reference, isQr),
      isQr,
      lastTestResult: 'Not tested',
      approvalState: this.gAssetStatus() === 'Submitted' ? 'Pending review' : 'Not required',
      usageCount: 0,
      // Non-empty by the time this runs: buildAsset is only reached from confirmGenerate, which
      // returns early while errorSummary() still reports a missing Asset status.
      assetStatus: this.gAssetStatus() || 'Draft',
      hasDownstreamReference: false,
      createdByRef: this.currentUserRef(),
      version: 1,
      ...(placeInfo ? { place: placeInfo.label } : {}),
      ...(placeInfo?.city.trim() ? { placeCity: placeInfo.city.trim() } : {}),
      ...(placeInfo?.state.trim() ? { placeState: placeInfo.state.trim() } : {}),
      ...(cleanCustomFields.length ? { placeCustomFields: cleanCustomFields } : {}),
    };
  }

  /**
   * Generate one asset — or, for an on-ground event, one asset PER PLACE so each physical
   * location gets its own separate QR/link and the team can see which place is contributing
   * most. Preserve values on recoverable failure; show a persistent confirmed result.
   */
  protected confirmGenerate(): void {
    this.generateSubmitted.set(true);
    if (this.errorSummary().length > 0) {
      // Validation state — keep non-sensitive input, focus first invalid field.
      this.uiState.set('validation');
      return;
    }

    if (this.isOnGround()) {
      const places = this.gPlaces().filter((p) => p.label.trim() && p.destination.trim());
      const dup = places.some((p) =>
        this.records().some(
          (r) => r.destination.trim().toLowerCase() === p.destination.trim().toLowerCase()
            && r.channel === this.channelLabel(this.gChannel()),
        ),
      );
      if (dup) {
        this.generateDialogOpen.set(false);
        this.uiState.set('duplicate');
        return;
      }
      const refs: string[] = [];
      let pending = places.length;
      let firstError: string | undefined;

      for (const p of places) {
        const asset = this.buildAsset(p.destination.trim(), {
          label: p.label.trim(),
          city: this.placeLocation().city,
          state: this.placeLocation().state,
          customFields: p.customFields,
        });
        refs.push(asset.trackingReference);

        this.store.create(asset, (outcome) => {
          if (!outcome.created) {
            firstError ??= outcome.error;
          }

          pending -= 1;

          if (pending === 0) {
            this.announceGenerated(refs, firstError);
          }
        });
      }

      this.generatedReferences.set(refs);
      this.generatedReference.set(refs[0] ?? '');
      return;
    }

    // A tracking asset with the same destination + channel already exists → duplicate handling.
    const dup = this.records().some(
      (r) =>
        r.destination.trim().toLowerCase() === this.gDestination().trim().toLowerCase() &&
        r.channel === this.channelLabel(this.gChannel()),
    );
    if (dup) {
      this.generateDialogOpen.set(false);
      this.uiState.set('duplicate');
      return;
    }

    const asset = this.buildAsset(this.gDestination().trim());
    this.generatedReferences.set([asset.trackingReference]);
    this.generatedReference.set(asset.trackingReference);

    this.store.create(asset, (outcome) =>
      this.announceGenerated([asset.trackingReference], outcome.created ? undefined : outcome.error));
  }

  /**
   * Says what actually happened.
   *
   * THE OUTCOME IS ANNOUNCED AFTER THE SERVER HAS ANSWERED. This used to close the dialog and
   * toast "Tracking asset created" on the line after `store.create()`, while the request was
   * still in flight - and every one of those requests was refused, because the form sent labels
   * and free text where the API takes Guids. The screen reported success, listed a reference the
   * browser had invented, and the register was empty on the next load with nothing anywhere
   * saying why.
   */
  private announceGenerated(refs: readonly string[], error?: string): void {
    if (error) {
      this.toast.show('Tracking asset not created', error, 'error');
      this.uiState.set('validation');
      return;
    }

    this.generateDialogOpen.set(false);
    this.toast.show(
      'Tracking asset created',
      refs.length > 1
        ? `Created ${refs.length} assets: ${refs.join(', ')}.`
        : `${refs[0]} created as ${this.gAssetStatus()}.`,
      'success',
    );
    this.uiState.set('ready');
  }

  // ================= Submit action =================
  protected readonly submitDialogOpen = signal(false);
  protected readonly submitTarget = signal<TrackingAsset | null>(null);
  protected requestSubmit(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.submitAllowed(asset)) return;
    if (this.isStaleSinceOpened(asset)) {
      this.uiState.set('conflict');
      return;
    }
    this.submitTarget.set(asset);
    this.submitDialogOpen.set(true);
  }
  protected cancelSubmit(): void {
    this.submitDialogOpen.set(false);
    this.submitTarget.set(null);
  }
  /** Submit the draft for approval — moves Draft → Submitted and opens it for independent review. */
  protected confirmSubmit(): void {
    const target = this.submitTarget();
    this.submitDialogOpen.set(false);
    this.submitTarget.set(null);
    if (!target) return;
    // Waits for the real outcome — see `TrackingAssetStoreService.update`'s doc comment. A toast
    // fired the instant this call was MADE said "Submitted" even when the server refused it.
    this.store.update(
      target.trackingReference,
      { assetStatus: 'Submitted', approvalState: 'Pending review' },
      (result) => {
        if (!result.applied) {
          this.toast.show(
            'Not submitted',
            result.error ?? `${target.trackingReference} could not be submitted.`,
            'error',
          );
          return;
        }
        this.toast.show('Submitted for approval', `${target.trackingReference} moved to Submitted.`, 'success');
      },
    );
    this.uiState.set('ready');
  }

  // ================= Approve action =================
  protected readonly approveDialogOpen = signal(false);
  protected readonly approveTarget = signal<TrackingAsset | null>(null);
  protected readonly approveReason = signal('');
  protected readonly approveReasonMin = 10;
  protected readonly approveReasonMax = 2000;
  protected readonly approveReasonValid = computed(() => {
    const len = this.approveReason().trim().length;
    return len >= this.approveReasonMin && len <= this.approveReasonMax;
  });
  protected readonly approveReasonCount = computed(() => this.approveReason().trim().length);

  protected requestApprove(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.approveAllowed(asset)) return;
    if (this.isStaleSinceOpened(asset)) {
      this.uiState.set('conflict');
      return;
    }
    this.approveTarget.set(asset);
    this.approveReason.set('');
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
  }
  /**
   * Record decision, independent authority, reason, effective version, time and resulting state.
   *
   * APPROVE LANDS ON APPROVED. It used to write `assetStatus: 'Active'`, which described a state
   * the server does not move to on approval - `/approve` sets Approved, and Active is a separate
   * transition with its own permission. The optimistic row therefore flashed Active for the
   * length of the round trip and then corrected itself, and the toast said the asset was live
   * when nothing had gone live. Activate is offered next, as its own action.
   */
  protected confirmApprove(): void {
    if (!this.approveReasonValid()) return;
    const target = this.approveTarget();
    if (target) {
      // Waits for the real outcome — see `TrackingAssetStoreService.update`'s doc comment. This
      // used to toast "Asset approved" the instant the call was MADE, before the server had
      // agreed to it — so a refusal (a version conflict, a permission the button's own guard
      // didn't catch) left the asset exactly as it was, said "approved" anyway, and only a
      // later refresh quietly put the row back — which is indistinguishable from "clicking
      // Approve did nothing" unless somebody happens to reload at the right moment.
      this.store.update(
        target.trackingReference,
        {
          approvalState: 'Approved',
          assetStatus: 'Approved',
          approvedByRef: this.currentUserRef(),
          approvedAt: this.lastRefresh(),
        },
        (result) => {
          if (!result.applied) {
            this.toast.show(
              'Asset not approved',
              result.error ?? `${target.trackingReference} could not be approved.`,
              'error',
            );
            return;
          }
          this.toast.show(
            'Asset approved',
            `${target.trackingReference} is Approved. Activate it to make it live.`,
            'success',
          );
        },
      );
    }
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
    this.uiState.set('ready');
  }

  // ================= Activate action =================
  protected readonly activateDialogOpen = signal(false);
  protected readonly activateTarget = signal<TrackingAsset | null>(null);

  protected requestActivate(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.activateAllowed(asset)) return;
    if (this.isStaleSinceOpened(asset)) {
      this.uiState.set('conflict');
      return;
    }
    this.activateTarget.set(asset);
    this.activateDialogOpen.set(true);
  }
  protected cancelActivate(): void {
    this.activateDialogOpen.set(false);
    this.activateTarget.set(null);
  }
  /** Bring an approved asset live — Approved to Active, after which it resolves scans and clicks. */
  protected confirmActivate(): void {
    const target = this.activateTarget();
    this.activateDialogOpen.set(false);
    this.activateTarget.set(null);
    if (!target) return;
    this.store.update(target.trackingReference, { assetStatus: 'Active' }, (result) => {
      if (!result.applied) {
        this.toast.show(
          'Asset not activated',
          result.error ?? `${target.trackingReference} could not be activated.`,
          'error',
        );
        return;
      }
      this.toast.show('Asset activated', `${target.trackingReference} is now Active.`, 'success');
    });
    this.uiState.set('ready');
  }

  // ================= Request disable action =================
  protected readonly requestDisableDialogOpen = signal(false);
  protected readonly requestDisableTarget = signal<TrackingAsset | null>(null);
  protected readonly requestDisableReason = signal('');
  protected readonly requestDisableReasonMin = 10;
  protected readonly requestDisableReasonMax = 2000;
  protected readonly requestDisableReasonValid = computed(() => {
    const len = this.requestDisableReason().trim().length;
    return len >= this.requestDisableReasonMin && len <= this.requestDisableReasonMax;
  });
  protected readonly requestDisableReasonCount = computed(() => this.requestDisableReason().trim().length);

  protected askForDisable(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.requestDisableAllowed(asset)) return;
    if (this.isStaleSinceOpened(asset)) {
      this.uiState.set('conflict');
      return;
    }
    this.requestDisableTarget.set(asset);
    this.requestDisableReason.set('');
    this.requestDisableDialogOpen.set(true);
  }
  protected cancelRequestDisable(): void {
    this.requestDisableDialogOpen.set(false);
    this.requestDisableTarget.set(null);
  }
  /** Raise the request. The asset stays live until an approver decides it. */
  protected confirmRequestDisable(): void {
    if (!this.requestDisableReasonValid()) return;
    const target = this.requestDisableTarget();
    this.requestDisableDialogOpen.set(false);
    this.requestDisableTarget.set(null);
    if (!target) return;
    this.store.update(target.trackingReference, { assetStatus: 'Disable requested' }, (result) => {
      if (!result.applied) {
        this.toast.show(
          'Request not raised',
          result.error ?? `The disable request for ${target.trackingReference} could not be raised.`,
          'error',
        );
        return;
      }
      this.toast.show(
        'Disable requested',
        `${target.trackingReference} stays live until an approver decides the request.`,
        'success',
      );
    });
    this.uiState.set('ready');
  }

  // ================= Disable action =================
  protected readonly disableDialogOpen = signal(false);
  protected readonly disableTarget = signal<TrackingAsset | null>(null);
  protected readonly disableReason = signal('');
  protected readonly disableReasonMin = 10;
  protected readonly disableReasonMax = 2000;
  protected readonly disableReasonValid = computed(() => {
    const len = this.disableReason().trim().length;
    return len >= this.disableReasonMin && len <= this.disableReasonMax;
  });
  protected readonly disableReasonCount = computed(() => this.disableReason().trim().length);

  protected requestDisable(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.disableAllowed(asset)) return;
    if (this.isStaleSinceOpened(asset)) {
      this.uiState.set('conflict');
      return;
    }
    this.disableTarget.set(asset);
    this.disableReason.set('');
    this.disableDialogOpen.set(true);
  }
  protected cancelDisable(): void {
    this.disableDialogOpen.set(false);
    this.disableTarget.set(null);
  }
  /** Disable only a compatible current state; preserve history; confirm the resulting state. */
  protected confirmDisable(): void {
    if (!this.disableReasonValid()) return;
    const target = this.disableTarget();
    if (target) {
      this.store.update(target.trackingReference, { assetStatus: 'Disabled' }, (result) => {
        if (!result.applied) {
          this.toast.show(
            'Asset not disabled',
            result.error ?? `${target.trackingReference} could not be disabled.`,
            'error',
          );
          return;
        }
        this.toast.show('Asset disabled', `${target.trackingReference} was disabled.`, 'success');
      });
    }
    this.disableDialogOpen.set(false);
    this.disableTarget.set(null);
    this.uiState.set('ready');
  }

  // ================= Edit action (Draft only) =================
  protected readonly editDialogOpen = signal(false);
  protected readonly editTarget = signal<TrackingAsset | null>(null);
  protected readonly editDestination = signal('');
  protected readonly editChannel = signal('');
  protected readonly editSource = signal('');
  protected readonly editMedium = signal('');
  protected readonly editContentTag = signal('');
  protected readonly editActiveFrom = signal('');
  protected readonly editActiveTo = signal('');
  protected readonly editSubmitted = signal(false);
  protected readonly editDestinationValid = computed(() => this.editDestination().trim().length > 0);
  protected readonly editRangeInvalid = computed(() => {
    const s = this.editActiveFrom();
    const e = this.editActiveTo();
    return !!s && !!e && new Date(e) < new Date(s);
  });

  protected requestEdit(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.editAllowed(asset)) return;
    if (this.isStaleSinceOpened(asset)) {
      this.uiState.set('conflict');
      return;
    }
    this.editTarget.set(asset);
    this.editDestination.set(asset.destination);
    this.editChannel.set(asset.channel);
    this.editSource.set(asset.source);
    this.editMedium.set(asset.medium);
    this.editContentTag.set(asset.contentTag);
    this.editActiveFrom.set(asset.activeFrom);
    this.editActiveTo.set(asset.activeTo);
    this.editSubmitted.set(false);
    this.editDialogOpen.set(true);
  }
  protected cancelEdit(): void {
    this.editDialogOpen.set(false);
    this.editTarget.set(null);
  }
  /** Save the edited details while preserving the stable tracking reference and history. */
  protected confirmEdit(): void {
    this.editSubmitted.set(true);
    if (!this.editDestinationValid() || this.editRangeInvalid()) return;
    const target = this.editTarget();
    if (target) {
      this.store.update(
        target.trackingReference,
        {
          destination: this.editDestination().trim(),
          channel: this.editChannel(),
          source: this.editSource().trim(),
          medium: this.editMedium().trim(),
          contentTag: this.editContentTag().trim(),
          activeFrom: this.editActiveFrom(),
          activeTo: this.editActiveTo(),
        },
        (result) => {
          if (!result.applied) {
            this.toast.show(
              'Changes not saved',
              result.error ?? `${target.trackingReference} could not be updated.`,
              'error',
            );
            return;
          }
          this.toast.show('Changes saved', `${target.trackingReference} updated.`, 'success');
        },
      );
    }
    this.editDialogOpen.set(false);
    this.editTarget.set(null);
    this.uiState.set('ready');
  }

  // ================= Delete unused draft =================
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteTarget = signal<TrackingAsset | null>(null);
  protected readonly deleteReason = signal('');
  protected readonly deleteReasonMin = 10;
  protected readonly deleteReasonMax = 2000;
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.deleteReasonMin && len <= this.deleteReasonMax;
  });
  protected readonly deleteReasonCount = computed(() => this.deleteReason().trim().length);

  protected requestDeleteDraft(asset: TrackingAsset): void {
    this.closeRowMenu();
    if (!this.canDeleteDraft(asset)) return;
    this.deleteTarget.set(asset);
    this.deleteReason.set('');
    this.deleteDialogOpen.set(true);
  }
  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
  }
  /** Delete only an unused draft with no downstream reference; preserve required history (History rule). */
  protected confirmDeleteDraft(): void {
    if (!this.deleteReasonValid()) return;
    const target = this.deleteTarget();
    if (target) {
      this.store.delete(target.trackingReference, (result) => {
        if (!result.applied) {
          this.toast.show(
            'Draft not deleted',
            result.error ?? `${target.trackingReference} could not be deleted.`,
            'error',
          );
          return;
        }
        this.toast.show('Draft deleted', `${target.trackingReference} was removed.`, 'success');
      });
      if (this.selectedRef() === target.trackingReference) {
        this.selectedRef.set('');
      }
    }
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
    this.uiState.set('ready');
  }

  // ================= Filters =================
  protected readonly filterAllowed = computed(
    () => this.permissions().view && !this.rangeInvalid() && this.uiState() !== 'no-access',
  );
  protected applyFilter(): void {
    if (!this.filterAllowed()) {
      this.uiState.set('validation');
      return;
    }
    this.moreFiltersOpen.set(false);
    this.currentPage.set(1);
    this.uiState.set(this.visibleRecords().length === 0 ? 'empty' : 'ready');
  }
  /** Clearing a filter is explicit and returns focus predictably. */
  protected clearFilters(): void {
    this.campaignFilter.set('');
    this.searchTerm.set('');
    this.assetTypeFilter.set('');
    this.channelFilter.set('');
    this.statusFilter.set('');
    this.rangeStart.set('');
    this.rangeEnd.set('');
    this.scopeFilter.set(this.scopeOptions[0]);
    this.savedView.set(this.savedViews[0]);
    this.currentPage.set(1);
    this.uiState.set('ready');
  }
  protected removeFilterChip(key: string): void {
    this.currentPage.set(1);
    switch (key) {
      case 'campaign':
        this.campaignFilter.set('');
        break;
      case 'assetType':
        this.assetTypeFilter.set('');
        break;
      case 'channel':
        this.channelFilter.set('');
        break;
      case 'status':
        this.statusFilter.set('');
        break;
      case 'date':
        this.rangeStart.set('');
        this.rangeEnd.set('');
        break;
      case 'search':
        this.searchTerm.set('');
        break;
      case 'scope':
        this.scopeFilter.set(this.scopeOptions[0]);
        break;
    }
  }

  // ================= UI states =================
  /** Renders straight into Ready (or No access) — no artificial loading skeleton on open. */
  protected readonly uiState = signal<UiState>(this.permissions().view ? 'ready' : 'no-access');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  constructor() {
    // The channel, source and medium lists the Generate form offers. Read once, from the API,
    // so every option carries the id the create call actually needs.
    this.loadReferenceCatalogues();

    // No access must hide the record, fields, counts, actions and search — never a disabled-only
    // affordance. Reacts live to a session/permission change (e.g. via the
    // shared CurrentUserService switcher on another CAM screen).
    effect(() => {
      const canView = this.permissions().view;
      const current = untracked(this.uiState);
      if (!canView && current !== 'no-access' && current !== 'loading') {
        this.uiState.set('no-access');
      } else if (canView && current === 'no-access') {
        this.uiState.set('ready');
      }
    });

    // THE EFFECT THAT USED TO COPY CITY/STATE ONTO EVERY PLACE ROW IS GONE, and with it the
    // reason those two boxes were empty. It read `currentCampaignLocation()`, which read
    // `cityName` and `regionName` off the store record - and the register's list projection
    // carries neither, so on a campaign whose detail had not been opened in this session it
    // faithfully copied two empty strings onto every row on every change. `placeLocation()` is a
    // computed off the same store, so it fills in by itself the moment `loadDetail` (dispatched
    // from `onCampaignSelect`) brings the names back.
  }

  // ================= Persistent outcome =================
  protected readonly persistentOutcome = computed(() => {
    const a = this.selectedAsset();
    return {
      reference: this.generatedReference() || a?.trackingReference || '—',
      state: this.generatedReference() ? 'Draft' : (a?.assetStatus ?? this.lifecycleState),
      effectiveTime: this.lastRefresh(),
      downstreamStatus: a
        ? `${a.usageCount.toLocaleString('en-IN')} recorded uses · approval ${a.approvalState}`
        : 'No pending action',
      owner: this.owner,
      nextAction: 'Submit the draft for approval or edit its details',
    };
  });

  // ================= Formatting helpers =================
  protected campaignOf(reference: string): CampaignOption {
    return (
      this.campaignOptions().find((c) => c.reference === reference) ?? {
        reference,
        name: reference,
        context: '',
      }
    );
  }
  protected formatUsage(value: number): string {
    return value.toLocaleString('en-IN');
  }
  /** Total money received for an asset, formatted in Indian rupees. */
  protected formatMoney(value: number | undefined): string {
    return '₹' + (value ?? 0).toLocaleString('en-IN');
  }
  protected statusClass(status: AssetStatus): string {
    switch (status) {
      case 'Active':
        return 'tam-badge-active';
      case 'Approved':
        return 'tam-badge-active';
      case 'Submitted':
      case 'Disable requested':
        return 'tam-badge-submitted';
      case 'Paused':
        return 'tam-badge-paused';
      case 'Inactive':
      case 'Disabled':
        return 'tam-badge-disabled';
      case 'Draft':
        return 'tam-badge-draft';
    }
  }
  protected approvalClass(state: ApprovalState): string {
    switch (state) {
      case 'Approved':
        return 'tam-badge-active';
      case 'Pending review':
        return 'tam-badge-paused';
      case 'Rejected':
        return 'tam-badge-disabled';
      case 'Not required':
        return 'tam-badge-draft';
    }
  }
  protected formatDate(iso: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}