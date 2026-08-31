/**
 * The Masters contract: Country, State/Province, City, Currency and Time Zone.
 *
 * WHY THIS FILE IS HAND-WRITTEN. The rest of the IAM contract is generated into
 * `iam-contract.model.ts` from the OpenAPI document. These five moved into IAM from the
 * standalone GlobalMaster service, and until the generator is re-run against the new document
 * their types would simply be missing. Every field below matches the server DTOs in
 * `YDot.IAM.Application/Features/GlobalMasters/DTOs`, name for name — a name invented on this
 * side is `undefined` at runtime, which is the failure this file exists to prevent.
 *
 * THE FIELD NAMES ARE THE DOMAIN ONES — `countryCode`, `cityName` — not the `code`/`name` the
 * entities use internally. That is deliberate on the server side too: the existing Masters
 * screens were written against these names and there was no reason for a backend refactor to
 * cost a front-end rewrite.
 */

// =============================================================================================
// Shared
// =============================================================================================

/** Where a master row lives. Platform rows are shared and read-only to an Organisation. */
export type MasterDataStatus = 'draft' | 'active' | 'inactive';

/** Which side of the shared catalogue a grid should show. */
export type MasterRowScope = 'all' | 'platform' | 'tenant';

/** One option in a master picker. */
export interface MasterLookup {
  id: string;
  code: string;
  name: string;
  status: MasterDataStatus;
  /** True for a seeded platform row: readable by everyone, editable only by SuperAdmin. */
  isPlatformRow: boolean;
  sortOrder: number;
}

/** A server-supplied enum option, so the client never hard-codes a list that can drift. */
export interface EnumOption {
  value: string;
  label: string;
  ordinal: number;
}

/** Everything the five Masters screens need to render their dropdowns, in one call. */
export interface GlobalMasterReferenceData {
  countries: MasterLookup[];
  stateProvinces: MasterLookup[];
  currencies: MasterLookup[];
  timeZones: MasterLookup[];
  regions: EnumOption[];
  jurisdictionTypes: EnumOption[];
  currencyTypes: EnumOption[];
  symbolPositions: EnumOption[];
  roundingModes: EnumOption[];
  statuses: EnumOption[];
}

/** What every master grid can be narrowed by. Mirrors the server's query parameters. */
export interface MasterSearchFilter {
  search?: string;
  status?: MasterDataStatus;
  scope?: MasterRowScope;
  page?: number;
  pageSize?: number;
  sort?: string;
}

/**
 * The body of an activate or deactivate call.
 *
 * It carries no status: the ROUTE decides the direction, and each route has its own permission
 * so an Organisation can grant "switch back on" without granting "switch off".
 */
export interface MasterStatusChangeRequest {
  expectedVersion: number;
  reason?: string;
}

/** The body of a delete call. Refused server-side while anything still points at the row. */
export interface DeleteMasterRequest {
  expectedVersion: number;
  reason?: string;
}

// =============================================================================================
// Countries
// =============================================================================================

export type GeographicRegion =
  | 'asia' | 'europe' | 'northAmerica' | 'southAmerica'
  | 'africa' | 'oceania' | 'middleEast' | 'antarctica';

