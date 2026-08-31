import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/* ---------------------------------------------------------------------- */
/*  Domain types                                                          */
/* ---------------------------------------------------------------------- */

type LifecycleState = 'Draft' | 'Pending review' | 'Approved' | 'Retired';

type EffectiveRole = 'Template Stakeholder' | 'Independent Approver' | 'Read-only Viewer';

interface TemplateRecord {
  id: string;
  reference: string;
  name: string;
  channel: string;
  language: string;
  purpose: string;
  subject: string;
  messageBody: string;
  placeholders: string;
  providerTemplateId: string;
  consentConfirmed: boolean;
  consentActor: string;
  consentTime: string;
  version: string;
  approvalState: 'Not submitted' | 'Pending review' | 'Approved' | 'Retired';
  lifecycleState: LifecycleState;
  usageCount: number;
  owner: string;
  updatedAt: string;
  scope: string;
  downstreamReferenceCount: number;
}

interface FieldErrors {
  [field: string]: string;
}

interface SuccessInfo {
  reference: string;
  state: string;
  effectiveTime: string;
  pendingDependency: string | null;
  nextAction: string;
}

interface ConfirmDialogState {
  open: boolean;
  type: 'approve' | 'retire' | 'delete' | null;
  title: string;
  consequence: string;
  reason: string;
  requiresTypedWord: string | null;
  typedValue: string;
  busy: boolean;
}

type PreviewState = 'live' | 'loading' | 'empty' | 'no-access' | 'conflict' | 'dependency-failure';

const CHANNEL_OPTIONS = ['Email', 'SMS', 'WhatsApp', 'Push notification', 'Letter'];
const LANGUAGE_OPTIONS = ['English', 'Hindi', 'Tamil', 'Telugu', 'Bengali', 'Marathi'];
const ALL_SCOPES = ['Community Outreach', 'Direct Mail Campaigns', 'Major Gifts', 'Corporate Partnerships'];

@Component({
  selector: 'app-template-catalogue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './template-catalogue.html',
  styleUrl: './template-catalogue.css',
})
export class TemplateCatalogueComponent implements OnInit {
  ngOnInit(): void {
    window.setTimeout(() => {
      this.isLoading = false;
    }, 900);
  }

  /* ------------------------------ reference data ------------------------------ */
  readonly channelOptions = CHANNEL_OPTIONS;
  readonly languageOptions = LANGUAGE_OPTIONS;
  readonly allScopes = ALL_SCOPES;

  readonly roleScopeMap: Record<EffectiveRole, string[]> = {
    'Template Stakeholder': ['Community Outreach', 'Direct Mail Campaigns'],
    'Independent Approver': ['Community Outreach', 'Major Gifts', 'Corporate Partnerships'],
    'Read-only Viewer': ['Community Outreach'],
  };

  readonly rolePermissions: Record<
    EffectiveRole,
    { view: boolean; draft: boolean; submit: boolean; approve: boolean; retire: boolean; deleteDraft: boolean }
  > = {
    'Template Stakeholder': { view: true, draft: true, submit: true, approve: false, retire: true, deleteDraft: true },
    'Independent Approver': { view: true, draft: false, submit: false, approve: true, retire: false, deleteDraft: false },
    'Read-only Viewer': { view: true, draft: false, submit: false, approve: false, retire: false, deleteDraft: false },
  };

  /* ------------------------------ access simulator ------------------------------ */
  effectiveRole: EffectiveRole = 'Template Stakeholder';

  get activeScopes(): string[] {
    return this.roleScopeMap[this.effectiveRole];
  }

  get permissions() {
    return this.rolePermissions[this.effectiveRole];
  }

  isScopeActive(scope: string): boolean {
    return this.activeScopes.includes(scope);
  }

  setRole(role: EffectiveRole): void {
    this.effectiveRole = role;
    this.closePanel();
  }

  /* ------------------------------ page-level state ------------------------------ */
  previewState: PreviewState = 'live';
  isLoading = true;
  lastRefreshed = this.formatTimestamp(new Date());

  /* ------------------------------ filters / context ------------------------------ */
  searchText = '';
  channelFilter = '';
  languageFilter = '';
  statusFilter = '';
  activeTab: 'all' | 'mine' | 'review' = 'all';

  savedFilters = ['My default view', 'Needs my review', 'Email templates only'];
  selectedSavedFilter = '';

