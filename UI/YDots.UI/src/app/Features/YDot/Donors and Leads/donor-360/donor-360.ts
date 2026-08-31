import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { WorkflowDonor, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { FormsModule } from '@angular/forms';
import {
  UiState,
  Donor360Data,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { effect, ElementRef, ViewChild } from '@angular/core';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { Donor360Response } from '../../../../Shared/models/donor-contract.model';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';


@Component({
  selector: 'app-donor-360',
  imports: [CommonModule, FormsModule],
  templateUrl: './donor-360.html',
  styleUrl: './donor-360.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Donor360Component {
    private readonly donorApi = inject(DonorApiService);

    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly workflow = inject(WorkflowStateService);
    private readonly people = inject(PeopleDirectoryService);
    readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
    readonly donorId = signal(this.route.snapshot.queryParamMap.get('donorId'));



  
    // ============================================================
    // ACTION DIALOG — a single native <dialog>, opened/closed
    // imperatively so it always renders in the browser's top layer.
    // This guarantees correct centred positioning even if this
    // component is later embedded inside a host shell that applies
    // a transform/filter/will-change to an ancestor element — a
    // transformed ancestor turns position:fixed into "fixed relative
    // to that ancestor" instead of the viewport, which is the classic
    // cause of a dialog appearing pinned to the top-left. <dialog>
    // shown via showModal() sits outside normal layout entirely, so
    // that problem cannot happen.
    // ============================================================
    @ViewChild('actionDialog') actionDialogRef?: ElementRef<HTMLDialogElement>;
  
    constructor() {
      this.loadProfile();
      effect(() => {
        const action = this.activeAction();
        const dialog = this.actionDialogRef?.nativeElement;
        if (!dialog) return;
        if (action && !dialog.open) {
          dialog.showModal();
        } else if (!action && dialog.open) {
          dialog.close();
        }
      });
    }
  
    // ============================================================
    // ACCESS SIMULATOR — role, permission and data-scope model
    // Implements spec §6.1 permission decision order and §4.3.3
    // ============================================================
  
    readonly roles = [
      'Authorised relationship users',
      'Manager',
      'Fundraiser',
      'Fundraising Manager',
      'Donor Care',
      'Data Steward',
      'System integration',
      'Read-only guest',
    ] as const;
    selectedRole = signal<typeof this.roles[number]>('Authorised relationship users');
  
    /** Controlled permission IDs from YDOT section 04 (SCR-DON-003 and §6.1). */
    private readonly permissionMap: Record<string, string[]> = {
      'Authorised relationship users': ['don.donor-360.view', 'don.contact.view', 'don.donor-360.correct', 'don.donor-360.follow-up', 'don.donor-360.create-intent'],
      'Manager': ['don.donor-360.view', 'don.contact.view', 'don.donor-360.correct', 'don.donor-360.follow-up', 'don.donor-360.create-intent', 'don.donor-360.delete-draft'],
      'Fundraiser': ['don.donor-360.view', 'don.contact.view', 'don.donor-360.follow-up', 'don.donor-360.create-intent'],
      'Fundraising Manager': ['don.donor-360.view', 'don.contact.view', 'don.donor-360.correct', 'don.donor-360.follow-up', 'don.donor-360.create-intent'],
      'Donor Care': ['don.donor-360.view', 'don.contact.view', 'don.donor-360.follow-up'],
      'Data Steward': ['don.donor-360.view'],
      'System integration': ['don.donor-360.view'],
      'Read-only guest': [],
    };
  
    hasPermission(perm: string): boolean {
      return this.permissionMap[this.selectedRole()].includes(perm);
    }
  
    readonly scopeUnits = ['Donation Operations', 'Community Outreach', 'Major Gifts', 'Corporate Partnerships'];
    readonly assignedScope = ['Donation Operations', 'Community Outreach'];
    isInScope(unit: string): boolean {
      return this.assignedScope.includes(unit);
    }
  
    // ============================================================
    // SCENARIO SIMULATOR — required UI states (spec §4.3.4)
    // ============================================================
  
    readonly scenarios: { id: Scenario; label: string }[] = [
      { id: 'loaded', label: 'Normal — active donor' },
      { id: 'loading', label: 'Loading' },
      { id: 'empty', label: 'Empty — nothing in scope yet' },
      { id: 'draft', label: 'Draft — create intent' },
      { id: 'duplicate', label: 'Possible duplicate found' },
      { id: 'conflict', label: 'Conflict — record changed' },
      { id: 'dependency-failure', label: 'Dependency failure' },
      { id: 'no-access', label: 'No access — out of scope' },
    ];
    scenario = signal<Scenario>('loaded');
  
    /** A guest role always resolves to the non-disclosing no-access presentation,
     *  regardless of the scenario picked — permission is evaluated before scenario. */
    effectiveState = computed<Scenario>(() => {
      if (!this.hasPermission('don.donor-360.view')) return 'no-access';
      return this.scenario();
    });
  
    setScenario(id: Scenario) {
      this.scenario.set(id);
      this.activeAction.set(null);
      this.successPanel.set(null);
      this.dependencyNotice.set(false);
    }
  
    setRole(role: typeof this.roles[number]) {
      this.selectedRole.set(role);
      this.activeAction.set(null);
    }
  
    // ============================================================
    // RECORD DATA (mock — server-derived, read-only in this view)
    // ============================================================
  
    readonly donor = computed(() => {
      const lead = this.workflow.getLead(this.leadId()) ?? this.workflow.leads().find((item) => item.donorId === this.donorId());
      const donor = this.workflow.getDonor(this.donorId() ?? lead?.donorId);
      const consentStatus = donor?.consentStatus === 'Do Not Contact'
        ? 'Withdrawn'
        : donor?.consentStatus === 'Partial'
          ? 'Partial'
          : 'Granted';
      return {
        reference: donor?.donorId ?? this.donorId() ?? lead?.donorId ?? (lead ? `DON-${lead.id.replace('LEAD-', '')}` : ''),
        fullName: donor?.name ?? lead?.name ?? '',
        lifecycleState: lead?.converted ? 'Active' : this.route.snapshot.queryParamMap.get('conversion') === 'pending' ? 'Conversion pending' : 'Active',
        owner: donor?.owner ?? lead?.owner ?? 'Unassigned',
        freshness: lead?.lastActivity ?? (donor ? `Last donation ${donor.lastDonationDate}` : ''),
        email: donor?.email ?? lead?.email ?? '',
        phone: donor?.mobile ?? lead?.mobile ?? '',
        address: donor ? `${donor.location}, ${donor.region}` : '',
        consentStatus: consentStatus as 'Granted' | 'Partial' | 'Withdrawn',
        consentUpdated: donor?.createdDate ?? '',
        consentChannels: consentStatus === 'Withdrawn' ? 'No permitted channels' : 'Email, SMS',
        commsChannel: 'Email preferred',
        commsFrequency: 'Monthly digest',
        doNotContact: donor?.consentStatus === 'Do Not Contact' || (lead?.contactRestricted ?? false),
      };
    });
  
    /**
     * The Donor 360 payload from the server.
     *
     * WHAT THIS REPLACES. The totals below were three hard-coded amounts - pledged 250,000,
     * received 180,000, reconciled 165,000 - and the campaign history was three invented campaigns
     * with invented gifts. EVERY DONOR SHOWED THE SAME FIGURES. This is the screen a fundraiser
     * opens before telephoning somebody, so those numbers were being read out to donors.
     *
     * The endpoint already returned all of it; the screen simply was not asking.
     */
    protected readonly profile = signal<Donor360Response | null>(null);

    protected readonly profileError = signal('');

    /**
     * What the donor has actually given, by stage.
     *
     * EMPTY UNTIL IT LOADS, and empty if the donor has given nothing. A blank total is obviously
     * blank; a plausible one is read out as fact.
     */
    readonly donationTotals = computed<DonationStage[]>(() =>
      (this.profile()?.donationTotalsByStage ?? []).map((total) => ({
        stage: total.stage,
        amount: total.totalAmount,

        // `asAtUtc` is the moment the figure is true for; `refreshedAtUtc` is when it was last
        // recomputed. The screen labels it "as of", so the former is the honest one.
        asOf: total.asAtUtc,
      })),
    );

    readonly campaignHistory = computed<CampaignHistoryItem[]>(() =>
      (this.profile()?.campaignHistory ?? []).map((entry) => ({
        id: entry.campaignCode,
        name: entry.campaignName,

        // THE ENTRY RECORDS A CONVERSION, not a gift. It says this donor came from that campaign's
        // lead; the amounts belong to the donation register, and inventing one here is exactly
        // what the three seeded rows did.
        role: 'Converted from lead',
        amount: 0,
        date: entry.convertedAtUtc ?? '',
        status: entry.convertedAtUtc ? 'Converted' : 'In progress',
      })),
    );

    /** Loads the profile for whichever donor the route names. */
    private loadProfile(): void {
      const reference = this.donorId() ?? this.donor().reference;

      if (!reference) {
        this.profile.set(null);
        return;
      }

      this.donorApi.getDonor360(reference).subscribe({
        next: (response) => {
          this.profile.set(response);
          this.profileError.set('');
        },
        error: (error: unknown) => {
          this.profile.set(null);
          this.profileError.set(
            apiErrorMessage(error, 'This donor\'s history could not be loaded.'),
          );
        },
      });
    }
  
    get conversations(): ConversationItem[] {
      const id = this.leadId() ?? this.workflow.leads().find((item) => item.donorId === this.donor().reference)?.id;
      const records = id ? this.workflow.communicationsFor(id) : [];
      if (records.length) {
        return records.map((record) => ({ id: record.id, channel: record.type, summary: record.summary, date: record.date, owner: record.createdBy }));
      }
      // THE SERVER'S CONVERSATIONS, then nothing. This fell back to two invented exchanges -
      // "Thanked donor for Winter Relief pledge" - shown against any donor with no logged
      // communication. A fundraiser reading that would believe somebody had already called.
      return (this.profile()?.conversations ?? []).map((conversation) => ({
        id: conversation.id,
        channel: conversation.channel ?? conversation.interactionType,
        summary: conversation.description ?? conversation.name,
        date: conversation.occurredAtUtc,
        owner: conversation.performedByName ?? '',
      }));
    }

    get followUps(): FollowUpItem[] {
      const id = this.leadId() ?? this.workflow.leads().find((item) => item.donorId === this.donor().reference)?.id;
      const records = id ? this.workflow.followUpsFor(id) : [];
      if (records.length) {
        return records.map((record) => ({ id: record.id, title: record.purpose, due: `${record.scheduledDate} ${record.scheduledTime}`, owner: record.assignedTo, status: record.status }));
      }
      // FROM THE PROFILE, not two invented tasks. These showed "Share Q3 impact report,
      // overdue" against every donor, so a fundraiser opened the page believing they had already
      // missed something.
      return (this.profile()?.followUps ?? []).map((followUp) => ({
        id: followUp.id,
        title: followUp.nextAction ?? followUp.followUpReference,
        due: followUp.dueAtUtc ?? '',
        owner: followUp.relationshipOwnerName ?? 'Unassigned',
        status: followUp.status,
      }));
    }

    /** Pledges the donor has made. From the profile - these were two invented promises. */
    readonly promises = computed<PromiseItem[]>(() =>
      (this.profile()?.promises ?? []).map((promise) => ({
        id: promise.id,
        amount: promise.amount,
        dueDate: promise.dueAtUtc ?? '',
        status: promise.status,
      })),
    );
  
    /**
     * The donor's documents.
     *
     * THESE WERE TWO INVENTED FILES, one of them an "80G receipt" - a tax document. A screen
     * listing a tax receipt that does not exist is one somebody will go looking for.
     */
    readonly documents = computed<DocumentItem[]>(() =>
      (this.profile()?.documents ?? []).map((document) => ({
        id: document.id,
        name: document.name,
        type: document.description ?? document.reference,
        uploadedOn: document.createdAtUtc,
        classification: document.classification,
      })),
    );
  
    /** Possible duplicates of this donor, from the server's own matching. */
    readonly duplicateLinks = computed<DuplicateLink[]>(() =>
      (this.profile()?.duplicateLinks ?? []).map((link) => ({
        id: link.mergeCaseId,
        reference: link.reviewReference,

        // THE SERVER DOES NOT SAY WHY two records matched on this projection - it says how
        // confident it is. Reporting the confidence is honest; inventing "name and phone number"
        // would tell a steward which fields to check, wrongly.
        matchReason: link.decision ?? `Awaiting review (${link.status})`,
        similarity: link.identityConfidence,
      })),
    );
  
    /**
     * The server's activity trail.
     *
     * THE FOUR ENTRIES THIS REPLACED were identical for every donor and named a person who does
     * not exist as having spoken to them - including a "logged phone conversation" that never
     * happened.
     */
    readonly activity = computed<ActivityItem[]>(() =>
      (this.profile()?.activityHistory ?? []).map((entry) => ({
        id: entry.id,
        // The audit row names the ACTION and its outcome rather than a person: who did it is in
        // the audit log proper, which this projection deliberately does not carry.
        actor: entry.targetType,
        action: entry.reason ? `${entry.actionCode} - ${entry.reason}` : entry.actionCode,
        timestamp: entry.occurredAtUtc,
      })),
    );
  
    // ============================================================
    // TABS — progressive disclosure of the main work area
    // ============================================================
  
    readonly tabs: { id: TabId; label: string }[] = [
      { id: 'overview', label: 'Overview' },
      { id: 'donations', label: 'Donations' },
      { id: 'communications', label: 'Communications' },
      { id: 'follow-ups', label: 'Follow-Ups' },
      { id: 'documents', label: 'Documents & duplicates' },
      { id: 'activity', label: 'Activity history' },
      { id: 'consent', label: 'Consent' },
      { id: 'identity-verification', label: 'Identity Verification' },
    ];
    activeTab = signal<TabId>((this.route.snapshot.queryParamMap.get('tab') as TabId | null) ?? 'overview');
  
    // ============================================================
    // WORKFLOW ACTIONS — Correct / Follow up / Create intent / Delete draft
    // ============================================================
  
    activeAction = signal<'correct' | 'follow-up' | 'create-intent' | 'delete-draft' | null>(null);
    successPanel = signal<SuccessPanel | null>(null);
    dependencyNotice = signal(false);
  
    // Correct form state
    correctReason = signal('');

    /**
     * The chosen relationship owner, held as the person's API ID.
     *
     * IT USED TO BE A FREE-TEXT NAME, which could not become an owner however carefully it was
     * typed: the correction endpoint routes work by id, and no amount of "Priya Sharma" resolves
     * to one. The field is now the same directory picker the lead capture and assignment screens
     * use, so the value the form holds is the value the API takes.
     */
    correctOwnerRef = signal<string>('');

    /** The people a donor relationship can be handed to - active staff in the caller's scope. */
    protected readonly ownerOptions = computed(() => this.people.assignable());

    /** The chosen owner's display name, for validation and for the success panel. */
    protected readonly correctOwner = computed(
      () => this.people.get(this.correctOwnerRef())?.name ?? '',
    );
    correctErrors = signal<Record<string, string>>({});
    correctHasErrors = computed(() => Object.keys(this.correctErrors()).length > 0);
  
    // Follow-up form state
    followUpNote = signal('');
    followUpDue = signal('');
    followUpErrors = signal<Record<string, string>>({});
  
    // Delete draft form state
    deleteReason = signal('');
    deleteConfirmText = signal('');
    deleteErrors = signal<Record<string, string>>({});
  
    // Create intent form state
    intentName = signal('');
    intentSource = signal('');
    intentErrors = signal<Record<string, string>>({});
  
    canCorrect = computed(() => this.hasPermission('don.donor-360.correct') && this.effectiveState() === 'loaded');
    canFollowUp = computed(() => this.hasPermission('don.donor-360.follow-up') && ['loaded', 'duplicate'].includes(this.effectiveState()));
    canCreateIntent = computed(() => this.hasPermission('don.donor-360.create-intent') && this.effectiveState() === 'draft');
    canDeleteDraft = computed(() => this.hasPermission('don.donor-360.delete-draft') && this.effectiveState() === 'draft');
  
    dialogTitleId = computed(() => {
      switch (this.activeAction()) {
        case 'correct': return 'correct-title';
        case 'follow-up': return 'followup-title';
        case 'create-intent': return 'intent-title';
        case 'delete-draft': return 'delete-title';
        default: return null;
      }
    });

    // ============================================================
    // PRESENTATION-ONLY DERIVED DATA (added — no existing signal,
    // computed or method above was changed or removed)
    // ============================================================

    /** KPI: total received-to-date, drives the "Lifetime giving" card. */
    lifetimeGiving = computed(() => this.donationTotals().find(d => d.stage === 'Received')?.amount ?? 0);

    /** KPI: number of campaigns this donor has given to. */
    totalDonationsCount = computed(() => this.campaignHistory.length);

    /** KPI: promises fulfilled to date. */
    fulfilledPromisesCount = computed(() => this.promises().filter(p => p.status === 'Fulfilled').length);

    /** KPI: follow-ups currently overdue — surfaced as the "attention" card. */
    overdueFollowUpsCount = computed(() => this.followUps.filter(f => f.status === 'Overdue').length);

    /** Maps a status/label string from any list in this view to a badge tone. */
    badgeClass(value: string): 'green' | 'blue' | 'amber' | 'red' | 'gray' {
      const tones: Record<string, 'green' | 'blue' | 'amber' | 'red' | 'gray'> = {
        Active: 'green', Granted: 'green', Fulfilled: 'green', Reconciled: 'green',
        Scheduled: 'blue', Received: 'blue',
        Draft: 'gray', Closed: 'gray', Low: 'gray',
        Pending: 'amber', Partial: 'amber', Pledged: 'amber', Medium: 'amber',
        Overdue: 'red', Withdrawn: 'red', Restricted: 'red', Inactive: 'red', High: 'red',
      };
      return tones[value] ?? 'gray';
    }
  
    // ============================================================
    // MORE PRESENTATION-ONLY DERIVED DATA (added — no existing signal,
    // computed or method above was changed or removed)
    // ============================================================

    /** Collapsible "STATES" scenario switcher in the preview bar. */
    statesOpen = signal(false);
    toggleStates() {
      this.statesOpen.update((open) => !open);
    }

    /** Free-text filter applied to the active tab's table/list. */
    searchTerm = signal('');
    setSearchTerm(value: string) {
      this.searchTerm.set(value);
    }
    matchesSearch(...fields: (string | number)[]): boolean {
      const q = this.searchTerm().trim().toLowerCase();
      if (!q) return true;
      return fields.some((f) => String(f).toLowerCase().includes(q));
    }

    /** Pledged amount and giving progress, feeding the header KPI bar. */
    pledgedAmount = computed(() => this.donationTotals().find((d) => d.stage === 'Pledged')?.amount ?? 0);
    progressPercent = computed(() => {
      const pledged = this.pledgedAmount();
      return pledged > 0 ? Math.round((this.lifetimeGiving() / pledged) * 100) : 0;
    });

    /** Promises pending, surfaced in the snapshot panel. */
    pendingPromisesCount = computed(() => this.promises().filter((p) => p.status === 'Pending').length);

    /** Cosmetic reload — re-asserts current scenario to recompute derived signals. */
    refreshData() {
      this.scenario.set(this.scenario());
    }

    openAction(action: 'correct' | 'follow-up' | 'create-intent' | 'delete-draft') {
      this.successPanel.set(null);
      this.correctErrors.set({});
      this.followUpErrors.set({});
      this.deleteErrors.set({});
      this.intentErrors.set({});

      // The correction form opens on the donor's CURRENT owner, so saving without touching the
      // field leaves the relationship where it is rather than clearing it.
      if (action === 'correct') {
        const current = this.workflow.getDonor(this.donorId() ?? this.donor().reference);
        this.correctOwnerRef.set(
          current?.ownerUserId ?? this.people.idOf(current?.owner) ?? '',
        );
        this.correctReason.set('');
      }

      this.activeAction.set(action);
    }
  
    closeAction() {
      this.activeAction.set(null);
    }
  
    submitCorrect() {
      const errors: Record<string, string> = {};
      if (!this.correctReason().trim()) errors['reason'] = 'Enter Reason for correction.';
      if (!this.correctOwnerRef().trim()) errors['owner'] = 'Choose a relationship owner.';
      this.correctErrors.set(errors);
      if (Object.keys(errors).length) return;
  
      if (this.effectiveState() === 'conflict') {
        // handled by the conflict banner instead of proceeding
        return;
      }

      if (this.effectiveState() === 'dependency-failure') {
        this.activeAction.set(null);
        this.dependencyNotice.set(true);
        return;
      }
  
      // THE CORRECTION GOES TO THE DONOR, which is the record being corrected. This used to call
      // `patchLead` with an owner - and `patchLead` routes only stage and scoring changes, so an
      // owner in its patch was dropped on the floor. Nothing was sent anywhere, the donor's
      // relationship owner never changed, and the success panel below said it had.
      const donorRecordId = this.donorId() ?? this.donor().reference;

      this.workflow.patchDonor(donorRecordId, {
        owner: this.correctOwner(),
        ownerUserId: this.correctOwnerRef(),
      });

      // The lead behind the donor keeps its own owner in step, so the queue and the donor record
      // do not name two different people for the same relationship.
      if (this.leadId()) {
        this.workflow.patchLead(this.leadId()!, {
          lastActivity: `Donor correction: ${this.correctReason()}`,
        });
      }

      this.activeAction.set(null);
      this.successPanel.set({
        title: 'Correction saved successfully.',
        reference: this.donor().reference,
        state: 'Active — corrected',
        effectiveTime: 'Just now',
        nextAction: 'View updated record',
      });
    }
  
    submitFollowUp() {
      const errors: Record<string, string> = {};
      if (!this.followUpNote().trim()) errors['note'] = 'Enter Follow-up note.';
      if (!this.followUpDue()) errors['due'] = 'Enter Due date.';
      this.followUpErrors.set(errors);
      if (Object.keys(errors).length) return;
  
      const donorRecordId = this.donorId() ?? this.donor().reference;
      const isPersistedDonor = Boolean(this.workflow.getDonor(donorRecordId));
      const recordId = isPersistedDonor ? donorRecordId : (this.leadId() ?? donorRecordId);
      const created = this.workflow.addFollowUp({
        recordId,
        recordType: isPersistedDonor ? 'Donor' : 'Lead',
        recordName: this.donor().fullName,
        assignedTo: this.donor().owner,
        scheduledDate: this.followUpDue(),
        purpose: this.followUpNote(),
        followUpType: 'Call',
      });
      this.activeAction.set(null);
      this.successPanel.set({
        title: 'Follow-up scheduled successfully.',
        reference: created.id,
        state: 'Scheduled',
        effectiveTime: this.followUpDue() || 'Just now',
        nextAction: 'Open follow-up queue',
      });
      this.router.navigate(['/app/fundraising/relationships/follow-up-queue'], { queryParams: { followUpId: created.id, leadId: created.recordType === 'Lead' ? created.recordId : null, donorId: created.recordType === 'Donor' ? created.recordId : null } });
    }
  
    submitDeleteDraft() {
      const errors: Record<string, string> = {};
      if (!this.deleteReason().trim()) errors['reason'] = 'Enter Reason for deletion.';
      if (this.deleteConfirmText().trim().toUpperCase() !== 'DELETE') errors['confirm'] = 'Type DELETE to confirm.';
      this.deleteErrors.set(errors);
      if (Object.keys(errors).length) return;
  
      this.activeAction.set(null);
      this.successPanel.set({
        title: 'Draft deleted successfully.',
        reference: this.donor().reference,
        state: 'Deleted (draft)',
        effectiveTime: 'Just now',
        nextAction: 'Return to lead work queue',
      });
      this.deleteConfirmText.set('');
    }
  
    submitCreateIntent() {
      const errors: Record<string, string> = {};
      if (!this.intentName().trim()) errors['name'] = 'Enter Full name.';
      if (!this.intentSource().trim()) errors['source'] = 'Enter Source.';
      this.intentErrors.set(errors);
      if (Object.keys(errors).length) return;
  
      this.activeAction.set(null);
      this.successPanel.set({
        title: 'Draft saved successfully.',
        reference: 'DON-2026-DRAFT-' + Math.floor(100 + Math.random() * 899),
        state: 'Draft',
        effectiveTime: 'Just now',
        nextAction: 'Continue remaining required information',
      });
    }
  
    openIdentityVerification() {
      this.router.navigate(['/app/don/donor-identity-verification'], { queryParams: { donorId: this.donor().reference, leadId: this.leadId() } });
    }

    openConsentPreferences() {
      this.router.navigate(['/app/fundraising/relationships/consent-and-preference-centre'], { queryParams: { donorId: this.donor().reference, leadId: this.leadId() } });
    }

    openFollowUpPlanner() {
      const donorId = this.donorId() ?? this.donor().reference;
      const isPersistedDonor = Boolean(this.workflow.getDonor(donorId));
      const leadId = isPersistedDonor ? null : (this.leadId() ?? this.workflow.leads().find((item) => item.donorId === donorId)?.id ?? null);
      this.router.navigate(['/app/don/follow-up-planner'], { queryParams: { leadId, donorId: isPersistedDonor ? donorId : null, mode: 'create' } });
    }

    executeFollowUp(followUpId: string) {
      const followUp = this.workflow.getFollowUp(followUpId);
      if (!followUp || ['Completed', 'Cancelled'].includes(followUp.status)) return;
      this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
        queryParams: { followUpId, leadId: followUp.recordType === 'Lead' ? followUp.recordId : null, donorId: followUp.recordType === 'Donor' ? followUp.recordId : null },
      });
    }

    openCommunicationHistory() {
      const leadId = this.leadId() ?? this.workflow.leads().find((item) => item.donorId === this.donor().reference)?.id;
      this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: { leadId, donorId: this.donor().reference } });
    }

    dismissSuccess() {
      this.successPanel.set(null);
      this.dependencyNotice.set(false);
    }
  
    // ============================================================
    // Conflict handling
    // ============================================================
    reviewConflict() {
      this.setScenario('loaded');
    }
  
    // ============================================================
    // Formatting helpers (kept local — imports array left untouched)
    // ============================================================
  
    formatINR(amount: number): string {
      return '₹' + amount.toLocaleString('en-IN');
    }
  
    maskedEmail(): string {
      return '•••••••@•••••.com';
    }
  
    maskedPhone(): string {
      return '+91 ••••• •••33';
    }
  
    /** Identity and contact summary is masked unless the separate field
     *  permission (don.contact.view) is present — spec §4.3.2. */
    maskedFullName(): string {
      const parts = this.donor().fullName.trim().split(/\s+/);
      if (parts.length === 0) return '••••••';
      if (parts.length === 1) return parts[0].charAt(0) + '•••••';
      return parts[0].charAt(0) + '••••• ' + parts[parts.length - 1].charAt(0) + '•••••';
    }
  
    consentIcon(): string {
      switch (this.donor().consentStatus) {
        case 'Granted': return '✓';
        case 'Partial': return '!';
        default: return '✕';
      }
    }

    /** Best-effort clipboard copy for reference values shown in the side panel. */
    async copyValue(text: string): Promise<void> {
      try {
        await navigator.clipboard.writeText(text);
      } catch {
        // Clipboard API unavailable/denied — silently ignored, non-critical affordance.
      }
    }
  }
  
  type Scenario =
    | 'loaded'
    | 'loading'
    | 'empty'
    | 'draft'
    | 'duplicate'
    | 'conflict'
    | 'dependency-failure'
    | 'no-access';
  
  type TabId = 'overview' | 'donations' | 'communications' | 'follow-ups' | 'documents' | 'activity' | 'consent' | 'identity-verification';
  
  interface SuccessPanel {
    title: string;
    reference: string;
    state: string;
    effectiveTime: string;
    nextAction: string;
  }
  
  interface DonationStage { stage: string; amount: number; asOf: string; }
  interface CampaignHistoryItem { id: string; name: string; role: string; amount: number; date: string; status: string; }
  interface ConversationItem { id: string; channel: string; summary: string; date: string; owner: string; }
  interface FollowUpItem { id: string; title: string; due: string; owner: string; status: string; }
  interface PromiseItem { id: string; amount: number; dueDate: string; status: string; }
  interface DocumentItem { id: string; name: string; type: string; uploadedOn: string; classification: string; }
  interface DuplicateLink { id: string; reference: string; matchReason: string; similarity: string; }
  interface ActivityItem { id: string; actor: string; action: string; timestamp: string; }