import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response.model';
import {
  CountryLookup,
  CurrencyLookup,
  GeoLookup,
  LanguageLookup,
  LanguageLookupList,
  TimeZoneLookup,
  TimeZoneLookupList,
} from '../models/geo-lookup.model';
import { MasterLookup } from '../models/global-master.model';

/**
 * The one door to Country, State, City, Currency, Time zone and Language for every page that is
 * NOT a Masters admin screen.
 *
 * WHAT THIS REPLACES. Address dropdowns used to be filled three different ways depending on the
 * page: a hard-coded `location.model.ts` array of sixteen Indian states, a literal
 * `countryOptions = ['India']` on lead capture, and a free-text box on organisation setup. All
 * three drifted from the database the moment anybody added a country, and none of them could
 * ever show a country the platform had actually been configured with.
 *
 * They were written that way for a reason, though, and it is worth stating so it is not undone:
 * the only route to the catalogue was `MastersController`, which is gated on the GlobalMaster
 * permission. A person filling in a lead capture form does not hold that permission, so the call
 * would have returned 403 where a country list should have been. `MasterLookupsController` is the
 * fix on the server side — authentication, no permission — and this service is the client half.
 *
 * IT IS SEPARATE FROM `MasterService`, DELIBERATELY. That one serves the five admin screens and
 * every method on it needs a GlobalMaster permission. Pointing an ordinary form at it is what
 * caused the 403s in the first place.
 *
 * NOTHING HERE THROWS. Every request ends in a `catchError` that returns an empty list, or — for
 * time zones — the honest "unfiltered" shape. That is not sloppiness about errors; it is the
 * brief's requirement that a dropdown degrade rather than break. A failed country fetch should
 * leave a form the person can still fill in by hand, not a red banner and a dead page. Genuine
 * failures still reach the console through the interceptor.
 */
