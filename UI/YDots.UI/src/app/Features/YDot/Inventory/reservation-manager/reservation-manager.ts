import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as pageData from '../../../../../assets/data/inventory/reservation-manager.json';
import { InventoryUiState, ReservationRecord } from '../../../../Shared/models/inventory.model';

interface ScreenData {
  readonly screen: { readonly viewId: string; readonly title: string; readonly route: string; readonly purpose: string; readonly primaryAction: string; readonly viewPermission: string; readonly primaryUsers: readonly string[]; readonly scope: string; readonly lastRefresh: string };
  readonly permissions: Record<string, boolean>;
  readonly reservationStates: readonly string[];
  readonly records: readonly ReservationRecord[];
  readonly savedFilters: readonly string[];
  readonly fieldContracts: readonly { label: string; control: string; required: boolean; visibility: string }[];
  readonly actions: readonly { id: string; label: string; placement: string; permission: string; allowedState: string; result: string }[];
}

@Component({
  selector: 'app-reservation-manager',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './reservation-manager.html',
  styleUrl: './reservation-manager.css',
})
export class ReservationManagerComponent {
  protected readonly data = (pageData as unknown as ScreenData);
  protected readonly uiState = signal<InventoryUiState>('ready');
  protected readonly searchTerm = signal('');
  protected readonly stateFilter = signal('');
  protected readonly currentPage = signal(1);
  protected readonly pageSize = 5;

  protected readonly filteredRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const st = this.stateFilter();
    return this.data.records.filter((r) => {
      if (q && !(r.reservationReference.toLowerCase().includes(q) || r.itemName.toLowerCase().includes(q) || r.eventOrAllocation.toLowerCase().includes(q))) return false;
      if (st && r.reservationState !== st) return false;
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
    if (this.stateFilter()) chips.push({ key: 'state', label: `State: ${this.stateFilter()}` });
    return chips;
  });

  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'P. Nair · Distribution Lead';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 02:30 PM · IST');

  // Detail modal
  protected readonly detailRow = signal<ReservationRecord | null>(null);

  protected openDetail(row: ReservationRecord): void {
    this.detailRow.set(row);
  }

  protected closeDetail(): void {
    this.detailRow.set(null);
  }

  protected goToPage(page: number): void { if (page >= 1 && page <= this.totalPages()) this.currentPage.set(page); }
  protected applyFilter(): void { this.currentPage.set(1); this.uiState.set(this.filteredRecords().length === 0 ? 'empty' : 'ready'); }
  protected clearFilters(): void { this.searchTerm.set(''); this.stateFilter.set(''); this.currentPage.set(1); this.uiState.set('ready'); }
  protected removeFilterChip(key: string): void { if (key === 'search') this.searchTerm.set(''); if (key === 'state') this.stateFilter.set(''); }
  protected setUiState(state: InventoryUiState): void { this.uiState.set(state); }
  protected dismissBanner(): void { this.uiState.set('ready'); }
  protected performAction(action: string): void { this.uiState.set('success'); }
  protected stateBadgeClass(state: string): string {
    if (state === 'Reserved') return 'bg-info bg-opacity-10 text-info';
    if (state === 'Pending') return 'bg-warning bg-opacity-10 text-warning';
    if (state === 'Consumed') return 'bg-success bg-opacity-10 text-success';
    if (state === 'Expired') return 'bg-danger bg-opacity-10 text-danger';
    return 'bg-secondary bg-opacity-10 text-secondary';
  }
}