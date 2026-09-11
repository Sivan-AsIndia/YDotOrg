/**
 * Server-supplied dropdown options, in the spelling the API's RECORDS use.
 *
 * THE TWO SPELLINGS DIFFER, AND THAT IS THE WHOLE REASON THIS FILE EXISTS. The option endpoints
 * (`/reference-data/enums`, `/masters/reference-data`) name each value by its C# enum member -
 * "MiddleEast", "SubMenu", "Active" - while every record the API returns carries the same value in
 * camelCase - "middleEast", "subMenu", "active" - because that is how the JSON serialiser writes
 * enums. Comparing one against the other never matches: the Country region filter printed raw codes
 * for exactly that reason, and an edit form bound to the enum-member spelling opens with its select
 * blank on every existing record.
 *
 * NOT FOR VALUES THAT ARE STORED TEXT. Organisation types are labels kept verbatim in a string
 * column; camel-casing "Non-profit / NGO" would produce a value nothing on the server recognises.
 */
export interface ApiEnumOption {
  /** What a request sends and a record carries: camelCase. */
  readonly value: string;
  /** What a person reads. */
  readonly label: string;
}

/** "MiddleEast" becomes "middleEast" - the JSON spelling of an enum member. */
export function apiEnumValue(value: string | null | undefined): string {
  return value ? value.charAt(0).toLowerCase() + value.slice(1) : '';
}

/** Maps a server option list to API spelling, dropping any option that arrived without a value. */
export function toApiEnumOptions(
  options: readonly { value?: string | null; label?: string | null }[] | null | undefined,
): ApiEnumOption[] {
  return (options ?? [])
    .filter((option) => !!option.value)
    .map((option) => ({
      value: apiEnumValue(option.value),
      label: option.label || option.value!,
    }));
}

/**
 * The label for a value, or the value itself when the list has not loaded yet.
 *
 * Compared case-insensitively, so it answers for either spelling.
 */
export function enumLabel(
  options: readonly ApiEnumOption[],
  value: string | null | undefined,
): string {
  if (!value) {
    return '';
  }

  const needle = value.toLowerCase();

  return options.find((option) => option.value.toLowerCase() === needle)?.label ?? value;
}
