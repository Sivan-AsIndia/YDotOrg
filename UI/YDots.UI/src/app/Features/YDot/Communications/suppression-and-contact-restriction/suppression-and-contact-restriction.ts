import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgFor, NgIf } from '@angular/common';

export interface RestrictionRecord {
  reference: string;
  partyName: string;
  partyReference: string;
  restrictionType: string;
  channel: string;
  purposeSummary: string;
  status: 'Active' | 'Scheduled' | 'Expired' | 'Withdrawn';
  effectiveFrom: string;
  effectiveTo: string | null;
  recordedBy: string;
  reviewDate: string | null;
  reviewDue: boolean;
  reasonPreview: string;
  scopeUnit: string;
  daysLeft: number | null;
}

@Component({
  selector: 'app-suppression-and-contact-restriction',
  imports: [FormsModule, NgFor, NgIf],
  templateUrl: './suppression-and-contact-restriction.html',
  styleUrl: './suppression-and-contact-restriction.css',
})
export class SuppressionAndContactRestrictionComponent {
  // Access simulator
  effectiveRole = 'Donor Care';
  scopeUnits: string[] = ['Donation Operations', 'Community Outreach'];

  // List state
  loading = false;
  lastRefreshed = '4 Aug 2026, 11:40 am';
  searchQuery = '';
  filterType = '';
  filterChannel = '';
  filterStatus = '';
  quickFilter: 'all' | 'active' | 'mine' | 'review' = 'all';

  // Permissions (simplified for UI demo – real app derives from role + scope + state)
  canAdd = true;
  canWithdraw = true;
  canRequestException = true;
  canExport = true;

  // Panel / form
  panelOpen = false;
  editingItem: RestrictionRecord | null = null;
  submitting = false;
  lastOutcome: { reference: string; state: string; effectiveTime: string } | null = null;
  validationErrors: { field: string; message: string }[] = [];

  form = this.emptyForm();

  // Confirm modal
  confirmModal: { action: string; item: RestrictionRecord; requiresReason: boolean } | null = null;
  confirmReason = '';

  // Mock data (scoped)
  private allRestrictions: RestrictionRecord[] = [
    {
      reference: 'SUP-2026-0142',
      partyName: 'Ananya Krishnan',
      partyReference: 'PTY-88421',
      restrictionType: 'Channel suppression',
      channel: 'Email',
      purposeSummary: 'Fundraising appeals',
      status: 'Active',
      effectiveFrom: '12 Jun 2026',
      effectiveTo: '12 Dec 2026',
      recordedBy: 'Sophie Bennett',
      reviewDate: '12 Sep 2026',
      reviewDue: true,
      reasonPreview: 'Donor requested pause on appeal emails after recent bereavement notice.',
      scopeUnit: 'Donation Operations',
      daysLeft: 130,
    },
    {
      reference: 'SUP-2026-0138',
      partyName: 'Ravi Menon',
      partyReference: 'PTY-77102',
      restrictionType: 'Full contact hold',
      channel: 'All channels',
      purposeSummary: 'All outbound contact',
      status: 'Active',
      effectiveFrom: '01 Jul 2026',
      effectiveTo: null,
      recordedBy: 'Priya Nair',
      reviewDate: '01 Oct 2026',
      reviewDue: false,
      reasonPreview: 'Legal hold pending investigation of complaint case CMP-2026-089.',
      scopeUnit: 'Community Outreach',
      daysLeft: null,
    },
    {
      reference: 'SUP-2026-0129',
      partyName: 'Meera Shah',
      partyReference: 'PTY-65033',
      restrictionType: 'Purpose block',
      channel: 'SMS',
      purposeSummary: 'Event invitations',
      status: 'Scheduled',
      effectiveFrom: '15 Aug 2026',
      effectiveTo: '15 Nov 2026',
      recordedBy: 'Sophie Bennett',
      reviewDate: '01 Nov 2026',
      reviewDue: false,
      reasonPreview: 'Temporary block while preference centre update is processed.',
      scopeUnit: 'Donation Operations',
      daysLeft: 103,
    },
    {
      reference: 'SUP-2026-0115',
      partyName: 'Joseph D’Souza',
      partyReference: 'PTY-44210',
      restrictionType: 'Temporary pause',
      channel: 'Phone',
      purposeSummary: 'Stewardship calls',
      status: 'Expired',
      effectiveFrom: '01 Mar 2026',
      effectiveTo: '30 Jun 2026',
      recordedBy: 'Arun Kapoor',
      reviewDate: null,
      reviewDue: false,
      reasonPreview: 'Travel abroad; request for no calls during absence.',
      scopeUnit: 'Community Outreach',
      daysLeft: 0,
    },
    {
      reference: 'SUP-2026-0102',
      partyName: 'Lakshmi Iyer',
      partyReference: 'PTY-33901',
      restrictionType: 'Channel suppression',
      channel: 'WhatsApp',
      purposeSummary: 'Campaign updates',
      status: 'Withdrawn',
      effectiveFrom: '10 Feb 2026',
      effectiveTo: '10 May 2026',
      recordedBy: 'Sophie Bennett',
      reviewDate: null,
      reviewDue: false,
      reasonPreview: 'Withdrawn after donor re-consented via preference centre.',
      scopeUnit: 'Donation Operations',
      daysLeft: null,
    },
  ];

