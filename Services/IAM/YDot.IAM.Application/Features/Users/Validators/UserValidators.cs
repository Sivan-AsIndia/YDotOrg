using FluentValidation;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Menus.DTOs;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Users.Validators;

/// <summary>Validators for the Users, Roles, Menus and Governance slices.</summary>
public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(request => request.FirstName)
            .NotEmpty().WithMessage("Enter a first name.")
            .MinimumLength(2).WithMessage("The first name is too short.")
            .MaximumLength(80);

        RuleFor(request => request.LastName)
            .NotEmpty().WithMessage("Enter a last name.")
            .MaximumLength(80);

        RuleFor(request => request.MiddleName).MaximumLength(80);
        RuleFor(request => request.DisplayName).MaximumLength(160);
        RuleFor(request => request.EmployeeNumber).MaximumLength(40);
        RuleFor(request => request.Designation).MaximumLength(120);

        RuleFor(request => request.Email)
            .NotEmpty().WithMessage("Enter an e-mail address.")
            .Must(value => EmailValue.TryParse(value) is not null)
            .WithMessage("That e-mail address is not valid.");

        RuleFor(request => request.Username)
            .Must(value => UsernameValue.TryParse(value) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.Username))
            .WithMessage("Use 3 to 64 letters, digits, dots, hyphens or underscores.");

        RuleFor(request => request)
            .Must(request => MobileNumberValue.TryParse(
                request.MobileCountryCode, request.MobileNumber) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.MobileNumber))
            .WithName(nameof(CreateUserRequest.MobileNumber))
            .WithMessage("Enter a valid mobile number with its country code.");

        // An access window that closes before it opens is never intended.
        RuleFor(request => request.AccessEndsAtUtc)
            .GreaterThan(request => request.AccessStartsAtUtc!.Value)
            .When(request => request.AccessStartsAtUtc.HasValue && request.AccessEndsAtUtc.HasValue)
            .WithMessage("The access end date must be after the start date.");

        // An Employee is expected to carry a staff number; other categories are not.
        RuleFor(request => request.EmployeeNumber)
            .NotEmpty()
            .When(request => request.AccountCategory == UserAccountCategory.Employee
                             && request.EngagementType is EngagementType.FullTime or EngagementType.PartTime)
            .WithMessage("An employee number is required for employees.");

        RuleFor(request => request.InvitationMessage).MaximumLength(1000);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("Reload the page and try again.");

        RuleFor(request => request.FirstName)
            .MinimumLength(2).MaximumLength(80)
            .When(request => !string.IsNullOrWhiteSpace(request.FirstName));

        RuleFor(request => request.LastName)
            .MaximumLength(80)
            .When(request => !string.IsNullOrWhiteSpace(request.LastName));

        RuleFor(request => request.MiddleName).MaximumLength(80);
        RuleFor(request => request.DisplayName).MaximumLength(160);
        RuleFor(request => request.EmployeeNumber).MaximumLength(40);
        RuleFor(request => request.Designation).MaximumLength(120);
        RuleFor(request => request.PreferredCulture).MaximumLength(20);
        RuleFor(request => request.TimeZone).MaximumLength(80);
        RuleFor(request => request.AvatarUrl).MaximumLength(500);

        RuleFor(request => request)
            .Must(request => MobileNumberValue.TryParse(
                request.MobileCountryCode, request.MobileNumber) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.MobileNumber))
            .WithName(nameof(UpdateUserRequest.MobileNumber))
            .WithMessage("Enter a valid mobile number with its country code.");

        RuleFor(request => request.ExitedOn)
            .GreaterThanOrEqualTo(request => request.JoinedOn!.Value)
            .When(request => request.JoinedOn.HasValue && request.ExitedOn.HasValue)
            .WithMessage("The exit date cannot be before the joining date.");
    }
}

