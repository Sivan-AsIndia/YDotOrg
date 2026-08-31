import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../../../Shared/models/api-response.model';
import {
  CityDetail,
  CityListItem,
  CitySearchFilter,
  CountryDetail,
  CountryListItem,
  CountrySearchFilter,
  CreateCityRequest,
  CreateCountryRequest,
  CreateCurrencyRequest,
  CreateStateProvinceRequest,
  CreateTimeZoneRequest,
  CurrencyDetail,
  CurrencyListItem,
  CurrencySearchFilter,
  DeleteMasterRequest,
  GlobalMasterReferenceData,
  MasterLookup,
  MasterSearchFilter,
  MasterStatusChangeRequest,
  StateProvinceDetail,
  StateProvinceListItem,
  StateProvinceSearchFilter,
  TimeZoneDetail,
  TimeZoneListItem,
  TimeZoneSearchFilter,
  UpdateCityRequest,
  UpdateCountryRequest,
  UpdateCurrencyRequest,
  UpdateStateProvinceRequest,
  UpdateTimeZoneRequest,
} from '../../../Shared/models/global-master.model';

/**
 * The five Masters screens' single door to the API.
 *
 * WHAT CHANGED, AND WHY IT MATTERS. This service used to point at
 * `http://localhost:6001/api/v1/Countries` — a hard-coded host, a service that no longer
 * exists, and no bearer token. Two consequences followed from that and both were real bugs:
 * the components did `new MasterService()`, which puts `inject(HttpClient)` outside an
 * injection context and throws NG0203, and the calls carried no `Authorization` header, so
 * every one of them would now answer 401.
 *
 * It is `providedIn: 'root'` and built from `environment.apiBaseUrl` instead. That routes every
 * call through `authInterceptor`, which attaches the token, renews it on a 401 and unwraps the
 * error envelope — so a component here gets the same behaviour as every other screen in the
 * application, and a component must `inject(MasterService)` rather than construct one.
 *
 * THE CATALOGUE IS SHARED, NOT COPIED PER ORGANISATION. Every read returns the seeded platform
 * rows plus whatever this Organisation added for itself; a row from another Organisation is not
 * merely hidden, it is unreachable. `isPlatformRow` on each row says which kind it is, and
 * `permittedActions` on a detail response says what this caller may do to it — so a screen
 * never has to work out for itself whether the edit pencil should be drawn.
 */
