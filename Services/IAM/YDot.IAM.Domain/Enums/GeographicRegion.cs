namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The broad grouping a Country belongs to, used for reporting roll-ups and for filtering
/// the country picker.
///
/// NAMED <c>GeographicRegion</c> RATHER THAN <c>Region</c>, which is what the GlobalMaster
/// service called it. "Region" collides with too much — the word already means an AWS
/// region, a UI region and a jurisdiction type in this same model — and a bare
/// <c>Region</c> in the IAM namespace would be ambiguous at every use site.
/// </summary>
public enum GeographicRegion
{
    Asia = 0,
    Europe = 1,
    NorthAmerica = 2,
    SouthAmerica = 3,
    Africa = 4,
    Oceania = 5,
    MiddleEast = 6,
    Antarctica = 7
}
