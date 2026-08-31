import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
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
  setStatus(campaignRef: string, id: string, status: ReadinessCheckStatus, notes?: string): void {
    if (status === 'Pending') {
      return;
    }

    const request = { expectedVersion: this.versionsByCheckId.get(id) ?? 0, notes: notes ?? null };

    const call =
      status === 'Passed'
        ? this.api.passReadinessCheck(id, request)
        : this.api.failReadinessCheck(id, request);

    call.subscribe({
      next: () => this.load(campaignRef),
      error: () => this.failed(campaignRef, 'The verdict could not be recorded.'),
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
   * Raises a blocker against a check.
   *
   * AT MOST ONE OPEN BLOCKER PER CHECK, enforced server-side. Two open blockers on one check means
   * two people each believing the other owns it.
   */
  addBlocker(campaignRef: string, checkId: string, ownerUserId: string, note: string): void {
    this.api
      .addReadinessBlocker(checkId, {
        ownerUserId,
        blockerNote: note,
        expectedVersion: this.versionsByCheckId.get(checkId) ?? 0,
      })
      .subscribe({
        next: () => this.load(campaignRef),
        error: () => this.failed(campaignRef, 'The blocker could not be raised.'),
      });
  }

  resolveBlocker(campaignRef: string, blockerId: string, resolutionNote: string): void {
    this.api.resolveReadinessBlocker(blockerId, { resolutionNote: resolutionNote || null }).subscribe({
      next: () => this.load(campaignRef),
      error: () => this.failed(campaignRef, 'The blocker could not be resolved.'),
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

      status: this.fromApiStatus(item.status),
    };
  }

  private fromApiStatus(status: string): ReadinessCheckStatus {
    return status === 'passed' ? 'Passed' : status === 'failed' ? 'Failed' : 'Pending';
  }

  /**
   * The screen's six categories onto the API's six.
   *
   * THEY ARE NOT THE SAME SIX. The screen groups by launch dependency (Content, Budget, Tracking,
   * Payment, Template, Consent); the API groups by who owns the check (content, compliance,
   * payment, attribution, communications, operations). Budget has no obvious owner category and
   * lands in operations, which is where a budget sign-off actually sits.
   */
  private toApiCategory(category: ReadinessCheckCategory): ApiReadinessCategory {
    switch (category) {
      case 'Content':
        return 'content';
      case 'Payment':
        return 'payment';
      case 'Tracking':
        return 'attribution';
      case 'Template':
        return 'communications';
      case 'Consent':
        return 'compliance';
      case 'Budget':
        return 'operations';
      default:
        return 'operations';
    }
  }

  private fromApiCategory(category: ApiReadinessCategory): ReadinessCheckCategory {
    switch (category) {
      case 'content':
        return 'Content';
      case 'payment':
        return 'Payment';
      case 'attribution':
        return 'Tracking';
      case 'communications':
        return 'Template';
      case 'compliance':
        return 'Consent';
      case 'operations':
        return 'Budget';
      default:
        return 'Content';
    }
  }
}
