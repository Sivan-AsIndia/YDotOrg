import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, map, switchMap, tap, throwError } from 'rxjs';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import {
  BudgetPlanDetail,
  BudgetPlanListItem,
  BudgetPlanVersion as ApiPlanVersion,
  PlanApprovalState as ApiApprovalState,
} from '../models/campaign-contract.model';
import { OutcomeResponse } from '../models/api-response.model';
import {
  ApprovalState,
  PlanEditableFields,
  PlanRecord,
  PlanVersion,
} from '../models/budget-target-plan.model';
import { CampaignStoreService } from './campaign-store.service';
import { PeopleDirectoryService } from './people-directory.service';

/** Result of a mutating store call — carries the version acted on and a stable effective time. */
export interface PlanMutationResult {
  readonly reference: string;
  readonly version: PlanVersion;
  readonly effectiveTime: string;
}

/**
 * Budget and target plans.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. The plans were loaded from `budget-target-plan-data.json`
 * compiled into the bundle, and every allocate, revise, submit and approve mutated an array in the
 * browser. Four consequences followed, and the fourth is the serious one:
 *
 *   - NOTHING WAS SAVED. A budget approved on Monday was a draft again on Tuesday.
 *   - EVERY ORGANISATION SAW THE SAME PLANS, because a JSON file in a bundle does not know who is
 *     asking. One charity's screen showed another's targets.
 *   - THE REFERENCE WAS MINTED LOCALLY, so two people allocating at the same moment would both
 *     have produced BTP-2026-0007 - and a plan reference is what a finance team quotes.
 *   - APPROVAL WAS A FIELD ASSIGNMENT. `approve()` set `approvalState = 'Approved'` and recorded
 *     whoever the browser said was acting. There was no check that the approver was not the
 *     submitter, so the one control that matters on a budget screen - somebody other than the
 *     author commits the money - did not exist. It was also possible to end up with two approved
 *     versions of one plan, which would have double-counted that plan in every campaign total.
 *
 * IT NOW READS AND WRITES `CAM /api/v1/budget-plans`. The synchronous signal surface is kept,
 * because the register and the campaign detail tabs read `all()` and `approvedForCampaign()` from
 * templates. Reads are synchronous against the loaded signal; writes go to the API and refresh it.
 *
 * THE SERVER DECIDES WHO MAY APPROVE. Every record carries `permittedActions`, and the screen must
 * read that rather than deciding locally - the submitter-cannot-approve rule is invisible from
 * here.
 */
@Injectable({ providedIn: 'root' })
export class BudgetTargetStoreService {
  private readonly api = inject(CampaignApiService);
  private readonly organisationScope = inject(OrganisationScopeService);
  private readonly campaigns = inject(CampaignStoreService);
  private readonly people = inject(PeopleDirectoryService);

  /** The loaded plans. Empty until the first response - never seeded. */
  private readonly records = signal<readonly PlanRecord[]>([]);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly all = computed(() => this.records());

  /** The server's total, which is not the same as the loaded page's length. */
  private readonly serverTotal = signal(0);

  readonly total = computed(() => this.serverTotal());

  /** The API id and concurrency stamp per plan reference, so a screen working in codes can write. */
  private readonly idsByReference = new Map<string, string>();
  private readonly versionsByReference = new Map<string, number>();

  /** The API version id per plan reference and version number, for the version-addressed calls. */
  private readonly versionIds = new Map<string, string>();
  private readonly versionStamps = new Map<string, number>();

  /** The server's action list per plan, which is what a screen must draw its buttons from. */
  private readonly actionsByReference = new Map<string, readonly string[]>();

  constructor() {
    this.refresh();
    this.organisationScope.onOrganisationChange(() => this.reloadForOrganisation());
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
    this.idsByReference.clear();
    this.versionsByReference.clear();
    this.versionIds.clear();
    this.versionStamps.clear();
    this.actionsByReference.clear();
    this.refresh();
  }

  get(reference: string): PlanRecord | undefined {
    return this.records().find((record) => record.reference === reference);
  }

  /**
   * The version a row should show.
   *
   * THE APPROVED ONE WHEN THERE IS ONE, else the latest. A register showing the newest draft next
   * to a colleague's approved figures would present two answers to "what is this plan?", and the
   * approved one is the answer that governs.
   */
  displayVersion(record: PlanRecord): PlanVersion {
    return this.approvedVersion(record) ?? record.versions[record.versions.length - 1];
  }

  approvedVersion(record: PlanRecord): PlanVersion | undefined {
    return record.versions.find((version) => version.approvalState === 'Approved');
  }

