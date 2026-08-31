import { Component, computed, signal } from '@angular/core';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';

/* ---------------------------------------------------------------------- */
/* Types                                                                  */
/* ---------------------------------------------------------------------- */

type LifecycleState = 'Draft' | 'Submitted' | 'Pending review' | 'Active' | 'Retired';
type SimulationResult = 'Not tested' | 'Passed' | 'Failed' | 'Testing';

/** Demo scenario harness — lets every required UI state in §4.6.4 be
 *  demonstrated on demand instead of only being reachable through a live
 *  backend. This selector is a review/QA aid, not a production control. */
type Scenario =
  | 'normal'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'noAccess'
  | 'conflict'
  | 'dependencyFailure'
  | 'success';

interface HolidayEntry {
  id: string;
  date: string;
  label: string;
}

interface OutcomeRecord {
  kind: 'version' | 'test' | 'publish' | 'retire';
  reference: string;
  state: LifecycleState;
  effectiveTime: string;
  downstreamStatus: string;
  owner: string;
  nextAction: string;
}

interface AuditEntry {
  time: string;
  actor: string;
  action: string;
  detail: string;
}

/* ---------------------------------------------------------------------- */
/* Component                                                              */
/* ---------------------------------------------------------------------- */

@Component({
  selector: 'app-sla-policy-calendar',
  imports: [],
  templateUrl: './sla-policy-calendar.html',
  styleUrl: './sla-policy-calendar.css',
})
export class SlaPolicyCalendarComponent {
  /* ---------------- Demo scenario harness ---------------- */
  scenario = signal<Scenario>('normal');

  scenarioOptions: { value: Scenario; label: string }[] = [
    { value: 'normal', label: 'Normal' },
    { value: 'loading', label: 'Loading' },
    { value: 'empty', label: 'Empty — no holidays' },
    { value: 'validation', label: 'Validation errors' },
    { value: 'duplicate', label: 'Duplicate policy name' },
    { value: 'noAccess', label: 'No access' },
    { value: 'conflict', label: 'Record conflict' },
    { value: 'dependencyFailure', label: 'Dependency failure' },
    { value: 'success', label: 'Persistent success' },
  ];

  setScenario(value: string) {
    const s = value as Scenario;
    this.scenario.set(s);
    this.formErrors.set({});
    this.showVersionPanel.set(false);
    this.showTestPanel.set(false);
    this.showPublishDialog.set(false);
    this.showRetireDialog.set(false);
    this.publishConfirmText.set('');
    this.retireReasonCode.set('');
    this.retireReasonNote.set('');

    if (s === 'empty') {
      this.holidays.set([]);
    } else {
      this.holidays.set(this.defaultHolidays());
    }

    if (s === 'success') {
      this.outcome.set({
        kind: 'publish',
        reference: this.policyReference(),
        state: 'Active',
        effectiveTime: '04 Aug 2026, 10:05 am IST',
        downstreamStatus: 'Routing engine sync — completed',
        owner: this.owner(),
        nextAction: 'View linked queues',
      });
    } else if (s === 'dependencyFailure') {
      this.outcome.set({
        kind: 'test',
        reference: this.policyReference(),
        state: this.status(),
        effectiveTime: '—',
        downstreamStatus: 'Routing simulation service — unavailable',
        owner: this.owner(),
        nextAction: 'Retry dependency',
      });
    } else {
      this.outcome.set(null);
    }
  }

  /* ---------------- Accordion (progressive disclosure) ---------------- */
  expanded = signal<Set<string>>(new Set(['identity', 'hours']));

  isExpanded(section: string) {
    return this.expanded().has(section);
  }

  toggleSection(section: string) {
    const next = new Set(this.expanded());
    next.has(section) ? next.delete(section) : next.add(section);
    this.expanded.set(next);
  }

  /* ---------------- Record identity / header ---------------- */
  policyReference = signal('SLA-2026-0142');
  policyName = signal('Donor Care — Standard Response SLA');
  version = signal('v3.2');
  status = signal<LifecycleState>('Submitted');
  owner = signal('Meera Krishnan');
  lastRefreshed = signal('04 Aug 2026, 09:41 am IST');

