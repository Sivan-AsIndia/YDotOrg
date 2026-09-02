import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AssignChargebackRequest,
  CancelDonationIntentRequest,
  ChargebackCaseDetail,
  ChargebackCaseListItem,
  ChargebackSearchFilter,
  CorrectReceiptRequest,
  CreateDonationIntentRequest,
  CreatePaymentLinkRequest,
  DecideRefundRequest,
  DismissPaymentEventRequest,
  DonationDetail,
  DonationIntentDetail,
  DonationIntentListItem,
  DonationIntentResponse,
  DonationIntentSearchFilter,
  DonationListItem,
  DonationSearchFilter,
  DonationStatistics,
  ExistingDonorCheckResponse,
  GatewayAccountResponse,
  IssueReceiptRequest,
  PaymentEventDetail,
  PaymentEventListItem,
  PaymentEventSearchFilter,
  PaymentLinkResponse,
  PaymentSupportCase,
  PaymentVerification,
  PaginationRequest,
  ReceiptDetail,
  ReceiptListItem,
  ReceiptRegisterFilter,
  ReceiptRegisterResponse,
  ReceiptSearchFilter,
  ReconcileDonationRequest,
  RecordOfflineDonationRequest,
  RefundCaseDetail,
  RefundCaseListItem,
  RefundSearchFilter,
  RejectRefundRequest,
  RequestRefundRequest,
  ReprocessPaymentEventRequest,
  ResendReceiptRequest,
  ResolveChargebackRequest,
  SafeRetryRequest,
  SafeRetryResponse,
  SubmitChargebackEvidenceRequest,
  UpsertGatewayAccountRequest,
  VerifyPaymentRequest,
  VoidReceiptRequest,
} from '../Shared/models/payment.model';

/**
 * The single door to the Donations and Payments service.
 *
 * IT UNWRAPS THE ENVELOPE AND RETURNS THE PAYLOAD. Every endpoint answers the same six-key
 * `ApiResponse`, so `map((response) => response.data!)` happens once here rather than in each of
 * eight screens. A failure never reaches that map: the HTTP interceptor rethrows the parsed
 * envelope, so a component's `error` callback receives something `apiErrorMessage` understands.
 *
 * TWO BASE PATHS, AND THE DIFFERENCE IS THE POINT.
 *
 *   `staff`  - /api/v1/*. Requires a token, a permission and a resolved organisation.
 *   `public` - /api/public/*. NO TOKEN AT ALL. A donor with a QR code has no account; requiring
 *              one would mean asking somebody to register before they may give money.
 *
 * The public methods are safe for the same reason the server accepts them anonymously: the
 * organisation is resolved from the unguessable reference in the route, never from anything the
 * caller can choose. The auth interceptor is told about `/public/donations` so those calls carry
 * no bearer token and a 401 from them never triggers a token refresh - which would otherwise send
 * an anonymous visitor to the sign-in page in the middle of giving money.
 *
 * NOTHING HERE CACHES. Every other register on the platform can afford a `shareReplay` on its
 * reference data; a donation register cannot. A payment captured thirty seconds ago must appear
 * on the next refresh, and a refund approved by somebody else must not be invisible because this
 * tab is holding an older answer.
 */
@Injectable({ providedIn: 'root' })
export class PaymentApiService {
  private readonly http = inject(HttpClient);

  /** Staff endpoints. Token, permission and organisation all required. */
  private readonly staff = `${environment.paymentApiBaseUrl}/v1`;

  /** The donor-facing flow. No token; the reference in the route is the authorisation. */
  private readonly publicApi = `${environment.paymentApiBaseUrl}/public/donations`;

  // =========================================================================================
  // The public donation flow - sections 11 to 14 and 19 to 23
  // =========================================================================================