public sealed class UserLifecycleRequestValidator : AbstractValidator<UserLifecycleRequest>
{
    public UserLifecycleRequestValidator()
    {
        // A status change with no reason is unanswerable at the next audit.
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Give a reason.")
            .MinimumLength(3)
            .MaximumLength(500);

        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class AdminResetPasswordRequestValidator : AbstractValidator<AdminResetPasswordRequest>
{
    public AdminResetPasswordRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        RuleFor(request => request.TemporaryPassword)
            .MinimumLength(10).MaximumLength(128)
            .When(request => !request.SendResetLink
                             && !string.IsNullOrWhiteSpace(request.TemporaryPassword))
            .WithMessage("A temporary password must be at least 10 characters.");
    }
}

public sealed class AssignUserRolesRequestValidator : AbstractValidator<AssignUserRolesRequest>
{
    public AssignUserRolesRequestValidator()
    {
        RuleFor(request => request.RoleIds)
            .NotNull().WithMessage("Send the full set of roles, even if it is empty.");

        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
        RuleFor(request => request.Justification).MaximumLength(1000);

        // A primary role that is not in the set being assigned is a contradiction.
        RuleFor(request => request)
            .Must(request => request.PrimaryRoleId is null
                             || (request.RoleIds?.Contains(request.PrimaryRoleId.Value) ?? false))
            .WithName(nameof(AssignUserRolesRequest.PrimaryRoleId))
            .WithMessage("The primary role must be one of the roles being assigned.");

        RuleFor(request => request.EffectiveToUtc)
            .GreaterThan(_ => DateTimeOffset.UtcNow)
            .When(request => request.EffectiveToUtc.HasValue)
            .WithMessage("The end date must be in the future.");
    }
}

public sealed class AssignUserDataScopesRequestValidator : AbstractValidator<AssignUserDataScopesRequest>
{
    public AssignUserDataScopesRequestValidator()
    {
        RuleFor(request => request.DataScopes).NotNull();
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
        RuleFor(request => request.Justification).MaximumLength(1000);

        RuleForEach(request => request.DataScopes).ChildRules(scope =>
            scope.RuleFor(item => item.ScopeValue)
                .NotEmpty().WithMessage("Each scope needs a value.")
                .MaximumLength(200));
    }
}

public sealed class ExtendUserAccessRequestValidator : AbstractValidator<ExtendUserAccessRequest>
{
    public ExtendUserAccessRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
        RuleFor(request => request.Reason).MaximumLength(500);
    }
}

public sealed class UnlockUserRequestValidator : AbstractValidator<UnlockUserRequest>
{
    public UnlockUserRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
        RuleFor(request => request.Reason).MaximumLength(500);
    }
}

public sealed class RequestLoginIdentifierChangeRequestValidator
    : AbstractValidator<RequestLoginIdentifierChangeRequest>
{
    public RequestLoginIdentifierChangeRequestValidator()
    {
        RuleFor(request => request.RequestedValue)
            .NotEmpty().WithMessage("Enter the new value.")
            .MaximumLength(320);

        RuleFor(request => request.RequestedValue)
            .Must(value => EmailValue.TryParse(value) is not null)
            .When(request => request.IsEmailChange)
            .WithMessage("That e-mail address is not valid.");

        RuleFor(request => request.RequestedValue)
            .Must(value => UsernameValue.TryParse(value) is not null)
            .When(request => !request.IsEmailChange)
            .WithMessage("Use 3 to 64 letters, digits, dots, hyphens or underscores.");

        RuleFor(request => request.Reason).MaximumLength(1000);
    }
}

public sealed class CreateBulkOperationRequestValidator : AbstractValidator<CreateBulkOperationRequest>
{
    /// <summary>
    /// A ceiling on one job. Beyond this the request should be split, because a single
    /// transaction over ten thousand users is a lock nobody wants to hold.
    /// </summary>
    private const int MaximumRows = 2000;

