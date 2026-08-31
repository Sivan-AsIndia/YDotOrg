import { Signal, computed, inject, signal } from '@angular/core';
import {
  CountryLookup,
  CurrencyLookup,
  LanguageLookup,
  TimeZoneLookup,
} from '../models/geo-lookup.model';
import { MasterLookup } from '../models/global-master.model';
import { GeoMasterService } from './geo-master.service';

/**
 * A ready-made Country to State to City cascade, for a component to own as a field.
 *
 * WHY A CONTROLLER AND NOT JUST THE SERVICE. Five screens need the same six things — the three
 * option lists, the current selection, "clear the state and city when the country changes", and
 * "match the stored name back to a row when an edit form opens". Written out per screen that is
 * five copies of the same subscription bookkeeping, and the fourth copy is where somebody
 * forgets to clear the city and an organisation gets saved in Chennai, Karnataka.
 *
 * THE OPTION LISTS ARE EXPOSED AS NAMES because that is what the records actually hold. The
 * Organisation, User and Lead tables store `country`, `state` and `city` as display strings from
 * long before there were master ids to store, and their APIs still take strings. So the cascade
 * is driven by the master catalogue but speaks the vocabulary the existing endpoints expect —
 * which is what lets these screens be corrected without a migration of every stored address.
 *
 * The ids are not thrown away, though: `selectedCountry()` gives the whole row, so a form can
 * still read the phone prefix, the default currency and the primary time zone off it.
 *
 * USAGE, inside a component's field initialiser so `inject` has a context:
 *
 * <code>
 * protected readonly geo = createGeoCascade();
 * // template: [options]="geo.countryNames()" (valueChange)="geo.selectCountry($event)"
 * </code>
 */
export class GeoCascadeController {
  private readonly service = inject(GeoMasterService);

  // ---- The raw catalogue rows ----------------------------------------------------------

  readonly countries = signal<readonly CountryLookup[]>([]);
  readonly states = signal<readonly MasterLookup[]>([]);
  readonly cities = signal<readonly MasterLookup[]>([]);
  readonly currencies = signal<readonly CurrencyLookup[]>([]);
  readonly timeZones = signal<readonly TimeZoneLookup[]>([]);
  readonly languages = signal<readonly LanguageLookup[]>([]);

  /**
   * False when the zone list is the whole catalogue rather than the selected country's.
   *
   * A form should use this to LABEL the field, never to hide it — see `GeoMasterService`.
   */
  readonly timeZonesAreCountryFiltered = signal(false);

  /**
   * False when the language list is the whole catalogue rather than the selected country's.
   *
   * Use it to LABEL the field, never to hide it — the same rule as the zone flag above.
   */
  readonly languagesAreCountryFiltered = signal(false);

  /** True until the first country fetch settles, so a form can disable rather than look empty. */
  readonly loading = signal(true);

  // ---- The current selection -----------------------------------------------------------

  readonly selectedCountry = signal<CountryLookup | null>(null);
  readonly selectedState = signal<MasterLookup | null>(null);

  // ---- What a template binds to --------------------------------------------------------

  readonly countryNames: Signal<readonly string[]> = computed(() =>
    this.countries().map((country) => country.name),
  );

  readonly stateNames: Signal<readonly string[]> = computed(() =>
    this.states().map((state) => state.name),
  );

  readonly cityNames: Signal<readonly string[]> = computed(() =>
    this.cities().map((city) => city.name),
  );

  readonly currencyCodes: Signal<readonly string[]> = computed(() =>
    this.currencies().map((currency) => currency.code),
  );

  /** Zone labels already carry their offset: "(+05:30) India Standard Time". */
  readonly timeZoneNames: Signal<readonly string[]> = computed(() =>
    this.timeZones().map((zone) => zone.name),
  );

  /** Language labels pair the English name with the native one: "Hindi — हिन्दी". */
  readonly languageLabels: Signal<readonly string[]> = computed(() =>
    this.languages().map((language) => language.displayLabel),
  );

