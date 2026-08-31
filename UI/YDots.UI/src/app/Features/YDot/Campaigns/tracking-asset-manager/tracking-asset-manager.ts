import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { UiState, HistoryRow } from '../../../../Shared/models/campaign.model';
import { AssetStatus, ApprovalState, TrackingAssetPermissions, CampaignOption, TrackingAsset, PlaceCustomField } from '../../../../Shared/models/tracking-asset.model';
import { generateQrMatrix, qrMatrixToPath } from '../../../../Shared/qr-code/qr-code';
import { TrackingAssetStoreService } from '../../../../Shared/services/tracking-asset-store.service';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { ToastService } from '../../../../Shared/services/toast.service';


@Component({
  selector: 'app-tracking-asset-manager',
  imports: [CommonModule, FormsModule, ClickOutsideDirective],
  templateUrl: './tracking-asset-manager.html',
  styleUrl: './tracking-asset-manager.css',
})
export class TrackingAssetManagerComponent {
  private readonly store = inject(TrackingAssetStoreService);
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly toast = inject(ToastService);

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
    disable: this.currentUser.hasPermission('cam.tracking-assets.deactivate'),
    replace: this.currentUser.hasPermission('cam.tracking-assets.edit'),
  }));

  /** Lifecycle states in which the Generate primary action is permitted. */
  private readonly generatePermittedStates = ['Active'];

  // ================= Context and filters =================

  /** Saved filter. */
  protected readonly savedViews = ['All tracking assets (Default)', 'QR destinations', 'Awaiting approval'];
  protected readonly savedView = signal(this.savedViews[0]);

  /** Fixed page size for pagination (records-per-page selector removed per design). */
  protected readonly pageSize = 5;
  protected readonly currentPage = signal(1);

  /** The filters section is hidden until the user opens it with the Filter button. */
  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  /** Search — scope-aware search over reference, destination and campaign. */
  protected readonly searchTerm = signal('');

  /** Asset type — searchable controlled choice; effective approved catalogue. */
  protected readonly assetTypeCatalogue: readonly string[] = [
    'QR Code',
    'Short Link',
    'UTM Link',
    'Landing Page',
  ];
  protected readonly assetTypeFilter = signal<string>('');

  /** Channel — searchable controlled choice; effective approved catalogue. */
  protected readonly channelCatalogue: readonly string[] = [
    'Website',
    'Facebook',
    'Instagram',
    'Email',
    'YouTube',
    'Offline',
  ];
  protected readonly channelFilter = signal<string>('');

  /** Asset status — search-select using only current catalogue values. */
  protected readonly statusCatalogue: readonly AssetStatus[] = ['Draft', 'Submitted', 'Approved', 'Active', 'Inactive', 'Paused', 'Disabled'];
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

  /** A campaign's channel/source label doesn't always match this page's own catalogue values
   *  1:1 (the two catalogues were built for different screens) — alias the ones that clearly
   *  correspond so auto-fill from a selected campaign still lands on a valid option. */
  private readonly channelAlias: Readonly<Record<string, string>> = {
    'On-ground event': 'Offline',
    'Social media': 'Instagram',
  };

  // ================= Campaign selector =================
  /** Scope-aware searchable selector with identity preview — reads live from
   *  the single shared CampaignStoreService. Never a hardcoded list. */
  protected readonly campaignOptions = computed<readonly CampaignOption[]>(() =>
    this.campaignStore.all().map((c) => ({
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
    const type = this.assetTypeFilter();
    const channel = this.channelFilter();
    const status = this.statusFilter();
    const start = this.rangeStart() ? new Date(this.rangeStart()) : null;
    const end = this.rangeEnd() ? new Date(this.rangeEnd()) : null;
    if (end) {
      end.setHours(23, 59, 59, 999);
    }

    return this.records().filter((r) => {
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
  /** Version of the selected asset at the moment it was opened — detects "record changed after
   *  you opened it" if a workflow action is then attempted on the same still-open panel. */
  protected readonly selectedSnapshotVersion = signal<number | null>(null);
  protected selectAsset(ref: string): void {
    this.selectedRef.set(ref);
    this.selectedSnapshotVersion.set(this.store.get(ref)?.version ?? null);
    this.openRowMenu.set(null);
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

  private readonly inGenerateState = () => this.generatePermittedStates.includes(this.lifecycleState);

  /** Generate — primary; appears only when role, permission, scope, state and dependencies allow. */
  protected readonly generateAllowed = computed(
    () => this.permissions().generate && this.inGenerateState() && this.uiState() !== 'no-access',
  );
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
    if (!this.permissions().approve) {
      return 'Approve requires the authorised independent approver permission.';
    }
    if (this.isOwnAsset(asset)) {
      return `This value cannot be approved by its creator (${this.currentUserName()}). An independent approver who did not create this asset must decide.`;
    }
    return '';
  }
  /** Disable — compatible current state only. */
  protected disableAllowed(asset: TrackingAsset | null): boolean {
    return !!asset && this.permissions().disable && (asset.assetStatus === 'Active' || asset.assetStatus === 'Paused');
  }
  /** Why Disable is unavailable — explains permission or an incompatible current state. */
  protected disableDisabledReason(asset: TrackingAsset | null): string {
    if (!asset) return '';
    if (!this.permissions().disable) return 'Disable requires the tracking-asset-manager Disable permission.';
    if (asset.assetStatus !== 'Active' && asset.assetStatus !== 'Paused') {
      return `Disable is only available from Active or Paused, not ${asset.assetStatus}.`;
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
    if (!this.permissions().replace) return 'Edit requires the tracking-asset-manager edit permission.';
    if (asset.assetStatus !== 'Draft') return `Edit is only available while this asset is a Draft, not ${asset.assetStatus}.`;
    return '';
  }
  /** Delete unused draft — Draft with no downstream reference only. */
  protected canDeleteDraft(asset: TrackingAsset): boolean {
    return asset.assetStatus === 'Draft' && !asset.hasDownstreamReference && this.permissions().disable;
  }

  // ----- Row overflow menu -----
  protected readonly openRowMenu = signal<string | null>(null);
  protected toggleRowMenu(ref: string): void {
    this.openRowMenu.update((cur) => (cur === ref ? null : ref));
  }

  // ================= Generate primary action =================
  protected readonly generateDialogOpen = signal(false);

  // Input fields collected by the Generate form.
  protected readonly gAssetType = signal<string>(''); // required
  protected readonly gDestination = signal<string>(''); // required
  protected readonly gCampaign = signal<string>(''); // required
  protected readonly gChannel = signal<string>(''); // optional
  protected readonly gAssetStatus = signal<AssetStatus>('Draft'); // catalogue value
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
    // The NAME, not the id: `channels` holds the API's Guids, and this prefill matches against
    // this screen's own channel labels.
    const firstChannel = rec.channelNames?.[0];
    if (firstChannel) {
      const mapped = this.channelCatalogue.includes(firstChannel) ? firstChannel : this.channelAlias[firstChannel];
      if (mapped) {
        this.gChannel.set(mapped);
        // Asset type is left for the person to choose — it is not auto-derived from the
        // selected campaign or channel.
      }
    }
    if (rec.sources?.length) this.gSource.set(rec.sources[0]);
    if (rec.startDate) this.gActiveFrom.set(rec.startDate);
    if (rec.endDate) this.gActiveTo.set(rec.endDate);
  }

  /** On-ground events happen in more than one physical place for the same campaign, so each place
   *  gets its own separate QR/link — this is what lets the team see which place is contributing most. */
  protected readonly isOnGround = computed(() => this.gChannel() === 'Offline');
  protected readonly gPlaces = signal<
    readonly {
      readonly id: string;
      label: string;
      city: string;
      state: string;
      destination: string;
      customFields: readonly { readonly id: string; key: string; value: string }[];
    }[]
  >([{ id: 'place-1', label: '', city: '', state: '', destination: '', customFields: [] }]);
  private placeSeq = 1;
  private placeFieldSeq = 1;
  /** City/State are never typed by hand — they are fetched from the selected campaign's own
   *  City / State (captured at campaign creation in the Wizard), the same source of truth every
   *  other page reads from. */
  private currentCampaignLocation(): { readonly city: string; readonly state: string } {
    const rec = this.campaignStore.get(this.gCampaign());
    return { city: rec?.city ?? '', state: rec?.region ?? '' };
  }
  protected addPlace(): void {
    this.placeSeq += 1;
    const loc = this.currentCampaignLocation();
    this.gPlaces.update((list) => [
      ...list,
      { id: `place-${this.placeSeq}`, label: '', city: loc.city, state: loc.state, destination: '', customFields: [] },
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
  /** A non-persisting preview of the reference this asset type would receive (pure — safe to call live). */
  protected previewReferenceFor(assetType: string): string {
    return assetType ? this.store.nextReference(assetType) : '';
  }
  protected previewUrlFor(assetType: string, reference: string): string {
    return reference ? this.store.buildGeneratedUrl(reference, assetType === 'QR Code') : '';
  }
  /** Single-destination preview (every asset type except an on-ground multi-place one). */
  protected readonly previewReference = computed(() => this.previewReferenceFor(this.gAssetType()));
  protected readonly previewUrl = computed(() =>
    this.previewUrlFor(this.gAssetType(), this.previewReference()),
  );
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
  /** Per-place preview reference/URL — each place gets a distinct reference so they never collide,
   *  even though none of them are persisted until Generate is confirmed. */
  protected placePreviewReference(index: number): string {
    const base = this.previewReferenceFor(this.gAssetType());
    return base ? `${base}${index > 0 ? `-P${index + 1}` : ''}` : '';
  }
  protected placePreviewUrl(index: number): string {
    return this.previewUrlFor(this.gAssetType(), this.placePreviewReference(index));
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
    this.gAssetStatus.set('Draft');
    this.gSource.set('');
    this.gMedium.set('');
    this.gContentTag.set('');
    this.gActiveFrom.set('');
    this.gActiveTo.set('');
    this.gPlaces.set([{ id: 'place-1', label: '', city: '', state: '', destination: '', customFields: [] }]);
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
      assetStatus: this.gAssetStatus(),
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
          (r) => r.destination.trim().toLowerCase() === p.destination.trim().toLowerCase() && r.channel === this.gChannel(),
        ),
      );
      if (dup) {
        this.generateDialogOpen.set(false);
        this.uiState.set('duplicate');
        return;
      }
      const refs: string[] = [];
      for (const p of places) {
        const asset = this.buildAsset(p.destination.trim(), {
          label: p.label.trim(),
          city: p.city,
          state: p.state,
          customFields: p.customFields,
        });
        this.store.create(asset);
        refs.push(asset.trackingReference);
      }
      this.generatedReferences.set(refs);
      this.generatedReference.set(refs[0] ?? '');
    } else {
      // A tracking asset with the same destination + channel already exists → duplicate handling.
      const dup = this.records().some(
        (r) =>
          r.destination.trim().toLowerCase() === this.gDestination().trim().toLowerCase() &&
          r.channel === this.gChannel(),
      );
      if (dup) {
        this.generateDialogOpen.set(false);
        this.uiState.set('duplicate');
        return;
      }
      const asset = this.buildAsset(this.gDestination().trim());
      this.store.create(asset);
      this.generatedReferences.set([asset.trackingReference]);
      this.generatedReference.set(asset.trackingReference);
    }

    this.generateDialogOpen.set(false);
    const refs = this.generatedReferences();
    this.toast.show(
      'Tracking asset created',
      refs.length > 1 ? `Created ${refs.length} assets: ${refs.join(', ')}.` : `${refs[0]} created as ${this.gAssetStatus()}.`,
      'success',
    );
    this.uiState.set('ready');
  }

  // ================= Submit action =================
  protected readonly submitDialogOpen = signal(false);
  protected readonly submitTarget = signal<TrackingAsset | null>(null);
  protected requestSubmit(asset: TrackingAsset): void {
    this.openRowMenu.set(null);
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
    this.store.update(target.trackingReference, { assetStatus: 'Submitted', approvalState: 'Pending review' });
    this.toast.show('Submitted for approval', `${target.trackingReference} moved to Submitted.`, 'success');
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
    this.openRowMenu.set(null);
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
  /** Record decision, independent authority, reason, effective version, time and resulting state. */
  protected confirmApprove(): void {
    if (!this.approveReasonValid()) return;
    const target = this.approveTarget();
    if (target) {
      this.store.update(target.trackingReference, {
        approvalState: 'Approved',
        assetStatus: target.assetStatus === 'Submitted' ? 'Active' : target.assetStatus,
        approvedByRef: this.currentUserRef(),
        approvedAt: this.lastRefresh(),
      });
    }
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
    if (target) this.toast.show('Asset approved', `${target.trackingReference} approved.`, 'success');
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
    this.openRowMenu.set(null);
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
      this.store.update(target.trackingReference, { assetStatus: 'Disabled' });
    }
    this.disableDialogOpen.set(false);
    this.disableTarget.set(null);
    if (target) this.toast.show('Asset disabled', `${target.trackingReference} was disabled.`, 'success');
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
    this.openRowMenu.set(null);
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
      this.store.update(target.trackingReference, {
        destination: this.editDestination().trim(),
        channel: this.editChannel(),
        source: this.editSource().trim(),
        medium: this.editMedium().trim(),
        contentTag: this.editContentTag().trim(),
        activeFrom: this.editActiveFrom(),
        activeTo: this.editActiveTo(),
      });
    }
    this.editDialogOpen.set(false);
    this.editTarget.set(null);
    if (target) this.toast.show('Changes saved', `${target.trackingReference} updated.`, 'success');
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
    this.openRowMenu.set(null);
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
      this.store.delete(target.trackingReference);
      if (this.selectedRef() === target.trackingReference) {
        this.selectedRef.set('');
      }
    }
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
    if (target) this.toast.show('Draft deleted', `${target.trackingReference} was removed.`, 'success');
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

    // City/State on every on-ground place are fetched from the selected campaign, never
    // typed by hand — keep them in sync whenever the campaign changes or the channel
    // switches to on-ground (Offline), covering either order the person fills the form in.
    effect(() => {
      const campaignRef = this.gCampaign();
      const onGround = this.isOnGround();
      if (!onGround) return;
      const rec = this.campaignStore.get(campaignRef);
      const city = rec?.city ?? '';
      const state = rec?.region ?? '';
      untracked(() => this.gPlaces.update((list) => list.map((p) => ({ ...p, city, state }))));
    });
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