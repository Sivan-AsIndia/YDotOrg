import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CurrentUserService } from '../../../../Service/current-user.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentVerification } from '../../../../Shared/models/payment.model';
import {
  destinationAfterPayment,
  payerKind,
} from '../../../../Shared/services/donation-redirect.policy';

type ResultState = 'checking' | 'confirmed' | 'pending' | 'failed' | 'unknown';

/**
 * Where the donor lands after paying — the return half of the public donation flow.
 *
 * WHY THIS EXISTS AT ALL. Razorpay's payment link took the donor away from us and, with no
 * callback configured, left them on Razorpay's own page. Nothing brought them back, so nothing
 * ever called verify: the donation sat Pending in the queue, no receipt was issued, no e-mail
 * went out and no donor account was created, until an administrator happened to open Support &
 * Retry and press Verify status. The money had moved and the system did not know.
 *
 * IT WORKS ON LOCALHOST, AND THAT IS THE POINT OF THE DESIGN. Two things happen here and neither
 * needs Razorpay to reach us:
 *
 *   1. THE RETURN IS A BROWSER REDIRECT. Razorpay sends the DONOR'S OWN BROWSER to this page and
 *      appends `razorpay_payment_link_reference_id` — our intent reference, set as the link's
 *      ReferenceId when it was created. A browser on the same machine reaches
 *      http://localhost:6700 perfectly well.
 *   2. VERIFICATION IS A PULL. This page asks our server, and our server asks Razorpay
 *      (GET payment_links/{id}). The connection is outbound in both hops.
 *
 * So a development machine with no public address, no tunnel and no registered webhook still
 * completes the whole flow. A webhook is the right thing in production — it also catches the
 * donor who pays and then closes the tab — but it is not a prerequisite for testing.
 *
 * IT NEVER DECIDES THE OUTCOME ITSELF. `razorpay_payment_link_status` arrives on the query
 * string and this page deliberately ignores it as evidence: it is a value in a URL the donor
 * could edit. It is used only to word the "checking" line. The state shown comes from our own
 * verify endpoint, which asked the gateway.
 *
 * PENDING IS POLLED, NOT REPORTED AS FAILURE. Capture can lag the redirect by a second or two,
 * and a donor told "failed" tries again — if the first attempt actually succeeded, they have now
 * given twice. So a pending answer is retried a few times before this settles on "still
 * confirming", which is the honest word for it.
 *
 * A CONFIRMED DONATION DOES NOT END HERE ANY MORE. This page used to render "Thank you" and stop,
 * with no link and no navigation of any kind on the confirmed branch — so whoever had just paid
 * was left on a dead end with the browser's Back button as their only way out, and Back leads to
 * the checkout they have already completed. Confirmation now returns them to the donation screen
 * on a short countdown; only the confirmed branch redirects, because "pending" and "failed" are
 * states somebody has to be able to sit with and read.
 */