  /**
   * Plain language names, for the screens that STORE a name rather than a culture code.
   *
   * Lead capture is the case: a lead's `language` is written as a display string and rendered
   * straight back out by the work queue and My Leads, so binding those options to "ta-IN" would
   * put a culture code in front of a fundraiser. The same reasoning as `countryNames` above.
   */
  readonly languageNames: Signal<readonly string[]> = computed(() =>
    this.languages().map((language) => language.name),
  );

  /**
   * Whether a state picker is worth drawing at all.
   *
   * False for Singapore, which genuinely has no subdivisions — a form should fall back to a
   * free-text box rather than show a dropdown that can never be satisfied.
   */
  readonly hasStates = computed(() => {
    const country = this.selectedCountry();

    return country ? country.hasStates && this.states().length > 0 : false;
  });

  constructor() {
    this.service.getCountries().subscribe((countries) => {
      this.countries.set(countries);
      this.loading.set(false);
    });

    // The unfiltered zone and currency lists load straight away, so a page that needs a time
    // zone WITHOUT ever asking for a country has one to offer from the first render.
    this.service.getTimeZones().subscribe((result) => {
      this.timeZones.set(result.timeZones);
      this.timeZonesAreCountryFiltered.set(result.isCountryFiltered);
    });

    this.service.getCurrencies().subscribe((currencies) => this.currencies.set(currencies));

    this.service.getLanguages().subscribe((result) => {
      this.languages.set(result.languages);
      this.languagesAreCountryFiltered.set(result.isCountryFiltered);
    });
  }

  // =========================================================================================
  // Selection
  // =========================================================================================

  /**
   * Picks a country by display name and reloads everything beneath it.
   *
   * THE STATE AND CITY ARE CLEARED, always. Leaving them is how a record ends up with a state
   * that does not exist in the country it claims to be in, and it is invisible on screen because
   * both boxes still look filled in.
   */
  selectCountry(name: string | null | undefined): void {
    const country = this.service.findCountryByName(this.countries(), name) ?? null;

    this.selectedCountry.set(country);
    this.selectedState.set(null);
    this.states.set([]);
    this.cities.set([]);

    if (!country) {
      // Country cleared: fall back to the full zone and currency catalogues rather than emptying
      // them. A half-filled form still has to offer a time zone.
      this.service.getTimeZones().subscribe((result) => {
        this.timeZones.set(result.timeZones);
        this.timeZonesAreCountryFiltered.set(result.isCountryFiltered);
      });

      this.service.getCurrencies().subscribe((currencies) => this.currencies.set(currencies));

      this.service.getLanguages().subscribe((result) => {
        this.languages.set(result.languages);
        this.languagesAreCountryFiltered.set(result.isCountryFiltered);
      });

      return;
    }

    this.service.getStates(country.id).subscribe((states) => this.states.set(states));

    this.service.getTimeZones(country.id).subscribe((result) => {
      this.timeZones.set(result.timeZones);
      this.timeZonesAreCountryFiltered.set(result.isCountryFiltered);
    });

    this.service.getCurrencies(country.id).subscribe((currencies) => this.currencies.set(currencies));

    this.service.getLanguages(country.id).subscribe((result) => {
      this.languages.set(result.languages);
      this.languagesAreCountryFiltered.set(result.isCountryFiltered);
    });
  }

  /** Picks a state by display name and reloads its cities. Clears the city list. */
  selectState(name: string | null | undefined): void {
    const state = this.service.findLookupByName(this.states(), name) ?? null;

    this.selectedState.set(state);
    this.cities.set([]);

    if (!state) {
      return;
    }

    this.service.getCities(state.id).subscribe((cities) => this.cities.set(cities));
  }

