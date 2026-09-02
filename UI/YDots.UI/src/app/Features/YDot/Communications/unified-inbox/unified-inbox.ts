import { Component, computed, signal, WritableSignal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/* ---------------------------------------------------------------------- *
 *  SCR-COM-001 — Unified inbox
 *  Route: /engagement/unified-inbox
 *  Purpose: Work permitted conversations by queue, SLA and assignment.
 *  Primary users: INITIATOR (and TENANT_ADMIN, who holds everything)
 *  Primary action: Accept
 * ---------------------------------------------------------------------- */

export type Channel = 'Email' | 'SMS' | 'WhatsApp' | 'Phone' | 'Web chat';
export type SlaState = 'On track' | 'Due soon' | 'Breaching' | 'Breached';
export type ConvStatus = 'New' | 'In progress' | 'Waiting on donor' | 'Closed';
export type Priority = 'Low' | 'Normal' | 'High' | 'Urgent';
export type ActionType = 'Accept' | 'Reply' | 'Transfer' | 'Escalate' | 'Close';
export type ViewState = 'normal' | 'loading' | 'empty' | 'no-access' | 'conflict' | 'dependency-failure';

export interface Conversation {
  id: string;                 // stable reference, e.g. CONV-2026-004512
  queue: string;
  channel: Channel;
  owner: string | null;       // null = Unassigned
  slaState: SlaState;
  slaDueLabel: string;
  unread: boolean;
  unreadCount: number;
  partyName: string;
  partyPreview: string;
  restricted?: boolean;       // masked party detail
  campaign: string;
  lastMessagePreview: string;
  status: ConvStatus;
  priority: Priority;
  updatedLabel: string;
  stale?: boolean;            // record changed after load -> conflict demo
}

interface ScopeQueue {
  name: string;
  inScope: boolean;
}

interface ActionModalState {
  type: ActionType;
  conversation: Conversation;
  reason: string;
  targetQueue: string;
  targetOwner: string;
  message: string;
  typedConfirm: string;
  submitting: boolean;
  errors: Record<string, string>;
}

interface OutcomeBanner {
  kind: 'success' | 'dependency-failure';
  reference: string;
  state: string;
  effectiveTime: string;
  nextAction: string;
  correlationRef?: string;
}

const CURRENT_USER = 'Sophie Bennett';

const MOCK_CONVERSATIONS: Conversation[] = [
  {
    id: 'CONV-2026-004512', queue: 'Donation Operations', channel: 'Email', owner: null,
    slaState: 'Breaching', slaDueLabel: 'Due in 18m', unread: true, unreadCount: 3,
    partyName: 'Meera Krishnan', partyPreview: 'Recurring donor · 3 yrs', campaign: 'Winter Relief Appeal',
    lastMessagePreview: 'I was charged twice for my monthly gift, can you help me sort this out?',
    status: 'New', priority: 'Urgent', updatedLabel: '4m ago',
  },
  {
    id: 'CONV-2026-004498', queue: 'Donation Operations', channel: 'WhatsApp', owner: 'Arjun Rao',
    slaState: 'Due soon', slaDueLabel: 'Due in 1h 40m', unread: true, unreadCount: 1,
    partyName: 'Restricted donor record', partyPreview: '••••••••', restricted: true, campaign: 'Winter Relief Appeal',
    lastMessagePreview: 'Thank you for confirming, please send the receipt when you can.',
    status: 'In progress', priority: 'Normal', updatedLabel: '22m ago', stale: true,
  },
  {
    id: 'CONV-2026-004471', queue: 'Donation Operations', channel: 'SMS', owner: 'Sophie Bennett',
    slaState: 'On track', slaDueLabel: 'Due in 5h 10m', unread: false, unreadCount: 0,
    partyName: 'Daniel Fernandes', partyPreview: 'First-time donor', campaign: 'Emergency Response Fund',
    lastMessagePreview: 'Got it, I will check my bank and reply tomorrow morning.',
    status: 'Waiting on donor', priority: 'Low', updatedLabel: '1h ago',
  },
  {
    id: 'CONV-2026-004450', queue: 'Community Outreach', channel: 'Web chat', owner: null,
    slaState: 'On track', slaDueLabel: 'Due in 3h 55m', unread: true, unreadCount: 2,
    partyName: 'Priya Subramaniam', partyPreview: 'Volunteer enquiry', campaign: 'Community Outreach Drive',
    lastMessagePreview: 'Can I volunteer for the weekend food distribution event?',
    status: 'New', priority: 'Normal', updatedLabel: '9m ago',
  },
  {
    id: 'CONV-2026-004433', queue: 'Community Outreach', channel: 'Phone', owner: 'Kavya Nair',
    slaState: 'Breached', slaDueLabel: 'Overdue by 32m', unread: false, unreadCount: 0,
    partyName: 'Ramesh Iyer', partyPreview: 'Monthly donor · 1 yr', campaign: 'Community Outreach Drive',
    lastMessagePreview: 'Called back — no answer. Left a voicemail regarding pledge renewal.',
    status: 'In progress', priority: 'High', updatedLabel: '48m ago',
  },
  {
    id: 'CONV-2026-004402', queue: 'Community Outreach', channel: 'Email', owner: 'Kavya Nair',
    slaState: 'On track', slaDueLabel: 'Due in 22h 0m', unread: false, unreadCount: 0,
    partyName: 'Lakshmi Venkatesh', partyPreview: 'Corporate matching enquiry', campaign: 'Community Outreach Drive',
    lastMessagePreview: 'Closing this out — matching gift confirmed with employer.',
    status: 'Closed', priority: 'Low', updatedLabel: '1d ago',
  },
];

const SCOPE_QUEUES: ScopeQueue[] = [
  { name: 'Donation Operations', inScope: true },
  { name: 'Community Outreach', inScope: true },
  { name: 'Major Gifts', inScope: false },
  { name: 'Corporate Partnerships', inScope: false },
];

const SAVED_FILTERS: { label: string; apply: (c: Conversation) => boolean }[] = [
  { label: 'My open conversations', apply: (c) => c.owner === CURRENT_USER && c.status !== 'Closed' },
  { label: 'Breaching SLA', apply: (c) => c.slaState === 'Breaching' || c.slaState === 'Breached' },
  { label: 'Unread only', apply: (c) => c.unread },
  { label: 'Unassigned', apply: (c) => c.owner === null },
];

@Component({
  selector: 'app-unified-inbox',
  imports: [CommonModule, FormsModule],
  templateUrl: './unified-inbox.html',
  styleUrl: './unified-inbox.css',
})
export class UnifiedInboxComponent {
  readonly currentUser = CURRENT_USER;
  readonly scopeQueues = SCOPE_QUEUES;
  readonly inScopeQueueNames = SCOPE_QUEUES.filter((q) => q.inScope).map((q) => q.name);
  readonly savedFilters = SAVED_FILTERS;
  readonly channels: Channel[] = ['Email', 'SMS', 'WhatsApp', 'Phone', 'Web chat'];
  readonly slaStates: SlaState[] = ['On track', 'Due soon', 'Breaching', 'Breached'];

  // ---- core data ----
  conversations: WritableSignal<Conversation[]> = signal(MOCK_CONVERSATIONS.map((c) => ({ ...c })));

  // ---- lifecycle / demo view-state control (for acceptance-criteria demonstration) ----
  viewState = signal<ViewState>('loading');
  lastRefreshed = signal(this.formatTime(new Date()));
  demoMenuOpen = signal(false);

  constructor() {
    setTimeout(() => {
      if (this.viewState() === 'loading') this.viewState.set('normal');
    }, 900);
  }

  // ---- filters ----
  activeTab = signal<'all' | 'mine' | 'unread' | 'breaching'>('all');
  searchTerm = signal('');
  selectedChannels = signal<Set<Channel>>(new Set());
  selectedOwner = signal<string>('');
  selectedSlaState = signal<SlaState | ''>('');
  savedFilterLabel = signal<string>('');
  filtersDrawerOpen = signal(false);

  owners = computed(() => {
    const set = new Set<string>();
    this.conversations().forEach((c) => c.owner && set.add(c.owner));
    return Array.from(set).sort();
  });

  activeFilterCount = computed(() => {
    let n = 0;
    if (this.searchTerm().trim()) n++;
    if (this.selectedChannels().size) n++;
    if (this.selectedOwner()) n++;
    if (this.selectedSlaState()) n++;
    if (this.savedFilterLabel()) n++;
    if (this.activeTab() !== 'all') n++;
    return n;
  });

  // conversations visible to this actor's effective data scope
  inScopeConversations = computed(() =>
    this.conversations().filter((c) => this.inScopeQueueNames.includes(c.queue))
  );

  filteredConversations = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const channels = this.selectedChannels();
    const owner = this.selectedOwner();
    const sla = this.selectedSlaState();
    const savedLabel = this.savedFilterLabel();
    const saved = SAVED_FILTERS.find((f) => f.label === savedLabel);
    const tab = this.activeTab();

    return this.inScopeConversations().filter((c) => {
      if (term) {
        const hay = `${c.id} ${c.queue} ${c.partyName} ${c.campaign} ${c.lastMessagePreview}`.toLowerCase();
        if (!hay.includes(term)) return false;
      }
      if (channels.size && !channels.has(c.channel)) return false;
      if (owner && c.owner !== owner) return false;
      if (sla && c.slaState !== sla) return false;
      if (saved && !saved.apply(c)) return false;
      if (tab === 'mine' && c.owner !== CURRENT_USER) return false;
      if (tab === 'unread' && !c.unread) return false;
      if (tab === 'breaching' && !(c.slaState === 'Breaching' || c.slaState === 'Breached')) return false;
      return true;
    });
  });

  groupedByQueue = computed(() => {
    const groups: Record<string, Conversation[]> = {};
    for (const name of this.inScopeQueueNames) groups[name] = [];
    for (const c of this.filteredConversations()) {
      (groups[c.queue] ||= []).push(c);
    }
    return Object.entries(groups).filter(([, list]) => list.length > 0);
  });

  collapsedGroups = signal<Set<string>>(new Set());
  toggleGroup(queue: string) {
    const next = new Set(this.collapsedGroups());
    next.has(queue) ? next.delete(queue) : next.add(queue);
    this.collapsedGroups.set(next);
  }
  isGroupCollapsed(queue: string) {
    return this.collapsedGroups().has(queue);
  }

  totals = computed(() => {
    const list = this.filteredConversations();
    return {
      count: list.length,
      unread: list.reduce((n, c) => n + c.unreadCount, 0),
      breaching: list.filter((c) => c.slaState === 'Breaching' || c.slaState === 'Breached').length,
    };
  });

  clearFilters() {
    this.searchTerm.set('');
    this.selectedChannels.set(new Set());
    this.selectedOwner.set('');
    this.selectedSlaState.set('');
    this.savedFilterLabel.set('');
    this.activeTab.set('all');
  }

  toggleChannel(ch: Channel) {
    const next = new Set(this.selectedChannels());
    next.has(ch) ? next.delete(ch) : next.add(ch);
    this.selectedChannels.set(next);
  }

  refresh() {
    this.viewState.set('loading');
    setTimeout(() => {
      this.lastRefreshed.set(this.formatTime(new Date()));
      this.viewState.set('normal');
    }, 700);
  }

  // ---- header primary action: Accept next eligible conversation ----
  nextEligibleForAccept = computed(() =>
    this.filteredConversations().find((c) => c.status === 'New' && !c.stale) || null
  );

  acceptNext() {
    const conv = this.nextEligibleForAccept();
    if (conv) this.openAction('Accept', conv);
  }

  // ---- restricted value popover ----
  restrictedTooltipFor = signal<string | null>(null);
  showRestrictedTooltip(id: string) {
    this.restrictedTooltipFor.set(id);
  }
  hideRestrictedTooltip() {
    this.restrictedTooltipFor.set(null);
  }

  // ---- scope chip no-access explanation ----
  outOfScopeTooltip = signal<string | null>(null);
  showOutOfScopeTooltip(name: string) {
    this.outOfScopeTooltip.set(name);
  }
  hideOutOfScopeTooltip() {
    this.outOfScopeTooltip.set(null);
  }

  // ---- copy stable reference ----
  copiedRef = signal<string | null>(null);
  copyReference(id: string) {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(id).catch(() => {});
    }
    this.copiedRef.set(id);
    setTimeout(() => {
      if (this.copiedRef() === id) this.copiedRef.set(null);
    }, 1600);
  }

  // ---- eligible actions per conversation ----
  eligibleActions(c: Conversation): ActionType[] {
    if (c.status === 'Closed') return [];
    if (c.status === 'New') return ['Accept'];
    return ['Reply', 'Transfer', 'Escalate', 'Close'];
  }

  // ---- action modal ----
  actionModal = signal<ActionModalState | null>(null);
  conflictModal = signal<Conversation | null>(null);
  expandedConversation = signal<Conversation | null>(null);

  openAction(type: ActionType, conversation: Conversation) {
    if (conversation.stale) {
      this.conflictModal.set(conversation);
      return;
    }
    this.actionModal.set({
      type,
      conversation,
      reason: '',
      targetQueue: this.inScopeQueueNames.find((q) => q !== conversation.queue) || '',
      targetOwner: '',
      message: '',
      typedConfirm: '',
      submitting: false,
      errors: {},
    });
  }

  closeActionModal() {
    this.actionModal.set(null);
  }

  resolveConflict(mode: 'compare' | 'reapply' | 'cancel') {
    const conv = this.conflictModal();
    if (!conv) return;
    if (mode === 'reapply') {
      this.conversations.update((list) =>
        list.map((c) => (c.id === conv.id ? { ...c, stale: false } : c))
      );
    }
    this.conflictModal.set(null);
  }

  validateModal(m: ActionModalState): Record<string, string> {
    const errors: Record<string, string> = {};
    if (m.type === 'Close') {
      if (!m.reason.trim()) errors['reason'] = 'Enter Reason.';
      else if (m.reason.trim().length < 8) errors['reason'] = 'Review Reason. The value does not meet the stated format or range.';
      if (m.conversation.priority === 'Urgent' && m.typedConfirm.trim().toUpperCase() !== 'CLOSE') {
        errors['typedConfirm'] = 'Type CLOSE to confirm this irreversible action.';
      }
    }
    if (m.type === 'Transfer' && !m.targetQueue) {
      errors['targetQueue'] = 'Enter Target queue.';
    }
    if (m.type === 'Reply' && !m.message.trim()) {
      errors['message'] = 'Enter Message.';
    }
    return errors;
  }

  outcome = signal<OutcomeBanner | null>(null);

  confirmAction() {
    const m = this.actionModal();
    if (!m) return;
    const errors = this.validateModal(m);
    if (Object.keys(errors).length) {
      this.actionModal.set({ ...m, errors });
      return;
    }
    this.actionModal.set({ ...m, submitting: true, errors: {} });

    setTimeout(() => {
      // demo: simulate an occasional dependency failure on Close/Escalate/Transfer
      const simulateFailure = (m.type === 'Close' || m.type === 'Escalate') && m.conversation.id === 'CONV-2026-004433';

      if (!simulateFailure) {
        this.applyAction(m);
      }

      const now = this.formatTime(new Date());
      if (simulateFailure) {
        this.outcome.set({
          kind: 'dependency-failure',
          reference: m.conversation.id,
          state: m.conversation.status,
          effectiveTime: now,
          nextAction: 'Retry the dependent step or contact support.',
          correlationRef: 'COR-' + Math.random().toString(36).slice(2, 8).toUpperCase(),
        });
      } else {
        this.outcome.set({
          kind: 'success',
          reference: m.conversation.id,
          state: this.resultingState(m.type, m.conversation),
          effectiveTime: now,
          nextAction: this.nextActionCopy(m.type),
        });
      }
      this.actionModal.set(null);
    }, 650);
  }

  private applyAction(m: ActionModalState) {
    this.conversations.update((list) =>
      list.map((c) => {
        if (c.id !== m.conversation.id) return c;
        switch (m.type) {
          case 'Accept':
            return { ...c, status: 'In progress' as ConvStatus, owner: CURRENT_USER, unread: false, updatedLabel: 'Just now' };
          case 'Reply':
            return { ...c, status: 'Waiting on donor' as ConvStatus, lastMessagePreview: m.message.trim(), unread: false, updatedLabel: 'Just now' };
          case 'Transfer':
            return { ...c, queue: m.targetQueue, owner: m.targetOwner || null, updatedLabel: 'Just now' };
          case 'Escalate':
            return { ...c, priority: 'Urgent' as Priority, updatedLabel: 'Just now' };
          case 'Close':
            return { ...c, status: 'Closed' as ConvStatus, updatedLabel: 'Just now' };
          default:
            return c;
        }
      })
    );
  }

  private resultingState(type: ActionType, c: Conversation): string {
    switch (type) {
      case 'Accept': return 'In progress';
      case 'Reply': return 'Waiting on donor';
      case 'Transfer': return 'Transferred';
      case 'Escalate': return 'Escalated · Urgent';
      case 'Close': return 'Closed';
      default: return c.status;
    }
  }

  private nextActionCopy(type: ActionType): string {
    switch (type) {
      case 'Accept': return 'Open the conversation and send a first reply.';
      case 'Reply': return 'Monitor for the donor\u2019s reply.';
      case 'Transfer': return 'The receiving queue owner can now Accept.';
      case 'Escalate': return 'A supervisor has been notified for review.';
      case 'Close': return 'Reopen is available from conversation history if needed.';
      default: return '';
    }
  }

  dismissOutcome() {
    this.outcome.set(null);
  }

  retryDependency() {
    const o = this.outcome();
    if (!o) return;
    this.outcome.set({
      kind: 'success',
      reference: o.reference,
      state: o.state,
      effectiveTime: this.formatTime(new Date()),
      nextAction: 'Retried successfully.',
    });
  }

  // ---- demo state switcher (QA / acceptance-criteria showcase) ----
  setDemoState(state: ViewState) {
    this.viewState.set(state);
    this.demoMenuOpen.set(false);
  }

  private formatTime(d: Date): string {
    return d.toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' }) + ', ' +
      d.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  trackByConvId(_: number, c: Conversation) {
    return c.id;
  }
}