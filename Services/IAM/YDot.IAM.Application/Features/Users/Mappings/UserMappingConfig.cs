using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Users.Mappings;

/// <summary>
/// Manual mapping for the Users slice. Plain extension methods rather than a mapping library:
/// the rules are visible, debuggable, and nothing is discovered at run time.
/// </summary>
public static class UserMappingConfig
{
    /// <summary>
    /// Applies an edit in place and returns the names of the fields that actually changed.
    ///
    /// The list is what goes into the audit row. "User updated" with no detail is nearly
    /// useless a year later; "User updated: Designation, DepartmentId" is not.
    ///
    /// Every field is null-guarded, so a screen that posts three fields does not blank the
    /// other twenty.
    /// </summary>
    public static IReadOnlyList<string> ApplyTo(this UpdateUserRequest request, User user)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var changed = new List<string>();

        void SetText(string? incoming, Func<string?> read, Action<string?> write, string field)
        {
            if (incoming is null)
            {
                return;
            }

            var value = string.IsNullOrWhiteSpace(incoming) ? null : incoming.Trim();
            if (!string.Equals(read(), value, StringComparison.Ordinal))
            {
                write(value);
                changed.Add(field);
            }
        }

        SetText(request.FirstName, () => user.FirstName, value => user.FirstName = value ?? user.FirstName, nameof(User.FirstName));
        SetText(request.MiddleName, () => user.MiddleName, value => user.MiddleName = value, nameof(User.MiddleName));
        SetText(request.LastName, () => user.LastName, value => user.LastName = value ?? user.LastName, nameof(User.LastName));
        SetText(request.EmployeeNumber, () => user.EmployeeNumber, value => user.EmployeeNumber = value, nameof(User.EmployeeNumber));
        SetText(request.Designation, () => user.Designation, value => user.Designation = value, nameof(User.Designation));
        SetText(request.MobileCountryCode, () => user.MobileCountryCode, value => user.MobileCountryCode = value, nameof(User.MobileCountryCode));
        SetText(request.MobileNumber, () => user.MobileNumber, value => user.MobileNumber = value, nameof(User.MobileNumber));
        SetText(request.PreferredCulture, () => user.PreferredCulture, value => user.PreferredCulture = value, nameof(User.PreferredCulture));
        SetText(request.TimeZone, () => user.TimeZone, value => user.TimeZone = value, nameof(User.TimeZone));
        SetText(request.AvatarUrl, () => user.AvatarUrl, value => user.AvatarUrl = value, nameof(User.AvatarUrl));