    public CreateBulkOperationRequestValidator()
    {
        RuleFor(request => request.UserIds)
            .NotNull().WithMessage("Choose at least one user.")
            .Must(ids => ids is { Count: > 0 }).WithMessage("Choose at least one user.")
            .Must(ids => ids is null || ids.Count <= MaximumRows)
            .WithMessage($"A single job can cover at most {MaximumRows} users. Split the selection.");

        RuleFor(request => request.RoleId)
            .NotEmpty()
            .When(request => request.ActionType is BulkActionType.AssignRole or BulkActionType.RemoveRole)
            .WithMessage("Choose a role for this action.");

        RuleFor(request => request.AccessEndsAtUtc)
            .NotNull()
            .When(request => request.ActionType == BulkActionType.ExtendAccess)
            .WithMessage("Choose the new access end date.");

        RuleFor(request => request.Reason)
            .NotEmpty()
            .When(request => request.ActionType is BulkActionType.Suspend or BulkActionType.Deactivate)
            .WithMessage("Give a reason for this action.")
            .MaximumLength(500);
    }
}

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter a role name.")
            .MinimumLength(2)
            .MaximumLength(120);

        RuleFor(request => request.Description).MaximumLength(500);
        RuleFor(request => request.DisplayTag).MaximumLength(40);

        RuleFor(request => request.Code)
            .Must(value => CodeValue.TryParse(value) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.Code))
            .WithMessage("Use upper-case letters, digits, underscores or hyphens.");

        RuleFor(request => request.Priority)
            .InclusiveBetween(0, 999)
            .WithMessage("Priority must be between 0 and 999.");
    }
}

public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        RuleFor(request => request.Name)
            .MinimumLength(2).MaximumLength(120)
            .When(request => !string.IsNullOrWhiteSpace(request.Name));

        RuleFor(request => request.Description).MaximumLength(500);
        RuleFor(request => request.DisplayTag).MaximumLength(40);

        RuleFor(request => request.Priority)
            .InclusiveBetween(0, 999)
            .When(request => request.Priority.HasValue);
    }
}

public sealed class AssignRolePermissionsRequestValidator : AbstractValidator<AssignRolePermissionsRequest>
{
    public AssignRolePermissionsRequestValidator()
    {
        RuleFor(request => request.PermissionCodes)
            .NotNull().WithMessage("Send the full set of permissions, even if it is empty.");

        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
        RuleFor(request => request.Justification).MaximumLength(1000);

        // A code that is both granted and denied is a contradiction; deny would win silently.
        RuleFor(request => request)
            .Must(request => request.DeniedPermissionCodes is null
                             || request.PermissionCodes is null
                             || !request.DeniedPermissionCodes.Intersect(
                                 request.PermissionCodes, StringComparer.Ordinal).Any())
            .WithName(nameof(AssignRolePermissionsRequest.DeniedPermissionCodes))
            .WithMessage("A permission cannot be both granted and denied.");
    }
}

public sealed class CreateRoleIncompatibilityRequestValidator
    : AbstractValidator<CreateRoleIncompatibilityRequest>
{
    public CreateRoleIncompatibilityRequestValidator()
    {
        RuleFor(request => request.RoleId).NotEmpty();

        RuleFor(request => request.ConflictingRoleId)
            .NotEmpty()
            .NotEqual(request => request.RoleId)
            .WithMessage("A role cannot conflict with itself.");

        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Say why these roles cannot be held together.")
            .MaximumLength(1000);
    }
}

public sealed class CreateMenuDefinitionRequestValidator : AbstractValidator<CreateMenuDefinitionRequest>
{
    public CreateMenuDefinitionRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty().WithMessage("Enter a menu code.")
            .Must(value => CodeValue.TryParse(value) is not null)
            .WithMessage("Use upper-case letters, digits, underscores or hyphens.");

        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter a menu label.")
            .MaximumLength(160);

        RuleFor(request => request.ModuleCode)
            .NotEmpty().WithMessage("Enter the owning module code.")
            .MaximumLength(20);

        RuleFor(request => request.Route).MaximumLength(300);
        RuleFor(request => request.Icon).MaximumLength(80);
        RuleFor(request => request.Description).MaximumLength(500);

        // A top-level node has no parent; anything deeper must name one.
        RuleFor(request => request.ParentMenuId)
            .NotEmpty()
            .When(request => request.Level != MenuLevel.Menu)
            .WithMessage("A submenu must have a parent.");

        RuleFor(request => request.ParentMenuId)
            .Empty()
            .When(request => request.Level == MenuLevel.Menu)
            .WithMessage("A top-level menu cannot have a parent.");
    }
}

