import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
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
  private readonly campaigns = inject(CampaignStoreService);
  private readonly checklist = inject(ReadinessChecklistStoreService);

  /** The loaded records, keyed by campaign reference. Empty until a campaign is loaded. */
  private readonly records = signal<Readonly<Record<string, CampaignReadinessRecord>>>({});

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

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
  addBlocker(ref: string, blocker: Blocker): void {
    const checkId = blocker.dependencyKey;
    const owner = blocker.ownerRef || blocker.owner;

    const existing = this.checklist.checksFor(ref).some((check) => check.id === checkId);

    if (!existing) {
      this.loadError.set(
        'A blocker must be raised against a readiness check. Add a check for this dependency first.',
      );
      return;
    }

    this.checklist.addBlocker(ref, checkId, owner, blocker.note);
    this.load(ref);
  }

  /** Resolves a blocker. It is closed with a resolution rather than removed - see the store above. */
  removeBlocker(ref: string, blockerId: string): void {
    this.checklist.resolveBlocker(ref, blockerId, 'Resolved from the readiness checklist.');
    this.load(ref);
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
    return this.checklist
      .checksFor(ref)
      .filter((check) => (check.notes ?? '').startsWith('Blocked.'))
      .map((check) => ({
        id: check.id,
        dependencyKey: check.id,
        dependencyLabel: check.name,
        owner: check.ownerId ?? 'Unassigned',
        ownerRef: check.ownerId ?? '',
        note: (check.notes ?? '').replace(/^Blocked\.\s*/, ''),
        createdByRef: check.ownerId ?? '',
        createdAt: check.dueDate ?? '',
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
