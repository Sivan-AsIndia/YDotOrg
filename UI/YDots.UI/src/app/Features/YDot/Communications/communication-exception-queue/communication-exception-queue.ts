import { Component, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

interface ExceptionRecord {
  id: string;
  eventReference: string;
  failureType: 'Bounced' | 'Blocked' | 'Unmatched' | 'Provider rejected';
  provider: string;
  state: 'New' | 'Retrying' | 'Escalated' | 'Suppressed' | 'Resolved';
  age: string;
  conversationRef: string;
  providerResponseCode: string;
  failureReason: string;
  maskedPayloadSummary: string;
  attempts: number;
  nextRetry: string;
  owner: string;
  resolutionAction: string;
  linkedRecord: string;
  resolutionReason: string;
}

type ViewState = 'normal' | 'loading' | 'empty' | 'noAccess' | 'conflict' | 'dependencyFailure';
type ActionType = 'retry' | 'link' | 'suppress' | 'escalate';

@Component({
  selector: 'app-communication-exception-queue',
  imports: [CommonModule, FormsModule],
  templateUrl: './communication-exception-queue.html',
  styleUrl: './communication-exception-queue.css',
})
export class CommunicationExceptionQueueComponent {
  // ----- Static catalogues (approved values only) -----
  readonly stateOrder: ExceptionRecord['state'][] = ['New', 'Retrying', 'Escalated', 'Suppressed', 'Resolved'];
  readonly failureTypes = ['All types', 'Bounced', 'Blocked', 'Unmatched', 'Provider rejected'];
  readonly stateOptions = ['All states', ...this.stateOrder];
  readonly previewStates: { key: ViewState; label: string }[] = [
    { key: 'normal', label: 'Live' },
    { key: 'loading', label: 'Loading' },
    { key: 'empty', label: 'Empty' },
    { key: 'noAccess', label: 'No access' },
    { key: 'conflict', label: 'Conflict' },
    { key: 'dependencyFailure', label: 'Dependency' },
  ];
  readonly skeletonRows = [1, 2, 3, 4];

  lastRefreshed = 'Today, 09:12 am';

  // ----- Seed data (effective-scope records) -----
  records: ExceptionRecord[] = [
    { id: 'exc-1001', eventReference: 'EVT-2026-88301', failureType: 'Bounced', provider: 'Email Relay', state: 'New', age: '25m', conversationRef: 'CNV-77410', providerResponseCode: '550 5.1.1', failureReason: 'Recipient mailbox does not exist.', maskedPayloadSummary: 'to: d***@ex****.org · subject: Winter Relief Appeal', attempts: 1, nextRetry: 'Not scheduled', owner: 'Unassigned', resolutionAction: '', linkedRecord: '', resolutionReason: '' },
    { id: 'exc-1002', eventReference: 'EVT-2026-88298', failureType: 'Blocked', provider: 'SMS Gateway', state: 'New', age: '1h', conversationRef: 'CNV-77390', providerResponseCode: '403 CARRIER_BLOCK', failureReason: 'Carrier blocked promotional content on this route.', maskedPayloadSummary: 'to: +91 9****210 · template: Donation Reminder', attempts: 1, nextRetry: 'Not scheduled', owner: 'Unassigned', resolutionAction: '', linkedRecord: '', resolutionReason: '' },
    { id: 'exc-1008', eventReference: 'EVT-2026-87510', failureType: 'Unmatched', provider: 'WhatsApp Gateway', state: 'New', age: '2h', conversationRef: 'CNV-75700', providerResponseCode: '—', failureReason: 'Sender number not recognised for any active queue.', maskedPayloadSummary: 'from: +91 9****018 · message: "Is my donation confirmed?"', attempts: 1, nextRetry: 'Not scheduled', owner: 'Unassigned', resolutionAction: '', linkedRecord: '', resolutionReason: '' },
    { id: 'exc-1003', eventReference: 'EVT-2026-88250', failureType: 'Provider rejected', provider: 'WhatsApp Gateway', state: 'Retrying', age: '3h', conversationRef: 'CNV-77120', providerResponseCode: '131047 TEMPLATE_PACING', failureReason: 'Provider rejected due to template pacing limit.', maskedPayloadSummary: 'to: +91 8****904 · template: Complaint Update', attempts: 2, nextRetry: 'Today, 4:30 pm', owner: 'Rahul Menon', resolutionAction: '', linkedRecord: '', resolutionReason: '' },
    { id: 'exc-1004', eventReference: 'EVT-2026-88190', failureType: 'Unmatched', provider: 'Email Relay', state: 'Retrying', age: '6h', conversationRef: 'CNV-76980', providerResponseCode: '—', failureReason: 'Inbound reply could not be matched to an open conversation.', maskedPayloadSummary: 'from: p***@g****.com · subject: Re: Receipt request', attempts: 1, nextRetry: 'Today, 6:00 pm', owner: 'Priya Raman', resolutionAction: '', linkedRecord: '', resolutionReason: '' },
    { id: 'exc-1005', eventReference: 'EVT-2026-87990', failureType: 'Blocked', provider: 'Push Notification Service', state: 'Escalated', age: '18h', conversationRef: 'CNV-76500', providerResponseCode: '401 INVALID_TOKEN', failureReason: 'Device token invalid or app uninstalled.', maskedPayloadSummary: 'device: iOS · campaign: Spring Membership Drive', attempts: 3, nextRetry: 'Not scheduled', owner: 'Arjun Iyer', resolutionAction: 'Escalated to supervisor', linkedRecord: '', resolutionReason: 'Repeated failures across three attempts; needs device-token refresh review.' },
    { id: 'exc-1006', eventReference: 'EVT-2026-87820', failureType: 'Provider rejected', provider: 'SMS Gateway', state: 'Suppressed', age: '1d', conversationRef: 'CNV-76210', providerResponseCode: '429 RATE_LIMIT', failureReason: 'Recipient previously opted out of SMS communication.', maskedPayloadSummary: 'to: +91 7****655 · template: SLA Breach Notice', attempts: 4, nextRetry: 'Not scheduled', owner: 'Priya Raman', resolutionAction: 'Suppressed', linkedRecord: '', resolutionReason: 'Recipient opted out; suppression aligned with contact restriction on file.' },
    { id: 'exc-1007', eventReference: 'EVT-2026-87610', failureType: 'Bounced', provider: 'Email Relay', state: 'Resolved', age: '2d', conversationRef: 'CNV-75810', providerResponseCode: '550 5.1.1', failureReason: 'Typo in donor-entered address.', maskedPayloadSummary: 'to: s***@y****.com · subject: Thank you for your gift', attempts: 2, nextRetry: 'Not scheduled', owner: 'Rahul Menon', resolutionAction: 'Linked to existing record', linkedRecord: 'PARTY-44210', resolutionReason: 'Corrected address confirmed with donor by phone and linked to updated party record.' },
  ];

  // ----- Filters -----
  failureTypeFilter = 'All types';
  providerFilter = '';
  stateFilter = 'All states';
  ageFilter = '';

  // ----- View state (design-review preview control) -----
  viewState: ViewState = 'normal';

  // ----- Detail drawer -----
  selected: ExceptionRecord | null = null;
  detailOpen = false;

  // ----- Action confirmation -----
  pendingAction: ActionType | null = null;
  actionReason = '';
  actionLinkedRecord = '';
  actionError = '';

  // ----- Toast -----
  toast: { message: string; tone: 'success' | 'error' } | null = null;
  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  get filteredRecords(): ExceptionRecord[] {
    return this.records.filter((r) => {
      if (this.failureTypeFilter !== 'All types' && r.failureType !== this.failureTypeFilter) return false;
      if (this.stateFilter !== 'All states' && r.state !== this.stateFilter) return false;
      if (this.providerFilter && !r.provider.toLowerCase().includes(this.providerFilter.trim().toLowerCase())) return false;
      if (this.ageFilter && !r.age.toLowerCase().includes(this.ageFilter.trim().toLowerCase())) return false;
      return true;
    });
  }

  get groups(): { state: ExceptionRecord['state']; records: ExceptionRecord[] }[] {
    return this.stateOrder
      .map((state) => ({ state, records: this.filteredRecords.filter((r) => r.state === state) }))
      .filter((g) => g.records.length > 0);
  }

  get totalInScope(): number {
    return this.records.length;
  }
  get filteredCount(): number {
    return this.filteredRecords.length;
  }
  get retryingCount(): number {
    return this.records.filter((r) => r.state === 'Retrying').length;
  }
  get escalatedCount(): number {
    return this.records.filter((r) => r.state === 'Escalated').length;
  }
  get suppressedCount(): number {
    return this.records.filter((r) => r.state === 'Suppressed').length;
  }

  hasActiveFilters(): boolean {
    return (
      this.failureTypeFilter !== 'All types' ||
      this.stateFilter !== 'All states' ||
      !!this.providerFilter ||
      !!this.ageFilter
    );
  }

  resetFilters(): void {
    this.failureTypeFilter = 'All types';
    this.stateFilter = 'All states';
    this.providerFilter = '';
    this.ageFilter = '';
  }

  refresh(): void {
    this.lastRefreshed = 'Just now';
    this.showToast('Queue refreshed. Counts, filters and exports reflect your effective scope.', 'success');
  }

  setViewState(state: ViewState): void {
    this.viewState = state;
    if (state !== 'normal') {
      this.closeDetail();
    }
  }

  openRecord(record: ExceptionRecord): void {
    this.selected = record;
    this.detailOpen = true;
  }

  closeDetail(): void {
    this.detailOpen = false;
    this.selected = null;
    this.pendingAction = null;
    this.actionError = '';
  }

  actionLabel(type: ActionType | null): string {
    switch (type) {
      case 'retry':
        return 'Retry';
      case 'link':
        return 'Link';
      case 'suppress':
        return 'Suppress';
      case 'escalate':
        return 'Escalate';
      default:
        return '';
    }
  }

  requestAction(type: ActionType): void {
    this.pendingAction = type;
    this.actionReason = this.selected?.resolutionReason ?? '';
    this.actionLinkedRecord = this.selected?.linkedRecord ?? '';
    this.actionError = '';
  }

  cancelAction(): void {
    this.pendingAction = null;
    this.actionError = '';
  }

  confirmAction(): void {
    if (!this.selected || !this.pendingAction) return;

    const needsReason = this.pendingAction === 'suppress' || this.pendingAction === 'escalate';
    if (needsReason && this.actionReason.trim().length < 10) {
      this.actionError = 'Enter resolution reason. Use at least 10 characters for controlled decisions.';
      return;
    }
    if (this.pendingAction === 'link' && !this.actionLinkedRecord.trim()) {
      this.actionError = 'Enter linked record.';
      return;
    }

    const record = this.selected;
    const effective = 'Today, ' + new Date().toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' });

    switch (this.pendingAction) {
      case 'retry':
        record.attempts += 1;
        record.state = 'Retrying';
        record.nextRetry = 'Queued now';
        record.resolutionAction = 'Retry queued';
        record.resolutionReason = this.actionReason || record.resolutionReason;
        break;
      case 'link':
        record.state = 'Resolved';
        record.linkedRecord = this.actionLinkedRecord.trim();
        record.resolutionAction = 'Linked to existing record';
        record.resolutionReason = this.actionReason;
        break;
      case 'suppress':
        record.state = 'Suppressed';
        record.resolutionAction = 'Suppressed';
        record.resolutionReason = this.actionReason;
        break;
      case 'escalate':
        record.state = 'Escalated';
        record.resolutionAction = 'Escalated to supervisor';
        record.resolutionReason = this.actionReason;
        break;
    }

    this.showToast(
      `Saved successfully. Reference ${record.eventReference}; state ${record.state}; effective ${effective}.`,
      'success'
    );
    this.pendingAction = null;
    this.closeDetail();
  }

  showToast(message: string, tone: 'success' | 'error' = 'success'): void {
    this.toast = { message, tone };
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => (this.toast = null), 5000);
  }

  dismissToast(): void {
    this.toast = null;
    clearTimeout(this.toastTimer);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.pendingAction) {
      this.cancelAction();
    } else if (this.detailOpen) {
      this.closeDetail();
    }
  }

  trackById(_: number, r: ExceptionRecord): string {
    return r.id;
  }

  stateBadgeClass(state: string): string {
    return 'state-' + state.toLowerCase();
  }

  failureIcon(type: string): string {
    switch (type) {
      case 'Bounced':
        return '↩';
      case 'Blocked':
        return '⛔';
      case 'Unmatched':
        return '❓';
      case 'Provider rejected':
        return '⚠';
      default:
        return '•';
    }
  }
}