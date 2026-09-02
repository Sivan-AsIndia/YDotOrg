import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentVerification } from '../../../../Shared/models/payment.model';

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
 */
@Component({
  selector: 'app-payment-result',
  imports: [CommonModule],
  templateUrl: './payment-result.html',
  styleUrl: './payment-result.css',
})
export class PaymentResultComponent implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly payments = inject(PaymentApiService);

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

  private attempts = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;

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
            return;

          case 'Failed':
            this.state.set('failed');
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
}
