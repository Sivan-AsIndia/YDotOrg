import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as pageData from '../../../../../assets/data/inventory/stock-adjustment-approval.json';
import { InventoryUiState } from '../../../../Shared/models/inventory.model';

interface AdjustmentRecord {
  readonly adjustmentReference: string;
  readonly itemAndBatch: string;
  readonly itemName: string;
  readonly warehouse: string;
  readonly systemQuantity: number;
  readonly observedQuantity: number;
  readonly difference: number;
  readonly reasonCategory: string;
  readonly detailedReason: string;
  readonly evidence: string;
  readonly proposer: string;
  readonly independentApprover: string;
  readonly resultingBalance: { readonly label: string; readonly tone: string; readonly icon: string };
}
interface ScreenData {
  readonly screen: { readonly viewId: string; readonly title: string; readonly route: string; readonly purpose: string; readonly primaryAction: string; readonly viewPermission: string; readonly primaryUsers: readonly string[]; readonly scope: string; readonly lastRefresh: string };
  readonly permissions: Record<string, boolean>;
  readonly records: readonly AdjustmentRecord[];
  readonly savedFilters: readonly string[];
  readonly actions: readonly { id: string; label: string; placement: string; permission: string; allowedState: string; result: string }[];
}

@Component({
  selector: 'app-stock-adjustment-approval',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-adjustment-approval.html',
  styleUrl: './stock-adjustment-approval.css',
})
export class StockAdjustmentApprovalComponent {
  protected readonly data = (pageData as unknown as ScreenData);
  protected readonly uiState = signal<InventoryUiState>('ready');
  protected readonly searchTerm = signal('');
  protected readonly currentPage = signal(1);
  protected readonly pageSize = 5;

  protected readonly filteredRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    return this.data.records.filter((r) =>
      !q || r.adjustmentReference.toLowerCase().includes(q) || r.itemName.toLowerCase().includes(q) || r.itemAndBatch.toLowerCase().includes(q)
    );
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

  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'P. Raghavan · Independent Approver';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 02:30 PM · IST');

  // Detail modal
  protected readonly detailRow = signal<AdjustmentRecord | null>(null);

  protected openDetail(row: AdjustmentRecord): void {
    this.detailRow.set(row);
  }

  protected closeDetail(): void {
    this.detailRow.set(null);
  }

  protected goToPage(page: number): void { if (page >= 1 && page <= this.totalPages()) this.currentPage.set(page); }
  protected applyFilter(): void { this.currentPage.set(1); this.uiState.set(this.filteredRecords().length === 0 ? 'empty' : 'ready'); }
  protected clearFilters(): void { this.searchTerm.set(''); this.currentPage.set(1); this.uiState.set('ready'); }
  protected setUiState(state: InventoryUiState): void { this.uiState.set(state); }
  protected dismissBanner(): void { this.uiState.set('ready'); }
  protected performAction(action: string): void { this.uiState.set('success'); }
  protected toneBadgeClass(tone: string): string {
    if (tone === 'success') return 'bg-success bg-opacity-10 text-success';
    if (tone === 'warning') return 'bg-warning bg-opacity-10 text-warning';
    if (tone === 'danger') return 'bg-danger bg-opacity-10 text-danger';
    return 'bg-primary bg-opacity-10 text-primary';
  }
}