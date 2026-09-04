import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  ChangePaymentGatewayStatusRequest,
  DeletePaymentGatewayConfigurationRequest,
  PaymentGatewayAuditEntry,
  PaymentGatewayAuditFilter,
  PaymentGatewayCatalogue,
  PaymentGatewayConfiguration,
  PaymentGatewayConfigurationFilter,
  PaymentGatewayTestResult,
  UpsertPaymentGatewayConfigurationRequest,
} from '../Shared/models/payment-gateway-config.model';

/**
 * The payment gateway configuration endpoints.
 *
 * THEY LIVE IN IAM, NOT PAY, and the base URL says so. The configuration is administrative
 * settings attached to an Organisation — the same place departments, units and roles are
 * configured — and IAM is the service that owns Organisations. The payments service READS the
 * result at the moment a donation is taken; it does not serve this screen.
 *
 * NO SECRET EVER COMES BACK FROM ANY METHOD HERE. Credentials travel one way, in a PUT body over
 * TLS. What is returned is a masked hint and three has-a-secret flags — see the model. That is
 * also why there is no "reveal" method: there is no endpoint behind one, and adding it would
 * put a merchant secret in the browser's memory and its dev tools for the sake of a convenience
 * nobody needs.
 *
 * NONE OF THESE TAKES AN ORGANISATION FOR A TENANTADMIN. The scope comes from the signed token,
 * and a `tenantId` in a filter is REPLACED by the server rather than validated. Only a
 * SuperAdmin's `tenantId` is honoured.
 */
@Injectable({ providedIn: 'root' })
export class PaymentGatewayConfigApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/configuration/payment-gateways`;

  /**
   * The providers, webhook events and payment methods the form offers.
   *
   * SERVED BY THE API RATHER THAN COMPILED INTO THIS BUNDLE, because the half that matters —
   * whether the payments service has an adapter for a provider — is a fact about the deployed
   * back end. A copy here goes stale the first time an adapter ships.
   */
  getCatalogue(): Observable<PaymentGatewayCatalogue> {
    return this.http
      .get<ApiResponse<PaymentGatewayCatalogue>>(`${this.base}/catalogue`)
      .pipe(map((response) => response.data!));
  }

  search(
    filter: PaymentGatewayConfigurationFilter,
  ): Observable<PagedResponse<PaymentGatewayConfiguration>> {
    return this.http
      .get<ApiResponse<PagedResponse<PaymentGatewayConfiguration>>>(this.base, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  get(id: string): Observable<PaymentGatewayConfiguration> {
    return this.http
      .get<ApiResponse<PaymentGatewayConfiguration>>(`${this.base}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Creates or updates a configuration.
   *
   * ONE VERB FOR BOTH, because the natural key is (organisation, provider, environment) rather
   * than an id this screen holds. `expectedVersion` is what separates them: absent means create,
   * present means "update this version", and a stale one answers 409 rather than overwriting
   * whatever somebody else saved in between.
   */
  save(
    request: UpsertPaymentGatewayConfigurationRequest,
  ): Observable<PaymentGatewayConfiguration> {
    return this.http
      .put<ApiResponse<PaymentGatewayConfiguration>>(this.base, request)
      .pipe(map((response) => response.data!));
  }

  /** Activating one stands the others in the same environment down, server-side. */
  changeStatus(
    id: string,
    request: ChangePaymentGatewayStatusRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.base}/${id}/status`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Reaches the provider with the stored credentials.
   *
   * IT DOES NOT MOVE MONEY. For Razorpay it creates a one-rupee order — the same first call the
   * donation path makes, which reserves nothing and charges nobody. The result is stored on the
   * row whether it passed or failed.
   */
  test(id: string): Observable<PaymentGatewayTestResult> {
    return this.http
      .post<ApiResponse<PaymentGatewayTestResult>>(`${this.base}/${id}/test`, {})
      .pipe(map((response) => response.data!));
  }

  /** Refused by the server while the configuration is the active one. */
  remove(
    id: string,
    request: DeletePaymentGatewayConfigurationRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.base}/${id}`, { body: request })
      .pipe(map((response) => response.data!));
  }

  /** The change log: who changed what, when, from what to what. */
  searchAudit(
    filter: PaymentGatewayAuditFilter,
  ): Observable<PagedResponse<PaymentGatewayAuditEntry>> {
    return this.http
      .get<ApiResponse<PagedResponse<PaymentGatewayAuditEntry>>>(`${this.base}/audit`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * Only the filters that were actually set are sent.
   *
   * An empty `environment=` on the query string is not the same as omitting it: the server would
   * try to parse the empty string as an enum and reject the whole request.
   */
  private toParams(filter: object): HttpParams {
    let params = new HttpParams();

    Object.entries(filter as Record<string, unknown>).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}