  /**
   * Restores a cascade from stored display names, for an edit form opening on an existing record.
   *
   * SEQUENCED RATHER THAN FIRED TOGETHER, because each step needs the one above it to have
   * arrived: the state cannot be matched until the country's states are loaded, and the city not
   * until the state's cities are. Firing all three at once is how an edit form opens with the
   * country filled in and the other two silently blank.
   *
   * Every step degrades on its own. A stored state that no longer exists in the catalogue leaves
   * the state box empty and still loads the country's list, so the person can pick a new one.
   */
  restore(
    countryName: string | null | undefined,
    stateName?: string | null,
    cityName?: string | null,
  ): void {
    const apply = (countries: readonly CountryLookup[]) => {
      const country = this.service.findCountryByName(countries, countryName) ?? null;

      this.selectedCountry.set(country);

      if (!country) {
        return;
      }

      this.service.getTimeZones(country.id).subscribe((result) => {
        this.timeZones.set(result.timeZones);
        this.timeZonesAreCountryFiltered.set(result.isCountryFiltered);
      });

      this.service
        .getCurrencies(country.id)
        .subscribe((currencies) => this.currencies.set(currencies));

      this.service.getLanguages(country.id).subscribe((result) => {
        this.languages.set(result.languages);
        this.languagesAreCountryFiltered.set(result.isCountryFiltered);
      });

      this.service.getStates(country.id).subscribe((states) => {
        this.states.set(states);

        const state = this.service.findLookupByName(states, stateName) ?? null;

        this.selectedState.set(state);

        if (!state) {
          return;
        }

        this.service.getCities(state.id).subscribe((cities) => this.cities.set(cities));
      });
    };

    // The countries may already be cached, in which case this resolves synchronously.
    if (this.countries().length > 0) {
      apply(this.countries());

      return;
    }

    this.service.getCountries().subscribe((countries) => {
      this.countries.set(countries);
      this.loading.set(false);
      apply(countries);
    });

    void cityName;
  }

  // =========================================================================================
  // Reading the selection back
  // =========================================================================================

  /** The country's default currency code, for a form that pre-fills one. */
  defaultCurrencyCode(): string {
    return this.currencies().find((currency) => currency.isDefaultForCountry)?.code
      ?? this.selectedCountry()?.defaultCurrencyCode
      ?? '';
  }

  /**
   * The zone to pre-select for the chosen country.
   *
   * Only offered when the list really is that country's — pre-selecting the first row of an
   * unfiltered catalogue would silently put the record in Honolulu.
   */
  primaryTimeZone(): TimeZoneLookup | null {
    if (!this.timeZonesAreCountryFiltered()) {
      return null;
    }

    return this.timeZones().find((zone) => zone.isPrimaryForCountry) ?? this.timeZones()[0] ?? null;
  }

  /** The dialling prefix for the chosen country, e.g. "+91". */
  phoneCountryCode(): string {
    return this.selectedCountry()?.phoneCountryCode ?? '';
  }

  /**
   * The language to pre-select for the chosen country.
   *
   * Only offered when the list really is that country's, for the same reason
   * `primaryTimeZone` is guarded — pre-selecting the first row of an unfiltered catalogue
   * would silently set a new Organisation's language to whatever sorts first.
   */
  primaryLanguage(): LanguageLookup | null {
    if (!this.languagesAreCountryFiltered()) {
      return null;
    }

    return this.languages().find((language) => language.isPrimaryForCountry)
      ?? this.languages()[0]
      ?? null;
  }

  /**
   * Resolves a stored language value — a culture code or a bare name — to its catalogue row.
   *
   * The bridge for records written before there was a catalogue. See
   * `GeoMasterService.findLanguage`.
   */
  resolveLanguage(value: string | null | undefined): LanguageLookup | undefined {
    return this.service.findLanguage(this.languages(), value);
  }
}

/**
 * Builds a cascade controller. Call it in a component field initialiser, where `inject` works.
 *
 * A factory rather than a `providedIn: 'root'` service because the SELECTION is per form — two
 * screens open at once must not share a chosen country — while the underlying HTTP caching lives
 * in `GeoMasterService`, which is shared and should be.
 */
export function createGeoCascade(): GeoCascadeController {
  return new GeoCascadeController();
}