  statusTone = computed<'draft' | 'submitted' | 'active' | 'retired'>(() => {
    switch (this.status()) {
      case 'Draft':
        return 'draft';
      case 'Submitted':
      case 'Pending review':
        return 'submitted';
      case 'Active':
        return 'active';
      case 'Retired':
        return 'retired';
    }
  });

  /* ---------------- Context, scope and filters ---------------- */
  effectiveScope = [
    { label: 'Donation Operations', granted: true },
    { label: 'Community Outreach', granted: true },
    { label: 'Major Gifts', granted: false },
    { label: 'Corporate Partnerships', granted: false },
  ];

  catalogueSearch = signal('');
  savedFilter = signal('My scope only');

  /* ---------------- Field and control contract (§4.6.2) ---------------- */
  policyNameOptions = [
    'Donor Care — Standard Response SLA',
    'Donor Care — Priority Response SLA',
    'Community Outreach — Weekend Coverage SLA',
    'Major Gifts — Concierge SLA',
  ];

  queueOrPriority = signal('Donor Care — Priority 1');
  queueOptions = [
    { label: 'Donor Care — Priority 1', disabledReason: null },
    { label: 'Donor Care — Priority 2', disabledReason: null },
    { label: 'Community Outreach — Priority 1', disabledReason: null },
    { label: 'Major Gifts — Priority 1', disabledReason: 'Outside effective data scope' },
  ];

  /**
   * The time-zone catalogue, from the GlobalMaster API.
   *
   * WHAT THIS REPLACES: four literal `<option>` tags — Kolkata, Dubai, London and New York —
   * with their offsets typed into the markup by hand. Those offsets were already wrong for
   * London and New York half the year, because a hard-coded "+01:00" cannot follow daylight
   * saving, and an SLA calendar that misreads the operating zone by an hour breaches its own
   * targets without anybody noticing.
   *
   * This page collects no country, so the list is the unfiltered catalogue — a supported case
   * that the API answers with the full set rather than refusing for want of a country link.
   */
  protected readonly geo = createGeoCascade();

  timeZone = signal('Asia/Kolkata');

  /**
   * The chosen zone as a person reads it: "(+05:30) India Standard Time".
   *
   * The signal itself holds the IANA key, because that is the stable identifier anything
   * converting a time needs. The label comes from the catalogue rather than being typed beside
   * it, so the offset shown on the review step cannot drift from the one actually in force.
   * Falls back to the raw key while the catalogue is still loading.
   */
  protected readonly timeZoneLabel = computed(
    () => this.geo.timeZones().find((zone) => zone.ianaKey === this.timeZone())?.name
      ?? this.timeZone(),
  );
  businessHoursOpen = signal('09:00');
  businessHoursClose = signal('18:00');
  workingDays = signal('Monday – Friday');

  holidays = signal<HolidayEntry[]>(this.defaultHolidays());
  newHolidayDate = signal('');
  newHolidayLabel = signal('');

  warningThreshold = signal('4');
  breachThreshold = signal('24');
  escalationRoute = signal('Tier 1 Agent → Tier 2 Supervisor → Duty Manager');

  simulationResult = signal<SimulationResult>('Passed');

  private defaultHolidays(): HolidayEntry[] {
    return [
      { id: 'h1', date: '15 Aug 2026', label: 'Independence Day' },
      { id: 'h2', date: '02 Oct 2026', label: 'Gandhi Jayanti' },
      { id: 'h3', date: '25 Dec 2026', label: 'Christmas Day' },
    ];
  }

  addHoliday() {
    if (!this.newHolidayDate() || !this.newHolidayLabel().trim()) return;
    const entry: HolidayEntry = {
      id: 'h' + Math.random().toString(36).slice(2, 8),
      date: this.newHolidayDate(),
      label: this.newHolidayLabel().trim(),
    };
    this.holidays.set([...this.holidays(), entry]);
    this.newHolidayDate.set('');
    this.newHolidayLabel.set('');
  }

  removeHoliday(id: string) {
    this.holidays.set(this.holidays().filter((h) => h.id !== id));
  }

  /* ---------------- Validation (§4.6.6) ---------------- */
  formErrors = signal<Record<string, string>>({});

