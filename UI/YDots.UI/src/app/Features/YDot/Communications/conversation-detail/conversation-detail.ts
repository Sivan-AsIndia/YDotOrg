import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/* ---------------------------------------------------------------------- */
/* Types                                                                   */
/* ---------------------------------------------------------------------- */

// 'agent' and 'supervisor' were job titles IAM no longer issues. Working a conversation is the
// maker's job and overriding a restriction is the checker's, so they map to INITIATOR and
// APPROVER; 'unauthorized' stays because it previews the no-access state, not a role.
type UserRole = 'tenant-admin' | 'initiator' | 'approver' | 'unauthorized';

type RecordState =
  | 'new'
  | 'assigned'
  | 'in_progress'
  | 'waiting'
  | 'resolved'
  | 'closed'
  | 'reopened'
  | 'restricted'
  | 'draft';

/** Demonstration scenario switch — makes every required UI state (4.2.4) reachable on demand. */
type ScenarioKey =
  | 'default'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no_access'
  | 'conflict'
  | 'dependency_failure'
  | 'success';

type ActiveTab = 'overview' | 'messages' | 'attachments' | 'events';

interface Message {
  id: string;
  direction: 'inbound' | 'outbound';
  channel: string;
  body: string;
  timestamp: string;
  status: 'delivered' | 'sent' | 'read' | 'failed';
}

interface Attachment {
  id: string;
  name: string;
  kind: string;
  sizeLabel: string;
  addedBy: string;
}

interface DeliveryEvent {
  id: string;
  label: string;
  timestamp: string;
  status: 'ok' | 'warning' | 'error';
}

interface TemplateOption {
  id: string;
  label: string;
  channel: string;
}

interface OutcomeOption {
  id: string;
  label: string;
  disabled?: boolean;
  disabledReason?: string;
}

interface FieldErrors {
  replyChannel?: string;
  approvedTemplate?: string;
  replyBody?: string;
  outcome?: string;
}

/* ---------------------------------------------------------------------- */
/* Component                                                               */
/* ---------------------------------------------------------------------- */

@Component({
  selector: 'app-conversation-detail',
  imports: [CommonModule, FormsModule],
  templateUrl: './conversation-detail.html',
  styleUrl: './conversation-detail.css',
})
export class ConversationDetailComponent {

  /* ---------- Access simulator (role / record state / scenario) ------- */

  role: UserRole = 'initiator';
  recordState: RecordState = 'in_progress';
  scenario: ScenarioKey = 'default';

  readonly roleOptions: { id: UserRole; label: string }[] = [
    { id: 'tenant-admin', label: 'Tenant admin' },
    { id: 'initiator', label: 'Initiator' },
    { id: 'approver', label: 'Approver' },
    { id: 'unauthorized', label: 'No view permission' },
  ];

  readonly stateOptions: { id: RecordState; label: string }[] = [
    { id: 'new', label: 'New' },
    { id: 'assigned', label: 'Assigned' },
    { id: 'in_progress', label: 'In progress' },
    { id: 'waiting', label: 'Waiting' },
    { id: 'resolved', label: 'Resolved' },
    { id: 'closed', label: 'Closed' },
    { id: 'reopened', label: 'Reopened' },
    { id: 'restricted', label: 'Restricted' },
    { id: 'draft', label: 'Unused draft' },
  ];

  readonly scenarioOptions: { id: ScenarioKey; label: string }[] = [
    { id: 'default', label: 'Steady state' },
    { id: 'loading', label: 'Loading' },
    { id: 'empty', label: 'Empty' },
    { id: 'validation', label: 'Validation error' },
    { id: 'duplicate', label: 'Duplicate candidate' },
    { id: 'no_access', label: 'No access' },
    { id: 'conflict', label: 'Version conflict' },
    { id: 'dependency_failure', label: 'Dependency failure' },
    { id: 'success', label: 'Persistent success' },
  ];

  /** Draft ownership + downstream-reference flags used for the Delete-draft rule. */
  isDraftCreator = true;
  draftHasDownstreamReference = false;