  /* ------------------------------ mock data store ------------------------------ */
  templates: TemplateRecord[] = [
    {
      id: 't1', reference: 'TPL-2026-00118', name: 'Winter Relief Appeal — Reminder',
      channel: 'Email', language: 'English',
      purpose: 'Reminder email sent to lapsed donors during the winter relief campaign to encourage a repeat gift.',
      subject: 'Your warmth can reach one more family tonight',
      messageBody: 'Dear {{first_name}}, winter is here and shelters need your help again...',
      placeholders: '{{first_name}}, {{last_gift_amount}}', providerTemplateId: '',
      consentConfirmed: true, consentActor: 'Sarah Johnson', consentTime: '02 Jul 2026, 11:20 am',
      version: 'v3', approvalState: 'Approved', lifecycleState: 'Approved',
      usageCount: 4820, owner: 'Sarah Johnson', updatedAt: '31 Jul 2026, 09:12 am',
      scope: 'Community Outreach', downstreamReferenceCount: 12,
    },
    {
      id: 't2', reference: 'TPL-2026-00121', name: 'Corporate Partnership Renewal',
      channel: 'Email', language: 'English',
      purpose: 'Annual renewal outreach to corporate partners nearing the end of their partnership term.',
      subject: 'Let\u2019s continue the impact we built together',
      messageBody: 'Dear {{contact_name}}, your partnership with us over the last year has helped...',
      placeholders: '{{contact_name}}, {{partnership_years}}', providerTemplateId: '',
      consentConfirmed: true, consentActor: 'Meera Nair', consentTime: '18 Jun 2026, 03:40 pm',
      version: 'v1', approvalState: 'Pending review', lifecycleState: 'Pending review',
      usageCount: 0, owner: 'Meera Nair', updatedAt: '30 Jul 2026, 04:02 pm',
      scope: 'Corporate Partnerships', downstreamReferenceCount: 0,
    },
    {
      id: 't3', reference: 'TPL-2026-00124', name: 'Major Gifts — Thank You Call Follow-up',
      channel: 'SMS', language: 'English',
      purpose: 'Short thank-you follow-up sent after a major gift stewardship call.',
      subject: 'Thank you',
      messageBody: 'Hi {{first_name}}, thank you again for your generous support today. \u2014 The Team',
      placeholders: '{{first_name}}', providerTemplateId: '',
      consentConfirmed: false, consentActor: '', consentTime: '',
      version: 'v1', approvalState: 'Not submitted', lifecycleState: 'Draft',
      usageCount: 0, owner: 'Arjun Rao', updatedAt: '01 Aug 2026, 10:05 am',
      scope: 'Major Gifts', downstreamReferenceCount: 0,
    },
    {
      id: 't4', reference: 'TPL-2026-00097', name: 'Legacy Direct Mail — Spring Renewal',
      channel: 'Letter', language: 'English',
      purpose: 'Printed renewal letter mailed to sustaining donors ahead of the spring appeal window.',
      subject: 'A personal note about the year ahead',
      messageBody: 'Dear {{first_name}}, as we look toward the months ahead...',
      placeholders: '{{first_name}}, {{giving_history}}', providerTemplateId: '',
      consentConfirmed: true, consentActor: 'David Chen', consentTime: '02 Feb 2026, 09:00 am',
      version: 'v5', approvalState: 'Retired', lifecycleState: 'Retired',
      usageCount: 15320, owner: 'David Chen', updatedAt: '15 May 2026, 02:30 pm',
      scope: 'Direct Mail Campaigns', downstreamReferenceCount: 40,
    },
    {
      id: 't5', reference: 'TPL-2026-00131', name: 'WhatsApp — Volunteer Shift Reminder',
      channel: 'WhatsApp', language: 'English',
      purpose: 'Reminder sent to registered volunteers the evening before their community outreach shift.',
      subject: 'Shift reminder',
      messageBody: 'Hi {{first_name}}, this is a reminder about your shift tomorrow at {{location}}.',
      placeholders: '{{first_name}}, {{location}}', providerTemplateId: 'WA-TEMPLATE-88213',
      consentConfirmed: true, consentActor: 'Priya Menon', consentTime: '20 Jul 2026, 01:15 pm',
      version: 'v2', approvalState: 'Approved', lifecycleState: 'Approved',
      usageCount: 980, owner: 'Priya Menon', updatedAt: '29 Jul 2026, 05:45 pm',
      scope: 'Community Outreach', downstreamReferenceCount: 6,
    },
    {
      id: 't6', reference: 'TPL-2026-00133', name: 'Push — Emergency Appeal Alert',
      channel: 'Push notification', language: 'English',
      purpose: 'High-urgency push notification used only during declared emergency appeals.',
      subject: 'Families need urgent help',
      messageBody: 'A crisis just unfolded. Your gift right now can provide immediate relief.',
      placeholders: '', providerTemplateId: '',
      consentConfirmed: false, consentActor: '', consentTime: '',
      version: 'v1', approvalState: 'Pending review', lifecycleState: 'Pending review',
      usageCount: 0, owner: 'Arjun Rao', updatedAt: '01 Aug 2026, 08:50 am',
      scope: 'Direct Mail Campaigns', downstreamReferenceCount: 0,
    },
    {
      id: 't7', reference: 'TPL-2026-00108', name: 'Corporate Gift Match Confirmation',
      channel: 'Email', language: 'English',
      purpose: 'Confirmation email sent to corporate contacts once a matched gift has been reconciled.',
      subject: 'Your matched gift has been confirmed',
      messageBody: 'Dear {{contact_name}}, we\u2019re pleased to confirm your company\u2019s matched gift of {{amount}}.',
      placeholders: '{{contact_name}}, {{amount}}', providerTemplateId: '',
      consentConfirmed: true, consentActor: 'Meera Nair', consentTime: '10 Jun 2026, 10:00 am',
      version: 'v2', approvalState: 'Approved', lifecycleState: 'Approved',
      usageCount: 312, owner: 'Meera Nair', updatedAt: '22 Jul 2026, 11:30 am',
      scope: 'Corporate Partnerships', downstreamReferenceCount: 3,
    },
  ];

