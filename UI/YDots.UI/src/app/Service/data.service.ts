import { Injectable, inject } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { PaymentVerificationRecord } from '../Shared/models/payment-verification.model';
import { PaymentEventRecord, PaymentStatus } from '../Shared/models/payment-event-queue.model';
import { RccRefundCaseRecord } from '../Shared/models/refund-chargeback-case.model';
import {
  DonationIntentListItem,
  DonationIntentSearchFilter,
  PublicDonationFormConfig,
} from '../Shared/models/payment.model';
import {
  formatMoment,
  DonationIntentScreenRecord,
  toChargebackCaseRecord,
  toIntentScreenRecord,
  toPaymentEventRecord,
  toRefundCaseRecord,
} from '../Shared/models/payment-adapters';
import { PaymentApiService } from './payment-api.service';

/**
 * The read side of the eight Donations and Payments screens.
 *
 * WHAT THIS USED TO BE, AND WHY IT MATTERED. Every method here fetched a static JSON file from
 * `assets/data/donations-payments/`, held it in a field, and let the screens mutate that field in
 * place. Three consequences followed and all three were real:
 *
 *   - NOTHING WAS EVER SAVED. A receipt "issued" on the register existed until the tab was
 *     refreshed and no further. The register and the API disagreed permanently.
 *   - THE DATA WAS THE SAME FOR EVERY ORGANISATION AND EVERY USER, because a file on the web
 *     server has no idea who asked for it. Tenant isolation stopped at the API boundary.
 *   - Two screens showing the same donation could disagree, because each held its own copy.
 *
 * Every method now calls the payments API through <see cref="PaymentApiService"/> and adapts the
 * response into the view model the screen already binds to - so the templates, the theme and the
 * responsive layout are untouched, and the numbers on them are real.
 *
 * THE CACHES ARE GONE FROM THE READ PATH, deliberately, and that is a change of behaviour worth
 * stating. Every other register on the platform can afford to cache; a donation register cannot.
 * A payment captured thirty seconds ago must appear on the next refresh, and a refund approved by
 * a colleague must not be invisible because this tab is holding an older answer. The `*Cache`
 * accessors survive because several screens call them, and they now return null - which those
 * screens already handle by fetching.
 *
 * THE `pending*` HAND-OFF FIELDS SURVIVE UNCHANGED. They carry a selected record from one screen
 * to the next during a navigation - the event queue to the verification page, the intent detail
 * to safe retry - which is genuine per-session UI state rather than data, and belongs on the
 * client.
 */
@Injectable({ providedIn: 'root' })
export class DataService {
  private readonly payments = inject(PaymentApiService);

  /**
   * How many rows a screen's first page asks for.
   *
   * The screens page in memory over whatever they are given, so this is the working set rather
   * than a page size. Capped because an unbounded fetch of a busy organisation's donation history
   * is both a slow screen and more donor data in the browser than any one view needs.
   */
  private static readonly WorkingSetSize = 200;

  // =========================================================================================
  // Public donation initiation and donation intents
  // =========================================================================================