  /* ---------- Record data (server-derived, read-only) ------------------ */

  reference = 'CONV-2026-004821';
  freshnessLabel = 'Refreshed 2 minutes ago';

  participant = {
    displayName: 'Amara Osei',
    channelHandle: '+91 98•• •• 4821',
    restricted: true,
  };

  identityConfidence: { label: string; tone: 'high' | 'medium' | 'low' } = {
    label: 'Verified',
    tone: 'high',
  };

  consent: { label: string; tone: 'granted' | 'restricted' | 'withdrawn' } = {
    label: 'Consented — SMS and email',
    tone: 'granted',
  };

  campaign = { name: 'Winter Relief Appeal', reference: 'CAM-2026-0101' };
  owner = { name: 'Sarah Johnson', unit: 'Donation Operations' };

  messages: Message[] = [
    { id: 'm1', direction: 'inbound', channel: 'SMS', body: 'Can I still change my monthly pledge amount?', timestamp: 'Today, 09:14', status: 'read' },
    { id: 'm2', direction: 'outbound', channel: 'SMS', body: 'Of course — I can help. What amount would you like to move to?', timestamp: 'Today, 09:22', status: 'delivered' },
    { id: 'm3', direction: 'inbound', channel: 'SMS', body: '₹1,500 a month starting next cycle please.', timestamp: 'Today, 09:26', status: 'read' },
  ];

  attachments: Attachment[] = [
    { id: 'a1', name: 'pledge-change-request.pdf', kind: 'PDF', sizeLabel: '184 KB', addedBy: 'Amara Osei' },
    { id: 'a2', name: 'call-summary.txt', kind: 'Text', sizeLabel: '3 KB', addedBy: 'Sarah Johnson' },
  ];

  deliveryEvents: DeliveryEvent[] = [
    { id: 'e1', label: 'Message accepted by SMS provider', timestamp: 'Today, 09:22', status: 'ok' },
    { id: 'e2', label: 'Delivery confirmed to handset', timestamp: 'Today, 09:23', status: 'ok' },
    { id: 'e3', label: 'Read receipt received', timestamp: 'Today, 09:24', status: 'ok' },
  ];

  relatedRecords = [
    { label: 'Pledge PLG-2026-3390', detail: 'Monthly gift, ₹1,000 → change requested' },
    { label: 'Case CASE-2026-0118', detail: 'Linked donor-services enquiry' },
  ];

  auditTrail = [
    { label: 'Conversation opened', by: 'System', timestamp: 'Today, 09:14' },
    { label: 'Assigned to Sarah Johnson', by: 'Queue router', timestamp: 'Today, 09:15' },
    { label: 'Reply sent', by: 'Sarah Johnson', timestamp: 'Today, 09:22' },
  ];

  /* ---------- Reply composer (conditional, editable fields) ------------ */

  readonly replyChannels = [
    { id: 'sms', label: 'SMS' },
    { id: 'email', label: 'Email' },
    { id: 'whatsapp', label: 'WhatsApp' },
  ];

  readonly approvedTemplates: TemplateOption[] = [
    { id: 'tpl-pledge-ack', label: 'Pledge change — acknowledgement', channel: 'sms' },
    { id: 'tpl-pledge-conf', label: 'Pledge change — confirmed', channel: 'sms' },
    { id: 'tpl-general', label: 'General response', channel: 'sms' },
  ];

  readonly outcomeOptions: OutcomeOption[] = [
    { id: 'resolved', label: 'Resolved — no follow-up needed' },
    { id: 'follow_up', label: 'Resolved — follow-up scheduled' },
    { id: 'escalate', label: 'Escalate to supervisor' },
    { id: 'complaint', label: 'Convert to complaint', disabled: true, disabledReason: 'Requires complaint-intake permission' },
  ];

  composerOpen = true;
  replyChannel = 'sms';
  approvedTemplateId = '';
  templateQuery = '';
  templateMenuOpen = false;
  replyBody = '';
  internalNote = '';
  outcomeId = '';
  nextFollowUp = '';