  validate(): boolean {
    const errors: Record<string, string> = {};
    const forceInvalid = this.scenario() === 'validation';

    if (forceInvalid || !this.policyName().trim()) {
      errors['policyName'] = 'Enter Policy name.';
    }
    if (forceInvalid || !this.queueOrPriority()) {
      errors['queueOrPriority'] = 'Enter Applicable queue or priority.';
    }
    if (forceInvalid || !this.businessHoursOpen() || !this.businessHoursClose()) {
      errors['businessHours'] = 'Enter Business hours.';
    }
    if (forceInvalid || !this.workingDays().trim()) {
      errors['workingDays'] = 'Enter Working days.';
    }
    if (!/^\d+$/.test(this.warningThreshold())) {
      errors['warningThreshold'] = 'Review Warning threshold. The value does not meet the stated format or range.';
    }
    if (!/^\d+$/.test(this.breachThreshold())) {
      errors['breachThreshold'] = 'Review Breach threshold. The value does not meet the stated format or range.';
    }
    if (forceInvalid || !this.escalationRoute().trim()) {
      errors['escalationRoute'] = 'Enter Escalation route.';
    }

    this.formErrors.set(errors);
    return Object.keys(errors).length === 0;
  }

  objectKeys(obj: Record<string, string>): string[] {
    return Object.keys(obj);
  }

