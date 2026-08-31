import { Component, ElementRef, QueryList, ViewChildren } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/* ---------------------------------------------------------------------- */
/*  4.7 COM-UI-07 — Outbound message composer — supporting data contracts */
/* ---------------------------------------------------------------------- */

type LifecycleState = 'Draft' | 'Ready to send' | 'Scheduled' | 'Sent' | 'Failed';
type ViewState = 'idle' | 'loading' | 'empty';
type SendMode = 'now' | 'schedule';
type SubmitPhase = 'idle' | 'reviewing' | 'sending' | 'success' | 'blocked' | 'dependency-failure' | 'conflict';
type AttachmentStatus = 'uploading' | 'scanning' | 'classified' | 'blocked';

interface ChannelOption {
  id: string;
  label: string;
  icon: string;
  destinationHint: string;
}

interface LanguageOption {
  id: string;
  label: string;
}

interface Placeholder {
  key: string;
  label: string;
  value: string;
}

interface TemplateOption {
  id: string;
  name: string;
  version: string;
  approvedOn: string;
  channelId: string;
  languageId: string;
  purposeHint: string;
  placeholders: Placeholder[];
}

interface ConsentOption {
  id: string;
  label: string;
  disabled: boolean;
  reason?: string;
  tone: 'positive' | 'warning' | 'restricted';
}

interface OwnerOption {
  id: string;
  name: string;
  role: string;
  scope: string;
  initials: string;
  inScope: boolean;
}

interface Attachment {
  id: string;
  name: string;
  sizeLabel: string;
  status: AttachmentStatus;
  classification?: string;
  progress: number;
}

interface FieldErrors {
  maskedDestination?: string;
  purpose?: string;
  channel?: string;
  language?: string;
  template?: string;
  consentState?: string;
  sendTime?: string;
  owner?: string;
  placeholders?: string;
}

interface HistoryEntry {
  time: string;
  actor: string;
  action: string;
  result: string;
}

interface LinkedRecord {
  reference: string;
  type: string;
  status: string;
}

interface EvidenceLink {
  label: string;
  reference: string;
}

@Component({
  selector: 'app-outbound-message-composer',
  imports: [CommonModule, FormsModule],
  templateUrl: './outbound-message-composer.html',
  styleUrl: './outbound-message-composer.css',
})
export class OutboundMessageComposerComponent {
  @ViewChildren('fieldAnchor') fieldAnchors!: QueryList<ElementRef<HTMLElement>>;

  /* ---------------------------- Shell / scope --------------------------- */

  readonly effectiveRole = 'Fundraiser';
  readonly effectiveScopes = [
    { label: 'Donation Operations', inScope: true },
    { label: 'Community Outreach', inScope: true },
    { label: 'Major Gifts', inScope: false },
    { label: 'Corporate Partnerships', inScope: false },
  ];

  /* ------------------------------ Task header ---------------------------- */

  viewState: ViewState = 'idle';
  lifecycleState: LifecycleState = 'Draft';
  readonly stableReference = 'MSG-2026-004821';
  readonly ownerOfRecord = 'Sarah Johnson';
  lastSavedLabel = 'Not saved yet';
  private lastSavedAt: Date | null = null;

  /* ------------------------------ Announcements --------------------------- */

  liveAnnouncement = '';

  /* ------------------------------ Catalogues ------------------------------ */

  readonly channels: ChannelOption[] = [
    { id: 'email', label: 'Email', icon: '✉️', destinationHint: 'm•••••n@meeradonor.org' },
    { id: 'sms', label: 'SMS', icon: '💬', destinationHint: '+91 98••• ••210' },
    { id: 'whatsapp', label: 'WhatsApp', icon: '🟢', destinationHint: '+91 98••• ••210' },
    { id: 'post', label: 'Postal letter', icon: '✉︎', destinationHint: '14, ••••• Layout, Chennai ••••06' },
  ];

  readonly languages: LanguageOption[] = [
    { id: 'en', label: 'English' },
    { id: 'hi', label: 'Hindi' },
    { id: 'ta', label: 'Tamil' },
    { id: 'te', label: 'Telugu' },
    { id: 'kn', label: 'Kannada' },
  ];