  version(record: PlanRecord, versionNumber: number): PlanVersion | undefined {
    return record.versions.find((version) => version.versionNumber === versionNumber);
  }

  forCampaign(campaignName: string): readonly PlanRecord[] {
    const query = campaignName.trim().toLowerCase();

    return this.records().filter(
      (record) =>
        record.campaign.trim().toLowerCase() === query
        || record.campaignRef.trim().toLowerCase() === query,
    );
  }

  /**
   * The approved versions for a campaign - what it is actually committed to.
   *
   * ONE PER PLAN, guaranteed by the database rather than by this filter: at most one version of a
   * plan may be approved at a time. Without that guarantee, summing these would double-count every
   * plan that had ever been revised.
   */
  approvedForCampaign(campaignName: string): readonly PlanVersion[] {
    return this.forCampaign(campaignName)
      .map((record) => this.approvedVersion(record))
      .filter((version): version is PlanVersion => !!version);
  }

  /** What the server says this caller may do to a plan. */
  permittedActions(reference: string): readonly string[] {
    return this.actionsByReference.get(reference) ?? [];
  }

  can(reference: string, action: string): boolean {
    return this.permittedActions(reference).some(
      (candidate) => candidate.toLowerCase() === action.toLowerCase(),
    );
  }

  /**
   * A plan already covering the same campaign, period and dimension.
   *
   * ADVISORY ONLY. The server enforces it with a unique index, which is what makes the rule hold
   * when two people allocate simultaneously - this exists so the screen can warn before the round
   * trip rather than instead of it.
   */
  findDuplicate(
    campaignRef: string,
    planPeriod: string,
    targetDimension: string,
  ): PlanRecord | undefined {
    const period = planPeriod.trim().toLowerCase();
    const dimension = targetDimension.trim().toLowerCase();

    return this.records().find(
      (record) =>
        record.campaignRef === campaignRef
        && record.planPeriod.trim().toLowerCase() === period
        && record.targetDimension.trim().toLowerCase() === dimension,
    );
  }

  refresh(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.searchBudgetPlans({ pageSize: 200 }).subscribe({
      next: (page) => {
        this.idsByReference.clear();
        this.versionsByReference.clear();
        this.actionsByReference.clear();

        for (const item of page.items) {
          this.idsByReference.set(item.code, item.id);
          this.versionsByReference.set(item.code, item.version);
          this.actionsByReference.set(item.code, item.permittedActions);
        }

        this.records.set(page.items.map((item) => this.toRecord(item)));
        this.serverTotal.set(page.totalCount);
        this.isLoading.set(false);
      },
      error: () => {
        this.records.set([]);
        this.serverTotal.set(0);
        this.isLoading.set(false);
        this.loadError.set('The budget plans could not be loaded.');
      },
    });
  }

  /**
   * Loads one plan's full version history.
   *
   * THE REGISTER CARRIES ONE VERSION PER PLAN - the one that governs - so a screen showing the
   * history has to ask for it. Fetching every version for every row would make the register far
   * more expensive to render than it needs to be.
   */
  loadHistory(reference: string): void {
    const id = this.idsByReference.get(reference);

    if (!id) {
      return;
    }

    this.api.getBudgetPlan(id).subscribe({
      next: (detail) => {
        this.versionIds.clear();
        this.versionStamps.clear();

        for (const version of detail.versions) {
          this.versionIds.set(`${reference}:${version.versionNumber}`, version.id);
          this.versionStamps.set(`${reference}:${version.versionNumber}`, version.version);
        }

        this.records.update((records) =>
          records.map((record) =>
            record.reference === reference ? this.toRecordFromDetail(detail) : record,
          ),
        );

        this.actionsByReference.set(reference, detail.permittedActions);
      },
      error: () => this.loadError.set('The plan history could not be loaded.'),
    });
  }

  /**
   * Allocates a plan.
   *
   * IT RETURNS AN OBSERVABLE, and the screen waits for it. The array-backed version returned the
   * new plan instantly, which is why the screen wrapped every action in a 500ms `setTimeout` to
   * make it feel real. The wait is now the actual round trip, and the reference in the result is
   * the one the server minted rather than one the browser composed.
   */
  allocate(fields: PlanEditableFields): Observable<PlanMutationResult> {
    const campaignId = this.campaigns.apiId(fields.campaignRef);

    if (!campaignId) {
      return throwError(() => new Error('The campaign could not be identified.'));
    }

    return this.api
      .allocateBudgetPlan({
        campaignId,
        planPeriod: fields.planPeriod,
        targetDimension: fields.targetDimension,
        ownerUserId: this.people.idOf(fields.ownerRef) ?? fields.ownerRef,
        targetAmount: fields.targetAmount,
        budgetAmount: fields.budgetAmount,
        budgetCategory: fields.budgetCategory,
        expectedVolume: fields.expectedVolume,
        assumptions: fields.assumptions || null,
      })
      .pipe(
        tap(() => this.refresh()),
        map((detail) => this.toMutationResult(detail)),
      );
  }