  /* ------------------------------ derived list ------------------------------ */
  get scopedTemplates(): TemplateRecord[] {
    return this.templates.filter((t) => this.isScopeActive(t.scope));
  }

  get filteredTemplates(): TemplateRecord[] {
    const q = this.searchText.trim().toLowerCase();
    return this.scopedTemplates.filter((t) => {
      if (this.activeTab === 'mine' && t.owner !== 'Sarah Johnson') return false;
      if (this.activeTab === 'review' && t.lifecycleState !== 'Pending review') return false;
      if (this.channelFilter && t.channel !== this.channelFilter) return false;
      if (this.languageFilter && t.language !== this.languageFilter) return false;
      if (this.statusFilter && t.lifecycleState !== this.statusFilter) return false;
      if (q && !(t.name.toLowerCase().includes(q) || t.reference.toLowerCase().includes(q) || t.subject.toLowerCase().includes(q))) {
        return false;
      }
      return true;
    });
  }

  get groupedTemplates(): { state: LifecycleState; label: string; items: TemplateRecord[] }[] {
    const order: LifecycleState[] = ['Draft', 'Pending review', 'Approved', 'Retired'];
    return order
      .map((state) => ({ state, label: state, items: this.filteredTemplates.filter((t) => t.lifecycleState === state) }))
      .filter((g) => g.items.length > 0);
  }

  get hasAnyFilterActive(): boolean {
    return !!(this.searchText || this.channelFilter || this.languageFilter || this.statusFilter || this.activeTab !== 'all');
  }

  get emptyReason(): string {
    if (this.scopedTemplates.length === 0) return 'scope';
    if (this.hasAnyFilterActive) return 'filters';
    return 'none';
  }

  clearFilters(): void {
    this.searchText = '';
    this.channelFilter = '';
    this.languageFilter = '';
    this.statusFilter = '';
    this.activeTab = 'all';
    this.selectedSavedFilter = '';
  }

  refresh(): void {
    this.isLoading = true;
    window.setTimeout(() => {
      this.isLoading = false;
      this.lastRefreshed = this.formatTimestamp(new Date());
    }, 650);
  }

  /* ------------------------------ panel / drawer state ------------------------------ */
  panelOpen = false;
  panelMode: 'view' | 'create' | 'edit' = 'view';
  working: TemplateRecord | null = null;
  originalSnapshot: TemplateRecord | null = null;
  errors: FieldErrors = {};
  errorSummary: { field: string; label: string; message: string }[] = [];
  submitting = false;
  successInfo: SuccessInfo | null = null;
  duplicateWarning: TemplateRecord | null = null;
  conflictActive = false;
  dependencyFailure: { message: string; correlationRef: string } | null = null;

