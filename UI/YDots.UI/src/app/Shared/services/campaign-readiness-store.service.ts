import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import { Blocker, CampaignReadinessRecord, ReadinessRequestState } from '../models/campaign-readiness.model';
import { CampaignStoreService } from './campaign-store.service';
import { ReadinessChecklistStoreService } from './readiness-checklist-store.service';

/**
 * The launch-readiness record for a campaign - who asked for launch, who approved it, what is
 * blocking it, and when it is planned for.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. The record lived in a signal seeded with one hard-coded
 * campaign, and `update()` incremented a local `version` on every patch. That version was the
 * mechanism the whole screen's conflict handling rested on - "somebody else already approved this"
 * - and it counted only THIS browser's edits. Two people approving the same campaign at the same
 * moment each saw version 1 become 2 and neither saw a conflict, which is the exact case the
 * check exists for.
 *
 * WORSE, `requestedByRef` WAS WHATEVER THE BROWSER PUT THERE. The self-approval block - the rule
 * that the person who requested a launch cannot also approve it - compared that field against the
 * current session. Both sides of that comparison came from the same browser, so the rule was
 * advisory at best.
 *
 * IT NOW DERIVES FROM THE SERVER. The request and approval state come from the campaign's own
 * lifecycle - which is where submission and approval actually happen, under a segregation-of-duties
 * check the server enforces - and the blockers come from the readiness checklist. This store no
 * longer decides anything; it assembles what the server said into the shape the screen reads.
 */
@Injectable({ providedIn: 'root' })
export class CampaignReadinessStoreService {
  private readonly api = inject(CampaignApiService);
  private readonly organisationScope = inject(OrganisationScopeService);
  private readonly campaigns = inject(CampaignStoreService);
  private readonly checklist = inject(ReadinessChecklistStoreService);

  /** The loaded records, keyed by campaign reference. Empty until a campaign is loaded. */
  private readonly records = signal<Readonly<Record<string, CampaignReadinessRecord>>>({});

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  constructor() {
    this.organisationScope.onOrganisationChange(() => this.discardForOrganisation());
  }

  /**
   * Discards the cache when the Organisation changes.
   *
   * NOTHING IS RE-FETCHED HERE, unlike the stores that hold a working set: these records are
   * loaded on demand for the campaign a screen is looking at, and after a switch there is no such
   * campaign — the screen that wanted one has been navigated away from. Emptying the map is
   * enough; whatever is opened next asks for itself. See `OrganisationScopeService`.
   */
  private discardForOrganisation(): void {
    this.records.set({});
    this.loadError.set(null);
  }


  readonly snapshot = computed(() => this.records());

  get(ref: string): CampaignReadinessRecord | undefined {
    return this.records()[ref];
  }

  /**
   * Makes sure a campaign's readiness record is loaded.
   *
   * IT NO LONGER FABRICATES ONE. The previous version created a Draft record on first access with
   * version 1 and no blockers - which is indistinguishable, on screen, from a campaign the server
   * has confirmed is ready to go. A campaign whose readiness has not loaded yet returns
   * `undefined`, and the screen shows its loading state, which is the truth.
   *
   * `ownerReference` IS ACCEPTED AND IGNORED for the callers that still pass it: the owner is the
   * campaign's, and a readiness record cannot name a different one.
   */
  ensure(ref: string, _ownerReference?: string): CampaignReadinessRecord | undefined {
    this.load(ref);
    return this.records()[ref];
  }

  /**
   * Loads the readiness record from the campaign and its checklist.
   *
   * TWO SOURCES, DELIBERATELY. The campaign owns the lifecycle - submitted by whom, approved by
   * whom, at what version - and the checklist owns the blockers. Neither is derivable from the
   * other, and merging them here keeps the screen reading one record.
   */
  load(ref: string): void {
    const campaignId = this.campaigns.apiId(ref);

    if (!campaignId) {
      this.campaigns.refresh();
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);

    // The checklist populates the blockers; it refreshes itself and this store reads whatever it
    // has when the campaign responds.
    this.checklist.load(ref);

    this.api.getCampaign(campaignId).subscribe({
      next: (detail) => {
        const readiness = this.checklist.verdict(ref);

        const record: CampaignReadinessRecord = {
          campaignRef: ref,
          requestState: this.toRequestState(detail.status),
          requestedByRef: detail.submittedByUserId,
          requestedByName: detail.submittedByUserId,
          requestedAt: detail.submittedAtUtc,

          // The campaign's start date IS the planned launch. A second, separate "planned launch
          // time" held only on the readiness record would drift from it, and the one that governs
          // an auto-activating campaign is the start date.
          plannedLaunchTime: detail.startDate ? detail.startDate.slice(0, 16) : '',

          ownerReference: detail.ownerIds[0] ?? '',
          blockers: this.blockersFrom(ref),
          approvedByRef: detail.approvedByUserId,
          approvedByName: detail.approvedByUserId,
          approvedAt: detail.approvedAtUtc,
          decisionReason: null,

          // THE SERVER'S VERSION, not a local counter. This is what makes the conflict state real:
          // an approval carrying a stale version is refused rather than silently overwriting
          // somebody else's decision.
          version: detail.version,
        };

        this.records.update((current) => ({ ...current, [ref]: record }));
        this.isLoading.set(false);

        // Kept in step so a screen reading canLaunch and one reading the record agree.
        if (readiness && readiness.campaignCode !== ref) {
          this.checklist.load(ref);
        }
      },
      error: () => {
        this.isLoading.set(false);
        this.loadError.set('The readiness record could not be loaded.');
      },
    });
  }

