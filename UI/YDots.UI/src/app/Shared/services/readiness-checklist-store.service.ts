import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import {
  CampaignReadiness,
  ReadinessCheckCategory as ApiReadinessCategory,
  ReadinessCheckListItem,
} from '../models/campaign-contract.model';
import {
  ReadinessCheck,
  ReadinessCheckCategory,
  ReadinessCheckStatus,
} from '../models/campaign-readiness-checklist.model';
import { CampaignStoreService } from './campaign-store.service';
import { apiErrorMessage } from '../models/api-response.model';

/**
 * The readiness checklist for a campaign.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. The checks lived in a signal keyed by campaign reference and
 * went no further than the browser. Four consequences followed:
 *
 *   - A CHECK AUTHORED ON MONDAY WAS GONE ON TUESDAY, so a checklist assembled before a launch
 *     could not be produced afterwards to show what had been verified.
 *   - THE APPROVER COULD NOT SEE IT. Readiness exists so somebody OTHER than the campaign's author
 *     can confirm it is safe to launch; a checklist visible only to its author answers nobody.
 *   - `setStatus` WROTE 'Passed' DIRECTLY, with no record of who decided it or on what evidence.
 *     Passing a check is a sign-off, and the whole point of a sign-off is that it is attributable.
 *   - REMOVING A CHECK ERASED IT. A check raised and then quietly deleted before launch is exactly
 *     the thing an audit needs to be able to find.
 *
 * IT NOW READS AND WRITES `CAM /api/v1/campaigns/{id}/readiness`. The synchronous surface is kept -
 * the checklist screen reads `checksFor(ref)` from a computed - so reads stay synchronous against
 * the loaded signal and writes go to the API and refresh it.
 *
 * PASS AND FAIL ARE SEPARATE ENDPOINTS WITH SEPARATE PERMISSIONS, which is why `setStatus` routes
 * rather than patching a field: an organisation may well want somebody able to record a problem
 * without being able to declare one solved.
 */
@Injectable({ providedIn: 'root' })
export class ReadinessChecklistStoreService {
  private readonly api = inject(CampaignApiService);
  private readonly organisationScope = inject(OrganisationScopeService);
  private readonly campaigns = inject(CampaignStoreService);

  /** The loaded checks, keyed by campaign reference. Empty until a campaign is loaded. */
  private readonly checks = signal<Readonly<Record<string, readonly ReadinessCheck[]>>>({});

  /** The server's own verdict per campaign, which no screen should try to recompute. */
  private readonly verdicts = signal<Readonly<Record<string, CampaignReadiness>>>({});

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly snapshot = computed(() => this.checks());

  /** The concurrency stamp per check id, sent back on the next write. */
  private readonly versionsByCheckId = new Map<string, number>();