  fieldLabels: Record<string, string> = {
    name: 'Template name',
    subject: 'Subject or header',
    messageBody: 'Message body',
    purpose: 'Purpose',
    placeholders: 'Placeholders',
    providerTemplateId: 'Provider template ID',
    consentConfirmed: 'Consent rule',
  };

  get requiresPlaceholders(): boolean {
    return !!this.working && /{{\s*[\w.]+\s*}}/.test(this.working.messageBody);
  }

  get requiresProviderId(): boolean {
    return !!this.working && this.working.channel === 'WhatsApp';
  }

  private blankRecord(): TemplateRecord {
    return {
      id: '', reference: '', name: '', channel: '', language: '', purpose: '',
      subject: '', messageBody: '', placeholders: '', providerTemplateId: '',
      consentConfirmed: false, consentActor: '', consentTime: '',
      version: '', approvalState: 'Not submitted', lifecycleState: 'Draft',
      usageCount: 0, owner: 'Sarah Johnson', updatedAt: this.formatTimestamp(new Date()),
      scope: this.activeScopes[0] ?? '', downstreamReferenceCount: 0,
    };
  }

  openCreate(): void {
    if (!this.permissions.draft) return;
    this.panelMode = 'create';
    this.working = this.blankRecord();
    this.originalSnapshot = null;
    this.errors = {};
    this.errorSummary = [];
    this.successInfo = null;
    this.duplicateWarning = null;
    this.conflictActive = false;
    this.dependencyFailure = null;
    this.panelOpen = true;
  }

  openRecord(record: TemplateRecord): void {
    if (!this.permissions.view) return;
    this.panelMode = 'view';
    this.working = { ...record };
    this.originalSnapshot = { ...record };
    this.errors = {};
    this.errorSummary = [];
    this.successInfo = null;
    this.duplicateWarning = null;
    this.conflictActive = false;
    this.dependencyFailure = null;
    this.panelOpen = true;
  }

  switchToEdit(): void {
    if (!this.working) return;
    if (this.working.lifecycleState !== 'Draft' || !this.permissions.draft) return;
    this.panelMode = 'edit';
  }

  closePanel(): void {
    this.panelOpen = false;
    this.working = null;
    this.originalSnapshot = null;
    this.errors = {};
    this.errorSummary = [];
    this.successInfo = null;
    this.duplicateWarning = null;
    this.conflictActive = false;
    this.dependencyFailure = null;
  }

  checkDuplicate(): void {
    if (!this.working || this.panelMode === 'view') return;
    if (!this.working.channel || !this.working.language || !this.working.purpose.trim()) {
      this.duplicateWarning = null;
      return;
    }
    const match = this.templates.find(
      (t) =>
        t.id !== this.working!.id &&
        t.lifecycleState !== 'Retired' &&
        t.channel === this.working!.channel &&
        t.language === this.working!.language &&
        t.purpose.trim().toLowerCase() === this.working!.purpose.trim().toLowerCase()
    );
    this.duplicateWarning = match ?? null;
  }

  dismissDuplicate(action: 'view' | 'continue' | 'cancel'): void {
    if (action === 'view' && this.duplicateWarning) {
      const existing = this.duplicateWarning;
      this.closePanel();
      this.openRecord(existing);
      return;
    }
    if (action === 'cancel') {
      this.closePanel();
      return;
    }
    this.duplicateWarning = null;
  }