@Injectable({ providedIn: 'root' })
export class MasterService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = `${environment.apiBaseUrl}/masters`;
  private readonly countriesUrl = `${this.baseUrl}/countries`;
  private readonly statesUrl = `${this.baseUrl}/states`;
  private readonly citiesUrl = `${this.baseUrl}/cities`;
  private readonly currenciesUrl = `${this.baseUrl}/currencies`;
  private readonly timeZonesUrl = `${this.baseUrl}/timezones`;

  /**
   * The unfiltered reference-data call, cached for the life of the application.
   *
   * Every one of the five screens opens by asking for the same countries, currencies and enum
   * lists, and none of it changes between two page views. `shareReplay` means the second screen
   * a person opens costs nothing. The COUNTRY-FILTERED variant is deliberately not cached — its
   * answer depends on an argument, and caching it would hand the wrong states to the next
   * caller.
   */
  private referenceData$?: Observable<GlobalMasterReferenceData>;

  // =========================================================================================
  // Reference data
  // =========================================================================================

  /**
   * Every dropdown the Masters screens need, in one call.
   *
   * Pass `countryId` on the City form to narrow the state list to one country. Omit it for the
   * grids, whose own filters want every state.
   */
  getReferenceData(countryId?: string): Observable<GlobalMasterReferenceData> {
    if (countryId) {
      return this.http
        .get<ApiResponse<GlobalMasterReferenceData>>(`${this.baseUrl}/reference-data`, {
          params: new HttpParams().set('countryId', countryId),
        })
        .pipe(map((response) => response.data!));
    }

    this.referenceData$ ??= this.http
      .get<ApiResponse<GlobalMasterReferenceData>>(`${this.baseUrl}/reference-data`)
      .pipe(
        map((response) => response.data!),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.referenceData$;
  }

  /**
   * Drops the cached reference data.
   *
   * Call it after adding or retiring a country, state, currency or time zone, so the next form
   * that opens sees the change rather than the list as it stood when the tab was first loaded.
   */
  invalidateReferenceData(): void {
    this.referenceData$ = undefined;
  }

  // =========================================================================================
  // Countries
  // =========================================================================================

  searchCountries(filter: CountrySearchFilter = {}): Observable<PagedResponse<CountryListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<CountryListItem>>>(this.countriesUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getCountry(id: string): Observable<CountryDetail> {
    return this.http
      .get<ApiResponse<CountryDetail>>(`${this.countriesUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  createCountry(request: CreateCountryRequest): Observable<CountryDetail> {
    return this.http
      .post<ApiResponse<CountryDetail>>(this.countriesUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateCountry(id: string, request: UpdateCountryRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.countriesUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  activateCountry(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.countriesUrl}/${id}/activate`, request);
  }

  deactivateCountry(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.countriesUrl}/${id}/deactivate`, request);
  }

  deleteCountry(id: string, request: DeleteMasterRequest): Observable<OutcomeResponse> {
    return this.delete(`${this.countriesUrl}/${id}`, request);
  }

  exportCountries(filter: CountrySearchFilter = {}): Observable<Blob> {
    return this.http.get(`${this.countriesUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  // =========================================================================================
  // States and provinces
  // =========================================================================================

  searchStates(
    filter: StateProvinceSearchFilter = {},
  ): Observable<PagedResponse<StateProvinceListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<StateProvinceListItem>>>(this.statesUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getState(id: string): Observable<StateProvinceDetail> {
    return this.http
      .get<ApiResponse<StateProvinceDetail>>(`${this.statesUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** Active states beneath one country, for the cascading City form. */
  lookupStates(countryId: string): Observable<MasterLookup[]> {
    return this.http
      .get<ApiResponse<MasterLookup[]>>(`${this.statesUrl}/lookup/${countryId}`)
      .pipe(map((response) => response.data ?? []));
  }

  createState(request: CreateStateProvinceRequest): Observable<StateProvinceDetail> {
    return this.http
      .post<ApiResponse<StateProvinceDetail>>(this.statesUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateState(id: string, request: UpdateStateProvinceRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.statesUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  activateState(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.statesUrl}/${id}/activate`, request);
  }

  deactivateState(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.statesUrl}/${id}/deactivate`, request);
  }

  deleteState(id: string, request: DeleteMasterRequest): Observable<OutcomeResponse> {
    return this.delete(`${this.statesUrl}/${id}`, request);
  }

  exportStates(filter: StateProvinceSearchFilter = {}): Observable<Blob> {
    return this.http.get(`${this.statesUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  // =========================================================================================
  // Cities
  // =========================================================================================

  searchCities(filter: CitySearchFilter = {}): Observable<PagedResponse<CityListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<CityListItem>>>(this.citiesUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getCity(id: string): Observable<CityDetail> {
    return this.http
      .get<ApiResponse<CityDetail>>(`${this.citiesUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** Active cities beneath one state, for an address form's third dropdown. */
  lookupCities(stateProvinceId: string): Observable<MasterLookup[]> {
    return this.http
      .get<ApiResponse<MasterLookup[]>>(`${this.citiesUrl}/lookup/${stateProvinceId}`)
      .pipe(map((response) => response.data ?? []));
  }

  createCity(request: CreateCityRequest): Observable<CityDetail> {
    return this.http
      .post<ApiResponse<CityDetail>>(this.citiesUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateCity(id: string, request: UpdateCityRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.citiesUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  activateCity(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.citiesUrl}/${id}/activate`, request);
  }

  deactivateCity(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.citiesUrl}/${id}/deactivate`, request);
  }

  deleteCity(id: string, request: DeleteMasterRequest): Observable<OutcomeResponse> {
    return this.delete(`${this.citiesUrl}/${id}`, request);
  }

  exportCities(filter: CitySearchFilter = {}): Observable<Blob> {
    return this.http.get(`${this.citiesUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  // =========================================================================================
  // Currencies
  // =========================================================================================

  searchCurrencies(filter: CurrencySearchFilter = {}): Observable<PagedResponse<CurrencyListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<CurrencyListItem>>>(this.currenciesUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getCurrency(id: string): Observable<CurrencyDetail> {
    return this.http
      .get<ApiResponse<CurrencyDetail>>(`${this.currenciesUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  createCurrency(request: CreateCurrencyRequest): Observable<CurrencyDetail> {
    return this.http
      .post<ApiResponse<CurrencyDetail>>(this.currenciesUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateCurrency(id: string, request: UpdateCurrencyRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.currenciesUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  activateCurrency(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.currenciesUrl}/${id}/activate`, request);
  }

  deactivateCurrency(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.currenciesUrl}/${id}/deactivate`, request);
  }

  deleteCurrency(id: string, request: DeleteMasterRequest): Observable<OutcomeResponse> {
    return this.delete(`${this.currenciesUrl}/${id}`, request);
  }

  exportCurrencies(filter: CurrencySearchFilter = {}): Observable<Blob> {
    return this.http.get(`${this.currenciesUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  // =========================================================================================
  // Time zones
  // =========================================================================================

  searchTimeZones(filter: TimeZoneSearchFilter = {}): Observable<PagedResponse<TimeZoneListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<TimeZoneListItem>>>(this.timeZonesUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getTimeZone(id: string): Observable<TimeZoneDetail> {
    return this.http
      .get<ApiResponse<TimeZoneDetail>>(`${this.timeZonesUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  createTimeZone(request: CreateTimeZoneRequest): Observable<TimeZoneDetail> {
    return this.http
      .post<ApiResponse<TimeZoneDetail>>(this.timeZonesUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateTimeZone(id: string, request: UpdateTimeZoneRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.timeZonesUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  activateTimeZone(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.timeZonesUrl}/${id}/activate`, request);
  }

  deactivateTimeZone(id: string, request: MasterStatusChangeRequest): Observable<OutcomeResponse> {
    return this.post(`${this.timeZonesUrl}/${id}/deactivate`, request);
  }

  deleteTimeZone(id: string, request: DeleteMasterRequest): Observable<OutcomeResponse> {
    return this.delete(`${this.timeZonesUrl}/${id}`, request);
  }

  exportTimeZones(filter: TimeZoneSearchFilter = {}): Observable<Blob> {
    return this.http.get(`${this.timeZonesUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  // =========================================================================================
  // Shared plumbing
  // =========================================================================================

  private post(url: string, body: unknown): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(url, body)
      .pipe(map((response) => response.data!));
  }

  /**
   * DELETE WITH A BODY, which is unusual enough to be worth stating.
   *
   * Every master delete carries `expectedVersion`, so the server can refuse a delete issued
   * from a stale screen rather than silently removing a row that changed underneath it. A query
   * string would work, but the version belongs with the other command fields and Angular's
   * HttpClient supports a delete body directly.
   */
  private delete(url: string, body: unknown): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(url, { body })
      .pipe(map((response) => response.data!));
  }

  /**
   * Turns a filter object into query parameters, dropping anything unset.
   *
   * The empty-string test matters as much as the null one: a cleared search box binds to `''`,
   * and sending `search=` narrows nothing while still busting any server-side cache keyed on
   * the query string.
   */
  private toParams<TFilter extends object>(filter: TFilter): HttpParams {
    let params = new HttpParams();

    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') {
        continue;
      }

      params = params.set(key, String(value));
    }

    return params;
  }
}