  /**
   * Applies a change to the readiness record.
   *
   * MOST FIELDS ON THIS RECORD ARE NOT WRITABLE HERE, and that is the point. Request state,
   * approval and the version belong to the campaign lifecycle endpoint, which enforces the
   * segregation-of-duties rule; a second write path to them would be a way around that check.
   * What this method still does is apply the server's answer locally after such an action, and
   * reload.
   */
  update(ref: string, patch: Partial<CampaignReadinessRecord>): void {
    this.records.update((current) => {
      const existing = current[ref];

      if (!existing) {
        return current;
      }

      // No local version bump. The version is the server's, and inventing one here is precisely
      // what made the old conflict check meaningless.
      return { ...current, [ref]: { ...existing, ...patch } };
    });

    // The planned launch time is the campaign's start date, so a change to it is a campaign edit.
    if (patch.plannedLaunchTime !== undefined) {
      const campaignId = this.campaigns.apiId(ref);
      const current = this.campaigns.get(ref);

      if (campaignId && current) {
        this.campaigns.update(ref, { startDate: patch.plannedLaunchTime.slice(0, 10) });
      }
    }
  }

  /**
   * Raises a blocker.
   *
   * A BLOCKER HANGS OFF A CHECK, not off the campaign. That is what gives it an owner and a
   * resolution, and it is why a blocker raised against a derived dependency - budget, tracking -
   * needs a check to hang from: if there is no check for the thing that is blocked, the blocker
   * has nothing to be resolved against.
   */
  addBlocker(ref: string, blocker: Blocker, onDone?: (outcome: {
    readonly raised: boolean;
    readonly error?: string;
  }) => void): void {
    const checkId = blocker.dependencyKey;
    const owner = blocker.ownerRef || blocker.owner;

    const existing = this.checklist.checksFor(ref).some((check) => check.id === checkId);

    // THE CALLER IS TOLD, and that is the point of `onDone`. The derived dependency cards —
    // Budget, Tracking, Public content and the rest — pass their own key ('budget', 'tracking')
    // as the dependency, and a key is not a readiness check id, so this branch is the one those
    // cards always take. It set `loadError` and returned; the screen had already closed the
    // dialog and announced "Blocker raised" on the line after the call, so the failure was
    // invisible and the blocker simply did not exist.
    if (!existing) {
      const message =
        'A blocker must be raised against a readiness check. Add a check for this dependency '
        + 'first, then raise the blocker against that check.';

      this.loadError.set(message);
      onDone?.({ raised: false, error: message });
      return;
    }

    this.checklist.addBlocker(ref, checkId, owner, blocker.note, (outcome) => {
      this.load(ref);
      onDone?.(outcome);
    });
  }

  /** Resolves a blocker. It is closed with a resolution rather than removed - see the store above. */
  removeBlocker(ref: string, blockerId: string, onDone?: (outcome: {
    readonly resolved: boolean;
    readonly error?: string;
  }) => void): void {
    this.checklist.resolveBlocker(ref, blockerId, 'Resolved from the readiness checklist.', (outcome) => {
      this.load(ref);
      onDone?.(outcome);
    });
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /**
   * The open blockers across a campaign's checks.
   *
   * ONLY THE OPEN ONES. A resolved blocker stays on the record for the audit trail but stops
   * counting against a launch, which is the whole difference between "was blocked" and "is
   * blocked".
   */
  private blockersFrom(ref: string): Blocker[] {
    // READ FROM THE BLOCKER THE SERVER SENT, not inferred from the notes text.
    //
    // This used to select checks whose notes happened to start with the literal 'Blocked.' and
    // then build a blocker whose `id` was the CHECK's id. Both halves were wrong. The prefix is
    // a display convention this client applies itself, so it identified a blocker by a string it
    // had just written; and the id it produced belongs to a readiness check, so
    // POST /readiness-blockers/{id}/resolve — the call behind "Resolve blocker" — answered
    // 404 "That blocker was not found" every time. A blocked check could never be unblocked, so
    // the campaign it blocked could never launch.
    return this.checklist
      .checksFor(ref)
      .filter((check) => !!check.openBlocker)
      .map((check) => ({
        id: check.openBlocker!.id,
        dependencyKey: check.id,
        dependencyLabel: check.name,
        owner: check.openBlocker!.ownerUserId || check.ownerId || 'Unassigned',
        ownerRef: check.openBlocker!.ownerUserId || check.ownerId || '',
        note: check.openBlocker!.note,
        createdByRef: check.openBlocker!.ownerUserId || '',
        createdAt: check.openBlocker!.createdAtUtc,
      }));
  }

  /**
   * The campaign's lifecycle state as a readiness request state.
   *
   * THREE STATES OUT OF NINE, because that is all the readiness screen is asking: has a launch
   * been requested, has it been approved, or neither. An Active or Paused campaign has been
   * approved by definition - it could not be running otherwise.
   */
  private toRequestState(status: string): ReadinessRequestState {
    switch (status) {
      case 'submitted':
        return 'Submitted';
      case 'approved':
      case 'scheduled':
      case 'active':
      case 'paused':
      case 'closed':
      case 'completed':
        return 'Approved';
      default:
        return 'Draft';
    }
  }
}