  /**
   * The public donation form's presentation configuration.
   *
   * WHAT IS HERE AND WHAT IS DELIBERATELY NOT. The page copy, the currency list and the amount
   * ceiling are PRESENTATION and belong on the client; they used to come from a JSON file for no
   * reason other than that everything else did.
   *
   * THE CAMPAIGN LIST IS EMPTY, and that is the important change. A public donation page must not
   * offer a stranger a list of campaigns to choose from - which charity's campaigns would it be?
   * The campaign is resolved by the API from the tracking reference in the link the donor
   * followed, and that resolution is also what decides which organisation the gift belongs to. A
   * list here would have offered every organisation's campaigns to everybody.
   *
   * THE PERMISSIONS ARE ALL TRUE because a public donor has no account and no permissions to
   * check. Whether they may actually donate is decided by the API from the link they arrived on -
   * an inactive tracking asset or a closed campaign is refused there, where it cannot be bypassed.
   */
  getPublicDonationInitiationData(): Observable<PublicDonationFormConfig> {
    return of({
      // THE DOCUMENT'S OWN WORDING, and it has to be, because this overwrites the component's
      // signals on load. Fig 1 and Fig 2 of the YDot Donation Flow guide both head this screen
      // "Public donation initiation" with the subtitle below - the component's defaults already
      // said exactly that, and these three values were quietly replacing them at runtime with a
      // different title, a truncated time zone and a consent version that names no policy.
      pageTitle: 'Public donation initiation',
      pageSubtitle: 'Collect minimum identity, amount and consent before creating a unique intent.',

      // THE FULL ZONE, as both figures print it. "IST" alone does not say which offset applies,
      // and this line sits directly above a form that records a legally significant consent
      // timestamp.
      operatingTimeZone: 'Asia/Kolkata · IST (UTC+05:30)',

      // The consent wording's version, recorded on the intent so a consent given today can be
      // told apart from one given under different wording last year. It is rendered verbatim
      // into "I acknowledge the …", so it names the documents rather than carrying a bare 'v1'.
      consentPolicyVersion: 'Privacy Notice v3.2 · Consent Terms v1.4',

      campaigns: [],

      currencies: [
        { reference: 'INR', label: 'INR - Indian Rupee' },
        { reference: 'USD', label: 'USD - US Dollar' },
        { reference: 'GBP', label: 'GBP - Pound Sterling' },
        { reference: 'EUR', label: 'EUR - Euro' },
      ],

      // THE APPROVED ADMINISTRATIVE GEOGRAPHY, AND IT HAS TO BE HARD-CODED HERE. The picker on
      // the form was fed an empty array, so "Select approved administrative geography" was the
      // only line it ever showed - a control the screen asks people to answer and that could
      // not be answered.
      //
      // IT DOES NOT COME FROM THE MASTERS API, deliberately. `GET /masters/reference-data` is
      // gated on the GlobalMaster section permission and refuses an anonymous caller outright,
      // and this same form is served to a donor who followed a QR code with no session at all.
      // Calling it would give a stranger a 401 and a staff Initiator without Masters rights a
      // 403 - both of them an empty dropdown again, with a failed request behind it.
      //
      // The list is the approved administrative catalogue the lead capture screen already works
      // from, and it is presentation for the same reason the currency list above is: what the
      // form OFFERS is a client concern; what it may RECORD is decided by the API.
      geographies: [
        { reference: 'IN-TN', label: 'India · Tamil Nadu' },
        { reference: 'IN-KA', label: 'India · Karnataka' },
        { reference: 'IN-KL', label: 'India · Kerala' },
        { reference: 'IN-AP', label: 'India · Andhra Pradesh' },
        { reference: 'IN-TG', label: 'India · Telangana' },
        { reference: 'IN-PY', label: 'India · Puducherry' },
        { reference: 'IN-MH', label: 'India · Maharashtra' },
        { reference: 'IN-DL', label: 'India · Delhi' },
        { reference: 'IN-GJ', label: 'India · Gujarat' },
        { reference: 'IN-WB', label: 'India · West Bengal' },
      ],

      permissions: { view: true, submit: true, continueToPayment: true },

      // A ceiling on the form, not a rule. The API has its own and enforces it; this only stops
      // an obvious typo - an extra zero - reaching the gateway at all.
      maxDonationAmount: 10_000_000,
    });
  }

  /** Kept because the public form calls it after a submission. It no longer holds anything. */
  updatePublicDonationInitiationData(_data: unknown): void {
    // Intentionally empty. The intent is persisted by the API; there is nothing to keep here.
  }

  getPublicDonationInitiationCache(): null {
    return null;
  }

  /**
   * The donation intent register.
   *
   * `needsAttention` IS NOT SET HERE. The screen decides whether it is showing everything or the
   * support subset, and passing a filter the screen did not ask for would silently hide rows
   * somebody expected to see.
   */
  getDonationIntentRows(
    filter: DonationIntentSearchFilter = {},
  ): Observable<DonationIntentListItem[]> {
    return this.payments
      .searchIntents({ pageSize: DataService.WorkingSetSize, ...filter })
      .pipe(map((page) => page.items));
  }

  /**
   * The intent detail screen's records, in the shape it binds to.
   *
   * IT FETCHES THE LIST AND THEN EACH DETAIL, which looks expensive and is bounded: the screen
   * shows ONE intent, so it is given the page of intents it can navigate between and the full
   * detail of them. The alternative - a list-only payload - would leave the attempt timeline and
   * the lifecycle history empty, and those are the two things somebody opens this screen for.
   *
   * A DETAIL THAT FAILS IS DROPPED, not fatal. One intent the caller cannot see must not blank
   * the whole screen.
   */
  getDonationIntentsData(
    filter: DonationIntentSearchFilter = {},
  ): Observable<{ intents: DonationIntentScreenRecord[] }> {
    return this.payments.searchIntents({ pageSize: 25, ...filter }).pipe(
      switchMap((page) =>
        page.items.length === 0
          ? of([] as DonationIntentScreenRecord[])
          : forkJoin(
              page.items.map((item) =>
                this.payments.getIntent(item.id).pipe(
                  map(toIntentScreenRecord),
                  catchError(() => of(null)),
                ),
              ),
            ).pipe(
              map((records) =>
                records.filter((record): record is DonationIntentScreenRecord => record !== null),
              ),
            ),
      ),
      map((intents) => ({ intents })),
    );
  }

