import { Injectable, NgZone, inject } from '@angular/core';
import { CheckoutSession, ConfirmCheckoutRequest } from '../models/payment.model';

/**
 * Opens whichever provider's checkout the ORGANISATION is configured for.
 *
 * WHY THIS EXISTS AS A SERVICE RATHER THAN AS CODE ON THE TWO DONATION SCREENS. Both screens used
 * to open Razorpay directly - one of them with a test key compiled into the bundle - which meant
 * the server could route an organisation to any provider it liked and the browser would still
 * draw Razorpay's card form. Provider selection was tenant-wise on the server and hard-coded in
 * the client, so it was not tenant-wise at all.
 *
 * THE SESSION DECIDES, AND ONLY THE SESSION. `CheckoutSession.gatewayName` is the provider the
 * server actually opened the order against - which comes from the `Provider` column of that
 * organisation's payment gateway configuration - and `publicKey` is that organisation's own
 * publishable key. Nothing here holds a key, a provider list per organisation, or a default.
 *
 * ADDING A PROVIDER IS ONE ENTRY IN {@link GatewayCheckoutService.adapters}. It mirrors the
 * server exactly: there, a provider is a new `IPaymentGatewayAdapter` and a registration line;
 * here, it is a new {@link CheckoutAdapter}. The two are keyed by the same string, compared
 * case-insensitively, so a name an administrator types once on the configuration screen reaches
 * both halves.
 *
 * AN UNKNOWN PROVIDER IS NOT AN ERROR AND MUST NOT BE TREATED AS ONE. An organisation whose
 * provider has a server adapter but no in-page checkout here - which is every provider on the day
 * it is added to the server, and any provider that has no browser SDK at all - gets
 * {@link CheckoutOutcome.Unsupported} back, and the caller sends the donor a payment link
 * instead. Refusing the donation because this file has not caught up yet would be the wrong way
 * round.
 *
 * THE CALLBACKS ARE RE-ENTERED THROUGH NgZone. Every provider's SDK calls back from outside
 * Angular, so change detection does not run on its own and a screen updated from one of these
 * would simply not repaint.
 */

/** What {@link GatewayCheckoutService.open} did. */
export type CheckoutOutcome =
  /** The provider's form is on screen. Everything after this arrives on the callbacks. */
  | 'opened'
  /** No in-page checkout for this provider. The caller falls back to a payment link. */
  | 'unsupported'
  /** The provider is known but its SDK could not be loaded or opened. */
  | 'failed';

/** What the caller wants to know about, once the form is open. */
export interface CheckoutCallbacks {
  /**
   * The donor paid and the provider handed back a signed result.
   *
   * NOTHING HERE IS TRUSTED. The argument is exactly what the confirm endpoint takes, and the
   * SERVER checks the signature and asks the provider what actually happened. A browser saying
   * "paid" is not a payment.
   */
  readonly onSucceeded: (confirmation: ConfirmCheckoutRequest) => void;

  /** The provider declined. A real outcome, and the donor is shown it. */
  readonly onFailed: () => void;

  /** The donor closed the form without paying. Not a failure; nothing was charged. */
  readonly onDismissed: () => void;
}

/** One provider's browser half. */
interface CheckoutAdapter {
  /** Matched against `CheckoutSession.gatewayName`, case-insensitively. */
  readonly gatewayName: string;

  /** Where the provider's own SDK is served from. It must be their copy, not a vendored one. */
  readonly scriptUrl: string;

  /** True once the SDK has finished loading and put itself on `window`. */
  readonly isLoaded: () => boolean;

  /** Draws the form. Called only after {@link isLoaded} returns true. */
  readonly open: (
    session: CheckoutSession,
    displayName: string,
    callbacks: CheckoutCallbacks,
    zone: NgZone,
  ) => void;
}

/** Razorpay Checkout, as much of its shape as is used here. */
interface RazorpayCheckoutResponse {
  readonly razorpay_payment_id: string;
  readonly razorpay_order_id: string;
  readonly razorpay_signature: string;
}

interface RazorpayInstance {
  open(): void;
  on(event: string, handler: (payload: unknown) => void): void;
}

type RazorpayConstructor = new (options: Record<string, unknown>) => RazorpayInstance;

@Injectable({ providedIn: 'root' })
export class GatewayCheckoutService {
  private readonly zone = inject(NgZone);

