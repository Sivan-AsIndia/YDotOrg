using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A first-level subdivision of a <see cref="Country"/>: a state, province, union territory
/// or whatever that country calls it.
///
/// THE TAX COLUMNS ARE THE REASON THIS IS NOT JUST A NAME. Indian GST is charged against a
/// two-digit state code, and getting it wrong on a receipt is a compliance problem rather
/// than a cosmetic one, so <see cref="GstStateCode"/> lives on the row that the receipt
/// already joins to.
/// </summary>
public class StateProvince : GlobalMasterEntity
{
    /// <summary>An alternative label for the pickers when the official name is unwieldy.</summary>
    public string? DisplayName { get; set; }

    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public JurisdictionType JurisdictionType { get; set; } = JurisdictionType.State;

    /// <summary>Set only when <see cref="JurisdictionType"/> is <c>Other</c>.</summary>
    public string? OtherJurisdictionType { get; set; }

    /// <summary>True where the subdivision is administered centrally rather than by its own legislature.</summary>
    public bool IsFederalJurisdiction { get; set; }

    /// <summary>The two-digit GST state code used on Indian tax documents.</summary>
    public string? GstStateCode { get; set; }

    /// <summary>The jurisdiction code used by any other state-level tax regime.</summary>
    public string? StateTaxJurisdictionCode { get; set; }

    /// <summary>The time zone addresses here default to. Optional: several states span two.</summary>
    public Guid? DefaultTimeZoneId { get; set; }

    public TimeZoneDefinition? DefaultTimeZone { get; set; }

    /// <summary>Overrides the country pattern where a subdivision has a narrower one.</summary>
    public string? PostalCodePattern { get; set; }

    /// <summary>Guidance shown beside the address form, for example "PIN code is six digits".</summary>
    public string? AddressFormatHint { get; set; }

    public ICollection<City> Cities { get; set; } = [];
}