  readonly replyBodyMaxLength = 480;

  fieldErrors: FieldErrors = {};
  showReview = false;
  showCloseDialog = false;
  showDeleteDialog = false;

  closeReasonCode = '';
  closeReasonText = '';
  readonly closeReasonCodes = [
    { id: 'resolved_no_reply', label: 'Resolved — no reply required' },
    { id: 'resolved_pledge_updated', label: 'Resolved — pledge updated' },
    { id: 'no_longer_reachable', label: 'Donor no longer reachable' },
  ];

  deleteReasonText = '';

  persistentOutcome: { reference: string; state: string; effectiveTime: string; nextAction: string } | null = null;

  /* ---------- Context / filters strip ----------------------------------- */

  searchWithinConversation = '';
  activeTab: ActiveTab = 'overview';

  /* ====================================================================== */
  /* Permission decision layer (mirrors 6.1 decision order, locally)        */
  /* ====================================================================== */

  get canView(): boolean {
    return this.role !== 'unauthorized' && this.scenario !== 'no_access';
  }

  private get isAgentOrSupervisor(): boolean {
    return this.role === 'tenant-admin' || this.role === 'initiator' || this.role === 'approver';
  }

  private get isOpenLifecycleState(): boolean {
    return ['new', 'assigned', 'in_progress', 'waiting', 'reopened'].includes(this.recordState);
  }

  get canSend(): { allowed: boolean; reason?: string } {
    if (!this.isAgentOrSupervisor) return { allowed: false, reason: 'Requires an Initiator, Approver or Tenant admin role.' };
    if (!this.isOpenLifecycleState) return { allowed: false, reason: `Not available in the ${this.stateLabel(this.recordState)} state.` };
    return { allowed: true };
  }

  get canCallLog(): { allowed: boolean; reason?: string } {
    if (!this.isAgentOrSupervisor) return { allowed: false, reason: 'Requires an Initiator, Approver or Tenant admin role.' };
    if (!this.isOpenLifecycleState) return { allowed: false, reason: `Not available in the ${this.stateLabel(this.recordState)} state.` };
    return { allowed: true };
  }

  get canAddNote(): { allowed: boolean; reason?: string } {
    if (!this.isAgentOrSupervisor) return { allowed: false, reason: 'Requires an Initiator, Approver or Tenant admin role.' };
    if (!this.isOpenLifecycleState) return { allowed: false, reason: `Not available in the ${this.stateLabel(this.recordState)} state.` };
    return { allowed: true };
  }

  get canCreateTask(): { allowed: boolean; reason?: string } {
    if (!this.isAgentOrSupervisor) return { allowed: false, reason: 'Requires an Initiator, Approver or Tenant admin role.' };
    if (!['draft', 'new', 'assigned', 'in_progress', 'waiting', 'reopened'].includes(this.recordState)) {
      return { allowed: false, reason: `Not available in the ${this.stateLabel(this.recordState)} state.` };
    }
    return { allowed: true };
  }

  get canClose(): { allowed: boolean; reason?: string } {
    if (!this.isAgentOrSupervisor) return { allowed: false, reason: 'Requires an Initiator, Approver or Tenant admin role.' };
    if (this.recordState === 'closed') return { allowed: false, reason: 'Conversation is already closed.' };
    if (this.recordState === 'restricted') return { allowed: false, reason: 'Restricted records cannot be closed from this view.' };
    return { allowed: true };
  }

  get canDeleteDraft(): { allowed: boolean; reason?: string } {
    if (this.recordState !== 'draft') return { allowed: false, reason: 'Only an unused draft can be deleted.' };
    if (!this.isDraftCreator) return { allowed: false, reason: 'Only the draft creator with delete authority can delete this draft.' };
    if (this.draftHasDownstreamReference) return { allowed: false, reason: 'This draft already has a downstream reference; use Close instead.' };
    return { allowed: true };
  }

