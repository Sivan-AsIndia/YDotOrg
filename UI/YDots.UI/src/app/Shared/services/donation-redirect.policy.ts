import { NavigationExtras } from '@angular/router';

/**
 * Where a donor goes when a payment finishes, in one place.
 *
 * WHY IT IS NOT INLINE ON THE THREE SCREENS THAT NEED IT. The donation flow has two entry points
 * (the public donor form and the in-app donation screen) and one exit point (the result page),
 * and each of them was deciding independently where somebody should land. They disagreed - one
 * sent every confirmed payer to the sign-in page, another sent them to the donation form, and the
 * failure branch navigated nowhere at all. A donor's destination is a rule about the FLOW rather
 * than about any one screen, so it lives once and every screen asks.
 *
 * THE RULE, as the payment-flow specification states it:
 *
 *   Signed in, paid       -> /app/donations/public-donation-initiation
 *   Signed in, not paid   -> /app/donations/payment-event-queue
 *   Not signed in         -> /auth/donor-form
 *
 * THE SESSION DECIDES, AND THAT IS A CORRECTION. This first keyed on whether the payer matched an
 * existing DONOR RECORD - `existingDonorMatched` on the verification - and it was wrong in the
 * most common case there is. That flag is true only when the payer's e-mail matches a row in the
 * donor table. A signed-in member of staff taking a donation is an IAM USER and generally not a
 * donor record at all, so the flag came back false, the payer was classified a lead, and somebody
 * who was signed in the whole time was redirected to the public donor form.
 *
 * A SESSION IS THE THING THAT ACTUALLY DETERMINES WHERE SOMEBODY CAN GO. Both signed-in
 * destinations are under /app, behind the authentication guard; the lead's destination is public.
 * Asking "does this browser hold a session" answers exactly the question the destinations pose,
 * and it cannot be wrong about a person the way a donor-record lookup can.
 *
 * WHY A LEAD NEVER GOES TO AN /app ROUTE, which is the part that is easy to get wrong. A lead is
 * converted to a Donor by the payment that just cleared, is given a login and is e-mailed an
 * activation link - so at the exact moment of this redirect they have an account they cannot yet
 * sign in to. Sending them to /app lands them on a sign-in form no password of theirs opens,
 * having just given money.
 */

/** Which of the two people the flow serves this is. */
export type DonationPayerKind = 'donor' | 'lead';

/** A destination, ready to hand to `Router.navigate`. */
export interface DonationDestination {
  readonly commands: readonly string[];
  readonly extras?: NavigationExtras;
}

/** The three routes this policy can name. Exported so screens do not retype them. */
export const DonationRoutes = {
  /** The in-app donation screen. Behind the authentication guard. */
  InAppDonation: '/app/donations/public-donation-initiation',

  /** The unified payments and receipts queue. Behind the authentication guard AND a permission. */
  PaymentEventQueue: '/app/donations/payment-event-queue',

  /** The public donor form. Anonymous, and the only one of the three that is. */
  PublicDonorForm: '/auth/donor-form',

  /**
   * Sign in.
   *
   * NOT A DESTINATION THIS POLICY EVER RETURNS - a payment that has finished never routes
   * through sign-in, which is why the donor branch that used to do so is gone. It is named here
   * because the donor FORM sends somebody here BEFORE they pay, when the address they typed
   * already has an account, and the two halves of the flow should not spell the route
   * differently.
   */
  SignIn: '/auth/sign-in',
} as const;

/**
 * Decides which of the two people finished this payment.
 *
 * `hasSession` IS THE ANSWER, and everything else is a tie-breaker on top of it. Somebody signed
 * in belongs in the application; somebody who is not belongs on the public form, because that is
 * the only one of the three routes they can open.
 *
 * `originatedFromLead` OVERRIDES A SESSION, and only in that direction. A fundraiser signed in
 * and capturing a donation against a LEAD's own link is completing that lead's donation - the
 * lead is the one being converted and invited, and the flow document keeps that gift on the
 * public form. It never promotes somebody to donor: a browser with no session cannot open /app
 * however the donation was started.
 */
export function payerKind(origin: {
  readonly hasSession: boolean;
  readonly originatedFromLead?: boolean;
}): DonationPayerKind {
  if (origin.originatedFromLead) {
    return 'lead';
  }

  return origin.hasSession ? 'donor' : 'lead';
}

/**
 * Where this payer goes now.
 *
 * `intentReference` travels on the lead's FAILURE branch only - see below.
 */
export function destinationAfterPayment(
  kind: DonationPayerKind,
  paid: boolean,
  intentReference?: string,
): DonationDestination {
  if (kind === 'lead') {
    // BOTH OUTCOMES GO TO THE SAME PLACE, and that is the answer rather than an omission. The
    // public form is where a lead can read what happened and try again; the payments queue is a
    // staff screen they cannot open, and leaving them on the result page is a dead end.
    //
    // THE REFERENCE TRAVELS ON A FAILURE AND NEVER ON A SUCCESS, which is the one asymmetry in
    // this function and the most important line in it. The donation form reopens the intent named
    // on `?intent=` and offers to pay it - which, after a FAILED attempt, is exactly the retry
    // the person wants and saves them retyping a form they have already filled in. After a
    // CONFIRMED one it is an invitation to pay for the same gift twice.
    if (!paid && intentReference) {
      return {
        commands: [DonationRoutes.PublicDonorForm],
        extras: { queryParams: { intent: intentReference } },
      };
    }

    return { commands: [DonationRoutes.PublicDonorForm] };
  }

  // SIGNED IN, so both destinations are reachable and no sign-in detour is needed. The previous
  // version routed a "donor" without a session through /auth/sign-in with a returnUrl; that
  // branch is gone with the flag that produced it, because a payer with no session is now a lead
  // by definition and never reaches this line.
  return {
    commands: [paid ? DonationRoutes.InAppDonation : DonationRoutes.PaymentEventQueue],
  };
}
