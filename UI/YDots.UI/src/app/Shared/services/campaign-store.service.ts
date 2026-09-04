import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import { CampaignRole } from '../../Service/current-user.service';
import {
  CampaignDetail,
  CampaignLifecycleRequest,
  CampaignListItem,
  CampaignStatus as ApiCampaignStatus,
} from '../models/campaign-contract.model';
import { CampaignRecord, CampaignStatus } from '../models/campaign.model';
import { NotificationService } from '../../Service/notification.service';
import { apiErrorMessage, apiFieldErrors } from '../models/api-response.model';

/**
 * What a lifecycle transition actually did, reported back to the screen that asked for it.
 *
 * WHY A CALLBACK AND NOT A RETURN. Every screen in this module drives lifecycle through the
 * store's synchronous signal surface, and the transitions themselves are HTTP - so a screen had
 * no way at all to learn whether the change it just announced had been accepted. The Pause /
 * Resume / Close panel showed a green "Saved successfully. state Active" on a fixed 700 ms
 * timer, whatever the server said, and it said it whether or not a request had even been sent.
 *
 * `applied: false` carries the server's own message wherever there was one.
 */
export type LifecycleOutcome = (result: { readonly applied: boolean; readonly error?: string }) => void;

/**
 * A save failure, said in a way the person can act on.
 *
 * THE ENVELOPE'S TOP LINE IS NOT ENOUGH BY ITSELF. A rejected write answers "Some of the
 * details are not valid." - accurate, and of no use at all: it names no field, and on a
 * four-step wizard the field it means is usually on a step that is no longer on screen. The
 * server has always sent the per-field messages beside that sentence; this is where they were
 * being thrown away.
 *
 * THE FIELD NAMES ARRIVE CAMEL-CASED, matching the JSON that was sent, so "publicDescription"
 * is turned back into "Public description" rather than shown as the wire name.
 */
