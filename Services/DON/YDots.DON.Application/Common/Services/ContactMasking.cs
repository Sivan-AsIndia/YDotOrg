using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Domain.ValueObjects;

namespace YDots.DON.Application.Common.Services;

/// <summary>
/// The single place that decides whether a contact value is shown or starred out.
///
/// UI section 2: "Masking is applied before the value reaches unauthorised UI state." Doing it
/// here rather than in the browser is the whole point — a masked value must never be in the
/// payload at all, because hidden DOM text, a tooltip or the network tab would give it away.
/// </summary>
public static class ContactMasking
{
    public static bool CanSeeContact(this ICurrentUser currentUser) =>
        currentUser.HasPermission(PermissionCodes.DonorsViewSensitiveContact);

    public static bool CanSeeEvidence(this ICurrentUser currentUser) =>
        currentUser.HasPermission(PermissionCodes.DonorsViewConfidentialEvidence);

    /// <summary>Returns the address as stored, or "ar***@example.com" when the caller is not permitted.</summary>
    public static string? Email(string? value, bool canSee) =>
        string.IsNullOrWhiteSpace(value) ? value : canSee ? value : EmailValue.Mask(value);

    /// <summary>Returns the number as stored, or "*******3210" when the caller is not permitted.</summary>
    public static string? Phone(string? value, bool canSee) =>
        string.IsNullOrWhiteSpace(value) ? value : canSee ? value : PrimaryPhoneValue.Mask(value);

    /// <summary>
    /// Confidential free text: evidence references, matching evidence, notes. An unpermitted
    /// caller gets the standard copy from UI section 4.x.6 instead of a partial value, because
    /// even the length of a redacted string can leak something.
    /// </summary>
    public static string? Confidential(string? value, bool canSee) =>
        string.IsNullOrWhiteSpace(value) ? value
        : canSee ? value
        : "This value cannot be displayed with your current access.";
}