  get participantMasked(): boolean {
    // Restricted field: masked unless the effective role/permission allows unmasking.
    return this.participant.restricted && this.role !== 'approver' && this.role !== 'tenant-admin';
  }

  get attachmentsVisible(): boolean {
    // Confidential: visible only within record scope and purpose — i.e. any authenticated, in-scope role.
    return this.canView;
  }

  /* ====================================================================== */
  /* Derived UI state                                                       */
  /* ====================================================================== */

  get isLoading(): boolean {
    return this.scenario === 'loading';
  }

  get isEmpty(): boolean {
    return this.scenario === 'empty';
  }

  get isDuplicate(): boolean {
    return this.scenario === 'duplicate';
  }

  get isConflict(): boolean {
    return this.scenario === 'conflict';
  }

  get isDependencyFailure(): boolean {
    return this.scenario === 'dependency_failure';
  }

  get visibleMessages(): Message[] {
    return this.isEmpty ? [] : this.messages;
  }

  get visibleAttachments(): Attachment[] {
    return this.isEmpty ? [] : this.attachments;
  }

  get visibleEvents(): DeliveryEvent[] {
    return this.isEmpty ? [] : this.deliveryEvents;
  }

  get selectedTemplate(): TemplateOption | undefined {
    return this.approvedTemplates.find((t) => t.id === this.approvedTemplateId);
  }

  get filteredTemplates(): TemplateOption[] {
    const q = this.templateQuery.trim().toLowerCase();
    const inChannel = this.approvedTemplates.filter((t) => t.channel === this.replyChannel);
    if (!q) return inChannel;
    return inChannel.filter((t) => t.label.toLowerCase().includes(q));
  }

  get replyBodyRemaining(): number {
    return this.replyBodyMaxLength - this.replyBody.length;
  }

  stateLabel(id: RecordState): string {
    return this.stateOptions.find((s) => s.id === id)?.label ?? id;
  }

  /* ====================================================================== */
  /* Interactions                                                           */
  /* ====================================================================== */

  setTab(tab: ActiveTab): void {
    this.activeTab = tab;
  }

  toggleTemplateMenu(): void {
    this.templateMenuOpen = !this.templateMenuOpen;
  }

  chooseTemplate(t: TemplateOption): void {
    this.approvedTemplateId = t.id;
    this.templateQuery = t.label;
    this.templateMenuOpen = false;
    if (this.fieldErrors.approvedTemplate) delete this.fieldErrors.approvedTemplate;
  }

  onChannelChange(): void {
    // Changing channel re-scopes the template catalogue; clear a now-invalid selection.
    if (this.selectedTemplate && this.selectedTemplate.channel !== this.replyChannel) {
      this.approvedTemplateId = '';
      this.templateQuery = '';
    }
  }

  private validateComposer(): boolean {
    const errors: FieldErrors = {};
    if (!this.replyChannel) errors.replyChannel = 'Enter Reply channel.';
    if (!this.approvedTemplateId) errors.approvedTemplate = 'Enter Approved template.';
    if (!this.replyBody.trim()) {
      errors.replyBody = 'Enter Reply body.';
    } else if (this.replyBody.length > this.replyBodyMaxLength) {
      errors.replyBody = 'Review Reply body. The value does not meet the stated format or range.';
    }
    if (!this.outcomeId) errors.outcome = 'Enter Outcome.';
    this.fieldErrors = errors;
    return Object.keys(errors).length === 0;
  }

  focusField(field: keyof FieldErrors): void {
    const el = document.getElementById(`field-${field}`);
    el?.focus();
  }

  openReview(): void {
    if (this.scenario === 'validation') {
      this.validateComposer();
      // Force a visible, deterministic validation demo regardless of current input.
      this.fieldErrors.replyBody = this.fieldErrors.replyBody || 'Review Reply body. The value does not meet the stated format or range.';
      this.focusField('replyBody');
      return;
    }
    if (!this.validateComposer()) {
      const firstKey = Object.keys(this.fieldErrors)[0] as keyof FieldErrors;
      this.focusField(firstKey);
      return;
    }
    this.showReview = true;
  }

