using YDots.CAM.Application.Features.ReferenceData.DTOs;
using YDots.CAM.Domain.Common.Enums;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Features.ReferenceData.Mappings;

/// <summary>
/// Manual mapping for the reference tables.
///
/// The three near-identical mapping profiles this replaces - one per table - each had a single
/// method turning a row into a differently named record with the same fields.
/// </summary>
public static class ReferenceDataMappingConfig
{
    public static ReferenceItemResponse ToResponse(this Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return new ReferenceItemResponse(
            channel.Id, channel.Code, channel.Name, channel.Description,
            channel.Status, channel.IsSelectable, channel.SortOrder);
    }

    public static ReferenceItemResponse ToResponse(this Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ReferenceItemResponse(
            source.Id, source.Code, source.Name, source.Description,
            source.Status, source.IsSelectable, source.SortOrder);
    }

    public static ReferenceItemResponse ToResponse(this Medium medium)
    {
        ArgumentNullException.ThrowIfNull(medium);

        return new ReferenceItemResponse(
            medium.Id, medium.Code, medium.Name, medium.Description,
            medium.Status, medium.IsSelectable, medium.SortOrder);
    }

    /// <summary>Turns an enum into the value/label pairs a dropdown binds to.</summary>
    public static IReadOnlyList<EnumOptionResponse> Describe<TEnum>() where TEnum : struct, Enum =>
    [
        .. Enum.GetValues<TEnum>()
            .Select(value => new EnumOptionResponse(
                value.ToString(),
                Humanise(value.ToString()),
                Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)))
    ];

    /// <summary>"RequestClose" becomes "Request close".</summary>
    private static string Humanise(string value)
    {
        var spaced = string.Concat(
            value.Select((character, index) =>
                index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }
}