  readonly templates: TemplateOption[] = [
    {
      id: 'tpl-winter-thanks',
      name: 'Winter Relief — Thank you & receipt',
      version: 'v3.2',
      approvedOn: '18 Jun 2026',
      channelId: 'email',
      languageId: 'en',
      purposeHint: 'Acknowledge a completed donation and share the tax receipt for Winter Relief Appeal.',
      placeholders: [
        { key: 'first_name', label: 'First name', value: 'Meera' },
        { key: 'campaign_name', label: 'Campaign name', value: 'Winter Relief Appeal' },
        { key: 'amount', label: 'Donation amount', value: '₹5,000' },
        { key: 'receipt_id', label: 'Receipt reference', value: 'RCT-2026-33871' },
      ],
    },
    {
      id: 'tpl-outreach-invite',
      name: 'Community Outreach — Event invitation',
      version: 'v1.4',
      approvedOn: '02 Jul 2026',
      channelId: 'sms',
      languageId: 'en',
      purposeHint: 'Invite a supporter in the assigned geography to an upcoming outreach event.',
      placeholders: [
        { key: 'first_name', label: 'First name', value: 'Meera' },
        { key: 'event_date', label: 'Event date', value: '16 Aug 2026' },
        { key: 'venue', label: 'Venue', value: 'Community Hall, T. Nagar' },
      ],
    },
    {
      id: 'tpl-pledge-reminder',
      name: 'Pledge reminder — Gentle nudge',
      version: 'v2.0',
      approvedOn: '25 May 2026',
      channelId: 'whatsapp',
      languageId: 'en',
      purposeHint: 'Remind a donor of an outstanding pledge instalment in a warm, non-pressuring tone.',
      placeholders: [
        { key: 'first_name', label: 'First name', value: 'Meera' },
        { key: 'pledge_amount', label: 'Pledge balance', value: '₹2,500' },
        { key: 'due_date', label: 'Due date', value: '20 Aug 2026' },
      ],
    },
  ];

  readonly consentOptions: ConsentOption[] = [
    { id: 'opted-in', label: 'Opted in for this channel', disabled: false, tone: 'positive' },
    {
      id: 'opted-out',
      label: 'Opted out for this channel',
      disabled: true,
      reason: 'Recipient opted out of this channel on 12 Jul 2026. Sending is not permitted.',
      tone: 'restricted',
    },
    {
      id: 'pending',
      label: 'Pending verification',
      disabled: false,
      reason: 'Consent was captured recently and has not completed verification.',
      tone: 'warning',
    },
  ];

  readonly owners: OwnerOption[] = [
    { id: 'own-1', name: 'Sarah Johnson', role: 'Fundraiser', scope: 'Donation Operations', initials: 'SJ', inScope: true },
    { id: 'own-2', name: 'Arjun Nair', role: 'Donor Care', scope: 'Community Outreach', initials: 'AN', inScope: true },
    { id: 'own-3', name: 'Priya Menon', role: 'Supervisor', scope: 'Donation Operations', initials: 'PM', inScope: true },
    { id: 'own-4', name: 'Karthik Rao', role: 'Fundraiser', scope: 'Major Gifts', initials: 'KR', inScope: false },
  ];

  /* ------------------------------ Recipient (read-only) -------------------- */

  readonly recipient = {
    name: 'Meera Krishnan',
    reference: 'DNR-2026-118342',
    type: 'Donor',
  };

  /* ------------------------------ Form model -------------------------------- */

  maskedDestination = 'm•••••n@meeradonor.org';
  purpose = '';
  channelId: string | null = null;
  channelQuery = '';
  channelMenuOpen = false;

  languageId: string | null = null;
  languageQuery = '';
  languageMenuOpen = false;

  templateId: string | null = null;
  templateQuery = '';
  templateMenuOpen = false;

  consentStateId: string | null = null;

  placeholders: Placeholder[] = [];

  attachments: Attachment[] = [];
  isDraggingFile = false;

  sendMode: SendMode = 'now';
  scheduleDate = '';
  scheduleTime = '';
  readonly operatingTimeZone = 'Asia/Kolkata';

  ownerId: string | null = 'own-1';
  ownerQuery = '';
  ownerMenuOpen = false;

  /* ------------------------------ Validation state --------------------------- */