@Component({
  selector: 'app-payment-result',
  imports: [CommonModule],
  templateUrl: './payment-result.html',
  styleUrl: './payment-result.css',
})
export class PaymentResultComponent implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly payments = inject(PaymentApiService);

  /**
   * Only used to tell the two audiences of this page apart on the way out.
   *
   * THE SAME SCREEN LIVES AT TWO ADDRESSES and the difference is the shell around it. A member of
   * staff who took the donation came from /app/donations/public-donation-initiation, which draws
   * the sidebar; a donor who scanned a QR code came from /donate, which deliberately does not —
   * and sending them to the /app copy would put them through authGuard and land them on a sign-in
   * form, which is not a thing to show somebody who has just given money.
   */
  private readonly currentUser = inject(CurrentUserService);

  protected readonly state = signal<ResultState>('checking');
  protected readonly verification = signal<PaymentVerification | null>(null);
  protected readonly errorMessage = signal('');
  protected readonly reference = signal('');

  /**
   * How many times a Pending answer is re-asked before this stops.
   *
   * FOUR TRIES OVER ABOUT TWELVE SECONDS. Long enough to cover the usual gap between the donor
   * being redirected and Razorpay marking the payment captured; short enough that somebody is
   * not watching a spinner wondering whether their money is gone.
   */
  private static readonly MaximumAttempts = 4;
  private static readonly RetryDelayMs = 3000;

  /**
   * How long the confirmation is left on screen before this navigates away.
   *
   * FIVE SECONDS, AND IT IS COUNTED DOWN IN FRONT OF THEM. Long enough to read the amount, the
   * receipt line and the donation reference — which is the one value support will ask for — and
   * short enough that nobody is left wondering whether the page has finished. A redirect that
   * fires with no warning reads as the page being yanked away mid-sentence, so the remaining
   * seconds are shown and a button skips the wait.
   */
  private static readonly RedirectSeconds = 5;

  /**
   * The same, for a payment that did not go through.
   *
   * TWICE AS LONG, AND FOR A REASON. The confirmed page says "thank you" and there is nothing to
   * decide; this one says a payment failed and offers Try again, so five seconds is long enough
   * to read the headline and not long enough to act on it. Ten leaves room to press the button
   * before the page moves on by itself.
   */
  private static readonly FailedRedirectSeconds = 10;

  private attempts = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** Ticks the countdown below. Separate from `timer`, which is the verify retry. */
  private countdownTimer: ReturnType<typeof setInterval> | null = null;

  /** Seconds left before the redirect; 0 means no countdown is running. */
  protected readonly redirectIn = signal(0);

  protected readonly checking = computed(() => this.state() === 'checking');

  protected readonly amountLabel = computed(() => {
    const amount = this.verification()?.requestedAmount;
    if (!amount) {
      return '';
    }
    const symbol = amount.currencyCode === 'INR' ? '₹' : '';
    return `${symbol}${amount.amount.toLocaleString('en-IN', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    })}`;
  });

  protected readonly receiptEligible = computed(
    () => this.verification()?.receiptEligibility === 'Eligible',
  );

  constructor() {
    const params = this.route.snapshot.queryParamMap;

    // RAZORPAY'S NAME FIRST, THEN OURS. The gateway appends its own parameter on the redirect;
    // `intent` is what an internal link or a re-opened result page would carry.
    const reference =
      params.get('razorpay_payment_link_reference_id') ??
      params.get('intent') ??
      params.get('intentReference') ??
      '';

    this.reference.set(reference);

    if (!reference) {
      // NOT AN ERROR MESSAGE ABOUT A MISSING PARAMETER. Somebody reaching this page without a
      // reference has bookmarked it or followed a stale link; telling them their donation failed
      // would be false, and telling them about a query-string parameter is meaningless to them.
      this.state.set('unknown');
      return;
    }

    this.verify();
  }

  ngOnDestroy(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
    }
    this.stopCountdown();
  }

  protected verify(): void {
    this.attempts += 1;
    this.state.set('checking');
    this.errorMessage.set('');

    this.payments.verifyPublicPayment(this.reference()).subscribe({
      next: (result) => {
        this.verification.set(result);

        switch (result.backendPaymentState) {
          case 'Confirmed':
            this.state.set('confirmed');
            this.startCountdown(PaymentResultComponent.RedirectSeconds);
            return;

          case 'Failed':
            this.state.set('failed');

            // A FAILURE NOW LEADS SOMEWHERE, which it did not before. The failed branch rendered
            // its message and stopped: a donor whose card was declined was left on a dead-end
            // page with the browser's Back button as their only way out, and Back leads to the
            // checkout they have already tried. The destination differs by who they are - see
            // `leave` - which is why it is on the same countdown as the confirmed branch rather
            // than being a hard-coded navigation here.
            this.startCountdown(PaymentResultComponent.FailedRedirectSeconds);
            return;

          default:
            // Still pending. Ask again shortly, up to the cap.
            if (this.attempts < PaymentResultComponent.MaximumAttempts) {
              this.timer = setTimeout(
                () => this.verify(),
                PaymentResultComponent.RetryDelayMs,
              );
              return;
            }
            this.state.set('pending');
        }
      },

      error: (error: unknown) => {
        this.errorMessage.set(apiErrorMessage(error));

        // A FAILED CHECK IS NOT A FAILED PAYMENT, and must never be shown as one. If we could
        // not reach our own server, the donor's money is in exactly the state it was already in
        // and the honest answer is that we do not yet know.
        this.state.set('pending');
      },
    });
  }

  /** Lets the donor ask again after the automatic attempts have run out. */
  protected checkAgain(): void {
    this.attempts = 0;
    this.verify();
  }

  // =============================================================================================
  // Leaving the page — the confirmed branch only
  // =============================================================================================

  /**
   * Starts the visible countdown that ends in the redirect.
   *
   * GUARDED AGAINST RUNNING TWICE. `verify()` can reach the confirmed branch more than once — the
   * retry poll may already have a pass in flight when Check again is pressed — and a second
   * interval over the same signal would count down at double speed and navigate early.
   */
  private startCountdown(seconds: number): void {
    if (this.countdownTimer !== null) {
      return;
    }

    this.redirectIn.set(seconds);

    this.countdownTimer = setInterval(() => {
      const remaining = this.redirectIn() - 1;
      this.redirectIn.set(remaining);

      if (remaining <= 0) {
        this.leave();
      }
    }, 1000);
  }

  private stopCountdown(): void {
    if (this.countdownTimer !== null) {
      clearInterval(this.countdownTimer);
      this.countdownTimer = null;
    }
  }

  /** The button beside the countdown, for somebody who has read enough and wants to move on. */
  protected continueNow(): void {
    this.leave();
  }

  /**
   * Out of this page, to wherever this person belongs next.
   *
   * THE RULE IS NOT THIS PAGE'S TO INVENT, so it does not. `destinationAfterPayment` holds the
   * four-way rule the payment-flow specification states - donor or lead, paid or not - and both
   * donation forms ask the same function. This page supplies the two facts only it has: WHO paid,
   * which comes from the verification rather than from anything the browser was handed, and
   * WHETHER it was paid.
   *
   * WHERE THE TWO FACTS COME FROM. The SESSION is this browser's own and is the primary
   * decision; `originatedFromLead` is on the verification because only the server knows it - a
   * lead is converted to a Donor by the very payment being verified.
   *
   * `existingDonorMatched` IS NO LONGER CONSULTED, and removing it was a correction rather than a
   * simplification. It is true only when the payer's e-mail matches a donor record, so a
   * signed-in member of staff - an IAM user, not a donor - came back false and was routed to the
   * public donor form having been signed in the whole time.
   *
   * NO `intent` ON THE WAY BACK, deliberately. A donation form reads that parameter on load and
   * reopens the intent it names, offering Continue to payment - which, for an intent that has
   * just been confirmed, is an invitation to pay for the same gift a second time. Arriving clean
   * gives an empty form, which is the only correct next state after a completed donation.
   */
  private leave(): void {
    this.stopCountdown();

    const verification = this.verification();

    // NO VERIFICATION MEANS NO INFORMED DESTINATION. Nothing came back - the endpoint was
    // unreachable, or this ran before the first answer - so the donor is treated as the flow
    // treats anybody it cannot identify: back to the public form, which is open to everyone and
    // where they can see and retry their donation.
    const kind = payerKind({
      // THE SESSION IS THE DECIDING FACT. Both signed-in destinations sit under /app behind the
      // authentication guard, and the lead's is public - so "does this browser hold a session"
      // answers exactly the question the three destinations pose.
      hasSession: this.currentUser.reference() !== '',

      // A DONATION STARTED FROM A LEAD'S OWN LINK STAYS THE LEAD'S, even when a signed-in
      // fundraiser completed it: the lead is the one being converted and invited.
      originatedFromLead: verification?.originatedFromLead,
    });

    const destination = destinationAfterPayment(
      kind,
      this.state() === 'confirmed',

      // ONLY EVER READ ON A LEAD'S FAILURE - see the policy. Passing it unconditionally is safe
      // because the policy is what decides whether it travels, and keeping that decision in one
      // place is the reason this page does not make it.
      this.reference(),
    );

    void this.router.navigate([...destination.commands], destination.extras);
  }
}