  /**
   * Revises a plan into a new version.
   *
   * THE APPROVED VERSION IS UNTOUCHED. That is the whole point: the figures somebody approved stay
   * exactly as approved, and the new ones sit alongside them until they are approved in turn.
   */
  revise(reference: string, fields: PlanEditableFields): Observable<PlanMutationResult> {
    const id = this.idsByReference.get(reference);

    if (!id) {
      return throwError(() => new Error('That plan is not loaded.'));
    }

    return this.api
      .reviseBudgetPlan(id, {
        expectedVersion: this.versionsByReference.get(reference) ?? 0,
        targetAmount: fields.targetAmount,
        budgetAmount: fields.budgetAmount,
        budgetCategory: fields.budgetCategory,
        expectedVolume: fields.expectedVolume,
        assumptions: fields.assumptions || null,
      })
      .pipe(
        tap(() => this.refresh()),
        map((detail) => this.toMutationResult(detail)),
      );
  }

  /** Edits a draft version in place. Refused by the server on anything already submitted. */
  update(
    reference: string,
    versionNumber: number,
    fields: PlanEditableFields,
  ): Observable<PlanMutationResult> {
    return this.versionAction(reference, versionNumber, (versionId, expectedVersion) =>
      this.api.updateBudgetPlanVersion(versionId, {
        expectedVersion,
        targetAmount: fields.targetAmount,
        budgetAmount: fields.budgetAmount,
        budgetCategory: fields.budgetCategory,
        expectedVolume: fields.expectedVolume,
        assumptions: fields.assumptions || null,
        ownerUserId: this.people.idOf(fields.ownerRef) ?? null,
      }),
    );
  }

  submit(
    reference: string,
    versionNumber: number,
    note?: string,
  ): Observable<PlanMutationResult> {
    return this.versionAction(reference, versionNumber, (versionId, expectedVersion) =>
      this.api.submitBudgetPlanVersion(versionId, { expectedVersion, note: note ?? null }),
    );
  }

  /**
   * Approves a version.
   *
   * NO APPROVER IS PASSED IN, and that is the change that matters. The old version took an
   * `approvedByRef` from the caller and wrote it down, so the person who prepared a budget could
   * commit the organisation to it. The server now decides, against the stored submitter, and
   * refuses when they are the same person.
   */
  approve(
    reference: string,
    versionNumber: number,
    reason?: string,
  ): Observable<PlanMutationResult> {
    return this.versionAction(reference, versionNumber, (versionId, expectedVersion) =>
      this.api.approveBudgetPlanVersion(versionId, { expectedVersion, reason: reason ?? null }),
    );
  }