  errors: FieldErrors = {};
  errorSummary: { field: keyof FieldErrors; message: string }[] = [];
  touched: Partial<Record<keyof FieldErrors, boolean>> = {};

  /* ------------------------------ Submit / review flow ------------------------ */

  submitPhase: SubmitPhase = 'idle';
  reasonForSend = '';
  typedConfirmation = '';
  readonly confirmationPhrase = 'SEND';

  recordChangedRemotely = false;

  result: {
    reference: string;
    state: LifecycleState;
    effectiveTime: string;
    downstream: string;
    nextAction: string;
  } | null = null;

  dependencyFailureNote = '';

  /* ------------------------------ Related & history panel ---------------------- */

  activeHistoryTab: 'linked' | 'documents' | 'activity' | 'integration' = 'activity';

  readonly linkedRecords: LinkedRecord[] = [
    { reference: 'DNR-2026-118342', type: 'Donor record', status: 'Active' },
    { reference: 'CAM-2026-0101', type: 'Campaign', status: 'Active' },
    { reference: 'RCT-2026-33871', type: 'Receipt', status: 'Issued' },
  ];

  readonly documents: LinkedRecord[] = [
    { reference: 'Winter Relief — Thank you & receipt v3.2', type: 'Approved template', status: 'Approved' },
    { reference: 'Consent capture form — 12 Jul 2026', type: 'Evidence', status: 'On file' },
  ];

  readonly historyLog: HistoryEntry[] = [
    { time: '31 Jul 2026, 11:02 am', actor: 'Sarah Johnson', action: 'Draft created', result: 'Draft saved' },
    { time: '31 Jul 2026, 11:14 am', actor: 'Sarah Johnson', action: 'Template selected', result: 'Winter Relief — Thank you & receipt v3.2' },
    { time: '31 Jul 2026, 11:20 am', actor: 'System', action: 'Consent check', result: 'Opted in — verified' },
  ];

  readonly integrationStatus = [
    { name: 'Email delivery provider', status: 'Connected', tone: 'positive' as const },
    { name: 'Consent registry', status: 'Connected', tone: 'positive' as const },
    { name: 'Document store', status: 'Connected', tone: 'positive' as const },
  ];

  readonly evidence: EvidenceLink[] = [
    { label: 'Consent capture form', reference: 'CNS-2026-77410' },
    { label: 'Template approval record', reference: 'TPL-APR-2026-0932' },
  ];

  discardConfirming = false;

  constructor() {
    this.simulateInitialLoad();
  }

  /* ============================== Loading / empty ============================= */

  private simulateInitialLoad(): void {
    this.viewState = 'loading';
    this.announce('Loading outbound message composer.');
    setTimeout(() => {
      this.viewState = 'idle';
      this.announce('Outbound message composer ready.');
    }, 650);
  }

  cancelLoading(): void {
    this.viewState = 'idle';
  }

  /* ============================== Derived / computed =========================== */

  get selectedChannel(): ChannelOption | null {
    return this.channels.find((c) => c.id === this.channelId) ?? null;
  }

  get selectedLanguage(): LanguageOption | null {
    return this.languages.find((l) => l.id === this.languageId) ?? null;
  }

  get selectedTemplate(): TemplateOption | null {
    return this.templates.find((t) => t.id === this.templateId) ?? null;
  }

  get selectedConsent(): ConsentOption | null {
    return this.consentOptions.find((c) => c.id === this.consentStateId) ?? null;
  }

  get selectedOwner(): OwnerOption | null {
    return this.owners.find((o) => o.id === this.ownerId) ?? null;
  }

  get filteredChannels(): ChannelOption[] {
    const q = this.channelQuery.trim().toLowerCase();
    if (!q) return this.channels;
    return this.channels.filter((c) => c.label.toLowerCase().includes(q));
  }

  get filteredLanguages(): LanguageOption[] {
    const q = this.languageQuery.trim().toLowerCase();
    if (!q) return this.languages;
    return this.languages.filter((l) => l.label.toLowerCase().includes(q));
  }

  get filteredTemplates(): TemplateOption[] {
    const q = this.templateQuery.trim().toLowerCase();
    let list = this.templates;
    if (this.channelId) list = list.filter((t) => t.channelId === this.channelId);
    if (!q) return list;
    return list.filter((t) => t.name.toLowerCase().includes(q));
  }

