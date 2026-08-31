namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The lifecycle of a row in the global master catalogue: Country, StateProvince, City,
/// Currency and TimeZoneDefinition.
///
/// SEPARATE FROM <see cref="RecordStatus"/> ON PURPOSE. A master row is never Archived —
/// something somewhere still refers to it, and an archived country would leave an address
/// pointing at nothing. It is retired by moving to <see cref="Inactive"/>, which keeps it
/// readable and joinable while removing it from every dropdown.
/// </summary>
public enum MasterDataStatus
{
    /// <summary>Being prepared. Never offered for selection.</summary>
    Draft = 0,

    /// <summary>In use, and offered in the pickers.</summary>
    Active = 1,

    /// <summary>Retired. Still readable for historic rows, never offered for a new one.</summary>
    Inactive = 2
}