  updateDonationIntentsData(_data: unknown): void {
    // Intentionally empty. See the class comment: writes go to the API, not to a field.
  }

  getDonationIntentsCache(): null {
    return null;
  }
    private intentExistingDonorFlags = new Map<string, boolean>();

  setIntentExistingDonorFlag(reference: string, isExistingDonor: boolean): void {
    this.intentExistingDonorFlags.set(reference, isExistingDonor);
  }
    getIntentExistingDonorFlag(reference: string): boolean | null {
    return this.intentExistingDonorFlags.get(reference) ?? null;
  }


  // =========================================================================================
  // Payment verification - SCR-PAY-002
  // =========================================================================================

  /**
   * The verification screen's records.
   *
   * IT RETURNS THE SUPPORT QUEUE, mapped, rather than "every payment". Verification is something
   * a person does to a payment whose outcome is in doubt; listing every settled donation on this
   * screen would bury the handful that need looking at.
   *
   * The screen calls `verifyPayment` on the API when somebody actually presses Verify - this is
   * only what populates the list.
   */
  getPaymentVerificationData(): Observable<PaymentVerificationRecord[]> {
    return this.payments
      .getSupportQueue({ pageSize: DataService.WorkingSetSize })
      .pipe(
        map((page) =>
          page.items.map<PaymentVerificationRecord>((item) => ({
            donationReference: item.intentReference,
            requestedAmount: item.amount.amount,
            currency: item.amount.currencyCode,

            // Uncertain outcomes show as Pending rather than Failed. The donor may already have
            // been charged, and telling them it failed would be worse than telling them nothing.
            backendPaymentState: item.requiresVerification
              ? 'Pending'
              : item.status === 'paid'
                ? 'Confirmed'
                : 'Failed',

            lastVerifiedTime: formatMoment(item.lastAttemptAtUtc),
            gatewayReference: item.lastGatewayResultCode ?? '',
            receiptEligibility: item.status === 'paid' ? 'Eligible' : 'Not yet eligible',
            receiptLink: null,
            supportCorrelationReference: item.intentReference,
          })),
        ),
      );
  }

  updatePaymentVerificationData(_data: PaymentVerificationRecord[]): void {
    // Intentionally empty.
  }

  getPaymentVerificationCache(): PaymentVerificationRecord[] | null {
    return null;
  }

  // =========================================================================================
  // Payment event queue - SCR-PAY-003
  // =========================================================================================

  getPaymentEventQueueData(): Observable<PaymentEventRecord[]> {
    return this.payments
      .searchPaymentEvents({ pageSize: DataService.WorkingSetSize })
      .pipe(map((page) => page.items.map(toPaymentEventRecord)));
  }

  updatePaymentEventQueueData(_data: PaymentEventRecord[]): void {
    // Intentionally empty.
  }

  getPaymentEventQueueCache(): PaymentEventRecord[] | null {
    return null;
  }

  /**
   * NO LONGER ADDS ANYTHING.
   *
   * A gateway event is created by a payment provider posting a signed webhook, and by nothing
   * else. The previous implementation let the browser push a row onto the queue, which meant the
   * screen could show an "event" no gateway had ever sent - indistinguishable, once rendered,
   * from one that had.
   *
   * The method survives because the public donation screen calls it after a submission; it is
   * now a no-op, and the row appears on the next refresh once the gateway has actually reported.
   */
  addDonationToPaymentEventQueue(_record: PaymentEventRecord): void {
    // Intentionally empty. See the comment above.
  }