  /* ------------------------------ validation ------------------------------ */
  private validate(forSubmit: boolean): boolean {
    const w = this.working!;
    const errors: FieldErrors = {};

    if (!w.name.trim()) errors['name'] = 'Enter Template name.';
    if (forSubmit && !w.subject.trim()) errors['subject'] = 'Enter Subject or header.';
    if (forSubmit && !w.messageBody.trim()) errors['messageBody'] = 'Enter Message body.';

    if (w.purpose.trim()) {
      const len = w.purpose.trim().length;
      if (len < 10 || len > 2000) {
        errors['purpose'] = 'Review Purpose. The value does not meet the stated format or range.';
      }
    } else if (forSubmit) {
      errors['purpose'] = 'Enter Purpose.';
    }

    if (forSubmit && this.requiresPlaceholders && !w.placeholders.trim()) {
      errors['placeholders'] = 'Enter Placeholders.';
    }
    if (forSubmit && this.requiresProviderId && !w.providerTemplateId.trim()) {
      errors['providerTemplateId'] = 'Enter Provider template ID.';
    }
    if (forSubmit && !w.consentConfirmed) {
      errors['consentConfirmed'] = 'Enter Consent rule.';
    }

    this.errors = errors;
    this.errorSummary = Object.keys(errors).map((field) => ({
      field,
      label: this.fieldLabels[field] ?? field,
      message: errors[field],
    }));

    if (this.errorSummary.length > 0) {
      window.setTimeout(() => {
        const el = document.getElementById('field-' + this.errorSummary[0].field);
        el?.focus();
      }, 0);
      return false;
    }
    return true;
  }

