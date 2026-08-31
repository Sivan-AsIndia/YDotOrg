namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// A latitude/longitude pair in decimal degrees.
///
/// WHY THE PAIR IS ONE TYPE. A city with a latitude and no longitude is not a
/// half-geocoded city, it is a broken row: nothing can plot it, and every consumer has to
/// remember to test both columns before using either. Binding them together means the
/// invariant "both or neither" is stated once, here, instead of being re-checked at every
/// call site and eventually forgotten at one of them.
///
/// The ranges are not decoration either. A latitude of 91 is not a distant place, it is a
/// transposed pair - the single most common way coordinates are entered wrongly - and
/// catching it at parse time is far cheaper than discovering it on a map months later.
/// </summary>
public sealed record GeoCoordinateValue
{
    private GeoCoordinateValue(decimal latitude, decimal longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Decimal degrees, positive north. Between -90 and 90 inclusive.</summary>
    public decimal Latitude { get; }

    /// <summary>Decimal degrees, positive east. Between -180 and 180 inclusive.</summary>
    public decimal Longitude { get; }

    /// <summary>
    /// Parses a pair. Both null is a valid answer meaning "not geocoded", and is reported
    /// separately from an invalid pair so a caller can tell the two apart.
    /// </summary>
    public static GeoCoordinateParseResult TryParse(decimal? latitude, decimal? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return GeoCoordinateParseResult.NotSupplied;
        }

        if (latitude is null || longitude is null)
        {
            return GeoCoordinateParseResult.Incomplete;
        }

        if (latitude is < -90 or > 90)
        {
            return GeoCoordinateParseResult.LatitudeOutOfRange;
        }

        if (longitude is < -180 or > 180)
        {
            return GeoCoordinateParseResult.LongitudeOutOfRange;
        }

        return GeoCoordinateParseResult.Parsed(new GeoCoordinateValue(latitude.Value, longitude.Value));
    }

    public override string ToString() => $"{Latitude:0.######}, {Longitude:0.######}";
}

/// <summary>
/// What <see cref="GeoCoordinateValue.TryParse"/> made of the pair it was given.
///
/// A discriminated answer rather than a nullable, because "no coordinates" and "bad
/// coordinates" need different messages on the form and a null cannot carry that difference.
/// </summary>
public sealed record GeoCoordinateParseResult(
    GeoCoordinateOutcome Outcome,
    GeoCoordinateValue? Value)
{
    public static readonly GeoCoordinateParseResult NotSupplied =
        new(GeoCoordinateOutcome.NotSupplied, null);

    public static readonly GeoCoordinateParseResult Incomplete =
        new(GeoCoordinateOutcome.Incomplete, null);

    public static readonly GeoCoordinateParseResult LatitudeOutOfRange =
        new(GeoCoordinateOutcome.LatitudeOutOfRange, null);

    public static readonly GeoCoordinateParseResult LongitudeOutOfRange =
        new(GeoCoordinateOutcome.LongitudeOutOfRange, null);

    public static GeoCoordinateParseResult Parsed(GeoCoordinateValue value) =>
        new(GeoCoordinateOutcome.Parsed, value);

    /// <summary>True for a usable pair AND for a deliberately absent one.</summary>
    public bool IsAcceptable =>
        Outcome is GeoCoordinateOutcome.Parsed or GeoCoordinateOutcome.NotSupplied;
}

/// <summary>The five answers a coordinate pair can produce.</summary>
public enum GeoCoordinateOutcome
{
    /// <summary>Both were null. The city is simply not geocoded.</summary>
    NotSupplied = 0,

    /// <summary>A valid pair.</summary>
    Parsed = 1,

    /// <summary>One was supplied without the other.</summary>
    Incomplete = 2,

    LatitudeOutOfRange = 3,

    LongitudeOutOfRange = 4
}