  /** Rejects a version. The server requires a reason, so a rejection is always actionable. */
  reject(
    reference: string,
    versionNumber: number,
    reason: string,
  ): Observable<PlanMutationResult> {
    return this.versionAction(reference, versionNumber, (versionId, expectedVersion) =>
      this.api.rejectBudgetPlanVersion(versionId, { expectedVersion, reason }),
    );
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /**
   * Runs an action addressed by version, loading the version ids first when they are not held.
   *
   * THE REGISTER DOES NOT CARRY VERSION IDS - it carries one version per plan and the plan's own
   * id - so the first action on a plan fetches the history and then proceeds. Doing it here means
   * no call site has to remember.
   */
  private versionAction(
    reference: string,
    versionNumber: number,
    call: (versionId: string, expectedVersion: number) => Observable<OutcomeResponse>,
  ): Observable<PlanMutationResult> {
    const key = `${reference}:${versionNumber}`;
    const held = this.versionIds.get(key);

    if (held) {
      return call(held, this.versionStamps.get(key) ?? 0).pipe(
        tap(() => this.refresh()),
        map((outcome) => this.toOutcomeResult(reference, versionNumber, outcome)),
      );
    }

    const id = this.idsByReference.get(reference);

    if (!id) {
      return throwError(() => new Error('That plan is not loaded.'));
    }

    return this.api.getBudgetPlan(id).pipe(
      switchMap((detail) => {
        for (const version of detail.versions) {
          this.versionIds.set(`${reference}:${version.versionNumber}`, version.id);
          this.versionStamps.set(`${reference}:${version.versionNumber}`, version.version);
        }

        const resolved = this.versionIds.get(key);

        if (!resolved) {
          return throwError(() => new Error(`Version v${versionNumber} was not found.`));
        }

        return call(resolved, this.versionStamps.get(key) ?? 0);
      }),
      tap(() => this.refresh()),
      map((outcome) => this.toOutcomeResult(reference, versionNumber, outcome)),
    );
  }

  /** A plan detail as the screen's mutation result. */
  private toMutationResult(detail: BudgetPlanDetail): PlanMutationResult {
    const version = detail.latestVersion ?? detail.versions[detail.versions.length - 1];

    return {
      reference: detail.code,
      version: this.toVersion(version),
      effectiveTime: version?.effectiveAtUtc ?? detail.createdAtUtc,
    };
  }

  /**
   * An outcome as the screen's mutation result.
   *
   * THE VERSION IS RE-READ FROM THE LOADED RECORD after the refresh, so the state shown is what the
   * server stored rather than what the browser expected it to store.
   */
  private toOutcomeResult(
    reference: string,
    versionNumber: number,
    outcome: OutcomeResponse,
  ): PlanMutationResult {
    const record = this.get(reference);
    const version = record ? this.version(record, versionNumber) : undefined;

    return {
      reference,
      version: version ?? {
        versionNumber,
        targetAmount: 0,
        budgetAmount: 0,
        budgetCategory: '',
        expectedVolume: 0,
        assumptions: '',
        approvalState: (outcome.status as ApprovalState) ?? 'Draft',
        submittedByRef: null,
        approvedByRef: null,
        effectiveTime: null,
        actualReconciledResult: '—',
        variance: '—',
      },
      effectiveTime: new Date().toISOString(),
    };
  }

  private failed(message: string): void {
    this.loadError.set(message);
    this.refresh();
  }

  /**
   * One register row as the screens read it.
   *
   * THE ROW CARRIES ONE VERSION. The screens iterate `versions`, so the display version goes in
   * alone until `loadHistory` fills the rest in - which is accurate rather than convenient: the
   * register genuinely does not know what the other versions say.
   */
  private toRecord(item: BudgetPlanListItem): PlanRecord {
    return {
      reference: item.code,
      campaign: item.campaignName,
      campaignRef: item.campaignCode,
      planPeriod: item.planPeriod,
      targetDimension: item.targetDimension,
      owner: this.people.name(item.ownerUserId),
      ownerRef: item.ownerUserId,
      versions: item.displayVersion ? [this.toVersion(item.displayVersion)] : [],
    };
  }

  private toRecordFromDetail(detail: BudgetPlanDetail): PlanRecord {
    return {
      reference: detail.code,
      campaign: detail.campaignName,
      campaignRef: detail.campaignCode,
      planPeriod: detail.planPeriod,
      targetDimension: detail.targetDimension,
      owner: this.people.name(detail.ownerUserId),
      ownerRef: detail.ownerUserId,

      // Oldest first, which is the order the history reads in.
      versions: [...detail.versions]
        .sort((left, right) => left.versionNumber - right.versionNumber)
        .map((version) => this.toVersion(version)),
    };
  }

  /**
   * One API version as the screen reads it.
   *
   * THE ACTUAL AND THE VARIANCE ARE THE SERVER'S, computed from the donations attributed to the
   * campaign. The old version stored them as strings on the seeded record, so they were whatever
   * the JSON file said and never changed.
   */
  private toVersion(version: ApiPlanVersion): PlanVersion {
    return {
      versionNumber: version.versionNumber,
      targetAmount: version.targetAmount,
      budgetAmount: version.budgetAmount,
      budgetCategory: version.budgetCategory,
      expectedVolume: version.expectedVolume,
      assumptions: version.assumptions ?? '',
      approvalState: this.toApprovalState(version.approvalState),
      submittedByRef: version.submittedByUserId,
      approvedByRef: version.approvedByUserId,
      effectiveTime: version.effectiveAtUtc,

      // Only the approved version is being run to, so only it has an actual worth reporting.
      actualReconciledResult: version.countsTowardTotals
        ? `${version.actualReconciledAmount.toLocaleString('en-IN')} ${version.currencyCode}`
        : '—',

      variance: version.countsTowardTotals
        ? `${version.variance >= 0 ? '+' : ''}${version.variance.toLocaleString('en-IN')} `
          + `(${version.variancePercentage >= 0 ? '+' : ''}${version.variancePercentage}%)`
        : '—',
    };
  }

  /** The API's five states onto the screen's five. They correspond exactly. */
  private toApprovalState(state: ApiApprovalState): ApprovalState {
    return state as ApprovalState;
  }
}
