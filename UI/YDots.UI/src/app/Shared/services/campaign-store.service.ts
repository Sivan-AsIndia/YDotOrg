import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { CampaignRole } from '../../Service/current-user.service';
import {
  CampaignDetail,
  CampaignLifecycleRequest,
  CampaignListItem,
  CampaignStatus as ApiCampaignStatus,
} from '../models/campaign-contract.model';
import { CampaignRecord, CampaignStatus } from '../models/campaign.model';
import { NotificationService } from '../../Service/notification.service';
import { apiErrorMessage } from '../models/api-response.model';

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

    // A Scheduled, auto-activate campaign becomes Active when its start date arrives.
    //
    // IT NOW ASKS THE SERVER rather than flipping a local string. The previous version mutated
    // the array every minute, so a campaign "activated" itself in one browser tab and nowhere
    // else. The sweep still runs on a timer because a campaign whose date passes while somebody
    // has the register open should appear active without a manual refresh.
    setInterval(() => this.refresh(), 60_000);
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
        stateId: draft.region || null,
        cityId: draft.city || null,
        zipCode: draft.pincode || null,
        lifecycleActivation: draft.activationMode === 'auto' ? 'auto' : 'manual',
        daysBeforeStart: draft.reminderDaysBefore ?? 0,
        reminderTime: draft.reminderTime || '09:00',
        publicDescription: draft.publicDescriptionHtml ?? draft.publicDescription ?? null,
        termsAndNotice: draft.termsNoticeHtml ?? draft.termsNotice ?? null,
        channelIds: draft.channels ? [...draft.channels] : null,
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
          const message = apiErrorMessage(error, 'The campaign could not be created.');

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
        stateId: merged.region || null,
        cityId: merged.city || null,
        zipCode: merged.pincode || null,
        lifecycleActivation: merged.activationMode === 'auto' ? 'auto' : 'manual',
        daysBeforeStart: merged.reminderDaysBefore ?? 0,
        reminderTime: merged.reminderTime || '09:00',
        publicDescription: merged.publicDescriptionHtml ?? merged.publicDescription ?? null,
        termsAndNotice: merged.termsNoticeHtml ?? merged.termsNotice ?? null,
        channelIds: merged.channels ? [...merged.channels] : null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: (error: unknown) => {
          this.loadError.set(apiErrorMessage(error, 'The campaign could not be saved.'));
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
  submitForApproval(ref: string, actorRole: CampaignRole, actorRef: string): void {
    void actorRole;
    void actorRef;

    this.lifecycle(ref, (id, request) => this.api.submitCampaign(id, request), 'submitted');
  }

  /**
   * Approves a submitted campaign.
   *
   * THE SERVER REFUSES THE PERSON WHO SUBMITTED IT. That is a per-record rule, so screens must
   * draw the Approve button from `permittedActions` on the campaign detail rather than from a
   * permission check - otherwise they offer an action that answers 409.
   */
  approveCampaign(ref: string, approverRef: string): void {
    void approverRef;

    this.lifecycle(ref, (id, request) => this.api.approveCampaign(id, request), 'approved');
  }

  activate(ref: string): void {
    this.lifecycle(ref, (id, request) => this.api.activateCampaign(id, request), 'activated');
  }

  pause(ref: string): void {
    this.lifecycle(ref, (id, request) => this.api.pauseCampaign(id, request), 'paused');
  }

  resume(ref: string): void {
    this.lifecycle(ref, (id, request) => this.api.resumeCampaign(id, request), 'resumed');
  }

  /**
   * Requests a campaign close.
   *
   * IT REQUESTS RATHER THAN CLOSES, which is a change of behaviour worth stating: closing a
   * campaign needs a second person on this platform, and this method previously set the status
   * to Closed on its own. The campaign moves to Closing and waits.
   */
  close(ref: string): void {
    this.lifecycle(ref, (id, request) => this.api.requestCampaignClose(id, request), 'closed');
  }

  /**
   * Cancels a campaign.
   *
   * THERE IS NO CANCEL ENDPOINT, deliberately: a campaign that has run has donations attributed
   * to it, and "cancelled" would misdescribe them. A campaign is closed with a reason instead,
   * which is what this now does - and the reason says it was cancelled.
   */
  cancel(ref: string): void {
    this.lifecycle(
      ref,
      (id, request) =>
        this.api.requestCampaignClose(id, {
          ...request,
          reasonCategory: 'Cancelled',
          detailedReason: request.detailedReason ?? 'The campaign was cancelled before completion.',
        }),
      'cancelled',
    );
  }

  /**
   * The generic setter, kept for the screens that own their own transition rules.
   *
   * IT ROUTES TO THE RIGHT ENDPOINT rather than writing a status. A status the API has no
   * transition for - Draft, say - is refused here rather than silently applied locally, because
   * applying it locally is exactly how the screen and the server came to disagree.
   */
  setStatus(ref: string, status: CampaignStatus): void {
    switch (status) {
      case 'Submitted':
        this.submitForApproval(ref, 'Campaign Manager', '');
        return;
      case 'Approved':
      case 'Scheduled':
        this.approveCampaign(ref, '');
        return;
      case 'Active': {
        const current = this.get(ref);
        if (current?.status === 'Paused') {
          this.resume(ref);
        } else {
          this.activate(ref);
        }
        return;
      }
      case 'Paused':
        this.pause(ref);
        return;
      case 'Closing':
      case 'Closed':
        this.close(ref);
        return;
      case 'Cancelled':
        this.cancel(ref);
        return;
      default:
        // Draft is not reachable by a transition: a campaign returns to Draft through the
        // readiness screen's own endpoint, which requires a reason.
        this.loadError.set(`A campaign cannot be moved to ${status} from here.`);
    }
  }

  /**
   * Deletes a DRAFT campaign.
   *
   * ONLY A DRAFT, and the server enforces it. Once a campaign has been submitted it has an
   * approval trail; once it has run it has donations attributed to it.
   */
  delete(ref: string): void {
    const id = this.idsByCode.get(ref);

    if (!id) {
      return;
    }

    this.records.update((records) => records.filter((record) => record.code !== ref));

    this.api.deleteCampaignDraft(id, this.versionsByCode.get(ref) ?? 0).subscribe({
      next: () => this.refresh(),
      error: () => {
        this.loadError.set('The campaign could not be deleted.');
        this.refresh();
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
  private lifecycle(
    ref: string,
    call: (
      id: string,
      request: CampaignLifecycleRequest,
    ) => ReturnType<CampaignApiService['submitCampaign']>,
    event: Parameters<NotificationService['emitCampaignEvent']>[1],
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
      this.loadError.set(
        'That campaign is not loaded yet, so the change was not sent. Refresh and try again.',
      );
      return;
    }

    call(id, { expectedVersion: this.versionsByCode.get(ref) ?? 0 }).subscribe({
      next: () => {
        this.notifications.emitCampaignEvent(record, event);
        this.refresh();
      },
      error: (error: unknown) => {
        this.loadError.set(apiErrorMessage(error, 'That change was refused.'));
        this.refresh();
      },
    });
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
      ownerReference: existing?.ownerReference ?? '',
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
      updatedAt: item.updatedAtUtc ?? undefined,
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
