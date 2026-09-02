using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// How a dotted permission code is read: <c>module.group.action</c>, or <c>MODULE.View</c> for a
/// section-level code with no group.
///
/// WHY THIS IS SHARED RATHER THAN PRIVATE TO THE SEEDER. Two places have to agree on what action
/// a code represents: the seeder, which writes <c>Permission.Action</c> into the catalogue, and
/// <see cref="RoleAccessProfiles"/>, which decides whether INITIATOR or APPROVER holds it. When
/// the derivation lived only in the seeder the profile had to guess, and a code the seeder filed
/// as Approve could land in the maker role - the exact failure the two roles exist to prevent.
///
/// IAM AND GM CODES ARE DERIVED. CAM, DON and PAY codes declare their action explicitly in
/// <see cref="ModulePermissionCatalogue"/>, and a declared action always wins over a derived one:
/// <c>cam.campaigns.close</c> means "approve a closure request" and <c>don.lead-work-queue.close</c>
/// means "finish with this lead", and no rule reading the verb alone can tell those apart.
/// </summary>
public static class PermissionCodeConventions
{
    /// <summary>
    /// The action a code's own text implies.
    ///
    /// Anything unrecognised becomes <see cref="PermissionAction.Operate"/>, which is the safe
    /// direction: Operate is a working verb, so an unclassified new code joins the maker role
    /// rather than silently granting an approval.
    /// </summary>
    public static PermissionAction DeriveAction(string code)
    {
        var segments = code.Split('.');

        var actionSegment = segments.Length >= 3
            ? segments[2]
            : segments.LastOrDefault() ?? "view";

        return actionSegment.ToLowerInvariant() switch
        {
            "view" => PermissionAction.View,
            "create" => PermissionAction.Create,
            "edit" or "update" => PermissionAction.Edit,
            "submit" => PermissionAction.Submit,
            "approve" or "review" => PermissionAction.Approve,
            "export" => PermissionAction.Export,
            _ => PermissionAction.Operate
        };
    }

    /// <summary>The module a code belongs to: IAM, GM, CAM, DON, PAY or PLATFORM.</summary>
    public static string DeriveModule(string code)
    {
        var segments = code.Split('.');

        return segments.Length > 0 ? segments[0].ToUpperInvariant() : "IAM";
    }

    /// <summary>
    /// The group a code belongs to. <c>iam.users.create</c> groups as Users; a two-segment
    /// section code such as <c>IAM.View</c> has no group of its own.
    /// </summary>
    public static string DeriveGroup(string code)
    {
        var segments = code.Split('.');

        return segments.Length >= 3 ? ToPascal(segments[1]) : "Section";
    }

    /// <summary>A readable name built from the code, e.g. "Create users".</summary>
    public static string DeriveName(string code)
    {
        var segments = code.Split('.');

        if (segments.Length < 3)
        {
            return string.Join(' ', segments.Select(ToPascal));
        }

        return $"{ToSentence(segments[2])} {ToSentence(segments[1])}";
    }

    public static string ToPascal(string value) =>
        string.Join(string.Empty, value.Split('-')
            .Select(part => part.Length == 0
                ? part
                : char.ToUpperInvariant(part[0]) + part[1..]));

    public static string ToSentence(string value)
    {
        var words = value.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 0
            ? value
            : char.ToUpperInvariant(words[0][0]) + string.Join(' ', words)[1..];
    }
}