        // DisplayName follows the name unless it was set explicitly, so a rename does not
        // leave a stale display name behind.
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            if (!string.Equals(user.DisplayName, request.DisplayName.Trim(), StringComparison.Ordinal))
            {
                user.DisplayName = request.DisplayName.Trim();
                changed.Add(nameof(User.DisplayName));
            }
        }
        else if (changed.Contains(nameof(User.FirstName)) || changed.Contains(nameof(User.LastName)))
        {
            user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
            changed.Add(nameof(User.DisplayName));
        }

        if (changed.Contains(nameof(User.MobileCountryCode)) || changed.Contains(nameof(User.MobileNumber)))
        {
            // Kept in step with the base PhoneNumber, which the framework SMS providers read.
            user.PhoneNumber = user.ToE164();
            user.MobileConfirmed = false;
        }

        if (request.AccountCategory.HasValue && request.AccountCategory != user.AccountCategory)
        {
            user.AccountCategory = request.AccountCategory.Value;
            changed.Add(nameof(User.AccountCategory));
        }

        if (request.EngagementType.HasValue && request.EngagementType != user.EngagementType)
        {
            user.EngagementType = request.EngagementType.Value;
            changed.Add(nameof(User.EngagementType));
        }

        if (request.DepartmentId.HasValue && request.DepartmentId != user.DepartmentId)
        {
            user.DepartmentId = request.DepartmentId;
            changed.Add(nameof(User.DepartmentId));
        }

        if (request.OrganisationUnitId.HasValue && request.OrganisationUnitId != user.OrganisationUnitId)
        {
            user.OrganisationUnitId = request.OrganisationUnitId;
            changed.Add(nameof(User.OrganisationUnitId));
        }

        if (request.ManagerUserId.HasValue && request.ManagerUserId != user.ManagerUserId)
        {
            user.ManagerUserId = request.ManagerUserId;
            changed.Add(nameof(User.ManagerUserId));
        }

        if (request.AccessStartsAtUtc.HasValue && request.AccessStartsAtUtc != user.AccessStartsAtUtc)
        {
            user.AccessStartsAtUtc = request.AccessStartsAtUtc.Value;
            changed.Add(nameof(User.AccessStartsAtUtc));
        }

        if (request.AccessEndsAtUtc.HasValue && request.AccessEndsAtUtc != user.AccessEndsAtUtc)
        {
            user.AccessEndsAtUtc = request.AccessEndsAtUtc;
            changed.Add(nameof(User.AccessEndsAtUtc));
        }

        if (request.MfaRequirement.HasValue && request.MfaRequirement != user.MfaRequirement)
        {
            user.MfaRequirement = request.MfaRequirement.Value;
            changed.Add(nameof(User.MfaRequirement));
        }

        if (request.JoinedOn.HasValue && request.JoinedOn != user.JoinedOn)
        {
            user.JoinedOn = request.JoinedOn;
            changed.Add(nameof(User.JoinedOn));
        }

        if (request.ExitedOn.HasValue && request.ExitedOn != user.ExitedOn)
        {
            user.ExitedOn = request.ExitedOn;
            changed.Add(nameof(User.ExitedOn));
        }

        return changed;
    }

    /// <summary>
    /// The legal status moves.
    ///
    /// Held here rather than scattered through the handlers, so "Withdrawn back to Active"
    /// is refused in exactly one place. Note that Withdrawn and Deactivated can be reversed
    /// by an administrator but never by the person themselves — a leaver reinstating their
    /// own account would be a straightforward hole.
    /// </summary>
    public static bool CanTransitionTo(UserStatus from, UserStatus to) => (from, to) switch
    {
        (UserStatus.Draft, UserStatus.Invited) => true,
        (UserStatus.Draft, UserStatus.Active) => true,
        (UserStatus.Draft, UserStatus.Withdrawn) => true,

        (UserStatus.Invited, UserStatus.Active) => true,
        (UserStatus.Invited, UserStatus.Withdrawn) => true,
        (UserStatus.Invited, UserStatus.Suspended) => true,

        (UserStatus.Active, UserStatus.Suspended) => true,
        (UserStatus.Active, UserStatus.Deactivated) => true,
        (UserStatus.Active, UserStatus.Expired) => true,

        (UserStatus.Suspended, UserStatus.Active) => true,
        (UserStatus.Suspended, UserStatus.Deactivated) => true,

        (UserStatus.Deactivated, UserStatus.Active) => true,

        (UserStatus.Expired, UserStatus.Active) => true,
        (UserStatus.Expired, UserStatus.Deactivated) => true,

        (UserStatus.Withdrawn, UserStatus.Invited) => true,

        _ => false
    };

    /// <summary>
    /// What the record STATE allows. Permission is a separate question checked on each
    /// endpoint; this is what the client uses to decide which buttons to draw.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(User user, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.IsSystemAccount)
        {
            return ["View"];
        }

        var actions = new List<string> { "View" };

        switch (user.Status)
        {
            case UserStatus.Draft:
                actions.AddRange(["Edit", "Invite", "AssignRoles", "Withdraw"]);
                break;

            case UserStatus.Invited:
                actions.AddRange(["Edit", "ResendInvitation", "RevokeInvitation", "AssignRoles", "Withdraw"]);
                break;

            case UserStatus.Active:
                actions.AddRange([
                    "Edit", "AssignRoles", "AssignScopes", "ResetPassword", "ForceSignOut",
                    "ChangeLoginIdentifier", "ExtendAccess", "Suspend", "Deactivate"
                ]);
                break;

            case UserStatus.Suspended:
                actions.AddRange(["View", "Reactivate", "Deactivate", "ResetPassword"]);
                break;

            case UserStatus.Deactivated:
                actions.Add("Reactivate");
                break;

            case UserStatus.Expired:
                actions.AddRange(["ExtendAccess", "Reactivate", "Deactivate"]);
                break;

            case UserStatus.Withdrawn:
                actions.Add("Invite");
                break;
        }

        if (user.IsLockedOut(asOf))
        {
            actions.Add("Unlock");
        }

        return actions.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Human wording for a status, so the client does not hard-code seven strings.</summary>
    public static string DescribeStatus(UserStatus status) => status switch
    {
        UserStatus.Draft => "Draft",
        UserStatus.Invited => "Invitation sent",
        UserStatus.Active => "Active",
        UserStatus.Suspended => "Suspended",
        UserStatus.Deactivated => "Deactivated",
        UserStatus.Expired => "Access expired",
        UserStatus.Withdrawn => "Withdrawn",
        _ => status.ToString()
    };

    /// <summary>Human wording for a sign-in outcome, for the recent-activity list.</summary>
    public static string DescribeOutcome(SignInOutcome outcome) => outcome switch
    {
        SignInOutcome.Succeeded => "Signed in",
        SignInOutcome.InvalidCredentials => "Wrong password",
        SignInOutcome.UnknownAccount => "Unknown account",
        SignInOutcome.LockedOut => "Locked out",
        SignInOutcome.Suspended => "Account suspended",
        SignInOutcome.Deactivated => "Account deactivated",
        SignInOutcome.Expired => "Access expired",
        SignInOutcome.MfaRequired => "Verification required",
        SignInOutcome.MfaFailed => "Wrong verification code",
        SignInOutcome.TenantInactive => "Organisation not active",
        SignInOutcome.TenantNotResolved => "Unknown web address",
        SignInOutcome.WrongTenant => "Wrong organisation",
        SignInOutcome.NotActivated => "Not activated",
        _ => outcome.ToString()
    };

    /// <summary>
    /// Masks contact details for a caller who lacks the sensitive-contact permission.
    ///
    /// Masked rather than omitted, so the screen can still show that a value EXISTS and offer
    /// a reveal to somebody who is allowed. An empty field reads as missing data and sends
    /// people off to re-enter something that is already there.
    /// </summary>
    public static string MaskEmail(string? email, bool canSee)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        if (canSee)
        {
            return email;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return "***";
        }

        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];

        return $"{visible}***{email[at..]}";
    }

    public static string? MaskMobile(string? mobile, bool canSee)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        return canSee ? mobile : $"***{(mobile.Length <= 4 ? mobile : mobile[^4..])}";
    }

    /// <summary>Groups a permission set by module and group, for the IAM-USR-03 preview.</summary>
    public static IReadOnlyList<PermissionGroupResponse> GroupPermissions(
        IEnumerable<(string Code, string Name, string ModuleCode, string? GroupCode,
            PermissionAction Action, bool IsSensitive, bool IsGranted, string? GrantedVia)> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return
        [
            .. permissions
                .GroupBy(permission => (permission.ModuleCode, permission.GroupCode))
                .OrderBy(group => group.Key.ModuleCode, StringComparer.Ordinal)
                .ThenBy(group => group.Key.GroupCode, StringComparer.Ordinal)
                .Select(group => new PermissionGroupResponse(
                    group.Key.ModuleCode,
                    group.Key.GroupCode,
                    [
                        .. group
                            .OrderBy(permission => permission.Code, StringComparer.Ordinal)
                            .Select(permission => new PermissionSummaryResponse(
                                permission.Code,
                                permission.Name,
                                permission.Action,
                                permission.IsSensitive,
                                permission.IsGranted,
                                permission.GrantedVia))
                    ]))
        ];
    }
}