  cancelReview(): void {
    this.showReview = false;
  }

  confirmSend(): void {
    this.showReview = false;
    const now = 'Today, 09:31';

    if (this.scenario === 'dependency_failure') {
      this.persistentOutcome = {
        reference: this.reference,
        state: 'In progress',
        effectiveTime: now,
        nextAction: 'Retry the failed delivery step',
      };
      return;
    }

    this.messages = [
      ...this.messages,
      {
        id: `m${this.messages.length + 1}`,
        direction: 'outbound',
        channel: this.replyChannels.find((c) => c.id === this.replyChannel)?.label ?? this.replyChannel,
        body: this.replyBody.trim(),
        timestamp: now,
        status: 'sent',
      },
    ];
    this.persistentOutcome = {
      reference: this.reference,
      state: 'In progress',
      effectiveTime: now,
      nextAction: this.outcomeId === 'follow_up' ? 'Follow up on ' + (this.nextFollowUp || 'the scheduled date') : 'None — monitor delivery',
    };
    this.replyBody = '';
    this.internalNote = '';
    this.outcomeId = '';
    this.nextFollowUp = '';
    this.approvedTemplateId = '';
    this.templateQuery = '';
  }

  dismissOutcome(): void {
    this.persistentOutcome = null;
  }

  resolveDuplicate(action: 'compare' | 'merge' | 'change' | 'review' | 'cancel'): void {
    if (action === 'cancel' || action === 'compare' || action === 'review') {
      this.scenario = 'default';
    }
    if (action === 'change') {
      this.scenario = 'default';
      this.focusField('replyChannel');
    }
    if (action === 'merge') {
      this.scenario = 'default';
    }
  }

  resolveConflict(action: 'compare' | 'reapply' | 'cancel'): void {
    this.scenario = 'default';
  }

  retryDependency(): void {
    this.scenario = 'default';
    if (this.persistentOutcome) {
      this.persistentOutcome = { ...this.persistentOutcome, nextAction: 'None — delivery confirmed' };
    }
  }

  openCloseDialog(): void {
    if (!this.canClose.allowed) return;
    this.closeReasonCode = '';
    this.closeReasonText = '';
    this.showCloseDialog = true;
  }

  cancelClose(): void {
    this.showCloseDialog = false;
  }

  confirmClose(): void {
    if (!this.closeReasonCode) return;
    this.recordState = 'closed';
    this.showCloseDialog = false;
    this.persistentOutcome = {
      reference: this.reference,
      state: 'Closed',
      effectiveTime: 'Today, 09:33',
      nextAction: 'None — conversation closed',
    };
  }

  openDeleteDialog(): void {
    if (!this.canDeleteDraft.allowed) return;
    this.deleteReasonText = '';
    this.showDeleteDialog = true;
  }

  cancelDelete(): void {
    this.showDeleteDialog = false;
  }

  confirmDelete(): void {
    this.showDeleteDialog = false;
    this.persistentOutcome = {
      reference: this.reference,
      state: 'Deleted (draft)',
      effectiveTime: 'Today, 09:34',
      nextAction: 'Return to the unified inbox',
    };
  }

  logCall(): void {
    if (!this.canCallLog.allowed) return;
    this.auditTrail = [{ label: 'Call logged', by: this.owner.name, timestamp: 'Just now' }, ...this.auditTrail];
  }

  addNote(): void {
    if (!this.canAddNote.allowed || !this.internalNote.trim()) return;
    this.auditTrail = [{ label: 'Internal note added', by: this.owner.name, timestamp: 'Just now' }, ...this.auditTrail];
  }

  createTask(): void {
    if (!this.canCreateTask.allowed) return;
    this.auditTrail = [{ label: 'Follow-up task created', by: this.owner.name, timestamp: 'Just now' }, ...this.auditTrail];
  }

  returnToLanding(): void {
    this.scenario = 'default';
  }
}