  /**
   * Starts a donation.
   *
   * ONE CALL FOR EVERY ENTRY CHANNEL. A QR scan, a website button, an e-mail link and a
   * fundraiser's lead link differ only in the attribution on the request.
   */
  initiateDonation(request: CreateDonationIntentRequest): Observable<DonationIntentResponse> {
    return this.http
      .post<ApiResponse<DonationIntentResponse>>(`${this.publicApi}/initiate`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Section 12: is this donor already known to this charity?
   *
   * IT TAKES THE INTENT REFERENCE, NOT AN E-MAIL. An endpoint that accepted a bare address would
   * be an oracle - type an address, learn whether that person gives to this charity. Working from
   * a reference the caller already holds means they can only ask about their own donation.
   */
  checkExistingDonor(intentReference: string): Observable<ExistingDonorCheckResponse> {
    return this.http
      .post<ApiResponse<ExistingDonorCheckResponse>>(
        `${this.publicApi}/${encodeURIComponent(intentReference)}/check-donor`,
        {},
      )
      .pipe(map((response) => response.data!));
  }

  /** Issues the payment link and opens an attempt. */
  createPaymentLink(
    intentReference: string,
    request: CreatePaymentLinkRequest,
  ): Observable<PaymentLinkResponse> {
    return this.http
      .post<ApiResponse<PaymentLinkResponse>>(
        `${this.publicApi}/${encodeURIComponent(intentReference)}/payment-link`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  /**
   * The donor's own view of their donation, for the result page.
   *
   * ALWAYS MASKED, even though it is the donor's own record: there is no session to prove who is
   * holding the link, so the safe branch is the only branch.
   */
  getPublicIntent(intentReference: string): Observable<DonationIntentDetail> {
    return this.http
      .get<ApiResponse<DonationIntentDetail>>(`${this.publicApi}/${encodeURIComponent(intentReference)}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Asks the gateway what actually happened - the donor's version of SCR-PAY-002.
   *
   * IT NEVER RETRIES. Verification asks; it does not pay. A retry disguised as a check is how a
   * donor gets charged twice.
   */
  verifyPublicPayment(intentReference: string): Observable<PaymentVerification> {
    return this.http
      .post<ApiResponse<PaymentVerification>>(
        `${this.publicApi}/${encodeURIComponent(intentReference)}/verify`,
        {},
      )
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Donation intents - SCR-PAY-001
  // =========================================================================================

  searchIntents(
    filter: DonationIntentSearchFilter,
  ): Observable<PagedResponse<DonationIntentListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<DonationIntentListItem>>>(`${this.staff}/donation-intents`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getIntent(id: string): Observable<DonationIntentDetail> {
    return this.http
      .get<ApiResponse<DonationIntentDetail>>(`${this.staff}/donation-intents/${id}`)
      .pipe(map((response) => response.data!));
  }

  resendPaymentLink(id: string, expectedVersion: number): Observable<PaymentLinkResponse> {
    return this.http
      .post<ApiResponse<PaymentLinkResponse>>(`${this.staff}/donation-intents/${id}/resend-link`, {
        expectedVersion,
      })
      .pipe(map((response) => response.data!));
  }

  cancelIntent(id: string, request: CancelDonationIntentRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/donation-intents/${id}/cancel`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Section 23: the payment support queue.
   *
   * Narrower than "failed": an intent that failed once and was then paid needs nobody. What lands
   * here has exhausted the retry allowance or has an attempt whose outcome is UNKNOWN - the
   * second being the more urgent, and the reason the server sorts those first.
   */
  getSupportQueue(pagination: PaginationRequest): Observable<PagedResponse<PaymentSupportCase>> {
    return this.http
      .get<ApiResponse<PagedResponse<PaymentSupportCase>>>(
        `${this.staff}/donation-intents/support-queue`,
        { params: this.toParams(pagination) },
      )
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Donations
  // =========================================================================================

  searchDonations(filter: DonationSearchFilter): Observable<PagedResponse<DonationListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<DonationListItem>>>(`${this.staff}/donations`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getDonation(id: string): Observable<DonationDetail> {
    return this.http
      .get<ApiResponse<DonationDetail>>(`${this.staff}/donations/${id}`)
      .pipe(map((response) => response.data!));
  }

  getDonationStatistics(): Observable<DonationStatistics> {
    return this.http
      .get<ApiResponse<DonationStatistics>>(`${this.staff}/donations/statistics`)
      .pipe(map((response) => response.data!));
  }

  /**
   * The CSV export.
   *
   * `responseType: 'blob'` because the endpoint returns a FILE rather than the envelope. The
   * caller saves it; the audit reference travels in the X-Export-Reference header, which is why
   * `observe: 'response'` is used rather than taking the body alone.
   */
  exportDonations(filter: DonationSearchFilter): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.staff}/donations/export`, {
        params: this.toParams(filter),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => this.toDownload(response, 'donations.csv')));
  }

  recordOfflineDonation(request: RecordOfflineDonationRequest): Observable<DonationDetail> {
    return this.http
      .post<ApiResponse<DonationDetail>>(`${this.staff}/donations/offline`, request)
      .pipe(map((response) => response.data!));
  }

  reconcileDonation(id: string, request: ReconcileDonationRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/donations/${id}/reconcile`, request)
      .pipe(map((response) => response.data!));
  }

  /** Issues the tax receipt. The amount is not a parameter - see the server's DTO for why. */
  issueReceipt(donationId: string, request: IssueReceiptRequest): Observable<ReceiptDetail> {
    return this.http
      .post<ApiResponse<ReceiptDetail>>(`${this.staff}/donations/${donationId}/receipt`, request)
      .pipe(map((response) => response.data!));
  }

  /** Raises a refund. RAISING IS NOT APPROVING - a different person has to decide it. */
  requestRefund(donationId: string, request: RequestRefundRequest): Observable<RefundCaseDetail> {
    return this.http
      .post<ApiResponse<RefundCaseDetail>>(`${this.staff}/donations/${donationId}/refunds`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Payment verification, the event queue and safe retry
  // =========================================================================================

  verifyPayment(request: VerifyPaymentRequest): Observable<PaymentVerification> {
    return this.http
      .post<ApiResponse<PaymentVerification>>(`${this.staff}/payments/verify`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Safe retry - section 23.
   *
   * NOT A PLAIN RETRY. The server verifies the previous attempt with the gateway first and
   * refuses if it actually succeeded. The `outcome` on the response says which of the four things
   * happened, so the operator can tell the donor something true.
   */
  safeRetry(intentId: string, request: SafeRetryRequest): Observable<SafeRetryResponse> {
    return this.http
      .post<ApiResponse<SafeRetryResponse>>(
        `${this.staff}/payments/intents/${intentId}/safe-retry`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  searchPaymentEvents(
    filter: PaymentEventSearchFilter,
  ): Observable<PagedResponse<PaymentEventListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<PaymentEventListItem>>>(`${this.staff}/payments/events`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getPaymentEvent(id: string): Observable<PaymentEventDetail> {
    return this.http
      .get<ApiResponse<PaymentEventDetail>>(`${this.staff}/payments/events/${id}`)
      .pipe(map((response) => response.data!));
  }

  reprocessPaymentEvent(
    id: string,
    request: ReprocessPaymentEventRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/payments/events/${id}/reprocess`, request)
      .pipe(map((response) => response.data!));
  }

  dismissPaymentEvent(id: string, request: DismissPaymentEventRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/payments/events/${id}/dismiss`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Receipts - SCR-PAY-005
  // =========================================================================================

  searchReceipts(filter: ReceiptSearchFilter): Observable<PagedResponse<ReceiptListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<ReceiptListItem>>>(`${this.staff}/receipts`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * The Receipt Register - rows and totals in one call.
   *
   * NOT `searchReceipts`. That one lists receipts, so a finance user can correct or void one.
   * This is the document's register: it includes failed payments, which have no receipt, because
   * the screen reports what happened to every payment rather than which documents exist.
   */
  getReceiptRegister(filter: ReceiptRegisterFilter): Observable<ReceiptRegisterResponse> {
    return this.http
      .get<ApiResponse<ReceiptRegisterResponse>>(`${this.staff}/receipts/register`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getReceipt(id: string): Observable<ReceiptDetail> {
    return this.http
      .get<ApiResponse<ReceiptDetail>>(`${this.staff}/receipts/${id}`)
      .pipe(map((response) => response.data!));
  }

  exportReceipts(filter: ReceiptSearchFilter): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.staff}/receipts/export`, {
        params: this.toParams(filter),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => this.toDownload(response, 'receipts.csv')));
  }

  /** A correction is a NEW VERSION. The original is never edited. */
  correctReceipt(id: string, request: CorrectReceiptRequest): Observable<ReceiptDetail> {
    return this.http
      .post<ApiResponse<ReceiptDetail>>(`${this.staff}/receipts/${id}/correct`, request)
      .pipe(map((response) => response.data!));
  }

  voidReceipt(id: string, request: VoidReceiptRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/receipts/${id}/void`, request)
      .pipe(map((response) => response.data!));
  }

  resendReceipt(id: string, request: ResendReceiptRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/receipts/${id}/resend`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Refunds and chargebacks - SCR-PAY-006 and SCR-PAY-008
  // =========================================================================================

  searchRefunds(filter: RefundSearchFilter): Observable<PagedResponse<RefundCaseListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<RefundCaseListItem>>>(`${this.staff}/refunds`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getRefund(id: string): Observable<RefundCaseDetail> {
    return this.http
      .get<ApiResponse<RefundCaseDetail>>(`${this.staff}/refunds/${id}`)
      .pipe(map((response) => response.data!));
  }

  exportRefunds(filter: RefundSearchFilter): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.staff}/refunds/export`, {
        params: this.toParams(filter),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => this.toDownload(response, 'refunds.csv')));
  }

  /**
   * Approves a refund, which is what actually sends money back.
   *
   * REFUSED TO THE PERSON WHO RAISED IT, whatever permissions they hold. Check
   * `canPerform(detail.permittedActions, 'Approve')` before drawing the button - the server's
   * answer already folds that in, and a local condition cannot.
   */
  approveRefund(id: string, request: DecideRefundRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/refunds/${id}/approve`, request)
      .pipe(map((response) => response.data!));
  }

  rejectRefund(id: string, request: RejectRefundRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/refunds/${id}/reject`, request)
      .pipe(map((response) => response.data!));
  }

  searchChargebacks(
    filter: ChargebackSearchFilter,
  ): Observable<PagedResponse<ChargebackCaseListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<ChargebackCaseListItem>>>(`${this.staff}/chargebacks`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getChargeback(id: string): Observable<ChargebackCaseDetail> {
    return this.http
      .get<ApiResponse<ChargebackCaseDetail>>(`${this.staff}/chargebacks/${id}`)
      .pipe(map((response) => response.data!));
  }

  assignChargeback(id: string, request: AssignChargebackRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/chargebacks/${id}/assign`, request)
      .pipe(map((response) => response.data!));
  }

  submitChargebackEvidence(
    id: string,
    request: SubmitChargebackEvidenceRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/chargebacks/${id}/evidence`, request)
      .pipe(map((response) => response.data!));
  }

  resolveChargeback(id: string, request: ResolveChargebackRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.staff}/chargebacks/${id}/resolve`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Gateway configuration
  // =========================================================================================

  getGatewayAccounts(): Observable<GatewayAccountResponse[]> {
    return this.http
      .get<ApiResponse<GatewayAccountResponse[]>>(`${this.staff}/gateway-accounts`)
      .pipe(map((response) => response.data ?? []));
  }

  /** An upsert: the natural key is (organisation, gateway, test mode), not an id the caller holds. */
  saveGatewayAccount(request: UpsertGatewayAccountRequest): Observable<GatewayAccountResponse> {
    return this.http
      .put<ApiResponse<GatewayAccountResponse>>(`${this.staff}/gateway-accounts`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /**
   * Sends a blob to the browser as a download, releasing the object URL afterwards.
   *
   * REVOKING MATTERS. An object URL that is never revoked keeps the whole blob alive for the life
   * of the document, and a finance user taking a dozen exports in an afternoon would hold every one
   * of them in memory.
   */
  saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = fileName;
    link.click();

    URL.revokeObjectURL(url);
  }

  /**
   * Turns a filter object into query parameters, dropping anything unset.
   *
   * NULLS AND EMPTY STRINGS ARE OMITTED, not sent. `?status=` binds as an empty string on the
   * server and would filter to nothing rather than to everything - a grid that mysteriously shows
   * no rows the moment somebody clears a dropdown.
   *
   * The generic is `extends object` rather than `Record<string, unknown>` because the filter
   * interfaces have named properties and no index signature, which TypeScript will not assign to
   * the latter.
   */
  private toParams<TFilter extends object>(filter: TFilter): HttpParams {
    let params = new HttpParams();

    for (const [key, value] of Object.entries(filter)) {
      if (value === null || value === undefined || value === '') {
        continue;
      }

      params = params.set(key, String(value));
    }

    return params;
  }

  /**
   * Pulls the file name out of the Content-Disposition header.
   *
   * The server names the file with a timestamp so two exports taken minutes apart do not
   * overwrite one another in the browser's downloads folder. Falling back to a fixed name loses
   * that, which is why the header is read rather than ignored.
   */
  private toDownload(
    response: { body: Blob | null; headers: { get(name: string): string | null } },
    fallbackName: string,
  ): { blob: Blob; fileName: string } {
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);

    return {
      blob: response.body ?? new Blob(),
      fileName: match ? decodeURIComponent(match[1]) : fallbackName,
    };
  }
}