  /** Which campaign each check belongs to, so a write knows what to reload. */
  private readonly campaignRefByCheckId = new Map<string, string>();

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
    this.checks.set({});
    this.verdicts.set({});
    this.loadError.set(null);
    this.versionsByCheckId.clear();
    this.campaignRefByCheckId.clear();
  }


  checksFor(campaignRef: string): readonly ReadinessCheck[] {
    return this.checks()[campaignRef] ?? [];
  }

  /**
   * The server's readiness verdict for a campaign.
   *
   * `canLaunch` IS THE ONLY ANSWER THAT COUNTS. It folds in the required outstanding checks, the
   * open blockers and the organisation's "allow launch with outstanding checks" setting - three
   * things a screen would have to reproduce exactly, and would eventually reproduce wrongly.
   */
  verdict(campaignRef: string): CampaignReadiness | null {
    return this.verdicts()[campaignRef] ?? null;
  }

  /**
   * Loads a campaign's checklist.
   *
   * IDEMPOTENT AND SAFE TO CALL FROM A ROUTE. The checklist screen calls it on entry; every write
   * calls it again afterwards, so the screen always shows what the server stored rather than what
   * the browser hoped it would store.
   */
  load(campaignRef: string): void {
    const campaignId = this.campaigns.apiId(campaignRef);

    if (!campaignId) {
      // The campaign is not in the loaded working set. Refreshing it and stopping is right: a
      // request built on a missing id addresses nothing, and inventing an empty checklist would
      // read as "nothing to check" - the most dangerous possible wrong answer here.
      this.campaigns.refresh();
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.getCampaignReadiness(campaignId).subscribe({
      next: (readiness) => {
        for (const item of readiness.items) {
          this.versionsByCheckId.set(item.id, item.version);
          this.campaignRefByCheckId.set(item.id, campaignRef);
        }

        this.checks.update((current) => ({
          ...current,
          [campaignRef]: readiness.items.map((item) => this.toCheck(item)),
        }));

        this.verdicts.update((current) => ({ ...current, [campaignRef]: readiness }));
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.loadError.set('The readiness checklist could not be loaded.');
      },
    });
  }

  /**
   * Adds a check.
   *
   * THE SUCCESS CRITERIA ARE REQUIRED by the API and that is deliberate: a check whose pass
   * condition is not written down is one that gets passed because somebody wanted to launch.
   */
  addCheck(campaignRef: string, check: ReadinessCheck): void {
    const campaignId = this.campaigns.apiId(campaignRef);

    if (!campaignId) {
      this.loadError.set('The campaign could not be identified.');
      return;
    }

    this.api
      .addReadinessCheck(campaignId, {
        checkName: check.name,
        category: this.toApiCategory(check.category),
        successCriteria: check.successCriteria,
        description: check.description || null,
        requiredForLaunch: check.requiredForLaunch,
        ownerUserId: check.ownerId || null,
        dueDate: check.dueDate || null,
        notes: check.notes || null,
      })
      .subscribe({
        next: () => this.load(campaignRef),
        error: () => this.loadError.set('The readiness check could not be added.'),
      });
  }

  /**
   * Edits a check's configuration.
   *
   * A PUT OF THE WHOLE RECORD, merged from what is loaded, because the API takes the complete
   * configuration rather than a patch - and sending only the changed fields would blank the rest.
   */
  updateCheck(campaignRef: string, id: string, patch: Partial<ReadinessCheck>): void {
    const current = this.checksFor(campaignRef).find((check) => check.id === id);

    if (!current) {
      return;
    }

    const merged = { ...current, ...patch, id };

    this.api
      .updateReadinessCheck(id, {
        expectedVersion: this.versionsByCheckId.get(id) ?? 0,
        checkName: merged.name,
        category: this.toApiCategory(merged.category),
        successCriteria: merged.successCriteria,
        description: merged.description || null,
        requiredForLaunch: merged.requiredForLaunch,
        ownerUserId: merged.ownerId || null,
        dueDate: merged.dueDate || null,
        notes: merged.notes || null,
      })
      .subscribe({
        next: () => this.load(campaignRef),
        error: () => this.failed(campaignRef, 'The readiness check could not be saved.'),
      });
  }

  /**
   * Records a verdict on a check.
   *
   * THIS IS A SIGN-OFF, NOT A FIELD. The server records who decided it and when, and refuses a
   * verdict on a check with an open blocker - because "passed, but there is an unresolved problem
   * against it" is not a state a launch decision should ever be made from.
   *
   * `Pending` IS NOT A VERDICT. There is no endpoint for it and there should not be: a check
   * returns to pending by being reopened, not by somebody withdrawing their own sign-off.
   */
  setStatus(
    campaignRef: string,
    id: string,
    status: ReadinessCheckStatus,
    notes?: string,
    onDone?: (outcome: { readonly recorded: boolean; readonly error?: string }) => void,
  ): void {
    if (status === 'Pending') {
      onDone?.({ recorded: false, error: 'A check cannot be returned to Pending.' });
      return;
    }

    const request = { expectedVersion: this.versionsByCheckId.get(id) ?? 0, notes: notes ?? null };

    const call =
      status === 'Passed'
        ? this.api.passReadinessCheck(id, request)
        : this.api.failReadinessCheck(id, request);

    call.subscribe({
      next: () => {
        this.load(campaignRef);
        onDone?.({ recorded: true });
      },

      // THE CALLER IS TOLD, AND TOLD WHY. `cam.readiness.pass` is an APPROVE permission, so an
      // Initiator does not hold it - and this call answered 403 while the screen, which announced
      // its own success on the line after invoking this, said "marked as passed" and left the
      // card exactly as it was. A refusal a person can act on ("you cannot sign this off", "there
      // is a blocker open against it") was being reported as nothing at all.
      error: (error: unknown) => {
        const message = apiErrorMessage(error, 'The verdict could not be recorded.');
        this.failed(campaignRef, message);
        onDone?.({ recorded: false, error: message });
      },
    });
  }

  /**
   * Retires a check from the checklist.
   *
   * IT IS NOT DELETED, AND THE API HAS NO DELETE. A check that was raised is part of the record of
   * how a launch decision was reached, and a checklist that can be pruned before launch proves
   * nothing about what was actually verified. It is marked as not required instead, so it stops
   * blocking the launch while remaining visible and attributable.
   */
  removeCheck(campaignRef: string, id: string): void {
    this.updateCheck(campaignRef, id, { requiredForLaunch: false });
  }

  /**
   * Deletes a check outright.
   *
   * DISTINCT FROM `removeCheck` ABOVE, which marks a check as not required for launch and leaves
   * it on the list - the closest thing to a delete available while CAM had no delete endpoint.
   * CAM has one now, restricted to a PENDING check with no blocker on it, because a judged check
   * holds somebody's verdict and a blocked one holds somebody's objection. The screen offers this
   * for exactly that case and `removeCheck` remains for the rest.
   */
  deleteCheck(
    campaignRef: string,
    id: string,
    onDone?: (outcome: { readonly deleted: boolean; readonly error?: string }) => void,
  ): void {
    this.api
      .deleteReadinessCheck(id, { expectedVersion: this.versionsByCheckId.get(id) ?? 0 })
      .subscribe({
        next: () => {
          this.load(campaignRef);
          onDone?.({ deleted: true });
        },
        error: (error: unknown) => {
          const message = apiErrorMessage(error, 'That readiness check could not be deleted.');
          this.failed(campaignRef, message);
          onDone?.({ deleted: false, error: message });
        },
      });
  }

  /**
   * Raises a blocker against a check.
   *
   * AT MOST ONE OPEN BLOCKER PER CHECK, enforced server-side. Two open blockers on one check means
   * two people each believing the other owns it.
   */
  addBlocker(
    campaignRef: string,
    checkId: string,
    ownerUserId: string,
    note: string,
    onDone?: (outcome: { readonly raised: boolean; readonly error?: string }) => void,
  ): void {
    this.api
      .addReadinessBlocker(checkId, {
        ownerUserId,
        blockerNote: note,
        expectedVersion: this.versionsByCheckId.get(checkId) ?? 0,
      })
      .subscribe({
        next: () => {
          this.load(campaignRef);
          onDone?.({ raised: true });
        },
        // THE SERVER'S OWN MESSAGE. A blocker is refused for reasons the person can act on -
        // one is already open, the version is stale - and a fixed sentence threw all of that
        // away. `onDone` exists so the calling screen stops announcing success unconditionally
        // on the line after this call.
        error: (error: unknown) => {
          const message = apiErrorMessage(error, 'The blocker could not be raised.');
          this.failed(campaignRef, message);
          onDone?.({ raised: false, error: message });
        },
      });
  }

  resolveBlocker(
    campaignRef: string,
    blockerId: string,
    resolutionNote: string,
    onDone?: (outcome: { readonly resolved: boolean; readonly error?: string }) => void,
  ): void {
    this.api.resolveReadinessBlocker(blockerId, { resolutionNote: resolutionNote || null }).subscribe({
      next: () => {
        this.load(campaignRef);
        onDone?.({ resolved: true });
      },
      error: (error: unknown) => {
        const message = apiErrorMessage(error, 'The blocker could not be resolved.');
        this.failed(campaignRef, message);
        onDone?.({ resolved: false, error: message });
      },
    });
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  private failed(campaignRef: string, message: string): void {
    this.loadError.set(message);
    this.load(campaignRef);
  }

  /**
   * One API check as the screen reads it.
   *
   * SEVERAL SCREEN FIELDS HAVE NO SERVER COUNTERPART - `checkType`, `validationMethod`,
   * `sourceType`, the evidence trio. They describe how a check is meant to be evaluated, which
   * the API models as free-text success criteria rather than as an enum. They are filled with the
   * honest defaults rather than guessed at, because a check labelled 'Integration verified' when
   * nothing verified an integration is worse than one labelled 'Manual confirmation'.
   */
  private toCheck(item: ReadinessCheckListItem): ReadinessCheck {
    const openBlocker = (item.blockers ?? []).find((blocker) => !blocker.isResolved);

    return {
      id: item.id,
      name: item.checkName,
      description: item.description ?? undefined,
      category: this.fromApiCategory(item.category),
      checkType: 'Manual',
      validationMethod: 'Manual confirmation',
      successCriteria: item.successCriteria,
      requiredForLaunch: item.requiredForLaunch,
      ownerId: item.ownerUserId ?? undefined,
      dueDate: item.dueDate ?? undefined,
      sourceType: 'Internal',
      evidenceRequired: false,

      // The notes carry the blocker when there is one, so a row that is holding up a launch says
      // why on the row rather than only in a panel somebody has to open.
      notes: item.hasOpenBlocker
        ? `Blocked. ${item.notes ?? ''}`.trim()
        : (item.notes ?? undefined),

      // THE REAL BLOCKER, WITH ITS REAL ID. Everything downstream that clears a blocker needs
      // one, and until the list projection carried the blockers there was none to pass — so the
      // screen used the check's own id and the resolve call answered 404 every time.
      openBlocker: openBlocker
        ? {
            id: openBlocker.id,
            note: openBlocker.blockerNote,
            ownerUserId: openBlocker.ownerUserId,
            createdAtUtc: openBlocker.createdAtUtc,
          }
        : undefined,

      status: this.fromApiStatus(item.status),
    };
  }

  private fromApiStatus(status: string): ReadinessCheckStatus {
    return status === 'passed' ? 'Passed' : status === 'failed' ? 'Failed' : 'Pending';
  }

  /**
   * The screen's six categories onto the API's six.
   *
   * THEY ARE THE SAME SIX, and the previous mapping said otherwise. It translated Tracking to
   * 'attribution', Template to 'communications', Consent to 'compliance' and Budget to
   * 'operations' — four names the server's `ReadinessCheckCategory` enum does not contain. The
   * API refused all four with 400 "Some of the details are not valid", naming a field the person
   * had filled in correctly; only Content and Payment could be created at all.
   */
  private toApiCategory(category: ReadinessCheckCategory): ApiReadinessCategory {
    switch (category) {
      case 'Content':
        return 'content';
      case 'Budget':
        return 'budget';
      case 'Tracking':
        return 'tracking';
      case 'Payment':
        return 'payment';
      case 'Template':
        return 'template';
      case 'Consent':
        return 'consent';
      default:
        return 'content';
    }
  }

  private fromApiCategory(category: ApiReadinessCategory): ReadinessCheckCategory {
    switch (category) {
      case 'content':
        return 'Content';
      case 'budget':
        return 'Budget';
      case 'tracking':
        return 'Tracking';
      case 'payment':
        return 'Payment';
      case 'template':
        return 'Template';
      case 'consent':
        return 'Consent';
      default:
        return 'Content';
    }
  }
}