  filteredRestrictions: RestrictionRecord[] = [...this.allRestrictions];

  metrics = {
    total: 5,
    active: 2,
    dueReview: 1,
    expiringSoon: 0,
  };

  // ---------- Lifecycle helpers ----------
  private emptyForm() {
    return {
      partyReference: '',
      restrictionType: '',
      channel: '',
      purpose: '',
      effectiveFrom: '',
      effectiveTo: '',
      reason: '',
      evidenceName: '',
      recordedBy: 'Sophie Bennett',
      reviewDate: '',
      overrideAuthority: '',
    };
  }

  trackByRef(_: number, item: RestrictionRecord) {
    return item.reference;
  }

  // ---------- Scope / filters ----------
  onScopeChange() {
    this.applyFilters();
  }

  toggleScope(unit: string) {
    if (this.scopeUnits.includes(unit)) {
      this.scopeUnits = this.scopeUnits.filter((u) => u !== unit);
    } else {
      this.scopeUnits = [...this.scopeUnits, unit];
    }
    this.applyFilters();
  }

  setQuick(q: 'all' | 'active' | 'mine' | 'review') {
    this.quickFilter = q;
    this.applyFilters();
  }

  applyFilters() {
    let list = this.allRestrictions.filter((r) => this.scopeUnits.includes(r.scopeUnit));

    if (this.searchQuery.trim()) {
      const q = this.searchQuery.toLowerCase();
      list = list.filter(
        (r) =>
          r.partyName.toLowerCase().includes(q) ||
          r.reference.toLowerCase().includes(q) ||
          r.reasonPreview.toLowerCase().includes(q) ||
          r.partyReference.toLowerCase().includes(q)
      );
    }
    if (this.filterType) list = list.filter((r) => r.restrictionType === this.filterType);
    if (this.filterChannel) list = list.filter((r) => r.channel === this.filterChannel || r.channel === 'All channels');
    if (this.filterStatus) list = list.filter((r) => r.status === this.filterStatus);

    if (this.quickFilter === 'active') list = list.filter((r) => r.status === 'Active');
    if (this.quickFilter === 'mine') list = list.filter((r) => r.recordedBy === 'Sophie Bennett');
    if (this.quickFilter === 'review') list = list.filter((r) => r.reviewDue);

    this.filteredRestrictions = list;
    this.recalcMetrics(list);
  }

  private recalcMetrics(list: RestrictionRecord[]) {
    this.metrics = {
      total: list.length,
      active: list.filter((r) => r.status === 'Active').length,
      dueReview: list.filter((r) => r.reviewDue).length,
      expiringSoon: list.filter((r) => r.daysLeft !== null && r.daysLeft <= 30 && r.status === 'Active').length,
    };
  }

  clearFilters() {
    this.searchQuery = '';
    this.filterType = '';
    this.filterChannel = '';
    this.filterStatus = '';
    this.quickFilter = 'all';
    this.applyFilters();
  }

  refresh() {
    this.loading = true;
    setTimeout(() => {
      this.lastRefreshed = new Date().toLocaleString('en-IN', {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        hour12: true,
      });
      this.loading = false;
      this.applyFilters();
    }, 600);
  }

  exportList() {
    // Controlled export – UI only shows the intent
    alert('Export requested. Purpose, scope, classification and audit reference will be recorded.');
  }

  // ---------- Panel / form ----------
  openAddPanel() {
    this.editingItem = null;
    this.form = this.emptyForm();
    this.validationErrors = [];
    this.lastOutcome = null;
    this.panelOpen = true;
  }

  openDetail(item: RestrictionRecord) {
    this.editingItem = item;
    this.form = {
      partyReference: item.partyReference,
      restrictionType: item.restrictionType,
      channel: item.channel,
      purpose: item.purposeSummary,
      effectiveFrom: '',
      effectiveTo: '',
      reason: item.reasonPreview,
      evidenceName: '',
      recordedBy: item.recordedBy,
      reviewDate: item.reviewDate || '',
      overrideAuthority: '',
    };
    this.validationErrors = [];
    this.lastOutcome = null;
    this.panelOpen = true;
  }

