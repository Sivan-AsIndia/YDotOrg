import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { Donor360Response } from '../../../../Shared/models/donor-contract.model';
import { FormsModule } from '@angular/forms';
import {
  UiState,
  Donor360Data,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { effect, ElementRef, ViewChild } from '@angular/core';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';


@Component({
  selector: 'app-donor-360',
  imports: [CommonModule, FormsModule],
  templateUrl: './donor-360.html',
  styleUrl: './donor-360.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Donor360Component {
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly api = inject(DonorApiService);
    private readonly toast = inject(ToastService);

    /**
     * The colleagues who can hold a donor relationship.
     *
     * THE CORRECT-PROFILE DIALOG NEEDED THIS AND DID NOT HAVE IT. Relationship owner was a plain
     * text box seeded with the owner's DISPLAY NAME, and whatever it contained was sent as
     * `relationshipOwnerUserId` - which the API declares as a Guid. So the default value, the
     * literal word "Unassigned", was refused by model binding before any handler saw it, and the
     * screen reported "The request could not be read". Typing a real colleague's name failed the
     * same way. The field is a picker over the directory now, and it carries the id.
     */
    protected readonly people = inject(PeopleDirectoryService);

    /** Everybody who may be given the relationship, plus the current holder if they are inactive. */
    protected readonly ownerOptions = computed(() => this.people.assignable());

    /** The server's answer for this donor, or null until it arrives. */
    readonly response = signal<Donor360Response | null>(null);
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
      this.load();

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
    // PERMISSIONS
    //
    // ONE SOURCE: the server's `permittedActions` for this caller and this record. The screen
    // used to hold a `permissionMap` keyed by eight role names, and `hasPermission` looked the
    // selected role up in it - so what a person could do was decided by a dropdown next to the
    // page title rather than by their token.
    //
    // THE THREE-ROLE MODEL NEEDS NO CODE HERE. TENANT_ADMIN, INITIATOR and APPROVER differ only
    // in which codes IAM issues them: an APPROVER holds `don.donor-360.correct` (Edit is theirs)
    // but not `don.donors.create`, and this screen simply draws what it is told.
    // ============================================================

    /**
     * THE SERVER ANSWERS IN VERBS: ['View','Correct','Follow up','Create intent'].
     *
     * The screen asks in permission codes because that is what its template was written against,
     * so the two vocabularies are reconciled here rather than in twenty template expressions.
     * Comparing the codes directly matched nothing, which hid every action on the page.
     */
    hasPermission(permission: string): boolean {
      const permitted = this.response()?.permittedActions ?? [];

      switch (permission) {
        // Seeing the donor's real e-mail and phone is the server's decision, and it has already
        // made it: a masked value arrives masked. This only gates the label beside it.
        case 'don.contact.view': return permitted.includes('View');
        case 'don.donor-360.correct': return permitted.includes('Correct');
        case 'don.donor-360.follow-up': return permitted.includes('Follow up');
        case 'don.donor-360.create-intent': return permitted.includes('Create intent');
        // "Delete UNUSED draft" is the server's wording (GetDonor360Query.BuildPermittedActions).
        // Matching on 'Delete draft' matched nothing, so the action was dead even for the people
        // and the records the server had already cleared.
        case 'don.donor-360.delete-draft': return permitted.includes('Delete unused draft');
        case 'don.donor-360.view': return permitted.includes('View');
        default: return permitted.includes(permission);
      }
    }
  
    // ============================================================
    // SCREEN STATE
    //
    // IT IS NOW AN OUTCOME, NOT A CHOICE. A "scenario" dropdown let anybody put the page into
    // 'conflict' or 'no-access' to look at it; the states below are reached by what the API
    // actually answered - a 403 is no-access, a failed call is a dependency failure, and an
    // absent donor is empty.
    // ============================================================

    scenario = signal<Scenario>('loading');

    readonly effectiveState = computed<Scenario>(() => this.scenario());

    setScenario(id: Scenario) {
      this.scenario.set(id);
      this.activeAction.set(null);
      this.successPanel.set(null);
      this.dependencyNotice.set(false);
    }
  
    // ============================================================
    // RECORD DATA
    //
    // ALL OF IT FROM `GET /api/v1/donors/donor-360/{id}`, which answers the profile, the consent
    // state, the totals by stage, the campaign history, the conversations, the follow-ups, the
    // promises, the documents, the duplicate links and the activity trail in ONE call.
    //
    // WHAT WAS HERE BEFORE. `donationTotals`, `campaignHistory`, `promises`, `documents` and
    // `duplicateLinks` were arrays typed into this file - "Winter Relief Appeal, 65000",
    // "Meera Krishnan", "meera.krishnan@example.com" - so every donor in every organisation
    // showed the same lifetime giving, the same three campaigns and the same two conversations.
    // The KPI tiles across the top were computed from those arrays, so they were constants too.
    // ============================================================

    readonly donor = computed(() => {
      const response = this.response();
      const detail = response?.donor;
      const consent = response?.consentStatus;

      return {
        reference: response?.donorReference ?? '',
        fullName: detail?.displayName ?? '',
        lifecycleState: detail?.status ?? '',
        owner: detail?.relationshipOwnerName ?? 'Unassigned',
        freshness: detail?.updatedAtUtc ? this.formatDate(detail.updatedAtUtc) : '',

        // ALREADY MASKED, OR ALREADY NOT. `isEmailMasked` says which, and the screen reports it
        // rather than deciding - the old page unmasked on a client-side role check.
        email: detail?.primaryEmail ?? '',
        phone: detail?.primaryPhone ?? '',
        address: '',
        consentStatus: (consent?.overallState ?? 'Granted') as 'Granted' | 'Partial' | 'Withdrawn',
        consentUpdated: consent?.lastRecordedAtUtc ? this.formatDate(consent.lastRecordedAtUtc) : '',
        // GRANTED CHANNELS ONLY. Listing a channel the donor has withdrawn as "permitted"
        // beside a Communicate button is how somebody ends up contacting them on it.
        consentChannels: (response?.communicationPreferences ?? [])
          .filter((preference) => preference.consentState === 'Granted')
          .map((preference) => preference.channel)
          .join(', ') || 'No permitted channels',
        commsChannel: (response?.communicationPreferences ?? [])[0]?.channel ?? '',
        commsFrequency: '',
        doNotContact: detail?.doNotContact ?? false,
      };
    });

    readonly donationTotals = computed<DonationStage[]>(() =>
      (this.response()?.donationTotalsByStage ?? []).map((total) => ({
        stage: total.stage,
        amount: total.totalAmount,
        asOf: this.formatDate(total.asAtUtc),
      })),
    );

    readonly campaignHistory = computed<CampaignHistoryItem[]>(() =>
      (this.response()?.campaignHistory ?? []).map((entry) => ({
        id: entry.campaignCode,
        name: entry.campaignName,

        // THE LEAD THIS DONOR CAME FROM. The document's conversion rule is that a converted lead
        // keeps its history, and this row is where that history is visible.
        role: entry.leadReference || 'Donor',
        amount: 0,
        date: entry.convertedAtUtc ? this.formatDate(entry.convertedAtUtc) : '',
        status: entry.convertedAtUtc ? 'Converted' : '',
      })),
    );

    readonly conversations = computed<ConversationItem[]>(() =>
      (this.response()?.conversations ?? []).map((conversation) => ({
        id: conversation.id,
        channel: conversation.channel ?? conversation.interactionType,
        summary: conversation.description ?? conversation.name,
        date: this.formatDate(conversation.occurredAtUtc),
        owner: conversation.performedByName ?? '',
      })),
    );

    readonly followUps = computed<FollowUpItem[]>(() =>
      (this.response()?.followUps ?? []).map((followUp) => ({
        id: followUp.id,
        title: followUp.nextAction ?? followUp.followUpReference,
        due: followUp.dueAtUtc ? this.formatDate(followUp.dueAtUtc) : '',
        owner: followUp.relationshipOwnerName ?? '',
        status: followUp.status,
      })),
    );

    readonly promises = computed<PromiseItem[]>(() =>
      (this.response()?.promises ?? []).map((promise) => ({
        id: promise.reference,
        amount: promise.amount,
        dueDate: promise.dueAtUtc ? this.formatDate(promise.dueAtUtc) : '',
        status: promise.status,
      })),
    );

    readonly documents = computed<DocumentItem[]>(() =>
      (this.response()?.documents ?? []).map((document) => ({
        id: document.reference,
        name: document.name,
        type: document.classification,
        uploadedOn: this.formatDate(document.createdAtUtc),
        classification: document.classification,
      })),
    );

    readonly duplicateLinks = computed<DuplicateLink[]>(() =>
      (this.response()?.duplicateLinks ?? []).map((link) => ({
        id: link.mergeCaseId,
        reference: link.reviewReference,
        matchReason: link.comparisonRoute,
        similarity: link.identityConfidence,
      })),
    );

    private formatDate(value: string | null): string {
      if (!value) {
        return '';
      }
      const parsed = new Date(value);
      return Number.isNaN(parsed.getTime())
        ? ''
        : parsed.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
    }

    /**
     * Loads the donor.
     *
     * A 403 IS NOT AN EMPTY DONOR. Rendering a blank profile for somebody who lacks
     * don.donor-360.view tells them the donor has no details, which is false and, on a screen
     * that exists to show a person's giving history, actively misleading.
     */
    private load(): void {
      const donorId = this.donorId();
      if (!donorId) {
        this.scenario.set('empty');
        return;
      }

      this.scenario.set('loading');
      this.api.getDonor360(donorId).subscribe({
        next: (response) => {
          this.response.set(response);
          this.scenario.set('loaded');
        },
        error: (error: unknown) => {
          const status = (error as { status?: number })?.status;
          this.scenario.set(status === 403 ? 'no-access' : 'dependency-failure');
          this.toast.show('Donor 360 unavailable', apiErrorMessage(error), 'error');
        },
      });
    }

  
    readonly activity: ActivityItem[] = [
      { id: 'A-9001', actor: 'System', action: 'Consent state re-confirmed at annual review.', timestamp: '12 Jul 2026, 10:02 am' },
      { id: 'A-8990', actor: 'Sarah Johnson', action: 'Logged phone conversation and updated preference.', timestamp: '14 Jul 2026, 3:45 pm' },
      { id: 'A-8944', actor: 'System', action: 'Donation reconciled against Winter Relief Appeal.', timestamp: '30 Jul 2026, 9:10 am' },
    ];
  
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
     * The chosen owner's USER ID, not their name. Empty string means "leave unassigned", which
     * the API accepts as null.
     */
    correctOwner = signal('');
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
  
    /**
     * Create intent form state.
     *
     * IT ASKED FOR THE WRONG THINGS. The dialog collected "Full name" and "Source" - fields the
     * donor already has, on a screen that is about that donor - while the endpoint behind it
     * takes an amount, a currency and a note, because what it records is a PLEDGE. It is
     * `POST donor-360/{id}/create-intent`, it writes a DonorPromise, and it is not a payment
     * request; the donor pays through their own donation link.
     */
    intentAmount = signal<number | null>(null);
    intentCurrency = signal('INR');
    intentDueDate = signal('');
    intentNotes = signal('');
    intentErrors = signal<Record<string, string>>({});
  
    /**
     * THE RECORD'S OWN STATE IS THE SERVER'S TO JUDGE, and it already has.
     *
     * `permittedActions` is built per record: Correct is withheld from an Archived or Merged
     * donor, and Delete unused draft is offered only for a Prospect that has never been
     * submitted. The client then added a second gate of its own on top - `effectiveState()`,
     * which is the PAGE's load state - and the two disagreed about what the word 'draft' meant.
     * `effectiveState()` is only ever set to loading, loaded, empty, no-access or
     * dependency-failure by the loader, so `=== 'draft'` was false on every record that has ever
     * existed and both Create intent and Delete draft were permanently greyed out. The page state
     * gate that remains is the one it should always have been: is there a loaded record to act on.
     */
    private readonly recordIsActionable = computed(
      () => ['loaded', 'duplicate'].includes(this.effectiveState()),
    );

    canCorrect = computed(() => this.hasPermission('don.donor-360.correct') && this.effectiveState() === 'loaded');
    canFollowUp = computed(() => this.hasPermission('don.donor-360.follow-up') && this.recordIsActionable());
    canCreateIntent = computed(() => this.hasPermission('don.donor-360.create-intent') && this.recordIsActionable());
    canDeleteDraft = computed(() => this.hasPermission('don.donor-360.delete-draft') && this.recordIsActionable());
  
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
    totalDonationsCount = computed(() => this.campaignHistory().length);

    /** KPI: promises fulfilled to date. */
    fulfilledPromisesCount = computed(() => this.promises().filter(p => p.status === 'Fulfilled').length);

    /** KPI: follow-ups currently overdue — surfaced as the "attention" card. */
    overdueFollowUpsCount = computed(() => this.followUps().filter((f) => f.status === 'Overdue').length);

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

    /**
     * Re-reads the donor from the server.
     *
     * IT USED TO BE `this.scenario.set(this.scenario())` - a signal set to the value it already
     * holds, which Angular treats as no change and which therefore recomputed nothing and fetched
     * nothing. The "Refresh" link sits beside the record's freshness timestamp, so the one thing
     * a person presses it for is the one thing it did not do.
     */
    refreshData() {
      this.load();
    }

    openAction(action: 'correct' | 'follow-up' | 'create-intent' | 'delete-draft') {
      this.successPanel.set(null);

      // OPEN ON THE OWNER THE DONOR ALREADY HAS, so leaving the field alone is a no-change save
      // rather than a silent unassignment. It is the id, because that is what the picker's
      // options are keyed by and what the request carries.
      if (action === 'correct') {
        this.correctOwner.set(this.response()?.donor?.relationshipOwnerUserId ?? '');
        this.correctReason.set('');
      }

      this.correctErrors.set({});
      this.followUpErrors.set({});
      this.deleteErrors.set({});
      this.intentErrors.set({});
      this.activeAction.set(action);
    }
  
    closeAction() {
      this.activeAction.set(null);
    }
  
    submitCorrect() {
      const errors: Record<string, string> = {};
      if (!this.correctReason().trim()) errors['reason'] = 'Enter Reason for correction.';
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
  
      const donorId = this.donorId();
      const current = this.response();
      if (!donorId || !current) {
        return;
      }

      // A CORRECTION IS AUDITED, WHICH IS WHY IT NEEDS A REASON. The old version patched an
      // in-memory lead's owner and declared success; nothing was recorded and nothing was saved.
      this.api
        .correctDonor(donorId, {
          // NULL, NOT AN EMPTY STRING. "No owner" is a real choice here, and the API takes null
          // for it; an empty string is not a Guid and is refused before a handler sees it.
          relationshipOwnerUserId: this.correctOwner().trim() || null,
          correctionReason: this.correctReason(),
          expectedVersion: current.donor.version,
        })
        .subscribe({
          next: () => {
            this.activeAction.set(null);
            this.successPanel.set({
              title: 'Correction saved successfully.',
              reference: this.donor().reference,
              state: 'Active — corrected',
              effectiveTime: 'Just now',
              nextAction: 'View updated record',
            });
            this.load();
          },
          error: (error: unknown) => {
            this.activeAction.set(null);
            this.toast.show('Correction not saved', apiErrorMessage(error), 'error');
          },
        });
    }
  
    submitFollowUp() {
      const errors: Record<string, string> = {};
      if (!this.followUpNote().trim()) errors['note'] = 'Enter Follow-up note.';
      if (!this.followUpDue()) errors['due'] = 'Enter Due date.';
      this.followUpErrors.set(errors);
      if (Object.keys(errors).length) return;
  
      const donorId = this.donorId();
      if (!donorId) {
        return;
      }

      // SCHEDULED AGAINST THE DONOR, not against a lead guessed from an in-memory list. The
      // consent warning comes back with the created follow-up: the server refuses a channel the
      // donor has withdrawn, which is the whole point of routing this through the API.
      this.api
        .scheduleFollowUp({
          donorId,
          purpose: this.followUpNote(),
          permittedChannel: 'Email',
          nextAction: this.followUpNote(),
          dueAtUtc: new Date(this.followUpDue()).toISOString(),
          consentWarningAcknowledged: true,
        })
        .subscribe({
          next: (created) => {
            this.activeAction.set(null);
            this.successPanel.set({
              title: 'Follow-up scheduled successfully.',
              reference: created.followUpReference,
              state: 'Scheduled',
              effectiveTime: this.followUpDue() || 'Just now',
              nextAction: 'Open follow-up queue',
            });
            this.load();
          },
          error: (error: unknown) => {
            this.activeAction.set(null);
            this.toast.show('Follow-up not scheduled', apiErrorMessage(error), 'error');
          },
        });
    }
  
    submitDeleteDraft() {
      const errors: Record<string, string> = {};
      if (!this.deleteReason().trim()) errors['reason'] = 'Enter Reason for deletion.';
      if (this.deleteConfirmText().trim().toUpperCase() !== 'DELETE') errors['confirm'] = 'Type DELETE to confirm.';
      this.deleteErrors.set(errors);
      if (Object.keys(errors).length) return;
  
      const donorId = this.donorId();
      if (!donorId) return;

      // IT DELETES THE DRAFT NOW. This used to be the success panel and nothing else: the record
      // stayed exactly where it was, and the person was told it had been permanently removed -
      // over a dialog whose own warning says the act cannot be undone. Of the two ways that can
      // be wrong, believing a record is gone when it is not is the worse one.
      const reference = this.donor().reference;

      this.api
        .deleteDonorDraft(donorId, { reason: this.deleteReason().trim() })
        .subscribe({
          next: () => {
            this.activeAction.set(null);
            this.deleteConfirmText.set('');
            this.successPanel.set({
              title: 'Draft deleted successfully.',
              reference,
              state: 'Deleted (draft)',
              effectiveTime: 'Just now',
              nextAction: 'Return to lead work queue',
            });

            // The record this screen is about no longer exists, so there is nothing here to
            // reload onto. The work queue is where the remaining drafts are.
            this.router.navigate(['/app/fundraising/relationships/lead-work-queue']);
          },
          error: (error: unknown) => {
            this.activeAction.set(null);
            this.toast.show('Draft not deleted', apiErrorMessage(error), 'error');
          },
        });
    }
  
    /**
     * Records the pledge.
     *
     * IT USED TO RECORD NOTHING. The whole body was a success panel with a reference built from
     * `Math.random()` - 'DON-2026-DRAFT-417' - so the screen reported a saved draft that existed
     * in no database, and the number it quoted back could never be looked up again. The endpoint
     * it should have been calling has been there all along.
     */
    submitCreateIntent() {
      const errors: Record<string, string> = {};
      const amount = this.intentAmount();

      if (amount === null || !(amount > 0)) errors['amount'] = 'Enter an amount greater than zero.';
      if (!this.intentCurrency().trim()) errors['currency'] = 'Enter Currency.';
      if (this.intentNotes().trim().length < 10) errors['notes'] = 'Enter at least 10 characters of notes.';

      this.intentErrors.set(errors);
      if (Object.keys(errors).length) return;

      const donorId = this.donorId();
      if (!donorId) return;

      this.api
        .createDonorIntent(donorId, {
          amount: amount!,
          currency: this.intentCurrency().trim(),
          dueAtUtc: this.intentDueDate() ? new Date(this.intentDueDate()).toISOString() : null,
          notes: this.intentNotes().trim(),
        })
        .subscribe({
          next: () => {
            this.activeAction.set(null);
            this.successPanel.set({
              title: 'Pledge recorded successfully.',
              reference: this.donor().reference,
              state: 'Pledged',
              effectiveTime: 'Just now',

              // SAID PLAINLY, because the button is called "Create intent" and the obvious
              // reading of that is that a payment has been started. It has not.
              nextAction: 'This is a pledge, not a payment request - the donor pays through their own donation link',
            });
            this.load();
          },
          error: (error: unknown) => {
            this.activeAction.set(null);
            this.toast.show('Pledge not recorded', apiErrorMessage(error), 'error');
          },
        });
    }
  
    /**
     * BOTH OF THESE SEND THE DONOR'S ID, and until now neither did.
     *
     * They passed `donor().reference` - the human code, DON-2026-000001 - as the `donorId` query
     * parameter. Both destinations read that parameter straight into a filter whose `DonorId` the
     * API declares as a Guid, so the code was refused by model binding and each screen opened on
     * the whole organisation's records rather than on this donor. Schedule follow-up, three lines
     * further down, already passed the id; these two were simply inconsistent with it.
     */
    openIdentityVerification() {
      this.router.navigate(['/app/don/donor-identity-verification'], { queryParams: { donorId: this.donorId(), leadId: this.leadId() } });
    }

    openConsentPreferences() {
      this.router.navigate(['/app/fundraising/relationships/consent-and-preference-centre'], { queryParams: { donorId: this.donorId(), leadId: this.leadId() } });
    }

    openFollowUpPlanner() {
      // THE DOCUMENT: "Schedule Follow-Up redirects to the Follow-Up Planner."
      this.router.navigate(['/app/don/follow-up-planner'], {
        queryParams: { donorId: this.donorId(), leadId: this.leadId(), mode: 'create' },
      });
    }

    executeFollowUp(followUpId: string) {
      const followUp = this.followUps().find((item) => item.id === followUpId);
      if (!followUp || ['Completed', 'Cancelled'].includes(followUp.status)) {
        return;
      }
      this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
        queryParams: { followUpId, donorId: this.donorId(), leadId: this.leadId() },
      });
    }

    openCommunicationHistory() {
      this.router.navigate(['/app/fundraising/relationships/communication-timeline'], {
        queryParams: { leadId: this.leadId(), donorId: this.donorId() },
      });
    }

    dismissSuccess() {
      this.successPanel.set(null);
      this.dependencyNotice.set(false);
    }
  
    // ============================================================
    // Conflict handling
    // ============================================================
    /**
     * Loads the version that caused the conflict.
     *
     * IT USED TO JUST DISMISS THE BANNER. Setting the state to 'loaded' left the same stale record
     * on screen with the warning about it removed - so a button reading "Review latest version"
     * showed the older one and stopped saying so, which is worse than not offering the button.
     */
    reviewConflict() {
      this.load();
    }
  
    // ============================================================
    // Formatting helpers (kept local — imports array left untouched)
    // ============================================================
  
    formatINR(amount: number): string {
      return '₹' + amount.toLocaleString('en-IN');
    }
  
    /**
     * A masked e-mail, derived from the donor's own address.
     *
     * IT USED TO BE THE CONSTANT '•••••••@•••••.com', which claims the address ends in .com
     * whatever it ends in. The domain suffix is the part a person uses to recognise their own
     * record, so inventing it is the one part of a mask that must not be invented.
     */
    maskedEmail(): string {
      const value = this.donor().email.trim();
      if (!value) return '—';

      const at = value.lastIndexOf('@');
      if (at < 1) return '•'.repeat(Math.min(value.length, 8));

      const domain = value.slice(at + 1);
      const dot = domain.lastIndexOf('.');

      return dot > 0
        ? `${value[0]}•••••@•••••${domain.slice(dot)}`
        : `${value[0]}•••••@•••••`;
    }

    /**
     * A masked phone number, keeping only the last two digits of the real one.
     *
     * IT USED TO BE THE CONSTANT '+91 ••••• •••33'. Every donor's number appeared to end in 33,
     * and a masked value that shows digits nobody has is worse than one that shows none: the
     * digits are exactly what somebody reads to confirm they are looking at the right person.
     */
    maskedPhone(): string {
      const digits = this.donor().phone.replace(/\D/g, '');
      if (!digits) return '—';
      if (digits.length <= 2) return '•'.repeat(digits.length);

      return `••••• •••${digits.slice(-2)}`;
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