function fieldLabel(field: string): string {
  const spaced = field.replace(/([A-Z])/g, ' $1').trim().toLowerCase();

  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function saveFailureMessage(error: unknown, fallback: string): string {
  const message = apiErrorMessage(error, fallback);
  const fields = Object.entries(apiFieldErrors(error));

  if (fields.length === 0) {
    return message;
  }

  return `${message} ${fields.map(([field, text]) => `${fieldLabel(field)} — ${text}`).join(' ')}`;
}

/**
 * The single shared source of truth for campaign data.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. The records used to live in a signal seeded with ten
 * hard-coded campaigns compiled into the bundle. Every screen read and mutated that array:
 * approving flipped a string, deleting spliced, creating pushed. Four consequences followed and
 * all four were real:
 *
 *   - NOTHING WAS EVER SAVED. A campaign approved on Monday was Draft again on Tuesday.
 *   - EVERY ORGANISATION SAW THE SAME TEN CAMPAIGNS, because an array in a browser has no idea
 *     who is asking. Tenant isolation stopped at the API boundary.
 *   - THE APPROVAL RULES WERE DECORATIVE. `submitForApproval` decided the tier from a role string
 *     the browser supplied, so anybody could have approved anything by editing it.
 *   - Segregation of duties could not work at all: the rule is "not the person who submitted",
 *     and the browser cannot be trusted to say who that was.
 *
 * IT NOW READS AND WRITES `CAM /api/v1/campaigns`, while keeping its SYNCHRONOUS SIGNAL SURFACE.
 * That shape is deliberate: eleven screens call `all()`, `get(ref)` and the lifecycle methods
 * from templates and computed properties, and turning those into observables would mean
 * rewriting every one of them. Reads stay synchronous against the loaded signal; writes go to
 * the API and refresh it.
 *
 * THE SIGNAL IS A CACHE OF THE SERVER'S ANSWER, not the source of truth. `refresh()` reloads it,
 * and every write calls that on completion - so a screen never shows a state the server did not
 * agree to.
 */
@Injectable({ providedIn: 'root' })
export class CampaignStoreService {
  private readonly notifications = inject(NotificationService);
  private readonly api = inject(CampaignApiService);
  private readonly organisationScope = inject(OrganisationScopeService);

  /**
   * The loaded page of campaigns.
   *
   * IT STARTS EMPTY rather than seeded. A screen that opens before the first response shows its
   * own empty state for a moment, which is honest; showing ten fabricated campaigns was not.
   */
  private readonly records = signal<readonly CampaignRecord[]>([]);

  /** True while the first load is in flight, so a screen can tell "loading" from "none". */
  readonly isLoading = signal(false);

  /** The last load failure, if any. Screens surface it rather than rendering a silent blank. */
  readonly loadError = signal<string | null>(null);

  readonly all = computed(() => this.records());

  /** The server's total, which is not the same as the loaded page's length. */
  private readonly serverTotal = signal(0);

  readonly total = computed(() => this.serverTotal());

  get(ref: string): CampaignRecord | undefined {
    return this.records().find((record) => record.code === ref);
  }

  /**
   * The API id behind a reference.
   *
   * KEPT SEPARATELY because the two address different things: the code is what a person quotes
   * and what appears in a tracking URL, while the id is what every endpoint takes. The screens
   * work in codes, so this map is what lets them keep doing that.
   */
  private readonly idsByCode = new Map<string, string>();

  /** The concurrency stamp per code, sent back on the next write. */
  private readonly versionsByCode = new Map<string, number>();

  /**
   * The API id behind a campaign code, for the stores that hang off a campaign.
   *
   * READINESS, TRACKING AND BUDGET ALL ADDRESS A CAMPAIGN BY ID while their screens work in
   * codes, and this is the one place the two are already reconciled. Exposing it beats each of
   * those stores keeping its own copy of the same map, which is how two of them ended up
   * disagreeing about which campaign 'CAMP-2025-0011' was.
   *
   * `undefined` means the campaign is not in the loaded working set - which a caller must handle
   * rather than treat as an id, because a request built on a missing id addresses nothing.
   */
  apiId(ref: string): string | undefined {
    return this.idsByCode.get(ref);
  }

  /** The concurrency stamp a write against this campaign should carry. */
  expectedVersion(ref: string): number {
    return this.versionsByCode.get(ref) ?? 0;
  }

  constructor() {
    this.refresh();

    this.organisationScope.onOrganisationChange(() => this.reloadForOrganisation());

    // A Scheduled, auto-activate campaign becomes Active when its start date arrives.
    //
    // IT NOW ASKS THE SERVER rather than flipping a local string. The previous version mutated
    // the array every minute, so a campaign "activated" itself in one browser tab and nowhere
    // else. The sweep still runs on a timer because a campaign whose date passes while somebody
    // has the register open should appear active without a manual refresh.
    setInterval(() => this.refresh(), 60_000);
  }

  /**
   * Everything here belongs to ONE Organisation, so a switch discards it and reloads.
   *
   * Discarded FIRST: reloading alone would leave the previous Organisation's rows readable on
   * screen for the length of a round trip. See `OrganisationScopeService`.
   */
  private reloadForOrganisation(): void {
    this.records.set([]);
    this.serverTotal.set(0);
    this.loadError.set(null);
    this.idsByCode.clear();
    this.versionsByCode.clear();
    this.refresh();
  }

  /**
   * Reloads from the API.
   *
   * A LARGE PAGE, deliberately. The screens page and filter in memory over whatever they are
   * given, so this is the working set rather than a page size - and 200 campaigns is more than
   * any organisation has open at once while still being a bounded request.
   */
  refresh(onLoaded?: () => void): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.searchCampaigns({ pageSize: 200 }).subscribe({
      next: (page) => {
        this.idsByCode.clear();
        this.versionsByCode.clear();

        for (const item of page.items) {
          this.idsByCode.set(item.code, item.id);
          this.versionsByCode.set(item.code, item.version);
        }

        this.records.set(page.items.map((item) => this.toRecord(item)));
        this.serverTotal.set(page.totalCount);
        this.isLoading.set(false);
        onLoaded?.();
      },
      error: (error: unknown) => {
        this.records.set([]);
        this.serverTotal.set(0);
        this.isLoading.set(false);
        this.loadError.set(apiErrorMessage(error, 'The campaign register could not be loaded.'));
      },
    });
  }

  /**
   * Creates a Draft and returns the reference.
   *
   * STILL RETURNS SYNCHRONOUSLY, because the wizard navigates on the returned code. The record
   * is added to the signal optimistically so that navigation lands on something, and `refresh()`
   * replaces it with the server's version - including the id and version the next write needs.
   *
   * A FAILED CREATE REMOVES THE OPTIMISTIC ROW again rather than leaving a campaign on screen
   * that does not exist. The wizard reports the error from the notification service.
   */
  create(
    draft: Partial<CampaignRecord> & { readonly name: string; readonly createdByRef: string },
    onSaved?: (outcome: { readonly saved: boolean; readonly error?: string }) => void,
  ): string {
    const code = draft.code || this.nextReference();
    const today = this.today();

    const optimistic: CampaignRecord = {
      code,
      name: draft.name,
      purpose: draft.purpose ?? '',
      status: draft.status ?? 'Draft',
      ownerReference: draft.ownerReference ?? draft.createdByRef,
      ownerReferences: draft.ownerReferences,
      managerReference: draft.managerReference,
      startDate: draft.startDate ?? '',
      endDate: draft.endDate ?? '',
      targetAmount: draft.targetAmount ?? 0,
      budgetAmount: draft.budgetAmount,
      reconciledAmount: draft.reconciledAmount ?? 0,
      progress: draft.progress ?? 0,
      hasDownstreamReference: false,
      createdByRef: draft.createdByRef,
      createdByRole: draft.createdByRole,
      fundProgramme: draft.fundProgramme,
      currency: draft.currency,
      channels: draft.channels,
      sources: draft.sources,
      country: draft.country,
      regionLabel: draft.regionLabel,
      region: draft.region,
      city: draft.city,
      pincode: draft.pincode,
      publicDescription: draft.publicDescription,
      publicDescriptionHtml: draft.publicDescriptionHtml,
      termsNotice: draft.termsNotice,
      termsNoticeHtml: draft.termsNoticeHtml,
      activationMode: draft.activationMode,
      reminderDaysBefore: draft.reminderDaysBefore,
      reminderTime: draft.reminderTime,
      createdAt: today,
      updatedAt: today,
      wasEdited: false,
    };

    this.records.update((current) => [optimistic, ...current]);

    // THE IDS COME FROM THE WIZARD, which resolved them from the masters and reference lists.
    // A campaign cannot be created without a currency and a country, so a draft that reaches
    // here without them is one the wizard should not have submitted - the server refuses it and
    // the optimistic row is withdrawn.
    this.api
      .createCampaign({
        name: draft.name,
        code,
        purpose: draft.purpose ?? '',
        fundOrProgramme: draft.fundProgramme ?? '',
        startDate: draft.startDate ?? today,
        endDate: draft.endDate ?? today,
        // targetAmount and budgetAmount are NOT sent: Target & Budget is on hold, no step
        // collects them, and they are no longer on the server's contract either.
        currencyId: draft.currency ?? '',
        countryId: draft.country ?? '',
        ownerIds: [...(draft.ownerReferences ?? [draft.ownerReference ?? draft.createdByRef])],
        // STEP TWO AND STEP THREE ARE MANDATORY ON THE SERVER NOW, so these are sent as values
        // rather than as nulls. An empty string or an empty array still fails validation - which
        // is the intended outcome: a draft that reaches here without a state, a city, a zip code,
        // a channel or its published wording is one the wizard should not have submitted, and the
        // 400 names each missing field rather than saving a campaign whose detail screen would
        // then have nothing to show.
        stateId: draft.region ?? '',
        cityId: draft.city ?? '',
        zipCode: draft.pincode ?? '',
        channelIds: [...(draft.channels ?? [])],
        lifecycleActivation: draft.activationMode === 'auto' ? 'auto' : 'manual',
        daysBeforeStart: draft.reminderDaysBefore ?? null,
        reminderTime: draft.reminderTime || null,
        publicDescription: draft.publicDescriptionHtml ?? draft.publicDescription ?? '',
        termsAndNotice: draft.termsNoticeHtml ?? draft.termsNotice ?? '',
      })
      .subscribe({
        // THE CALLBACK RUNS AFTER THE REFRESH, not before it. A caller that wants to act on
        // the new campaign - the wizard's Submit does, immediately - needs `idsByCode` to
        // hold its id, and only the refresh puts it there.
        next: (created) => {
          this.refresh(() => onSaved?.({ saved: true }));
          void created;
        },
        error: (error: unknown) => {
          this.records.update((current) => current.filter((record) => record.code !== code));

          // THE SERVER'S OWN MESSAGE, not a fixed sentence. A create is refused for reasons
          // the caller can usually act on - a duplicate code, a field the validator rejected -
          // and "The campaign could not be created." threw all of that away, which is what
          // made a 400 on this call so hard to see from the screen.
          const message = saveFailureMessage(error, 'The campaign could not be created.');

          this.loadError.set(message);
          onSaved?.({ saved: false, error: message });
        },
      });

    return code;
  }

  /**
   * A content edit.
   *
   * IT SENDS THE WHOLE RECORD, because the API's update is a PUT rather than a patch: sending
   * only the changed fields would blank everything omitted. The current record supplies whatever
   * the patch does not.
   */
  update(ref: string, patch: Partial<CampaignRecord>): void {
    const current = this.get(ref);
    const id = this.idsByCode.get(ref);

    if (!current || !id) {
      return;
    }

    const merged = { ...current, ...patch };

    // Applied locally first so the screen reflects the edit immediately; refresh() then replaces
    // it with what the server actually stored.
    this.records.update((records) =>
      records.map((record) =>
        record.code === ref ? { ...merged, updatedAt: this.today(), wasEdited: true } : record,
      ),
    );

    this.api
      .updateCampaign(id, {
        expectedVersion: this.versionsByCode.get(ref) ?? 0,
        name: merged.name,
        purpose: merged.purpose,
        fundOrProgramme: merged.fundProgramme ?? '',
        startDate: merged.startDate,
        endDate: merged.endDate,
        // Not sent on edit either, which is what stops a save from writing 0 over a target the
        // record already holds.
        currencyId: merged.currency ?? '',
        countryId: merged.country ?? '',
        ownerIds: [...(merged.ownerReferences ?? [merged.ownerReference])],
        // The same mandatory set as create: an edit must not be a way to empty a campaign out.
        stateId: merged.region ?? '',
        cityId: merged.city ?? '',
        zipCode: merged.pincode ?? '',
        channelIds: [...(merged.channels ?? [])],
        lifecycleActivation: merged.activationMode === 'auto' ? 'auto' : 'manual',
        daysBeforeStart: merged.reminderDaysBefore ?? null,
        reminderTime: merged.reminderTime || null,
        publicDescription: merged.publicDescriptionHtml ?? merged.publicDescription ?? '',
        termsAndNotice: merged.termsNoticeHtml ?? merged.termsNotice ?? '',
      })
      .subscribe({
        next: () => this.refresh(),
        error: (error: unknown) => {
          this.loadError.set(saveFailureMessage(error, 'The campaign could not be saved.'));
          this.refresh();
        },
      });
  }

  // ================= Lifecycle =================
  //
  // EVERY ONE OF THESE NOW CALLS ITS OWN ENDPOINT, and the server decides whether it is allowed.
  // The notification is emitted on SUCCESS only - the previous version emitted before the state
  // had changed anywhere, so a refused transition still told everybody it had happened.

  /**
   * Submits a campaign for approval.
   *
   * THE TIERED RULE IS THE SERVER'S NOW. This method used to read a role string the browser
   * supplied and auto-approve for "Super Admin" - which is to say, anybody who could edit their
   * own session state could approve their own campaign. The role parameter is kept so the eleven
   * call sites do not change, and is no longer trusted for anything.
   */
  submitForApproval(
    ref: string,
    actorRole: CampaignRole,
    actorRef: string,
    onDone?: LifecycleOutcome,
  ): void {
    void actorRole;
    void actorRef;

    this.lifecycle(ref, (id, request) => this.api.submitCampaign(id, request), 'submitted', onDone);
  }

  /**
   * Approves a submitted campaign.
   *
   * THE SERVER REFUSES THE PERSON WHO SUBMITTED IT. That is a per-record rule, so screens must
   * draw the Approve button from `permittedActions` on the campaign detail rather than from a
   * permission check - otherwise they offer an action that answers 409.
   */
  approveCampaign(ref: string, approverRef: string, onDone?: LifecycleOutcome): void {
    void approverRef;

    this.lifecycle(ref, (id, request) => this.api.approveCampaign(id, request), 'approved', onDone);
  }

  activate(ref: string, onDone?: LifecycleOutcome): void {
    this.lifecycle(ref, (id, request) => this.api.activateCampaign(id, request), 'activated', onDone);
  }

  pause(ref: string, onDone?: LifecycleOutcome): void {
    this.lifecycle(ref, (id, request) => this.api.pauseCampaign(id, request), 'paused', onDone);
  }

  resume(ref: string, onDone?: LifecycleOutcome): void {
    this.lifecycle(ref, (id, request) => this.api.resumeCampaign(id, request), 'resumed', onDone);
  }

  /**
   * Requests a campaign close.
   *
   * IT REQUESTS RATHER THAN CLOSES, which is a change of behaviour worth stating: closing a
   * campaign needs a second person on this platform, and this method previously set the status
   * to Closed on its own. The campaign moves to Closing and waits.
   */
  close(ref: string, onDone?: LifecycleOutcome): void {
    this.lifecycle(ref, (id, request) => this.api.requestCampaignClose(id, request), 'closed', onDone);
  }

  /**
   * Cancels a campaign.
   *
   * THERE IS NO CANCEL ENDPOINT, deliberately: a campaign that has run has donations attributed
   * to it, and "cancelled" would misdescribe them. A campaign is closed with a reason instead,
   * which is what this now does - and the reason says it was cancelled.
   */
  cancel(ref: string, onDone?: LifecycleOutcome): void {
    this.lifecycle(
      ref,
      (id, request) =>
        this.api.requestCampaignClose(id, {
          ...request,
          reasonCategory: 'Cancelled',
          detailedReason: request.detailedReason ?? 'The campaign was cancelled before completion.',
        }),
      'cancelled',
      onDone,
    );
  }

  /**
   * The generic setter, kept for the screens that own their own transition rules.
   *
   * IT ROUTES TO THE RIGHT ENDPOINT rather than writing a status. A status the API has no
   * transition for - Draft, say - is refused here rather than silently applied locally, because
   * applying it locally is exactly how the screen and the server came to disagree.
   */
  setStatus(ref: string, status: CampaignStatus, onDone?: LifecycleOutcome): void {
    switch (status) {
      case 'Submitted':
        // 'Initiator' rather than the 'Campaign Manager' this named: the role catalogue no longer
        // carries that name. The argument is inert either way - submitForApproval voids it and
        // the server decides from the token - but it has to be a role that exists.
        this.submitForApproval(ref, 'Initiator', '', onDone);
        return;
      case 'Approved':
        this.approveCampaign(ref, '', onDone);
        return;

      // SCHEDULED IS NOT A TRANSITION ANYBODY RUNS. It is where approval leaves a campaign whose
      // lifecycle activation is automatic, so it is reached by approving a SUBMITTED campaign and
      // never from Approved. Routing it to approve() sent an already-approved campaign back to an
      // endpoint that only accepts Submitted, which answered 409 and left the screen unchanged.
      case 'Scheduled': {
        const current = this.get(ref);

        if (current?.status === 'Submitted') {
          this.approveCampaign(ref, '', onDone);
          return;
        }

        this.refuse(
          'A campaign is scheduled by approving it while its activation is set to automatic. '
          + 'It cannot be moved to Scheduled from here.',
          onDone,
        );
        return;
      }
      case 'Active': {
        const current = this.get(ref);
        if (current?.status === 'Paused') {
          this.resume(ref, onDone);
        } else {
          this.activate(ref, onDone);
        }
        return;
      }
      case 'Paused':
        this.pause(ref, onDone);
        return;
      case 'Closing':
      case 'Closed':
        this.close(ref, onDone);
        return;
      case 'Cancelled':
        this.cancel(ref, onDone);
        return;
      default:
        // Draft is not reachable by a transition: a campaign returns to Draft through the
        // readiness screen's own endpoint, which requires a reason.
        this.refuse(`A campaign cannot be moved to ${status} from here.`, onDone);
    }
  }

  /** A transition refused before it was sent — surfaced on the store AND reported to the caller,
   *  so a screen waiting on the outcome is not left waiting for a call that never went out. */
  private refuse(message: string, onDone?: LifecycleOutcome): void {
    this.loadError.set(message);
    onDone?.({ applied: false, error: message });
  }

  /**
   * Deletes a DRAFT campaign.
   *
   * ONLY A DRAFT, and the server enforces it. Once a campaign has been submitted it has an
   * approval trail; once it has run it has donations attributed to it.
   */
  delete(ref: string, onDone?: LifecycleOutcome): void {
    const id = this.idsByCode.get(ref);

    if (!id) {
      onDone?.({ applied: false, error: 'That campaign is not loaded yet. Refresh and try again.' });
      return;
    }

    this.records.update((records) => records.filter((record) => record.code !== ref));

    this.api.deleteCampaignDraft(id, this.versionsByCode.get(ref) ?? 0).subscribe({
      next: () => {
        this.refresh();
        onDone?.({ applied: true });
      },
      error: (error: unknown) => {
        const message = apiErrorMessage(error, 'The campaign could not be deleted.');
        this.loadError.set(message);
        this.refresh();
        onDone?.({ applied: false, error: message });
      },
    });
  }

  // ================= Internals =================

  /**
   * Runs one lifecycle transition and refreshes.
   *
   * THE NOTIFICATION FIRES ON SUCCESS ONLY. The previous implementation emitted it before the
   * state had changed anywhere at all, so a transition the rules would have refused still
   * announced itself to every subscriber.
   */
  /**
   * Re-reads a campaign after a transition.
   *
   * THE DETAIL IS RE-READ TOO, not just the list. `permittedActions` - the only thing every
   * lifecycle button on the campaign detail and readiness screens is drawn from - lives on the
   * DETAIL response, and `refresh()` reloads the list projection, which does not carry it.
   * Chained on the list load rather than fired beside it, so the detail merge lands on the row
   * the refresh has already replaced instead of racing it.
   */
  private reloadAfterTransition(ref: string): void {
    this.refresh(() => {
      if (this.get(ref)?.detailLoaded) {
        this.loadDetail(ref);
      }
    });
  }

  /**
   * Sends a campaign back to Draft from the readiness screen, with a reason.
   *
   * IT GOES TO THE SERVER, which is the whole point of adding it. The readiness screen's
   * "Return to draft" used to call `update(ref, { status: 'Draft' })` - and `update` is the
   * CONTENT edit, a PUT whose body carries no status at all. So the row flipped to Draft in the
   * browser, the PUT saved the campaign's fields unchanged, the refresh that followed replaced
   * the row with the server's - still Submitted - and the person watching saw the status revert
   * on its own a moment after they had changed it. `POST /campaigns/{id}/readiness/return-to-draft`
   * is the endpoint that actually moves it, and it has existed on both sides all along with
   * nothing in the client calling it.
   */
  returnToDraft(ref: string, reason: string, onDone?: LifecycleOutcome): void {
    const record = this.get(ref);
    const id = this.idsByCode.get(ref);

    if (!record || !id) {
      this.refuse(
        'That campaign is not loaded yet, so the change was not sent. Refresh and try again.',
        onDone,
      );
      return;
    }

    this.api
      .returnCampaignToDraft(id, {
        expectedVersion: this.versionsByCode.get(ref) ?? 0,
        reason,
      })
      .subscribe({
        next: () => {
          this.reloadAfterTransition(ref);
          onDone?.({ applied: true });
        },
        error: (error: unknown) => {
          const message = apiErrorMessage(error, 'The campaign could not be returned to draft.');

          this.loadError.set(message);
          this.reloadAfterTransition(ref);
          onDone?.({ applied: false, error: message });
        },
      });
  }

  private lifecycle(
    ref: string,
    call: (
      id: string,
      request: CampaignLifecycleRequest,
    ) => ReturnType<CampaignApiService['submitCampaign']>,
    event: Parameters<NotificationService['emitCampaignEvent']>[1],
    onDone?: LifecycleOutcome,
  ): void {
    const record = this.get(ref);
    const id = this.idsByCode.get(ref);

    // A TRANSITION ON A CAMPAIGN WE HAVE NO ID FOR CANNOT BE SENT, but it must not vanish
    // either. This used to `return` silently, and the wizard's Submit hit it every single
    // time: Submit called `create()` and this method in the same tick, and `idsByCode` is not
    // populated until the create's refresh comes back. So the campaign was created as a Draft,
    // nothing was ever submitted, no request was made, no error was raised, and it never
    // reached a Campaign Manager for approval. Callers now chain on the create's completion -
    // and if one still arrives early, it says so instead of doing nothing.
    if (!record || !id) {
      const message =
        'That campaign is not loaded yet, so the change was not sent. Refresh and try again.';

      this.loadError.set(message);
      onDone?.({ applied: false, error: message });
      return;
    }

    // THE DETAIL IS RE-READ TOO, not just the list. `permittedActions` - the only thing every
    // lifecycle button on the campaign detail screen is drawn from - lives on the DETAIL response,
    // and `refresh()` reloads the list projection, which does not carry it. Without this a
    // campaign kept the actions of the state it had BEFORE the transition: Approve stayed on
    // screen after the approval that had just consumed it, and answered 409 on the second press.
    // Chained on the list load rather than fired beside it, so the detail merge lands on the row
    // the refresh has already replaced instead of racing it.
    const reloadDetail = () => this.reloadAfterTransition(ref);

    call(id, { expectedVersion: this.versionsByCode.get(ref) ?? 0 }).subscribe({
      next: () => {
        this.notifications.emitCampaignEvent(record, event);
        reloadDetail();
        onDone?.({ applied: true });
      },
      error: (error: unknown) => {
        const message = apiErrorMessage(error, 'That change was refused.');

        this.loadError.set(message);
        reloadDetail();
        onDone?.({ applied: false, error: message });
      },
    });
  }

  /**
   * Loads the FULL record for one campaign and merges it into the store.
   *
   * WITHOUT THIS, THE DETAIL SCREEN WAS SHOWING A REGISTER ROW. Everything on that screen came
   * from `CampaignListItem` - the list projection - which carries a code, a name, dates, a
   * status and some counts, and nothing else. Purpose, currency, channels, location, the
   * publication wording, the activation mode and `permittedActions` are all on the DETAIL
   * response, and `getCampaign` was never called by anything in the application.
   *
   * The visible symptom was a detail screen reading "-" against Currency, Channel and Location on
   * a campaign that had all three filled in. It looked intermittent because it was not: the
   * fields appeared for a campaign this browser had just created - the optimistic record was
   * still in memory - and vanished on the next page load. The invisible symptom was worse: with
   * no `permittedActions`, every screen fell back to guessing lifecycle buttons from permission
   * codes.
   *
   * IT IS SAFE TO CALL REPEATEDLY. A campaign that is not in the store yet, or has no server id
   * yet, is skipped rather than fetched blindly.
   */
  loadDetail(ref: string): void {
    const id = this.idsByCode.get(ref);

    if (!id) {
      return;
    }

    this.api.getCampaign(id).subscribe({
      next: (detail) => {
        this.versionsByCode.set(ref, detail.version);

        this.records.update((records) =>
          records.map((record) => (record.code === ref ? this.mergeDetail(record, detail) : record)),
        );
      },
      error: (error: unknown) => {
        this.loadError.set(apiErrorMessage(error, 'That campaign could not be loaded.'));
      },
    });
  }

  /**
   * The detail response merged onto the register row the store already holds.
   *
   * NAMES AND IDS ARE BOTH KEPT. The ids are what the wizard needs to re-select a dropdown when
   * somebody edits the campaign; the names are what the detail screen prints. Storing only one of
   * the two is how this screen ended up with a choice between showing a Guid and showing a dash.
   */
  private mergeDetail(record: CampaignRecord, detail: CampaignDetail): CampaignRecord {
    const description = this.splitRichText(detail.publicDescription);
    const terms = this.splitRichText(detail.termsAndNotice);

    return {
      ...record,
      name: detail.name,
      purpose: detail.purpose,
      status: this.toDisplayStatus(detail.status),
      startDate: detail.startDate,
      endDate: detail.endDate,
      fundProgramme: detail.fundOrProgramme,

      ownerReference: detail.ownerIds?.[0] ?? record.ownerReference,
      ownerReferences: detail.ownerIds?.length ? [...detail.ownerIds] : record.ownerReferences,

      currency: detail.currencyId,
      currencyName: detail.currencyCode ?? undefined,

      channels: detail.channelIds?.length ? [...detail.channelIds] : record.channels,
      channelNames: detail.channels?.length
        ? detail.channels.map((channel) => channel.name).filter((name) => !!name)
        : undefined,

      country: detail.countryId,
      countryName: detail.countryName ?? undefined,
      region: detail.stateId ?? undefined,
      regionName: detail.stateName ?? undefined,
      city: detail.cityId ?? undefined,
      cityName: detail.cityName ?? undefined,
      pincode: detail.zipCode ?? undefined,

      activationMode: detail.lifecycleActivation === 'auto' ? 'auto' : 'manual',
      reminderDaysBefore: detail.daysBeforeStart,
      reminderTime: detail.reminderTime,

      // THE MARKUP AND THE TEXT ARE SEPARATED AGAIN HERE. See `splitRichText` - the API keeps one
      // field per value and it holds markup, so reading it straight into the plain-text field is
      // what put "<div><br></div>" on the summary card.
      publicDescription: description.text,
      publicDescriptionHtml: description.html,
      termsNotice: terms.text,
      termsNoticeHtml: terms.html,

      trackingAssetCount: detail.trackingAssetCount,
      outstandingCheckCount: detail.outstandingCheckCount,
      hasDownstreamReference: detail.trackingAssetCount > 0,

      // THE WHOLE POINT OF THE ROUND TRIP. Every lifecycle button on the detail screen is drawn
      // from this list and from nothing else.
      permittedActions: [...detail.permittedActions],
      detailLoaded: true,

      createdAt: detail.createdAtUtc,
      updatedAt: detail.updatedAtUtc ?? undefined,
      wasEdited: !!detail.updatedAtUtc,
    } as CampaignRecord;
  }

  /**
   * Splits one stored rich-text value back into the markup and the plain text.
   *
   * WHY THIS EXISTS. Public description and Terms and notice are authored in a contenteditable
   * editor, so the wizard holds each of them TWICE - `publicDescriptionHtml` for the markup and
   * `publicDescription` for the text - and the API has ONE column for the pair. The client
   * therefore sends the markup (see the create and update bodies above, which is right: the
   * formatting is the point of the editor, and the CAM validators exempt these three fields from
   * the no-markup rule precisely so it survives).
   *
   * WHAT WENT WRONG WAS THE WAY BACK. The detail response was read straight into the PLAIN field
   * and the html field was left undefined - so after any reload the "plain text" was markup, and
   * every screen that prints it as text printed the tags. On the campaign summary card that read:
   *
   *     The key decision<div><br></div><div>If you're asking me:</div>...
   *
   * with a Read more link beside it, on a field a donor-facing page is meant to publish.
   *
   * A value with no tags in it is text and stays text, so a description typed as one plain
   * paragraph does not acquire an html twin it never had.
   */
  private splitRichText(value: string | null | undefined): {
    readonly text: string | undefined;
    readonly html: string | undefined;
  } {
    const stored = (value ?? '').trim();

    if (!stored) {
      return { text: undefined, html: undefined };
    }

    if (!/<[a-z!/][^>]*>/i.test(stored)) {
      return { text: stored, html: undefined };
    }

    return { text: CampaignStoreService.toPlainText(stored), html: stored };
  }

  /**
   * Markup as the text inside it.
   *
   * DELIBERATELY NOT `innerHTML` ON A DETACHED ELEMENT. Parsing a string the server returned by
   * assigning it to a DOM node runs the loader on any `<img onerror>` or `<iframe src>` it
   * contains, and this method is called on every detail load, for every campaign, whoever wrote
   * the description. Block boundaries become line breaks so the paragraphs of a long description
   * do not run into each other in the preview.
   */
  private static toPlainText(html: string): string {
    return html
      .replace(/<(script|style)\b[\s\S]*?<\/\1>/gi, '')
      .replace(/<br\s*\/?>/gi, '\n')
      .replace(/<\/(p|div|li|h[1-6]|tr|blockquote)\s*>/gi, '\n')
      .replace(/<[^>]*>/g, '')
      .replace(/&nbsp;/gi, ' ')
      .replace(/&lt;/gi, '<')
      .replace(/&gt;/gi, '>')
      .replace(/&quot;/gi, '"')
      .replace(/&#0*39;|&apos;/gi, "'")

      // LAST, so an escaped "&amp;lt;" does not become a "<" that the tag strip already ran past.
      .replace(/&amp;/gi, '&')
      .replace(/[ \t]+$/gm, '')
      .replace(/\n{3,}/g, '\n\n')
      .trim();
  }

  /** One API row as the screens read it. */
  private toRecord(item: CampaignListItem): CampaignRecord {
    const existing = this.records().find((record) => record.code === item.code);

    return {
      ...existing,
      code: item.code,
      name: item.name,
      purpose: existing?.purpose ?? '',
      status: this.toDisplayStatus(item.status),

      // THE OWNERS COME FROM THE SERVER NOW. This read `existing?.ownerReference ?? ''`, and
      // `existing` only exists for a campaign this browser created in the current session - so
      // after any page load the reference was the empty string, which every owner card resolves
      // to 'Unassigned'. Every campaign on the register showed as unowned, however many owners
      // had been chosen for it in the wizard. The server's own list projection now carries the
      // ids; `existing` remains the fallback for the optimistic row that has not been refreshed
      // yet, and only when the server sent nothing.
      ownerReference: item.ownerIds?.[0] ?? existing?.ownerReference ?? '',
      ownerReferences: item.ownerIds?.length ? [...item.ownerIds] : existing?.ownerReferences,
      startDate: item.startDate,
      endDate: item.endDate,
      targetAmount: item.targetAmount,
      budgetAmount: item.budgetAmount ?? undefined,

      // NOT ON THE LIST PROJECTION. The reconciled figure belongs to the PAYMENTS module and is
      // shown on the campaign detail, which fetches it separately - putting a stale zero here
      // would be worse than showing nothing.
      reconciledAmount: existing?.reconciledAmount ?? 0,

      progress: item.elapsedPercent ?? 0,

      // A campaign with tracking assets has something pointing at it, which is what the delete
      // rule turns on.
      hasDownstreamReference: item.trackingAssetCount > 0,

      fundProgramme: item.fundOrProgramme,
      currency: item.currencyId,
      currencyName: item.currencyCode ?? existing?.currencyName,

      trackingAssetCount: item.trackingAssetCount,
      outstandingCheckCount: item.outstandingCheckCount,
      updatedAt: item.updatedAtUtc ?? undefined,

      // A REFRESH MUST NOT UNDO A DETAIL LOAD. `refresh()` rebuilds every row from the list
      // projection, which carries none of the detail fields - so without carrying these forward
      // a background refresh would blank the Currency, Channel and Location the user is looking
      // at, and drop the permittedActions the lifecycle buttons are drawn from.
      permittedActions: existing?.permittedActions,
      detailLoaded: existing?.detailLoaded,
    } as CampaignRecord;
  }

  /** The API's lower-case status to the screens' capitalised one. */
  private toDisplayStatus(status: ApiCampaignStatus): CampaignStatus {
    switch (status) {
      case 'draft':
        return 'Draft';
      case 'submitted':
        return 'Submitted';
      case 'approved':
        return 'Approved';
      case 'scheduled':
        return 'Scheduled';
      case 'active':
        return 'Active';
      case 'paused':
        return 'Paused';
      case 'closing':
        return 'Closing';
      case 'closed':
        return 'Closed';
      case 'cancelled':
        return 'Cancelled';
      default:
        return 'Draft';
    }
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  /**
   * A provisional reference for a campaign the wizard did not name.
   *
   * PROVISIONAL, because the server allocates the real one. It exists so the wizard has something
   * to navigate to before the create call returns; `refresh()` replaces it.
   */
  private nextReference(): string {
    const year = new Date().getFullYear();

    const existingNumbers = this.records()
      .map((record) => Number(record.code.split('-').pop()))
      .filter((value) => !Number.isNaN(value));

    const next = (existingNumbers.length ? Math.max(...existingNumbers) : 0) + 1;

    return `CAMP-${year}-${String(next).padStart(4, '0')}`;
  }
}

/** Re-exported so the detail screen can read the richer server record when it needs to. */
export type { CampaignDetail };