  closePanel() {
    this.panelOpen = false;
    this.editingItem = null;
    this.validationErrors = [];
  }

  lookupParty() {
    // Demo: simulate a successful lookup
    if (!this.form.partyReference.trim()) {
      this.form.partyReference = 'PTY-';
      return;
    }
    // In real app this opens a scoped search selector
  }

  triggerUpload() {
    const el = document.querySelector('input[type="file"]') as HTMLInputElement | null;
    el?.click();
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.form.evidenceName = file.name;
    }
  }

  clearEvidence(event: Event) {
    event.stopPropagation();
    this.form.evidenceName = '';
  }

  focusField(fieldId: string) {
    const el = document.getElementById(fieldId);
    el?.focus();
  }

  submitRestriction() {
    this.validationErrors = [];

    if (!this.form.restrictionType) {
      this.validationErrors.push({ field: 'restrictionType', message: 'Enter Restriction type.' });
    }
    if (!this.form.channel) {
      this.validationErrors.push({ field: 'channel', message: 'Enter Channel.' });
    }
    if (!this.form.effectiveFrom) {
      this.validationErrors.push({ field: 'effectiveFrom', message: 'Enter Effective from.' });
    }
    if (!this.form.reason || this.form.reason.trim().length < 10) {
      this.validationErrors.push({
        field: 'reason',
        message: 'Enter Reason. Provide at least 10 meaningful characters.',
      });
    }
    if (this.form.purpose && this.form.purpose.trim().length > 0 && this.form.purpose.trim().length < 10) {
      this.validationErrors.push({
        field: 'purpose',
        message: 'Review Purpose. The value does not meet the stated format or range (10–2,000 characters).',
      });
    }

    if (this.validationErrors.length) {
      // Focus first invalid
      setTimeout(() => this.focusField(this.validationErrors[0].field), 50);
      return;
    }

    this.submitting = true;
    // Simulate server decision + persistent confirmation
    setTimeout(() => {
      const ref = this.editingItem?.reference || `SUP-2026-${Math.floor(Math.random() * 9000 + 1000)}`;
      this.lastOutcome = {
        reference: ref,
        state: 'Active',
        effectiveTime: new Date().toLocaleString('en-IN'),
      };
      this.submitting = false;

      // Optimistic add for demo
      if (!this.editingItem) {
        const newRec: RestrictionRecord = {
          reference: ref,
          partyName: 'New party (lookup)',
          partyReference: this.form.partyReference || 'PTY-NEW',
          restrictionType: this.form.restrictionType,
          channel: this.form.channel,
          purposeSummary: this.form.purpose || this.form.restrictionType,
          status: 'Active',
          effectiveFrom: this.form.effectiveFrom.slice(0, 10),
          effectiveTo: this.form.effectiveTo ? this.form.effectiveTo.slice(0, 10) : null,
          recordedBy: this.form.recordedBy,
          reviewDate: this.form.reviewDate || null,
          reviewDue: false,
          reasonPreview: this.form.reason.slice(0, 120),
          scopeUnit: this.scopeUnits[0] || 'Donation Operations',
          daysLeft: 90,
        };
        this.allRestrictions = [newRec, ...this.allRestrictions];
        this.applyFilters();
      }
    }, 700);
  }

  // ---------- Actions ----------
  reviewHistory(item: RestrictionRecord) {
    alert(`Review history for ${item.reference}\n\nAudit chronology and linked evidence would open here. History is preserved.`);
  }

  requestException(item: RestrictionRecord) {
    alert(`Request exception for ${item.reference}\n\nException request will be routed for independent review.`);
  }

  confirmWithdraw(item: RestrictionRecord) {
    this.confirmModal = {
      action: 'Withdraw restriction',
      item,
      requiresReason: true,
    };
    this.confirmReason = '';
  }

  cancelConfirm() {
    this.confirmModal = null;
    this.confirmReason = '';
  }

  executeConfirm() {
    if (!this.confirmModal) return;
    if (this.confirmModal.requiresReason && !this.confirmReason.trim()) return;

    const item = this.confirmModal.item;
    // Simulate lifecycle transition
    this.allRestrictions = this.allRestrictions.map((r) =>
      r.reference === item.reference ? { ...r, status: 'Withdrawn' as const, daysLeft: null } : r
    );
    this.applyFilters();
    this.confirmModal = null;
    this.confirmReason = '';
  }
}