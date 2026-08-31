import { Injectable, signal } from '@angular/core';
import { AccountRecoveryGuidanceResponse, MfaChallengeResponse } from '../models/auth.model';

/** What the MFA screen needs to carry on from where sign-in stopped. */
export interface MfaHandoff {
  challenge: MfaChallengeResponse;
  rememberDevice: boolean;

  /**
   * Where the person was originally headed, carried across the second-factor step.
   *
   * Without it the returnUrl the guard put on the sign-in URL died the moment sign-in answered
   * `mfaRequired`: the MFA screen navigated to the default landing route, so anybody with a
   * second factor - which is everybody the policy requires one of - lost the deep link they
   * followed and arrived at the dashboard instead. It rides here rather than in the MFA screen's
   * URL for the same reason the challenge token does: what is in the URL is in the history, the
   * Referer and the access log.
   */
  returnUrl?: string | null;
}

/** What the account-unavailable screen needs to explain itself and offer recovery. */
export interface UnavailableHandoff {
  detail: AccountRecoveryGuidanceResponse | null;
  emailOrUsername: string;
}

/**
 * Carries the half-finished sign-in from one screen to the next.
 *
 * WHY NOT PUT THE CHALLENGE TOKEN IN THE URL
 * ------------------------------------------
 * `/auth/mfa?challengeToken=abc…` would look convenient and would be a mistake. A URL lands in
 * browser history, in the `Referer` header of the next outbound request, in server access logs
 * and in any analytics script on the page. The challenge token is a credential: it is proof that
 * the password step already succeeded. It should be no more visible than the password was.
 *
 * WHY THERE IS A sessionStorage COPY
 * ----------------------------------
 * A signal alone lives in memory and dies on F5, which would strand somebody who refreshed the
 * MFA page mid-flow. `sessionStorage` survives a refresh, is scoped to this one tab, and is wiped
 * when the tab closes — the shortest-lived storage that solves the problem. The entry is removed
 * the moment the flow ends, successfully or not.
 */
@Injectable({ providedIn: 'root' })
export class MfaHandoffService {
  private static readonly MFA_KEY = 'ydot.mfaHandoff';
  private static readonly UNAVAILABLE_KEY = 'ydot.unavailableHandoff';

  private readonly mfaState = signal<MfaHandoff | null>(this.read<MfaHandoff>(MfaHandoffService.MFA_KEY));
  private readonly unavailableState = signal<UnavailableHandoff | null>(
    this.read<UnavailableHandoff>(MfaHandoffService.UNAVAILABLE_KEY),
  );

  readonly mfa = this.mfaState.asReadonly();
  readonly unavailable = this.unavailableState.asReadonly();

  // ---- MFA ---------------------------------------------------------------------------------

  store(challenge: MfaChallengeResponse, rememberDevice: boolean, returnUrl?: string | null): void {
    const handoff: MfaHandoff = { challenge, rememberDevice, returnUrl: returnUrl ?? null };
    this.mfaState.set(handoff);
    sessionStorage.setItem(MfaHandoffService.MFA_KEY, JSON.stringify(handoff));
  }

  /** Replaces the challenge after a resend or a switch of method, keeping the same token. */
  updateChallenge(challenge: MfaChallengeResponse): void {
    const current = this.mfaState();
    if (!current) {
      return;
    }

    // The resend endpoint echoes the token back, but a defensive fallback keeps the original if
    // it ever comes back blank — losing it would end the transaction for no reason.
    const merged: MfaHandoff = {
      ...current,
      challenge: { ...challenge, challengeToken: challenge.challengeToken || current.challenge.challengeToken },
    };

    this.mfaState.set(merged);
    sessionStorage.setItem(MfaHandoffService.MFA_KEY, JSON.stringify(merged));
  }

  // ---- Account unavailable ------------------------------------------------------------------

  storeUnavailable(detail: AccountRecoveryGuidanceResponse | null, emailOrUsername: string): void {
    const handoff: UnavailableHandoff = { detail, emailOrUsername };
    this.unavailableState.set(handoff);
    sessionStorage.setItem(MfaHandoffService.UNAVAILABLE_KEY, JSON.stringify(handoff));
  }

  // ---- Clean-up ------------------------------------------------------------------------------

  /** Drops everything. Called when sign-in starts again, and when a flow completes. */
  clear(): void {
    this.mfaState.set(null);
    this.unavailableState.set(null);
    sessionStorage.removeItem(MfaHandoffService.MFA_KEY);
    sessionStorage.removeItem(MfaHandoffService.UNAVAILABLE_KEY);
  }

  private read<T>(key: string): T | null {
    const raw = sessionStorage.getItem(key);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as T;
    } catch {
      sessionStorage.removeItem(key);
      return null;
    }
  }
}
