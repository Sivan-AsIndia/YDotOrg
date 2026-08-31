import { Injectable, computed, inject } from '@angular/core';
import { DependencyStatus } from '../models/campaign-readiness.model';
import { ReadinessCheck, ReadinessCheckCategory } from '../models/campaign-readiness-checklist.model';
import { ReadinessChecklistStoreService } from './readiness-checklist-store.service';

/**
 * Content, template, payment and consent readiness for a campaign.
 *
 * THIS WAS A STUB, AND IT SAID SO. It held one hard-coded campaign whose notes ended in "(stub)",
 * and a `setReachable(false)` switch for demonstrating the dependency-failure state. Every other
 * campaign got "pending / no record" for all four dependencies. Two problems followed, and the
 * second is the serious one:
 *
 *   - The one seeded campaign showed a FAILED payment dependency - "UPI integration pending
 *     certification" - that no system had reported. A launch held up by that would have been held
 *     up by a string in a bundle.
 *   - Every other campaign showed four pending dependencies forever, so the readiness percentage
 *     could never reach 100 and the four cards became something people learned to ignore. A
 *     checklist that is always partly red teaches its readers that red means nothing.
 *
 * IT NOW DERIVES FROM THE READINESS CHECKLIST, which is where these four dependencies are actually
 * recorded. A campaign with no content check does not show "content is pending"; it shows that
 * nobody has recorded a content check, which is a different statement and the accurate one.
 *
 * THE `unknown` STATUS STILL MEANS WHAT IT MEANT: the backing data could not be read. It is now
 * driven by an actual load failure rather than a manual switch, which is the only way it can tell
 * a screen anything useful.
 */
export interface ContentReadinessRecord {
  readonly campaignRef: string;
  readonly publicContentStatus: DependencyStatus;
  readonly publicContentNote: string;
  readonly templateStatus: DependencyStatus;
  readonly templateNote: string;
  readonly paymentStatus: DependencyStatus;
  readonly paymentNote: string;
  /** The consent notice version a consent check names, when one has been recorded. */
  readonly consentNoticeVersion: string;
  readonly consentPublished: boolean;
}

@Injectable({ providedIn: 'root' })
export class ContentReadinessStoreService {
  private readonly checklist = inject(ReadinessChecklistStoreService);

  /**
   * Whether the readiness data could be read at all.
   *
   * A REAL SIGNAL NOW. It reports whether the last checklist load succeeded, so the screen's
   * dependency-failure state means "the readiness service could not be reached" rather than
   * "somebody flipped a switch". A screen that cannot tell a failed load from an empty checklist
   * will eventually present one as the other.
   */
  readonly serviceReachable = computed(() => this.checklist.loadError() === null);

  /**
   * Retained so the readiness screen's existing call sites keep compiling; it does nothing.
   *
   * REACHABILITY IS NOT SOMETHING A SCREEN DECIDES. It is the outcome of a request, and letting a
   * button set it would let the UI claim a dependency was unreachable when it had answered
   * perfectly well - or, far worse, claim it was reachable when it had not.
   */
  setReachable(_value: boolean): void {
    // Intentionally empty. See the note above.
  }

  /**
   * The four dependencies for one campaign.
   *
   * DERIVED, NEVER INVENTED. Each dependency folds every check in its category: any failed check
   * fails the dependency, any pending check leaves it pending, and a dependency with no checks at
   * all says so rather than claiming to be either ready or blocked.
   */
  get(campaignRef: string): ContentReadinessRecord {
    if (!this.serviceReachable()) {
      return {
        campaignRef,
        publicContentStatus: 'unknown',
        publicContentNote: 'The readiness service could not be reached',
        templateStatus: 'unknown',
        templateNote: 'The readiness service could not be reached',
        paymentStatus: 'unknown',
        paymentNote: 'The readiness service could not be reached',
        consentNoticeVersion: '—',
        consentPublished: false,
      };
    }

    const checks = this.checklist.checksFor(campaignRef);
    const content = this.summarise(checks, 'Content', 'public content');
    const template = this.summarise(checks, 'Template', 'communication template');
    const payment = this.summarise(checks, 'Payment', 'payment');
    const consent = this.summarise(checks, 'Consent', 'consent notice');

    return {
      campaignRef,
      publicContentStatus: content.status,
      publicContentNote: content.note,
      templateStatus: template.status,
      templateNote: template.note,
      paymentStatus: payment.status,
      paymentNote: payment.note,

      // The consent notice version is whatever a consent check names in its success criteria -
      // 'v1.2' and so on. A version cannot be derived, so an unnamed one shows as unrecorded
      // rather than as a plausible-looking default.
      consentNoticeVersion: this.versionFrom(checks) ?? '—',
      consentPublished: consent.status === 'pass',
    };
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /**
   * One category's checks folded into a single verdict.
   *
   * THE ORDER MATTERS: a failure outranks anything pending, and anything pending outranks a pass.
   * Reporting a dependency as ready because most of its checks passed is how a campaign launches
   * with an unresolved problem behind it.
   */
  private summarise(
    checks: readonly ReadinessCheck[],
    category: ReadinessCheckCategory,
    label: string,
  ): { status: DependencyStatus; note: string } {
    const relevant = checks.filter((check) => check.category === category);

    if (relevant.length === 0) {
      return { status: 'pending', note: `No ${label} check has been recorded` };
    }

    const failed = relevant.filter((check) => check.status === 'Failed');

    if (failed.length > 0) {
      return {
        status: 'fail',
        note:
          failed.length === 1
            ? failed[0].name
            : `${failed.length} ${label} checks have failed`,
      };
    }

    const pending = relevant.filter((check) => check.status === 'Pending');

    if (pending.length > 0) {
      return {
        status: 'pending',
        note:
          pending.length === 1
            ? `${pending[0].name} is outstanding`
            : `${pending.length} ${label} checks are outstanding`,
      };
    }

    return {
      status: 'pass',
      note:
        relevant.length === 1
          ? relevant[0].name
          : `All ${relevant.length} ${label} checks have passed`,
    };
  }

  /** A version string named by a consent check, e.g. "v1.2". Null when none names one. */
  private versionFrom(checks: readonly ReadinessCheck[]): string | null {
    for (const check of checks) {
      if (check.category !== 'Consent') {
        continue;
      }

      const match = /\bv\d+(\.\d+)*\b/i.exec(
        `${check.successCriteria} ${check.notes ?? ''} ${check.description ?? ''}`,
      );

      if (match) {
        return match[0];
      }
    }

    return null;
  }
}