public sealed class ConfigureTenantMenuRequestValidator : AbstractValidator<ConfigureTenantMenuRequest>
{
    public ConfigureTenantMenuRequestValidator()
    {
        RuleFor(request => request.Items).NotNull();

        RuleForEach(request => request.Items).ChildRules(item =>
        {
            item.RuleFor(node => node.MenuDefinitionId).NotEmpty();
            item.RuleFor(node => node.DisplayNameOverride).MaximumLength(160);
            item.RuleFor(node => node.IconOverride).MaximumLength(80);
        });
    }
}

public sealed class MapRoleMenusRequestValidator : AbstractValidator<MapRoleMenusRequest>
{
    public MapRoleMenusRequestValidator()
    {
        RuleFor(request => request.VisibleMenuIds).NotNull();
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        RuleFor(request => request)
            .Must(request => request.LandingMenuId is null
                             || (request.VisibleMenuIds?.Contains(request.LandingMenuId.Value) ?? false))
            .WithName(nameof(MapRoleMenusRequest.LandingMenuId))
            .WithMessage("The landing page must be one of the visible menu items.");
    }
}

public sealed class CreateAccessRequestRequestValidator : AbstractValidator<CreateAccessRequestRequest>
{
    public CreateAccessRequestRequestValidator()
    {
        RuleFor(request => request.RequestedForUserId).NotEmpty();

        // The justification is the whole point of the record, so a token one is refused.
        RuleFor(request => request.BusinessJustification)
            .NotEmpty().WithMessage("Explain why this access is needed.")
            .MinimumLength(10).WithMessage("Give at least a sentence of justification.")
            .MaximumLength(1000);

        RuleFor(request => request.RoleId)
            .NotEmpty()
            .When(request => request.RequestType is AccessRequestType.RoleAssignment
                             or AccessRequestType.TemporaryElevation)
            .WithMessage("Choose a role.");

        RuleFor(request => request.PermissionCode)
            .NotEmpty()
            .When(request => request.RequestType == AccessRequestType.PermissionGrant)
            .WithMessage("Choose a permission.");

        RuleFor(request => request.ScopeValue)
            .NotEmpty()
            .When(request => request.RequestType == AccessRequestType.DataScopeGrant)
            .WithMessage("Enter the scope value.");

        RuleFor(request => request.AccessEndsAtUtc)
            .NotNull()
            .When(request => request.RequestType == AccessRequestType.TemporaryElevation)
            .WithMessage("Temporary access needs an end date.");

        RuleFor(request => request.AccessEndsAtUtc)
            .GreaterThan(_ => DateTimeOffset.UtcNow)
            .When(request => request.AccessEndsAtUtc.HasValue)
            .WithMessage("The end date must be in the future.");
    }
}

public sealed class DecideAccessRequestRequestValidator : AbstractValidator<DecideAccessRequestRequest>
{
    public DecideAccessRequestRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        RuleFor(request => request.Notes)
            .NotEmpty()
            .When(request => !request.Approved)
            .WithMessage("Give a reason so the requester knows what to do next.")
            .MaximumLength(1000);
    }
}

public sealed class CreateAccessReviewCampaignRequestValidator
    : AbstractValidator<CreateAccessReviewCampaignRequest>
{
    public CreateAccessReviewCampaignRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter a campaign name.")
            .MaximumLength(200);

        RuleFor(request => request.Description).MaximumLength(1000);

        RuleFor(request => request.DueAtUtc)
            .GreaterThan(_ => DateTimeOffset.UtcNow)
            .WithMessage("The due date must be in the future.");
    }
}

public sealed class DecideAccessReviewRequestValidator : AbstractValidator<DecideAccessReviewRequest>
{
    public DecideAccessReviewRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        // Modify and Revoke both take something away, so the person losing it gets a reason.
        RuleFor(request => request.DecisionReason)
            .NotEmpty()
            .When(request => request.Decision != AccessReviewDecision.Retain)
            .WithMessage("Give a reason for changing or removing this access.")
            .MaximumLength(1000);
    }
}
