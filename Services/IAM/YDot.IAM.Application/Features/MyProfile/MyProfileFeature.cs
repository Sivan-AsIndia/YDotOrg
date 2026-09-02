using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Application.Features.Users.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.MyProfile;

/// <summary>
/// Body of "save my own profile".
///
/// FIVE FIELDS, AND THE LIST IS THE POINT. An administrative edit may move somebody's
/// department, manager, account category, access window and MFA requirement; a person editing
/// their own record may not, because every one of those is a statement about their place in the
/// organisation rather than about them. Anything absent from this record simply cannot be
/// reached through this endpoint, which is a tighter guarantee than checking it in a handler.
/// </summary>
public sealed record UpdateMyProfileRequest(
    long ExpectedVersion,
    string? DisplayName = null,
    string? MobileCountryCode = null,
    string? MobileNumber = null,
    string? Designation = null,
    string? PreferredCulture = null,
    string? TimeZone = null,
    string? Reason = null);

/// <summary>Saves the caller's own profile. There is no user id: it is always the caller.</summary>
public sealed record UpdateMyProfileCommand(UpdateMyProfileRequest Request);

/// <summary>
/// The caller's own profile, on the write side.
///
/// WHY THIS EXISTS RATHER THAN REUSING <c>PUT /users/{id}</c>. That route lives on
/// <c>UsersController</c>, which carries <c>TenantContextRequired</c> at class level and
/// <c>iam.users.edit</c> on the action. Both are correct for administering somebody else and
/// both are wrong for editing yourself: the permission belongs to ten of the fifteen roles, and
/// the Organisation requirement fails outright for a root user who has not chosen one. So the
/// Edit profile button on a person's own record answered "You do not have permission to perform
/// this action" — about their own name.
///
/// The read side already had exactly this problem and was already solved exactly this way; see
/// <c>MyProfileController</c>. This is the other half of it.
/// </summary>
public sealed class MyProfileFeatureHandler(
    IUserRepository users,
    IAuditService audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<OutcomeResponse>(Error.Unauthorised());
        }

        // The same optimistic check the administrative edit makes. Two tabs open on the same
        // record is not a rare accident, and without this the second save silently undoes the
        // first.
        if (user.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Enter a display name.",
                    [new ValidationError(nameof(request.DisplayName), "A display name is required.")]));
        }

        // Validated as a pair, because a number without its country code is not a number
        // anybody can dial.
        if (!string.IsNullOrWhiteSpace(request.MobileNumber))
        {
            var mobile = MobileNumberValue.TryParse(
                request.MobileCountryCode ?? user.MobileCountryCode, request.MobileNumber);

            if (mobile is null)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.Validation("Enter a valid mobile number with its country code.",
                        [new ValidationError(nameof(request.MobileNumber), "That mobile number is not valid.")]));
            }
        }

        // Reused rather than reimplemented, so a self-edit and an administrative edit stamp the
        // same columns in the same way. The projection below is what limits the reach: every
        // field this endpoint may not touch is left null, and null means "leave alone".
        var changes = new UpdateUserRequest(
            ExpectedVersion: request.ExpectedVersion,
            DisplayName: request.DisplayName,
            MobileCountryCode: request.MobileCountryCode,
            MobileNumber: request.MobileNumber,
            Designation: request.Designation,
            PreferredCulture: request.PreferredCulture,
            TimeZone: request.TimeZone,
            Reason: request.Reason).ApplyTo(user);

        await audit.WriteAsync(
            AuditActionCodes.UserUpdated, nameof(User), user.Id, user.DisplayName,
            new { ChangedFields = changes, Self = true, request.Reason },
            request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            user.Id, user.Status.ToString(), user.Version, "Your profile has been saved.", ["View"]));
    }
}