  private formatTimestamp(d: Date): string {
    return d.toLocaleString('en-IN', {
      day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }

  private nextReference(): string {
    const n = 100 + this.templates.length + 1;
    return `TPL-2026-${String(n).padStart(5, '0')}`;
  }

  /* ------------------------------ primary / workflow actions ------------------------------ */
  saveDraft(): void {
    if (!this.working || !this.permissions.draft) return;
    if (!this.validate(false)) return;

    this.submitting = true;
    window.setTimeout(() => {
      const w = this.working!;
      if (!w.id) {
        w.id = 't' + (this.templates.length + 1) + '-' + Date.now();
        w.reference = this.nextReference();
        w.version = 'v1';
        w.owner = 'Sarah Johnson';
        w.scope = w.scope || this.activeScopes[0];
        this.templates = [w, ...this.templates];
      } else {
        this.templates = this.templates.map((t) => (t.id === w.id ? w : t));
      }
      w.updatedAt = this.formatTimestamp(new Date());
      this.originalSnapshot = { ...w };
      this.submitting = false;
      this.panelMode = 'edit';

      const remaining: string[] = [];
      if (!w.subject.trim()) remaining.push('Subject or header');
      if (!w.messageBody.trim()) remaining.push('Message body');
      if (!w.consentConfirmed) remaining.push('Consent rule');

      this.successInfo = {
        reference: w.reference,
        state: 'Draft saved',
        effectiveTime: w.updatedAt,
        pendingDependency: remaining.length ? `Remaining required information: ${remaining.join(', ')}` : null,
        nextAction: remaining.length ? 'Complete the remaining fields, then Submit for review.' : 'Ready to Submit for review.',
      };
    }, 500);
  }

  submit(): void {
    if (!this.working || !this.permissions.submit) return;
    if (this.working.lifecycleState !== 'Draft') return;
    if (!this.validate(true)) return;

    this.submitting = true;
    window.setTimeout(() => {
      const w = this.working!;
      w.lifecycleState = 'Pending review';
      w.approvalState = 'Pending review';
      w.consentActor = 'Sarah Johnson';
      w.consentTime = this.formatTimestamp(new Date());
      w.updatedAt = this.formatTimestamp(new Date());
      this.templates = this.templates.map((t) => (t.id === w.id ? w : t));
      this.originalSnapshot = { ...w };
      this.submitting = false;

      if (this.previewState === 'dependency-failure') {
        this.dependencyFailure = {
          message: 'The provider template sync step did not respond in time.',
          correlationRef: 'COR-' + Math.random().toString(36).slice(2, 10).toUpperCase(),
        };
      }

      this.successInfo = {
        reference: w.reference,
        state: 'Submitted \u2014 Pending review',
        effectiveTime: w.updatedAt,
        pendingDependency: this.previewState === 'dependency-failure' ? 'Provider template sync is pending.' : null,
        nextAction: 'Awaiting an independent approver\u2019s decision.',
      };
    }, 650);
  }

  /* ------------------------------ confirm-guarded actions ------------------------------ */
  confirmDialog: ConfirmDialogState = {
    open: false, type: null, title: '', consequence: '', reason: '',
    requiresTypedWord: null, typedValue: '', busy: false,
  };

  openApprove(): void {
    if (!this.working || !this.permissions.approve || this.working.lifecycleState !== 'Pending review') return;
    this.confirmDialog = {
      open: true, type: 'approve', title: 'Confirm Approve',
      consequence: `Approving will publish "${this.working.name}" as the current version available for use across the effective scope.`,
      reason: '', requiresTypedWord: null, typedValue: '', busy: false,
    };
  }

  openRetire(): void {
    if (!this.working || !this.permissions.retire || this.working.lifecycleState !== 'Approved') return;
    this.confirmDialog = {
      open: true, type: 'retire', title: 'Confirm Retire',
      consequence: `Retiring will stop "${this.working.name}" from being selectable for new messages. ${this.working.usageCount.toLocaleString('en-IN')} historical sends and all linked history are preserved.`,
      reason: '', requiresTypedWord: 'RETIRE', typedValue: '', busy: false,
    };
  }

  openDelete(): void {
    if (!this.working || !this.permissions.deleteDraft) return;
    if (this.working.lifecycleState !== 'Draft' || this.working.downstreamReferenceCount > 0) return;
    this.confirmDialog = {
      open: true, type: 'delete', title: 'Confirm delete unused draft',
      consequence: `Deleting will permanently remove the unused draft "${this.working.name}". This draft has no downstream reference.`,
      reason: '', requiresTypedWord: null, typedValue: '', busy: false,
    };
  }

  get confirmDisabled(): boolean {
    const c = this.confirmDialog;
    if (!c.reason.trim()) return true;
    if (c.requiresTypedWord && c.typedValue.trim().toUpperCase() !== c.requiresTypedWord) return true;
    return false;
  }

  cancelConfirm(): void {
    this.confirmDialog = { open: false, type: null, title: '', consequence: '', reason: '', requiresTypedWord: null, typedValue: '', busy: false };
  }

  runConfirmedAction(): void {
    if (!this.working || this.confirmDisabled) return;
    const type = this.confirmDialog.type;
    this.confirmDialog.busy = true;

    window.setTimeout(() => {
      const w = this.working!;
      const now = this.formatTimestamp(new Date());

      if (type === 'approve') {
        w.lifecycleState = 'Approved';
        w.approvalState = 'Approved';
        w.updatedAt = now;
        this.templates = this.templates.map((t) => (t.id === w.id ? w : t));
        this.successInfo = {
          reference: w.reference, state: 'Approved', effectiveTime: now, pendingDependency: null,
          nextAction: 'Template stakeholders may now use this version in outbound messages.',
        };
      } else if (type === 'retire') {
        w.lifecycleState = 'Retired';
        w.approvalState = 'Retired';
        w.updatedAt = now;
        this.templates = this.templates.map((t) => (t.id === w.id ? w : t));
        this.successInfo = {
          reference: w.reference, state: 'Retired', effectiveTime: now, pendingDependency: null,
          nextAction: 'This version is no longer selectable for new messages.',
        };
      } else if (type === 'delete') {
        this.templates = this.templates.filter((t) => t.id !== w.id);
        this.successInfo = {
          reference: w.reference, state: 'Deleted (unused draft)', effectiveTime: now, pendingDependency: null,
          nextAction: 'Returning to the Template catalogue.',
        };
      }

      this.confirmDialog.busy = false;
      this.cancelConfirm();
      this.panelMode = 'view';
    }, 600);
  }

  /* ------------------------------ preview / conflict demo ------------------------------ */
  preview(record: TemplateRecord): void {
    this.openRecord(record);
  }

  simulateConflict(): void {
    if (!this.working) return;
    this.conflictActive = true;
  }

  reapplyLatest(): void {
    if (!this.originalSnapshot) return;
    this.working = { ...this.originalSnapshot };
    this.conflictActive = false;
  }

  compareVersions(): void {
    // Keeps both versions visible in the review region; no destructive action taken.
    this.conflictActive = true;
  }

  retryDependency(): void {
    this.dependencyFailure = null;
  }

  setPreviewState(state: PreviewState): void {
    this.previewState = state;
    if (state === 'loading') {
      this.isLoading = true;
      window.setTimeout(() => (this.isLoading = false), 1400);
    } else {
      this.isLoading = false;
    }
    if (state !== 'conflict') this.conflictActive = false;
  }

  trackByTemplateId(_index: number, item: TemplateRecord): string {
    return item.id;
  }

  focusField(field: string): void {
    const el = document.getElementById('field-' + field) as HTMLElement | null;
    el?.focus();
  }
}