  /**
   * NO LONGER WRITES ANYTHING, and this one is the most important removal in the file.
   *
   * It used to mark a payment Success or Fail from the browser AND auto-generate a receipt with
   * a random number - `REC-2025-` plus four random digits. Three separate problems with that:
   *
   *   - A CLIENT CANNOT DECIDE WHETHER A PAYMENT SUCCEEDED. Only the gateway knows, and the
   *     answer reaches the platform through a signed webhook or a verification call.
   *   - A RANDOM RECEIPT NUMBER IS NOT A RECEIPT NUMBER. Tax receipt numbers must run in an
   *     unbroken per-organisation series; the API allocates them under a row lock precisely so
   *     that two receipts issued in the same instant cannot collide.
   *   - The receipt existed only in this tab, so the donor's copy and the register's copy would
   *     have disagreed the moment anybody refreshed.
   *
   * Payment outcome now comes from `verifyPayment` or from the gateway's webhook, and receipts
   * are issued by `issueReceipt`.
   */
  updatePaymentEventQueueStatus(_eventReference: string, _paymentStatus: PaymentStatus): void {
    // Intentionally empty. See the comment above.
  }

  // =========================================================================================
  // Cross-screen hand-off
  //
  // GENUINE CLIENT STATE, not data. Each field carries a selected record from one screen to the
  // next during a navigation, and lives exactly as long as that navigation.
  // =========================================================================================

  /** The record chosen on the event queue and carried into the payment continuation. */
  private pendingDonationForPayment: PaymentEventRecord | null = null;

  setPendingDonationForPayment(record: PaymentEventRecord): void {
    this.pendingDonationForPayment = record;
  }

  getPendingDonationForPayment(): PaymentEventRecord | null {
    return this.pendingDonationForPayment;
  }

  clearPendingDonationForPayment(): void {
    this.pendingDonationForPayment = null;
  }

  /** The record carried from the event queue into the verification page. */
  private pendingVerificationRecord: PaymentEventRecord | null = null;

  setPendingVerificationRecord(record: PaymentEventRecord): void {
    this.pendingVerificationRecord = record;
  }

  getPendingVerificationRecord(): PaymentEventRecord | null {
    return this.pendingVerificationRecord;
  }

  clearPendingVerificationRecord(): void {
    this.pendingVerificationRecord = null;
  }

  // =========================================================================================
  // THE RECEIPT REGISTER AND THE PAYMENT SUPPORT / SAFE RETRY READS ARE GONE.
  //
  // Both screens have been withdrawn and folded into Payments & Receipts, which reads the
  // payments API directly rather than through this service. What was left here were four
  // register accessors and three support-queue accessors that nothing called - and a read method
  // for a deleted page is not harmless: it is the thing somebody finds when they go looking for
  // how to bring the page back, and it keeps a model file alive that describes a screen that no
  // longer exists.
  //
  // WHAT REPLACED THEM. `PaymentEventQueueComponent` calls `getReceiptRegister` and
  // `searchPaymentEvents` on `PaymentApiService` and merges the two, so a donation's payment
  // status and its receipt status arrive together on one row. Safe retry survives as the
  // `safeRetry` ACTION on that page's detail panel, which is what it always was - a recovery
  // step on a failed payment rather than a screen of its own.
  // =========================================================================================

  // =========================================================================================
  // Refunds and chargebacks - SCR-PAY-006 and SCR-PAY-008
  // =========================================================================================

  /**
   * The combined refund and chargeback register.
   *
   * TWO CALLS, ONE LIST, because the screen shows one register and filters it by case type. They
   * are fetched in parallel and merged newest-first, so a chargeback opened this morning sits
   * above a refund raised last week rather than after every refund.
   */
  getRefundChargebackData(): Observable<RccRefundCaseRecord[]> {
    const refunds$ = this.payments
      .searchRefunds({ pageSize: DataService.WorkingSetSize })
      .pipe(map((page) => page.items.map(toRefundCaseRecord)));

    // CHARGEBACKS FALL BACK TO AN EMPTY LIST rather than failing the screen. The two halves are
    // separately permissioned - a Payment Operations user sees refunds and may not resolve
    // chargebacks - and half a register is far more useful than an error page.
    const chargebacks$ = this.payments
      .searchChargebacks({ pageSize: DataService.WorkingSetSize })
      .pipe(
        map((page) => page.items.map(toChargebackCaseRecord)),
        catchError(() => of([] as RccRefundCaseRecord[])),
      );

    return forkJoin([refunds$, chargebacks$]).pipe(
      // Newest first across BOTH kinds, so a chargeback opened this morning sits above a refund
      // raised last week rather than after every refund.
      map(([refundRows, chargebackRows]) =>
        [...refundRows, ...chargebackRows].sort((left, right) =>
          right.createdIso.localeCompare(left.createdIso),
        ),
      ),
    );
  }

  updateRefundChargebackData(_data: RccRefundCaseRecord[]): void {
    // Intentionally empty.
  }

  getRefundChargebackCache(): RccRefundCaseRecord[] | null {
    return null;
  }
}