  focusField(fieldId: string) {
    const el = document.getElementById('field-' + fieldId);
    el?.focus();
    el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  /* ---------------- Duplicate detection demo (§4.6.4 / §4.6.6) ---------------- */
  duplicateCandidate = computed(() => {
    if (this.scenario() !== 'duplicate') return null;
    return { reference: 'SLA-2026-0089', name: 'Donor Care — Standard Response SLA (Draft copy)' };
  });

  /* ---------------- Loading ---------------- */
  isBusy = signal(false);

  private runWithLoading(after: () => void, ms = 900) {
    this.isBusy.set(true);
    setTimeout(() => {
      this.isBusy.set(false);
      after();
    }, ms);
  }

  /* ---------------- Persistent outcome (§4.6.1 / §4.6.6) ---------------- */
  outcome = signal<OutcomeRecord | null>(null);

  dismissOutcome() {
    this.outcome.set(null);
  }

  /* ---------------- Action: Version ---------------- */
  showVersionPanel = signal(false);
  versionHistory = [
    { version: 'v3.2', state: 'Active' as LifecycleState, date: '04 Aug 2026', actor: 'Meera Krishnan' },
    { version: 'v3.1', state: 'Retired' as LifecycleState, date: '02 Jun 2026', actor: 'Arjun Nair' },
    { version: 'v3.0', state: 'Retired' as LifecycleState, date: '14 Feb 2026', actor: 'Meera Krishnan' },
  ];

  canOpenVersion = computed(() => this.scenario() !== 'noAccess');

  openVersionPanel() {
    if (!this.canOpenVersion()) return;
    this.showVersionPanel.set(true);
  }

  closeVersionPanel() {
    this.showVersionPanel.set(false);
  }

  applyVersion(v: string) {
    this.runWithLoading(() => {
      this.version.set(v);
      this.outcome.set({
        kind: 'version',
        reference: this.policyReference(),
        state: this.status(),
        effectiveTime: '04 Aug 2026, 10:12 am IST',
        downstreamStatus: 'No downstream dependency triggered',
        owner: this.owner(),
        nextAction: 'Review changed fields',
      });
      this.showVersionPanel.set(false);
    });
  }

  /* ---------------- Action: Test ---------------- */
  showTestPanel = signal(false);
  canTest = computed(() => this.scenario() !== 'noAccess');

  runTest() {
    if (!this.canTest()) return;
    this.showTestPanel.set(true);
    this.simulationResult.set('Testing');
    this.runWithLoading(() => {
      if (this.scenario() === 'dependencyFailure') {
        this.simulationResult.set('Failed');
        this.outcome.set({
          kind: 'test',
          reference: this.policyReference(),
          state: this.status(),
          effectiveTime: '—',
          downstreamStatus: 'Routing simulation service — unavailable',
          owner: this.owner(),
          nextAction: 'Retry dependency',
        });
      } else {
        this.simulationResult.set('Passed');
        this.outcome.set({
          kind: 'test',
          reference: this.policyReference(),
          state: this.status(),
          effectiveTime: '04 Aug 2026, 10:20 am IST',
          downstreamStatus: 'Routing simulation service — completed',
          owner: this.owner(),
          nextAction: 'Proceed to publish',
        });
      }
    }, 1100);
  }

  closeTestPanel() {
    this.showTestPanel.set(false);
  }

  retryDependency() {
    this.runTest();
  }

  /* ---------------- Action: Publish (primary decision) ---------------- */
  canPublish = computed(
    () => (this.status() === 'Submitted' || this.status() === 'Pending review') && this.scenario() !== 'noAccess'
  );

  showPublishDialog = signal(false);
  publishConfirmText = signal('');
  publishReason = signal('');

  openPublishDialog() {
    if (!this.canPublish()) return;
    if (!this.validate()) {
      const first = Object.keys(this.formErrors())[0];
      if (first) this.focusField(first);
      return;
    }
    this.showPublishDialog.set(true);
  }

  closePublishDialog() {
    this.showPublishDialog.set(false);
    this.publishConfirmText.set('');
  }

  confirmPublish() {
    if (this.publishConfirmText().trim().toUpperCase() !== 'PUBLISH') return;
    this.runWithLoading(() => {
      this.status.set('Active');
      this.showPublishDialog.set(false);
      this.publishConfirmText.set('');
      this.outcome.set({
        kind: 'publish',
        reference: this.policyReference(),
        state: 'Active',
        effectiveTime: '04 Aug 2026, 10:31 am IST',
        downstreamStatus: 'Routing engine sync — in progress',
        owner: this.owner(),
        nextAction: 'View linked queues',
      });
    }, 1200);
  }

  /* ---------------- Action: Retire (secondary / danger) ---------------- */
  canRetire = computed(() => this.status() === 'Active' && this.scenario() !== 'noAccess');

  showRetireDialog = signal(false);
  retireReasonCode = signal('');
  retireReasonNote = signal('');
  retireReasonOptions = [
    'Superseded by a newer policy version',
    'Queue or channel decommissioned',
    'Incorrect configuration — replacing immediately',
    'Other (add detail below)',
  ];

  openRetireDialog() {
    if (!this.canRetire()) return;
    this.showRetireDialog.set(true);
  }

  closeRetireDialog() {
    this.showRetireDialog.set(false);
    this.retireReasonCode.set('');
    this.retireReasonNote.set('');
  }

  confirmRetire() {
    if (!this.retireReasonCode()) return;
    this.runWithLoading(() => {
      this.status.set('Retired');
      this.showRetireDialog.set(false);
      this.outcome.set({
        kind: 'retire',
        reference: this.policyReference(),
        state: 'Retired',
        effectiveTime: '04 Aug 2026, 10:38 am IST',
        downstreamStatus: 'Linked queues reassigned to fallback SLA',
        owner: this.owner(),
        nextAction: 'View replacement policy',
      });
    }, 1200);
  }

  /* ---------------- Conflict recovery (§4.6.4) ---------------- */
  reviewLatestVersion() {
    this.scenario.set('normal');
  }

  reapplyChanges() {
    this.scenario.set('normal');
  }

  /* ---------------- Related & history ---------------- */
  activeTab = signal<'linked' | 'documents' | 'activity'>('linked');

  setTab(tab: 'linked' | 'documents' | 'activity') {
    this.activeTab.set(tab);
  }

  linkedQueues = [
    { name: 'Donor Care — Inbound Queue', status: 'Synced' },
    { name: 'Donor Care — Escalation Queue', status: 'Synced' },
    { name: 'Community Outreach — Weekend Queue', status: 'Pending sync' },
  ];

  documents = [
    { name: 'SLA policy approval memo.pdf', updated: '01 Jun 2026' },
    { name: 'Escalation matrix v3.pdf', updated: '28 May 2026' },
  ];

  auditLog: AuditEntry[] = [
    { time: '04 Aug 2026, 09:41 am', actor: 'Meera Krishnan', action: 'Version refreshed', detail: 'v3.1 → v3.2' },
    { time: '02 Jun 2026, 04:12 pm', actor: 'Arjun Nair', action: 'Retired', detail: 'Reason: Superseded by v3.1' },
    { time: '14 Feb 2026, 11:03 am', actor: 'Meera Krishnan', action: 'Published', detail: 'Effective 14 Feb 2026' },
  ];

  copyReference() {
    navigator.clipboard?.writeText(this.policyReference()).catch(() => undefined);
  }
}