  get filteredOwners(): OwnerOption[] {
    const q = this.ownerQuery.trim().toLowerCase();
    if (!q) return this.owners;
    return this.owners.filter((o) => o.name.toLowerCase().includes(q));
  }

  get purposeLength(): number {
    return this.purpose.trim().length;
  }

  get purposeLimitLabel(): string {
    return `${this.purposeLength} / 2,000`;
  }

  get hasAttachmentsScanning(): boolean {
    return this.attachments.some((a) => a.status === 'scanning' || a.status === 'uploading');
  }

  get hasBlockedAttachments(): boolean {
    return this.attachments.some((a) => a.status === 'blocked');
  }

  get scheduledLabel(): string {
    if (!this.scheduleDate || !this.scheduleTime) return '';
    const d = new Date(`${this.scheduleDate}T${this.scheduleTime}`);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true,
    });
  }

  get primaryActionEligible(): boolean {
    // Mirrors: role, permission, scope, state and dependencies all allow it.
    return this.viewState === 'idle' && this.lifecycleState !== 'Sent';
  }

  /* ============================== Field interactions =========================== */

  toggleChannelMenu(open?: boolean): void {
    this.channelMenuOpen = open ?? !this.channelMenuOpen;
    if (this.channelMenuOpen) this.languageMenuOpen = this.templateMenuOpen = this.ownerMenuOpen = false;
  }

  chooseChannel(option: ChannelOption): void {
    this.channelId = option.id;
    this.maskedDestination = option.destinationHint;
    this.channelQuery = '';
    this.channelMenuOpen = false;
    this.clearError('channel');
    this.clearError('maskedDestination');
    // Selecting a channel narrows eligible templates; drop an incompatible choice.
    if (this.selectedTemplate && this.selectedTemplate.channelId !== option.id) {
      this.templateId = null;
      this.placeholders = [];
    }
  }

  toggleLanguageMenu(open?: boolean): void {
    this.languageMenuOpen = open ?? !this.languageMenuOpen;
    if (this.languageMenuOpen) this.channelMenuOpen = this.templateMenuOpen = this.ownerMenuOpen = false;
  }

  chooseLanguage(option: LanguageOption): void {
    this.languageId = option.id;
    this.languageQuery = '';
    this.languageMenuOpen = false;
    this.clearError('language');
  }

  toggleTemplateMenu(open?: boolean): void {
    this.templateMenuOpen = open ?? !this.templateMenuOpen;
    if (this.templateMenuOpen) this.channelMenuOpen = this.languageMenuOpen = this.ownerMenuOpen = false;
  }

  chooseTemplate(option: TemplateOption): void {
    this.templateId = option.id;
    this.templateQuery = '';
    this.templateMenuOpen = false;
    this.placeholders = option.placeholders.map((p) => ({ ...p }));
    if (!this.purpose.trim()) this.purpose = option.purposeHint;
    if (!this.channelId) this.chooseChannel(this.channels.find((c) => c.id === option.channelId)!);
    if (!this.languageId) this.languageId = option.languageId;
    this.clearError('template');
    this.clearError('placeholders');
  }

  chooseConsent(id: string): void {
    const opt = this.consentOptions.find((c) => c.id === id);
    if (!opt || opt.disabled) return;
    this.consentStateId = id;
    this.clearError('consentState');
  }

  toggleOwnerMenu(open?: boolean): void {
    this.ownerMenuOpen = open ?? !this.ownerMenuOpen;
    if (this.ownerMenuOpen) this.channelMenuOpen = this.languageMenuOpen = this.templateMenuOpen = false;
  }

  chooseOwner(option: OwnerOption): void {
    if (!option.inScope) return;
    this.ownerId = option.id;
    this.ownerQuery = '';
    this.ownerMenuOpen = false;
    this.clearError('owner');
  }

  setSendMode(mode: SendMode): void {
    this.sendMode = mode;
    this.clearError('sendTime');
  }

  copyValue(value: string, label: string): void {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(value).catch(() => undefined);
    }
    this.announce(`${label} copied.`);
  }

  /* ============================== Attachments =================================== */

  onDropFiles(event: DragEvent): void {
    event.preventDefault();
    this.isDraggingFile = false;
    const files = event.dataTransfer?.files;
    if (files) this.ingestFiles(files);
  }

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files) this.ingestFiles(input.files);
    input.value = '';
  }

  private ingestFiles(files: FileList): void {
    Array.from(files).forEach((file) => {
      const id = `att-${Date.now()}-${Math.round(Math.random() * 1000)}`;
      const sizeLabel = file.size > 1024 * 1024 ? `${(file.size / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(file.size / 1024))} KB`;
      const attachment: Attachment = { id, name: file.name, sizeLabel, status: 'uploading', progress: 0 };
      this.attachments = [...this.attachments, attachment];
      this.runAttachmentPipeline(id);
    });
  }

  private runAttachmentPipeline(id: string): void {
    const tick = (progress: number, status: AttachmentStatus, delay: number) => {
      setTimeout(() => {
        this.attachments = this.attachments.map((a) => (a.id === id ? { ...a, progress, status } : a));
      }, delay);
    };
    tick(45, 'uploading', 250);
    tick(100, 'scanning', 700);
    setTimeout(() => {
      this.attachments = this.attachments.map((a) =>
        a.id === id ? { ...a, status: 'classified', classification: 'Internal — no restricted content detected' } : a
      );
    }, 1500);
  }

  removeAttachment(id: string): void {
    this.attachments = this.attachments.filter((a) => a.id !== id);
  }

  /* ============================== Validation ===================================== */

  private clearError(field: keyof FieldErrors): void {
    delete this.errors[field];
    this.errorSummary = this.errorSummary.filter((e) => e.field !== field);
  }

  private setError(field: keyof FieldErrors, message: string): void {
    this.errors[field] = message;
  }

  markTouched(field: keyof FieldErrors): void {
    this.touched[field] = true;
  }

  private validate(): boolean {
    this.errors = {};
    this.errorSummary = [];

    if (!this.maskedDestination.trim()) {
      this.setError('maskedDestination', 'Enter Masked destination.');
    }

    const purposeLen = this.purpose.trim().length;
    if (purposeLen === 0) {
      this.setError('purpose', 'Enter Purpose.');
    } else if (purposeLen < 10 || purposeLen > 2000) {
      this.setError('purpose', 'Review Purpose. The value does not meet the stated format or range.');
    }

    if (!this.channelId) this.setError('channel', 'Enter Channel.');
    if (!this.languageId) this.setError('language', 'Enter Language.');
    if (!this.templateId) this.setError('template', 'Enter Approved template.');

    if (this.placeholders.some((p) => !p.value.trim())) {
      this.setError('placeholders', 'Review Resolved placeholders. The value does not meet the stated format or range.');
    }

    if (!this.consentStateId) {
      this.setError('consentState', 'Enter Consent state.');
    }

    if (this.sendMode === 'schedule') {
      if (!this.scheduleDate || !this.scheduleTime) {
        this.setError('sendTime', 'Enter Send time.');
      } else {
        const chosen = new Date(`${this.scheduleDate}T${this.scheduleTime}`);
        if (isNaN(chosen.getTime()) || chosen.getTime() < Date.now() - 60000) {
          this.setError('sendTime', 'Review Send time. The value does not meet the stated format or range.');
        }
      }
    }

    if (!this.ownerId) this.setError('owner', 'Enter Owner.');

    this.errorSummary = (Object.keys(this.errors) as (keyof FieldErrors)[]).map((field) => ({
      field,
      message: this.errors[field]!,
    }));

    if (this.errorSummary.length > 0) {
      this.focusField(this.errorSummary[0].field);
      this.announce(`${this.errorSummary.length} field${this.errorSummary.length > 1 ? 's need' : ' needs'} attention.`);
      return false;
    }
    return true;
  }

  private focusField(field: keyof FieldErrors): void {
    setTimeout(() => {
      const target = this.fieldAnchors?.find((el) => el.nativeElement.dataset['field'] === field);
      target?.nativeElement.focus();
    }, 0);
  }

  focusFieldFromSummary(field: keyof FieldErrors): void {
    this.focusField(field);
  }

  /* ============================== Actions ========================================= */

  onPreview(): void {
    if (!this.validate()) return;
    this.submitPhase = 'reviewing';
    this.reasonForSend = '';
    this.typedConfirmation = '';
    this.announce('Review your message before sending.');
  }

  closeReview(): void {
    this.submitPhase = 'idle';
  }

  onValidateConsent(): void {
    if (!this.consentStateId) {
      this.setError('consentState', 'Enter Consent state.');
      this.focusField('consentState');
      return;
    }
    this.announce('Checking consent…');
    setTimeout(() => {
      if (this.selectedConsent?.tone === 'restricted') {
        this.announce('Consent could not be verified.');
      } else {
        this.announce('Consent verified against the current registry.');
      }
    }, 500);
  }

  saveDraft(): void {
    this.lastSavedAt = new Date();
    this.lastSavedLabel = 'Saved just now';
    this.lifecycleState = this.lifecycleState === 'Draft' ? 'Draft' : this.lifecycleState;
    this.announce('Draft saved.');
  }

  requestDiscard(): void {
    this.discardConfirming = true;
  }

  cancelDiscard(): void {
    this.discardConfirming = false;
  }

  confirmDiscard(): void {
    this.purpose = '';
    this.channelId = null;
    this.languageId = null;
    this.templateId = null;
    this.consentStateId = null;
    this.placeholders = [];
    this.attachments = [];
    this.sendMode = 'now';
    this.scheduleDate = '';
    this.scheduleTime = '';
    this.errors = {};
    this.errorSummary = [];
    this.discardConfirming = false;
    this.lastSavedLabel = 'Draft discarded';
    this.announce('Draft discarded. Required information is still outstanding.');
  }

  get canConfirmSend(): boolean {
    return this.typedConfirmation.trim().toUpperCase() === this.confirmationPhrase;
  }

  confirmSend(): void {
    if (!this.canConfirmSend) return;

    // Consent must not be a restricted state at the point of send.
    if (this.selectedConsent?.tone === 'restricted') {
      this.submitPhase = 'blocked';
      return;
    }

    this.submitPhase = 'sending';
    this.announce('Sending your message.');

    setTimeout(() => {
      if (this.hasAttachmentsScanning) {
        this.submitPhase = 'dependency-failure';
        this.dependencyFailureNote =
          'The message content was accepted, but the attachment scan has not completed. Delivery is held until the dependency clears.';
        this.lifecycleState = 'Ready to send';
        return;
      }

      const now = new Date();
      const effective =
        this.sendMode === 'now'
          ? now.toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit', hour12: true })
          : this.scheduledLabel;

      this.lifecycleState = this.sendMode === 'now' ? 'Sent' : 'Scheduled';
      this.result = {
        reference: this.stableReference,
        state: this.lifecycleState,
        effectiveTime: `${effective} (${this.operatingTimeZone})`,
        downstream: this.sendMode === 'now' ? 'Delivery pending with provider' : 'Awaiting scheduled dispatch',
        nextAction: this.sendMode === 'now' ? 'View delivery status in the communications log' : 'Edit or cancel the scheduled send',
      };
      this.submitPhase = 'success';
      this.announce('Message accepted. Reference ' + this.stableReference + '.');
    }, 1100);
  }

  retryDependency(): void {
    this.attachments = this.attachments.map((a) =>
      a.status === 'scanning' || a.status === 'uploading' ? { ...a, status: 'classified', classification: 'Internal — no restricted content detected' } : a
    );
    this.submitPhase = 'reviewing';
    this.announce('Retrying the dependent scan.');
  }

  backToComposer(): void {
    this.submitPhase = 'idle';
  }

  startNewMessage(): void {
    this.confirmDiscard();
    this.submitPhase = 'idle';
    this.lifecycleState = 'Draft';
    this.result = null;
  }

  dismissConflict(reapply: boolean): void {
    this.recordChangedRemotely = false;
    if (!reapply) {
      this.submitPhase = 'idle';
    }
  }

  /* ============================== History tabs =================================== */

  setHistoryTab(tab: 'linked' | 'documents' | 'activity' | 'integration'): void {
    this.activeHistoryTab = tab;
  }

  /* ============================== Accessibility helpers =========================== */

  private announce(message: string): void {
    this.liveAnnouncement = '';
    setTimeout(() => (this.liveAnnouncement = message), 30);
  }

  trackByIndex(index: number): number {
    return index;
  }
}