export interface CountryListItem {
  id: string;
  /** Null for a shared platform row; the Organisation's id for one of its own. */
  tenantId: string | null;
  countryCode: string;
  countryName: string;
  officialName: string | null;
  region: GeographicRegion | null;
  iso2: string;
  iso3: string | null;
  /** Rendered by the server from the ISO code, so every client shows the same flag. */
  flagEmoji: string;
  defaultCurrencyCode: string | null;
  phoneCountryCode: string | null;
  hasStates: boolean;
  status: MasterDataStatus;
  statusDescription: string;
  /** Convenience mirror of `status === 'active'`, for the grid's toggle. */
  isActive: boolean;
  isPlatformRow: boolean;
  sortOrder: number;
  stateProvinceCount: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface CountryDetail extends Omit<CountryListItem, 'stateProvinceCount'> {
  businessUnitId: string;
  numericCode: string | null;
  postalCodePattern: string | null;
  notes: string | null;
  stateProvinceCount: number;
  cityCount: number;
  createdAtUtc: string;
  createdByUserId: string;
  updatedByUserId: string | null;
  /**
   * What the record's state allows, decided by the server. Render buttons from this and they
   * can never offer something the API will refuse — a platform row returns View and Export
   * only, and Delete is absent while anything sits beneath the country.
   */
  permittedActions: string[];
}

export interface CreateCountryRequest {
  countryCode: string;
  countryName: string;
  iso2: string;
  officialName?: string | null;
  region?: GeographicRegion | null;
  iso3?: string | null;
  numericCode?: string | null;
  defaultCurrencyCode?: string | null;
  hasStates?: boolean;
  postalCodePattern?: string | null;
  phoneCountryCode?: string | null;
  status?: MasterDataStatus;
  sortOrder?: number;
  notes?: string | null;
}

/** Every field is "leave it alone" when omitted, except `expectedVersion`. */
export interface UpdateCountryRequest {
  expectedVersion: number;
  countryName?: string | null;
  officialName?: string | null;
  region?: GeographicRegion | null;
  iso2?: string | null;
  iso3?: string | null;
  numericCode?: string | null;
  defaultCurrencyCode?: string | null;
  hasStates?: boolean | null;
  postalCodePattern?: string | null;
  phoneCountryCode?: string | null;
  sortOrder?: number | null;
  notes?: string | null;
}

export interface CountrySearchFilter extends MasterSearchFilter {
  region?: GeographicRegion;
  hasStates?: boolean;
  defaultCurrencyCode?: string;
}

// =============================================================================================
// States and provinces
// =============================================================================================

export type JurisdictionType =
  | 'state' | 'unionTerritory' | 'province' | 'territory'
  | 'region' | 'district' | 'prefecture' | 'other';

export interface StateProvinceListItem {
  id: string;
  tenantId: string | null;
  stateProvinceCode: string;
  stateProvinceName: string;
  displayName: string | null;
  countryId: string;
  countryCode: string;
  countryName: string;
  jurisdictionType: JurisdictionType;
  /** Shows the free-text description for `other`, rather than the useless word "Other". */
  jurisdictionDescription: string;
  isFederalJurisdiction: boolean;
  gstStateCode: string | null;
  status: MasterDataStatus;
  statusDescription: string;
  isActive: boolean;
  isPlatformRow: boolean;
  sortOrder: number;
  cityCount: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface StateProvinceDetail extends StateProvinceListItem {
  businessUnitId: string;
  otherJurisdictionType: string | null;
  stateTaxJurisdictionCode: string | null;
  defaultTimeZoneId: string | null;
  defaultTimeZoneName: string | null;
  postalCodePattern: string | null;
  addressFormatHint: string | null;
  notes: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedByUserId: string | null;
  permittedActions: string[];
}

export interface CreateStateProvinceRequest {
  stateProvinceCode: string;
  stateProvinceName: string;
  countryId: string;
  displayName?: string | null;
  jurisdictionType?: JurisdictionType;
  otherJurisdictionType?: string | null;
  isFederalJurisdiction?: boolean;
  gstStateCode?: string | null;
  stateTaxJurisdictionCode?: string | null;
  defaultTimeZoneId?: string | null;
  postalCodePattern?: string | null;
  addressFormatHint?: string | null;
  status?: MasterDataStatus;
  sortOrder?: number;
  notes?: string | null;
}

/**
 * Editing a state.
 *
 * `countryId` is absent on purpose: re-parenting a state would silently rewrite the geography
 * of every address beneath it. Delete and recreate is the honest operation.
 */
export interface UpdateStateProvinceRequest {
  expectedVersion: number;
  stateProvinceName?: string | null;
  displayName?: string | null;
  jurisdictionType?: JurisdictionType | null;
  otherJurisdictionType?: string | null;
  isFederalJurisdiction?: boolean | null;
  gstStateCode?: string | null;
  stateTaxJurisdictionCode?: string | null;
  defaultTimeZoneId?: string | null;
  postalCodePattern?: string | null;
  addressFormatHint?: string | null;
  sortOrder?: number | null;
  notes?: string | null;
}

export interface StateProvinceSearchFilter extends MasterSearchFilter {
  countryId?: string;
  jurisdictionType?: JurisdictionType;
  isFederalJurisdiction?: boolean;
}

// =============================================================================================
// Cities
// =============================================================================================

export interface CityListItem {
  id: string;
  tenantId: string | null;
  cityCode: string;
  cityName: string;
  displayName: string | null;
  stateProvinceId: string;
  stateProvinceCode: string;
  stateProvinceName: string;
  countryId: string;
  countryCode: string;
  countryName: string;
  isMetro: boolean;
  latitude: number | null;
  longitude: number | null;
  status: MasterDataStatus;
  statusDescription: string;
  isActive: boolean;
  isPlatformRow: boolean;
  sortOrder: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface CityDetail extends CityListItem {
  businessUnitId: string;
  defaultPostalCodePattern: string | null;
  hasCoordinates: boolean;
  notes: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedByUserId: string | null;
  permittedActions: string[];
}

/**
 * Creating a city.
 *
 * There is no `countryId`: the server takes it from the chosen state, which is the only way its
 * denormalised country column can be guaranteed to agree with the state above it.
 */
export interface CreateCityRequest {
  cityCode: string;
  cityName: string;
  stateProvinceId: string;
  displayName?: string | null;
  defaultPostalCodePattern?: string | null;
  isMetro?: boolean;
  latitude?: number | null;
  longitude?: number | null;
  status?: MasterDataStatus;
  sortOrder?: number;
  notes?: string | null;
}

export interface UpdateCityRequest {
  expectedVersion: number;
  cityName?: string | null;
  displayName?: string | null;
  defaultPostalCodePattern?: string | null;
  isMetro?: boolean | null;
  latitude?: number | null;
  longitude?: number | null;
  sortOrder?: number | null;
  notes?: string | null;
  /**
   * Clears both coordinates. Needed because a null latitude already means "unchanged", so
   * there would otherwise be no way to un-geocode a city that was geocoded wrongly.
   */
  clearCoordinates?: boolean;
}

export interface CitySearchFilter extends MasterSearchFilter {
  countryId?: string;
  stateProvinceId?: string;
  isMetro?: boolean;
  /** False lists the cities still awaiting geocoding, which is how that gap gets worked through. */
  hasCoordinates?: boolean;
}

// =============================================================================================
// Currencies
// =============================================================================================

export type CurrencyType = 'fiat' | 'crypto' | 'other';
export type SymbolPosition = 'prefix' | 'suffix';
export type RoundingMode = 'halfUp' | 'halfDown' | 'bankers';

export interface CurrencyListItem {
  id: string;
  tenantId: string | null;
  currencyCode: string;
  currencyName: string;
  numericCode: number | null;
  currencyType: CurrencyType;
  symbol: string | null;
  symbolPosition: SymbolPosition;
  decimalPlaces: number;
  /** A worked example of the format, so the grid shows the effect rather than the rule. */
  sampleAmount: string;
  status: MasterDataStatus;
  statusDescription: string;
  isActive: boolean;
  isPlatformRow: boolean;
  sortOrder: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface CurrencyDetail extends CurrencyListItem {
  businessUnitId: string;
  displayFormat: string | null;
  minorUnitName: string | null;
  roundingMode: RoundingMode;
  roundingStep: number | null;
  isZeroDecimal: boolean;
  notes: string | null;
  /** Countries naming this currency as their default. Non-zero blocks deletion. */
  countryUsageCount: number;
  createdAtUtc: string;
  createdByUserId: string;
  updatedByUserId: string | null;
  permittedActions: string[];
}

export interface CreateCurrencyRequest {
  currencyCode: string;
  currencyName: string;
  numericCode?: number | null;
  currencyType?: CurrencyType;
  symbol?: string | null;
  symbolPosition?: SymbolPosition;
  displayFormat?: string | null;
  decimalPlaces?: number;
  minorUnitName?: string | null;
  roundingMode?: RoundingMode;
  roundingStep?: number | null;
  status?: MasterDataStatus;
  sortOrder?: number;
  notes?: string | null;
}

/** `currencyCode` is absent: the code IS the currency, and repointing it would redenominate history. */
export interface UpdateCurrencyRequest {
  expectedVersion: number;
  currencyName?: string | null;
  numericCode?: number | null;
  currencyType?: CurrencyType | null;
  symbol?: string | null;
  symbolPosition?: SymbolPosition | null;
  displayFormat?: string | null;
  decimalPlaces?: number | null;
  minorUnitName?: string | null;
  roundingMode?: RoundingMode | null;
  roundingStep?: number | null;
  sortOrder?: number | null;
  notes?: string | null;
  clearRoundingStep?: boolean;
}

export interface CurrencySearchFilter extends MasterSearchFilter {
  currencyType?: CurrencyType;
}

// =============================================================================================
// Time zones
// =============================================================================================

export interface TimeZoneListItem {
  id: string;
  tenantId: string | null;
  /** The IANA identifier as written: `Asia/Kolkata`. */
  timeZoneKey: string;
  displayName: string;
  shortName: string | null;
  standardUtcOffsetMinutes: number;
  /** The offset written the way a person reads it: "+05:30". */
  offsetDisplay: string;
  supportsDaylightSaving: boolean;
  isDefaultRecommended: boolean;
  status: MasterDataStatus;
  statusDescription: string;
  isActive: boolean;
  isPlatformRow: boolean;
  sortOrder: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface TimeZoneDetail extends TimeZoneListItem {
  businessUnitId: string;
  daylightSavingRuleNote: string | null;
  notes: string | null;
  /** States defaulting to this zone. Non-zero blocks deletion. */
  stateUsageCount: number;
  createdAtUtc: string;
  createdByUserId: string;
  updatedByUserId: string | null;
  permittedActions: string[];
}

export interface CreateTimeZoneRequest {
  timeZoneKey: string;
  displayName: string;
  standardUtcOffsetMinutes: number;
  shortName?: string | null;
  supportsDaylightSaving?: boolean;
  daylightSavingRuleNote?: string | null;
  isDefaultRecommended?: boolean;
  status?: MasterDataStatus;
  sortOrder?: number;
  notes?: string | null;
}

/** `timeZoneKey` is absent: it identifies the zone, and repointing it changes what every stamp means. */
export interface UpdateTimeZoneRequest {
  expectedVersion: number;
  displayName?: string | null;
  shortName?: string | null;
  standardUtcOffsetMinutes?: number | null;
  supportsDaylightSaving?: boolean | null;
  daylightSavingRuleNote?: string | null;
  isDefaultRecommended?: boolean | null;
  sortOrder?: number | null;
  notes?: string | null;
}

export interface TimeZoneSearchFilter extends MasterSearchFilter {
  supportsDaylightSaving?: boolean;
  isDefaultRecommended?: boolean;
}

// =============================================================================================
// Helpers
// =============================================================================================

/**
 * Whether the caller may take an action on a record, read from the server's own answer.
 *
 * Always ask this rather than re-deriving the rule in a component. The server already decided
 * it from the record's state AND the caller's permissions, and a second copy of the rule on
 * this side is one that will eventually disagree.
 */
export function canPerform(
  record: { permittedActions?: string[] } | null | undefined,
  action: string,
): boolean {
  return record?.permittedActions?.includes(action) ?? false;
}