@Injectable({ providedIn: 'root' })
export class GeoMasterService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = `${environment.apiBaseUrl}/masters/lookups`;

  /**
   * Caches, keyed by the argument that produced them.
   *
   * The catalogue does not change between two page views, and an address form that reopens
   * should not re-fetch two hundred countries. `shareReplay({ refCount: false })` keeps the
   * value after the last subscriber leaves, which is what makes the SECOND form free.
   *
   * The per-country and per-state maps matter more than the country cache: a person correcting a
   * typo in an address will switch country back and forth several times, and without these that
   * is a request each way.
   */
  private countries$?: Observable<CountryLookup[]>;
  private allTimeZones$?: Observable<TimeZoneLookupList>;
  private readonly statesByCountry = new Map<string, Observable<MasterLookup[]>>();
  private readonly citiesByState = new Map<string, Observable<MasterLookup[]>>();
  private readonly timeZonesByCountry = new Map<string, Observable<TimeZoneLookupList>>();
  private readonly currenciesByCountry = new Map<string, Observable<CurrencyLookup[]>>();
  private allLanguages$?: Observable<LanguageLookupList>;
  private readonly languagesByCountry = new Map<string, Observable<LanguageLookupList>>();

  // =========================================================================================
  // Countries
  // =========================================================================================

  /** Every active country, each carrying its default currency and primary time zone. */
  getCountries(): Observable<CountryLookup[]> {
    this.countries$ ??= this.http
      .get<ApiResponse<CountryLookup[]>>(`${this.baseUrl}/countries`)
      .pipe(
        map((response) => response.data ?? []),
        catchError(() => of<CountryLookup[]>([])),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.countries$;
  }

  // =========================================================================================
  // The cascade
  // =========================================================================================

  /**
   * The states beneath one country — step two of the cascade.
   *
   * An empty `countryId` short-circuits to an empty list WITHOUT a request. A form clearing its
   * country should empty the state box, and asking the server "the states of nothing" is a round
   * trip whose answer is already known.
   */
  getStates(countryId: string | null | undefined): Observable<MasterLookup[]> {
    if (!countryId) {
      return of([]);
    }

    let cached = this.statesByCountry.get(countryId);

    if (!cached) {
      cached = this.http
        .get<ApiResponse<MasterLookup[]>>(`${this.baseUrl}/states`, {
          params: new HttpParams().set('countryId', countryId),
        })
        .pipe(
          map((response) => response.data ?? []),
          catchError(() => of<MasterLookup[]>([])),
          shareReplay({ bufferSize: 1, refCount: false }),
        );

      this.statesByCountry.set(countryId, cached);
    }

    return cached;
  }

  /**
   * The cities beneath one state — step three of the cascade.
   *
   * Falls back to the country's cities when no state is given, which is what the several forms
   * that collect a city but never ask for a state actually need. Neither given is an empty list
   * rather than every city in the catalogue.
   */
  getCities(
    stateProvinceId: string | null | undefined,
    countryId?: string | null,
  ): Observable<MasterLookup[]> {
    if (!stateProvinceId && !countryId) {
      return of([]);
    }

    const key = stateProvinceId ? `s:${stateProvinceId}` : `c:${countryId}`;
    let cached = this.citiesByState.get(key);

    if (!cached) {
      let params = new HttpParams();

      if (stateProvinceId) {
        params = params.set('stateProvinceId', stateProvinceId);
      } else if (countryId) {
        params = params.set('countryId', countryId);
      }

      cached = this.http
        .get<ApiResponse<MasterLookup[]>>(`${this.baseUrl}/cities`, { params })
        .pipe(
          map((response) => response.data ?? []),
          catchError(() => of<MasterLookup[]>([])),
          shareReplay({ bufferSize: 1, refCount: false }),
        );

      this.citiesByState.set(key, cached);
    }

    return cached;
  }

  // =========================================================================================
  // Currencies and time zones
  // =========================================================================================

  /**
   * The currencies, with one country's default flagged and sorted first.
   *
   * The country ORDERS this list, it does not narrow it — an Indian organisation taking a
   * donation in USD is ordinary, and a picker that hid every currency but INR would make that
   * impossible to record. Pass null on a page with no country in play.
   */
  getCurrencies(countryId?: string | null): Observable<CurrencyLookup[]> {
    const key = countryId ?? '';
    let cached = this.currenciesByCountry.get(key);

    if (!cached) {
      const params = countryId ? new HttpParams().set('countryId', countryId) : new HttpParams();

      cached = this.http
        .get<ApiResponse<CurrencyLookup[]>>(`${this.baseUrl}/currencies`, { params })
        .pipe(
          map((response) => response.data ?? []),
          catchError(() => of<CurrencyLookup[]>([])),
          shareReplay({ bufferSize: 1, refCount: false }),
        );

      this.currenciesByCountry.set(key, cached);
    }

    return cached;
  }

  /**
   * The time zones, narrowed to a country's own when it has any mapped.
   *
   * THIS IS THE METHOD THE BRIEF TURNS ON, so what each call does is worth being exact about:
   *
   * - `getTimeZones()` with nothing — the whole catalogue, `isCountryFiltered: false`. This is
   *   the page that needs a time zone and never asks for a country. A SUPPORTED case, not a
   *   degraded one, and it neither throws nor requires a country to be linked first.
   * - `getTimeZones(countryId)` where the country has zones — all of them, primary first,
   *   `isCountryFiltered: true`. The United States returns seven, not one.
   * - `getTimeZones(countryId)` where it has none, or the id is unknown — the whole catalogue,
   *   `isCountryFiltered: false`. Never an empty dropdown: a required field nobody can satisfy
   *   is indistinguishable, to the person filling it in, from the page being broken.
   *
   * Use `isCountryFiltered` to label the field honestly rather than to decide whether to render
   * it. There is always something to render.
   */
  getTimeZones(countryId?: string | null): Observable<TimeZoneLookupList> {
    if (!countryId) {
      this.allTimeZones$ ??= this.fetchTimeZones(null);

      return this.allTimeZones$;
    }

    let cached = this.timeZonesByCountry.get(countryId);

    if (!cached) {
      cached = this.fetchTimeZones(countryId);
      this.timeZonesByCountry.set(countryId, cached);
    }

    return cached;
  }

  /** The time-zone rows alone, for a caller that has no use for the filtered flag. */
  getTimeZoneOptions(countryId?: string | null): Observable<TimeZoneLookup[]> {
    return this.getTimeZones(countryId).pipe(map((result) => result.timeZones));
  }

  // =========================================================================================
  // Languages
  // =========================================================================================

  /**
   * The languages, narrowed to a country's own when it has any mapped.
   *
   * THE SAME THREE CASES AS `getTimeZones`, and the same guarantee — never an empty dropdown:
   *
   * - `getLanguages()` with nothing — the whole catalogue, `isCountryFiltered: false`. The setup
   *   wizard and user creation both collect a language and never a country, so this is the
   *   ordinary case rather than a degraded one.
   * - `getLanguages(countryId)` where the country has languages — all of them, primary first,
   *   `isCountryFiltered: true`. India returns its scheduled set, not just Hindi.
   * - `getLanguages(countryId)` where it has none, or the id is unknown — the whole catalogue,
   *   `isCountryFiltered: false`.
   *
   * BIND AN OPTION'S VALUE TO `cultureCode`, NOT `id`. Every column that stores a language today
   * holds "en-IN" and its API still takes that string.
   */
  getLanguages(countryId?: string | null): Observable<LanguageLookupList> {
    if (!countryId) {
      this.allLanguages$ ??= this.fetchLanguages(null);

      return this.allLanguages$;
    }

    let cached = this.languagesByCountry.get(countryId);

    if (!cached) {
      cached = this.fetchLanguages(countryId);
      this.languagesByCountry.set(countryId, cached);
    }

    return cached;
  }

  /** The language rows alone, for a caller that has no use for the filtered flag. */
  getLanguageOptions(countryId?: string | null): Observable<LanguageLookup[]> {
    return this.getLanguages(countryId).pipe(map((result) => result.languages));
  }

  // =========================================================================================
  // The whole payload
  // =========================================================================================

  /**
   * Every picker in one call, for a form opening cold.
   *
   * Prefer this over five separate calls on first load: one request means one render, where five
   * parallel ones mean the form fills in visibly over several frames and any one failing leaves a
   * box empty with nothing to explain why. Use the individual methods afterwards, for the
   * cascade.
   *
   * NOT CACHED, because its answer depends on two arguments and handing a stale one to the next
   * caller would put another country's states on the form.
   */
  getGeoLookup(countryId?: string | null, stateProvinceId?: string | null): Observable<GeoLookup> {
    let params = new HttpParams();

    if (countryId) {
      params = params.set('countryId', countryId);
    }

    if (stateProvinceId) {
      params = params.set('stateProvinceId', stateProvinceId);
    }

    return this.http.get<ApiResponse<GeoLookup>>(`${this.baseUrl}/geo`, { params }).pipe(
      map((response) => response.data ?? GeoMasterService.emptyGeoLookup()),
      catchError(() => of(GeoMasterService.emptyGeoLookup())),
    );
  }

  // =========================================================================================
  // Helpers a form actually wants
  // =========================================================================================

  /**
   * Resolves a country NAME to its row.
   *
   * THE BRIDGE FOR THE RECORDS THAT ALREADY EXIST. Organisation, user and lead rows stored
   * `country` as a display string — "India" — long before there were ids to store, and an edit
   * form opening on one of those has a name and no id. Matching by name lets that form select the
   * right option instead of opening blank and silently discarding the stored value on save.
   *
   * Compared case-insensitively and against both the name and the ISO code, so "india", "India"
   * and "IN" all land on the same row.
   */
  findCountryByName(
    countries: readonly CountryLookup[],
    value: string | null | undefined,
  ): CountryLookup | undefined {
    if (!value) {
      return undefined;
    }

    const needle = value.trim().toLowerCase();

    return countries.find(
      (country) =>
        country.name.toLowerCase() === needle ||
        country.code.toLowerCase() === needle ||
        country.iso2.toLowerCase() === needle,
    );
  }

  /** Resolves a state or city NAME to its row, for the same reason as `findCountryByName`. */
  findLookupByName(
    options: readonly MasterLookup[],
    value: string | null | undefined,
  ): MasterLookup | undefined {
    if (!value) {
      return undefined;
    }

    const needle = value.trim().toLowerCase();

    return options.find(
      (option) => option.name.toLowerCase() === needle || option.code.toLowerCase() === needle,
    );
  }

  /**
   * Resolves a stored language value to its row.
   *
   * THE BRIDGE FOR THE RECORDS THAT ALREADY EXIST, exactly as `findCountryByName` is. The stored
   * value may be a culture code ("en-IN"), which is what user creation and the setup wizard have
   * always written — or a bare display name ("English", "Tamil"), which is what lead capture's
   * hard-coded list produced. Both are matched, so an edit form opening on either kind of record
   * selects the right option rather than opening blank and discarding the stored value on save.
   */
  findLanguage(
    options: readonly LanguageLookup[],
    value: string | null | undefined,
  ): LanguageLookup | undefined {
    if (!value) {
      return undefined;
    }

    const needle = value.trim().toLowerCase();

    return options.find(
      (language) =>
        language.cultureCode.toLowerCase() === needle ||
        language.code.toLowerCase() === needle ||
        language.name.toLowerCase() === needle ||
        (language.nativeName ?? '').toLowerCase() === needle,
    );
  }

  /**
   * Drops every cache.
   *
   * Call it after a Masters screen adds or retires a country, state, currency or zone, so the
   * next form that opens sees the change rather than the catalogue as it stood when the tab was
   * first loaded.
   */
  invalidate(): void {
    this.countries$ = undefined;
    this.allTimeZones$ = undefined;
    this.statesByCountry.clear();
    this.citiesByState.clear();
    this.timeZonesByCountry.clear();
    this.currenciesByCountry.clear();
    this.allLanguages$ = undefined;
    this.languagesByCountry.clear();
  }

  // =========================================================================================
  // Plumbing
  // =========================================================================================

  private fetchTimeZones(countryId: string | null): Observable<TimeZoneLookupList> {
    const params = countryId ? new HttpParams().set('countryId', countryId) : new HttpParams();

    return this.http
      .get<ApiResponse<TimeZoneLookupList>>(`${this.baseUrl}/timezones`, { params })
      .pipe(
        map((response) => response.data ?? { timeZones: [], isCountryFiltered: false }),
        catchError(() => of<TimeZoneLookupList>({ timeZones: [], isCountryFiltered: false })),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
  }

  private fetchLanguages(countryId: string | null): Observable<LanguageLookupList> {
    const params = countryId ? new HttpParams().set('countryId', countryId) : new HttpParams();

    return this.http
      .get<ApiResponse<LanguageLookupList>>(`${this.baseUrl}/languages`, { params })
      .pipe(
        map((response) => response.data ?? { languages: [], isCountryFiltered: false }),
        catchError(() => of<LanguageLookupList>({ languages: [], isCountryFiltered: false })),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
  }

  private static emptyGeoLookup(): GeoLookup {
    return {
      countries: [],
      stateProvinces: [],
      cities: [],
      currencies: [],
      timeZones: [],
      timeZonesAreCountryFiltered: false,
      languages: [],
      languagesAreCountryFiltered: false,
    };
  }
}
