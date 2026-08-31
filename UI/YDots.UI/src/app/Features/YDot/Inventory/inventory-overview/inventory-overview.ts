import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as pageData from '../../../../../assets/data/inventory/inventory-overview.json';
import { InventoryUiState, InventoryOverviewRow, InventoryOverviewPermissions } from '../../../../Shared/models/inventory.model';

interface ScreenData {
  readonly screen: { readonly viewId: string; readonly title: string; readonly route: string; readonly purpose: string; readonly primaryAction: string; readonly viewPermission: string; readonly primaryUsers: readonly string[]; readonly scope: string; readonly lastRefresh: string };
  readonly permissions: Record<string, boolean>;
  readonly warehouses: readonly { reference: string; name: string; context: string }[];
  readonly stockStates: readonly string[];
  readonly records: readonly InventoryOverviewRow[];
  readonly savedFilters: readonly string[];
  readonly fieldContracts: readonly { label: string; control: string; required: boolean; visibility: string }[];
  readonly actions: readonly { id: string; label: string; placement: string; permission: string; allowedState: string; result: string }[];
}

@Component({
  selector: 'app-inventory-overview',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './inventory-overview.html',
  styleUrl: './inventory-overview.css',
})
export class InventoryOverviewComponent {
  protected readonly data = (pageData as unknown as ScreenData);
  protected readonly uiState = signal<InventoryUiState>('ready');
  protected readonly searchTerm = signal('');
  protected readonly warehouseFilter = signal('WH-0000');
  protected readonly stockStateFilter = signal('');
  protected readonly currentPage = signal(1);
  protected readonly pageSize = 5;

  protected readonly permissions: InventoryOverviewPermissions = { view: true, export: true };

  protected readonly filteredRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const wh = this.warehouseFilter();
    const ss = this.stockStateFilter();
    return this.data.records.filter((r) => {
      if (q && !(r.itemOrSku.toLowerCase().includes(q) || r.itemName.toLowerCase().includes(q) || r.batch.toLowerCase().includes(q))) return false;
      if (wh !== 'WH-0000' && r.warehouse !== wh) return false;
      if (ss && r.stockState !== ss) return false;
      return true;
    });
  });

  protected readonly recordCount = computed(() => this.filteredRecords().length);
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.recordCount() / this.pageSize)));
  protected readonly pagedRecords = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.filteredRecords().slice(start, start + this.pageSize);
  });
  protected readonly pageStart = computed(() => (this.recordCount() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize + 1));
  protected readonly pageEnd = computed(() => Math.min(this.currentPage() * this.pageSize, this.recordCount()));
  protected readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    const pages: number[] = [];
    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  });

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim()) chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    const wh = this.data.warehouses.find((w) => w.reference === this.warehouseFilter());
    if (wh && wh.reference !== 'WH-0000') chips.push({ key: 'warehouse', label: `Warehouse: ${wh.name}` });
    if (this.stockStateFilter()) chips.push({ key: 'state', label: `State: ${this.stockStateFilter()}` });
    return chips;
  });

  protected readonly totals = computed(() => {
    const rows = this.filteredRecords();
    return {
      onHand: rows.reduce((s, r) => s + r.onHandQuantity, 0),
      reserved: rows.reduce((s, r) => s + r.reservedQuantity, 0),
      available: rows.reduce((s, r) => s + r.availableQuantity, 0),
      quarantined: rows.reduce((s, r) => s + r.quarantinedQuantity, 0),
      inTransit: rows.reduce((s, r) => s + r.inTransitQuantity, 0),
      damaged: rows.reduce((s, r) => s + r.damagedQuantity, 0),
    };
  });

  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'R. Kumar · Warehouse Lead';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 02:30 PM · IST');

  protected goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.currentPage.set(page);
  }
  protected applyFilter(): void {
    this.currentPage.set(1);
    this.uiState.set(this.filteredRecords().length === 0 ? 'empty' : 'ready');
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.warehouseFilter.set('WH-0000');
    this.stockStateFilter.set('');
    this.currentPage.set(1);
    this.uiState.set('ready');
  }
  protected removeFilterChip(key: string): void {
    if (key === 'search') this.searchTerm.set('');
    if (key === 'warehouse') this.warehouseFilter.set('WH-0000');
    if (key === 'state') this.stockStateFilter.set('');
  }
  protected setUiState(state: InventoryUiState): void { this.uiState.set(state); }
  protected dismissBanner(): void { this.uiState.set('ready'); }
  protected stateBadgeClass(state: string): string {
    if (state === 'Available') return 'bg-success bg-opacity-10 text-success';
    if (state === 'Quarantined') return 'bg-warning bg-opacity-10 text-warning';
    if (state === 'In Transit') return 'bg-info bg-opacity-10 text-info';
    if (state === 'Damaged') return 'bg-danger bg-opacity-10 text-danger';
    return 'bg-primary bg-opacity-10 text-primary';
  }
  protected stockIcon(state: string): string {
    if (state === 'Available') return 'ri-checkbox-circle-line';
    if (state === 'Quarantined') return 'ri-error-warning-line';
    if (state === 'In Transit') return 'ri-truck-line';
    if (state === 'Damaged') return 'ri-alert-line';
    return 'ri-inbox-line';
  }
  protected performAction(action: string): void {
    this.uiState.set('success');
  }
  protected readonly selectedCount = signal(0);

  /** Row detail modal — shows all record fields on responsive screens. */
  protected readonly detailRow = signal<InventoryOverviewRow | null>(null);
  protected openDetail(row: InventoryOverviewRow): void {
    this.detailRow.set(row);
  }
  protected closeDetail(): void {
    this.detailRow.set(null);
  }
}
