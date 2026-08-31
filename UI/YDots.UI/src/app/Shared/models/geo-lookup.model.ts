import { MasterDataStatus, MasterLookup } from './global-master.model';

/**
 * The address-form picker contracts, mirroring `api/v1/masters/lookups`.
 *
 * SEPARATE FROM `global-master.model.ts` ON PURPOSE. That file types the five Masters ADMIN
 * screens — grids, detail panels, create and update payloads, permitted actions. This file types
 * the far smaller thing every OTHER page needs: what may I pick, and what should the rest of the
 * form become once I have picked it.
 *
 * A page that only needs a country dropdown should not have to import a `CreateCountryRequest`
 * to get one.
 */

/**
 * One option in a country picker.
 *
 * `defaultCurrencyId`, `primaryTimeZoneId` and `timeZoneCount` are the whole reason this is not
 * `MasterLookup`. They let a form react to a country change LOCALLY — pre-select the currency,
 * pre-select the zone, decide whether the zone picker is even a real choice — instead of firing
 * another request and showing the person an empty box while it runs.
 */
export interface CountryLookup {
  id: string;
  code: string;
  name: string;
  iso2: string;
  /** Rendered by the API from the ISO code, so the browser holds no second copy of that mapping. */
  flagEmoji: string;
  phoneCountryCode: string | null;
  /** False for a city-state such as Singapore. A state picker should hide itself rather than sit empty. */
  hasStates: boolean;
  defaultCurrencyId: string | null;
  defaultCurrencyCode: string | null;
  primaryTimeZoneId: string | null;
  /** 1 means the zone can be pre-selected and left alone; more than 1 means the person must be asked. */
  timeZoneCount: number;
  status: MasterDataStatus;
  isPlatformRow: boolean;
  sortOrder: number;
}

/** One option in a time-zone picker. */
export interface TimeZoneLookup {
  id: string;
  code: string;
  /** The IANA key — what to send onward to anything that actually converts a time. */
  ianaKey: string;
  /** Already prefixed with the offset by the API: "(+05:30) India Standard Time". */
  name: string;
  shortName: string | null;
  offsetDisplay: string;
  standardUtcOffsetMinutes: number;
  supportsDaylightSaving: boolean;
  /** Only meaningful when the list was requested for a country. False throughout an unfiltered list. */
  isPrimaryForCountry: boolean;
  isDefaultRecommended: boolean;
  status: MasterDataStatus;
  isPlatformRow: boolean;
  sortOrder: number;
}

/**
 * A time-zone list, plus whether it was actually narrowed to the country asked for.
 *
 * `isCountryFiltered` separates two cases that look identical from the outside: twelve zones
 * because the country observes twelve, and twelve zones because the country had none mapped and
 * the whole catalogue came back instead. A form that cannot tell them apart will either mislabel
 * the field or pre-select a zone on the wrong continent.
 */
export interface TimeZoneLookupList {
  timeZones: TimeZoneLookup[];
  isCountryFiltered: boolean;
}

/** One option in a currency picker. */
export interface CurrencyLookup {
  id: string;
  code: string;
  name: string;
  symbol: string | null;
  /** 0 for JPY. An amount field must reformat when the currency changes, or it shows "¥1,200.00". */
  decimalPlaces: number;
  isDefaultForCountry: boolean;
  status: MasterDataStatus;
  isPlatformRow: boolean;
  sortOrder: number;
}

/**
 * One option in a language picker.
 *
 * `cultureCode` IS THE VALUE TO BIND, NOT `id`. Every column that stores a language on this
 * platform holds a BCP-47 string — the Organisation's `defaultCulture`, a user's
 * `preferredLanguage`, a lead's language — and those APIs still take one. A picker keyed on the
 * id would fail to match any record that already exists, and would silently save something the
 * server cannot read back.
 *
 * `nativeName` is shown beside the English name rather than instead of it: a person choosing
 * their OWN language is looking for the word they recognise, and that is rarely the English one.
 */
export interface LanguageLookup {
  id: string;
  code: string;
  /** The BCP-47 culture code — "en-IN". This is what a form binds and stores. */
  cultureCode: string;
  name: string;
  nativeName: string | null;
  /** "Hindi — हिन्दी", or just the name where the two would be the same word twice. */
  displayLabel: string;
  /** ISO 639-1. Not unique across the catalogue: en-GB and en-IN both carry "en". */
  iso2: string;
  isRightToLeft: boolean;
  /** Only meaningful when the list was requested for a country. False throughout an unfiltered list. */
  isPrimaryForCountry: boolean;
  isOfficialInCountry: boolean;
  isDefaultRecommended: boolean;
  status: MasterDataStatus;
  isPlatformRow: boolean;
  sortOrder: number;
}

/**
 * A language list, plus whether it was actually narrowed to the country asked for.
 *
 * The same contract as `TimeZoneLookupList`, and it separates the same two cases: eleven
 * languages because the country has eleven mapped, and eleven because it had none and the whole
 * catalogue came back instead.
 */
export interface LanguageLookupList {
  languages: LanguageLookup[];
  isCountryFiltered: boolean;
}

/** Every address-form picker in one payload, for a form opening cold. */
export interface GeoLookup {
  countries: CountryLookup[];
  stateProvinces: MasterLookup[];
  cities: MasterLookup[];
  currencies: CurrencyLookup[];
  timeZones: TimeZoneLookup[];
  timeZonesAreCountryFiltered: boolean;
  languages: LanguageLookup[];
  languagesAreCountryFiltered: boolean;
}