  /**
   * The providers with a browser checkout, keyed by the name the server sends.
   *
   * RAZORPAY IS THE ONLY ENTRY TODAY, and that is a statement about what is implemented rather
   * than a default. An organisation configured for anything else takes the payment-link route,
   * which every server adapter supports, until its entry is added here.
   */
  private readonly adapters: readonly CheckoutAdapter[] = [
    {
      gatewayName: 'Razorpay',
      scriptUrl: 'https://checkout.razorpay.com/v1/checkout.js',
      isLoaded: () => !!(window as unknown as { Razorpay?: RazorpayConstructor }).Razorpay,
      open: (session, displayName, callbacks, zone) => {
        const Razorpay = (window as unknown as { Razorpay?: RazorpayConstructor }).Razorpay!;

        const checkout = new Razorpay({
          // THE ORGANISATION'S OWN PUBLISHABLE KEY, from the session. This is the line that used
          // to read `key: 'rzp_test_…'` on the donor form, which sent every organisation's
          // donations to one test merchant account.
          key: session.publicKey,

          // THE ORDER, AND NO PRICE OF OUR OWN. `amount` and `currency` travel because Razorpay
          // renders them; what is CHARGED is whatever the order held by the provider says, which
          // is why it does not matter that this object is editable in a browser.
          order_id: session.orderReference,
          amount: session.amountMinorUnits,
          currency: session.currencyCode,

          name: displayName,
          description: session.description,

          // What is already known, so nobody retypes it. Razorpay rejects a malformed contact
          // outright, so an empty string is sent rather than a half-remembered one.
          prefill: {
            name: session.donorName,
            email: session.email ?? '',
            contact: session.mobile ?? '',
          },

          notes: { intent_reference: session.intentReference },

          handler: (response: RazorpayCheckoutResponse) =>
            zone.run(() =>
              callbacks.onSucceeded({
                paymentReference: response.razorpay_payment_id,
                orderReference: response.razorpay_order_id,
                signature: response.razorpay_signature,
              }),
            ),

          modal: {
            ondismiss: () => zone.run(() => callbacks.onDismissed()),
            escape: true,
          },
        });

        // A DECLINE IS A REAL OUTCOME AND THE DONOR IS SHOWN IT. Without this the form simply
        // closes on a failed card and the page behind it looks as though nothing happened.
        checkout.on('payment.failed', () => zone.run(() => callbacks.onFailed()));

        checkout.open();
      },
    },
  ];

  /**
   * One in-flight load per provider.
   *
   * KEYED BY PROVIDER, not a single field, because an administrator can switch an organisation's
   * provider between two donations in the same browser session and both scripts then have to be
   * loadable. A REJECTED load is forgotten, so a donor whose first attempt was blocked by a
   * dropped connection or an ad blocker can press the button again and have it genuinely retried
   * rather than handed the same rejected promise for the rest of the session.
   */
  private readonly loading = new Map<string, Promise<void>>();

  /** Whether an in-page checkout exists for the provider this session names. */
  supports(session: CheckoutSession): boolean {
    return this.adapterFor(session.gatewayName) !== undefined;
  }

  /**
   * Opens the configured provider's payment form over the current page.
   *
   * `displayName` heads the form - the campaign, where the screen knows it. It is presentation
   * only; the amount, the currency and the merchant all come from the session.
   */
  async open(
    session: CheckoutSession,
    displayName: string,
    callbacks: CheckoutCallbacks,
  ): Promise<CheckoutOutcome> {
    const adapter = this.adapterFor(session.gatewayName);

    if (!adapter) {
      return 'unsupported';
    }

    try {
      await this.loadScript(adapter);
    } catch {
      return 'failed';
    }

    if (!adapter.isLoaded()) {
      return 'failed';
    }

    try {
      adapter.open(session, displayName, callbacks, this.zone);
      return 'opened';
    } catch {
      return 'failed';
    }
  }

  private adapterFor(gatewayName: string | null | undefined): CheckoutAdapter | undefined {
    const name = (gatewayName ?? '').trim().toLowerCase();

    if (!name) {
      return undefined;
    }

    return this.adapters.find((adapter) => adapter.gatewayName.toLowerCase() === name);
  }

  /**
   * Puts the provider's script on the page, once.
   *
   * LOADED WHEN IT IS NEEDED rather than in index.html. A donor-facing form should not fetch a
   * payment provider's JavaScript - and hand that provider a page view - before anybody has
   * decided to give anything.
   */
  private loadScript(adapter: CheckoutAdapter): Promise<void> {
    const existing = this.loading.get(adapter.gatewayName);

    if (existing) {
      return existing;
    }

    const pending = new Promise<void>((resolve, reject) => {
      if (adapter.isLoaded()) {
        resolve();
        return;
      }

      const script = document.createElement('script');
      script.src = adapter.scriptUrl;
      script.async = true;
      script.onload = () => resolve();

      script.onerror = () => {
        this.loading.delete(adapter.gatewayName);
        reject(new Error('The payment form could not be loaded.'));
      };

      document.body.appendChild(script);
    });

    this.loading.set(adapter.gatewayName, pending);
    return pending;
  }
}
