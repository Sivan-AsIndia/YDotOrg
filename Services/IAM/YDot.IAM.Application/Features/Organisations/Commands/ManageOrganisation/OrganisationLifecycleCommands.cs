using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.Commands.ManageOrganisation;

/// <summary>The TenantAdmin editing their own Organisation profile.</summary>
public sealed record UpdateOrganisationProfileCommand(Guid TenantId, UpdateOrganisationProfileRequest Request);

/// <summary>The TenantAdmin submitting the profile for approval.</summary>
public sealed record SubmitOrganisationCommand(Guid TenantId, SubmitOrganisationRequest Request);

/// <summary>SuperAdmin picking a submission up.</summary>
public sealed record StartOrganisationReviewCommand(Guid TenantId, StartOrganisationReviewRequest Request);

/// <summary>SuperAdmin approving or rejecting.</summary>
public sealed record ReviewOrganisationCommand(Guid TenantId, ReviewOrganisationRequest Request);

/// <summary>SuperAdmin activating an approved Organisation.</summary>
public sealed record ActivateOrganisationCommand(Guid TenantId, TransitionRequest Request);

/// <summary>SuperAdmin suspending a live Organisation.</summary>
public sealed record SuspendOrganisationCommand(Guid TenantId, SuspendOrganisationRequest Request);

/// <summary>SuperAdmin lifting a suspension.</summary>
public sealed record ReactivateOrganisationCommand(Guid TenantId, ReactivateOrganisationRequest Request);

/// <summary>SuperAdmin retiring an Organisation. Terminal.</summary>
public sealed record ArchiveOrganisationCommand(Guid TenantId, ArchiveOrganisationRequest Request);

/// <summary>Editing the Organisation security policy from inside it.</summary>
public sealed record UpdateOrganisationSettingsCommand(Guid TenantId, UpdateOrganisationSettingsRequest Request);

/// <summary>
/// The Organisation lifecycle.
///
/// EVERY TRANSITION GOES THROUGH <see cref="TransitionAsync"/>, which does four things in one
/// place: check the move is legal against <c>Tenant.AllowedTransitionsFrom</c>, check the
/// caller optimistic version, stamp the timestamps, and append the history row. Doing it once
/// is why "Invited straight to Active" is not expressible here, and why the Organisation
/// timeline is complete by construction rather than by each handler remembering to write to it.
///
/// THE LADDER, from section 8 of the brief:
///
/// <code>
/// Invited -> InvitationAccepted -> ProfileIncomplete -> Submitted -> UnderReview
///                                                                       |
///                                             Rejected -> Resubmitted --+
///                                                                       |
///                                                        Approved -> Active
///                                                                       |
///                                                     Suspended <-------+-------> Archived
/// </code>
/// </summary>
public sealed class OrganisationLifecycleCommandHandler(
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    IUserRepository users,
    INotificationService notifications,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SecuritySettings> securityOptions,
    IOptions<ClientAppSettings> clientOptions)
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private readonly ClientAppSettings _client = clientOptions.Value;

    // =================================================================================
    // Profile
    // =================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateOrganisationProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        if (tenant.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // An Approved or Active Organisation may still correct its details; one that is
        // Submitted or UnderReview may not, because the reviewer is looking at it right now
        // and the thing they approve must be the thing they read.
        if (tenant.Status is TenantStatus.Submitted or TenantStatus.UnderReview or TenantStatus.Resubmitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This organisation is being reviewed and cannot be edited until a decision is made."));
        }

        if (tenant.Status == TenantStatus.Archived)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "An archived organisation cannot be edited."));
        }

        // ---- WHAT AN APPROVED ORGANISATION MAY STILL CHANGE ------------------------------------
        //
        // Everything, until SuperAdmin approves it; contact e-mail, telephone and address only,
        // afterwards. The identity fields — name, legal name, registration number, PAN, GSTIN,
        // type — are what the reviewer checked the registration certificate against, so leaving
        // them writable would let an Organisation be approved as one legal entity and then become
        // another with the accepted documents still attached.
        //
        // THE FIELDS ARE DROPPED RATHER THAN THE REQUEST REFUSED. The screen posts the whole form
        // whether or not it changed, so rejecting a request that merely REPEATS the stored name
        // would make an address correction impossible. `ApplyContactAndAddress` simply never
        // reads the other properties.
        var verified = OrganisationMappingConfig.IsProfileVerified(tenant.Status);

        if (verified)
        {
            command.Request.ApplyContactAndAddress(tenant);
        }
        else
        {
            command.Request.ApplyProfile(tenant);
        }

        await audit.WriteAsync(
            AuditActionCodes.TenantUpdated, nameof(Tenant), tenant.Id, tenant.Name,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var outstanding = OrganisationMappingConfig.OutstandingProfileFields(tenant);

        var message = verified
            ? "Contact details and address saved."
            : outstanding.Count == 0
                ? "Organisation profile saved. It is ready to submit."
                : $"Organisation profile saved. {outstanding.Count} field(s) still needed before you can submit.";

        return Result.Success(new OutcomeResponse(
            tenant.Id,
            tenant.Status.ToString(),
            tenant.Version,
            message,
            OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    // =================================================================================
    // Submission
    // =================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        SubmitOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        // A rejected Organisation resubmits rather than submits, so the review queue can show
        // that this one has been round the loop before.
        var isResubmission = tenant.Status == TenantStatus.Rejected;
        var target = isResubmission ? TenantStatus.Resubmitted : TenantStatus.Submitted;

        // ---- THE STATE COMES FIRST -------------------------------------------------------------
        //
        // The profile and document checks below used to run before anything asked whether this
        // Organisation could be submitted AT ALL, and the result was a genuinely misleading
        // refusal. An Organisation still in Invited - the invitation sent, not yet accepted -
        // cannot move to Submitted from any state machine's point of view, but it was told
        //
        //     "Attach your registration certificate before submitting for approval.
        //      No documents have been uploaded yet."
        //
        // which names a fixable-looking problem that is not the reason. Somebody chasing it
        // uploads the certificate, submits again, and is then told the real answer for the first
        // time. Worse, they reasonably conclude the upload did not work.
        //
        // `CanTransitionTo` is a pure check - it changes nothing - so asking it here costs
        // nothing and TransitionAsync below still asks it again along with the version check.
        if (!tenant.CanTransitionTo(target))
        {
            var allowed = Tenant.AllowedTransitionsFrom(tenant.Status);

            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                allowed.Count == 0
                    ? $"An organisation that is {OrganisationMappingConfig.DescribeStatus(tenant.Status).ToLowerInvariant()} cannot be submitted."
                    : $"An organisation that is {OrganisationMappingConfig.DescribeStatus(tenant.Status).ToLowerInvariant()} "
                      + $"can only move to: {string.Join(", ", allowed.Select(OrganisationMappingConfig.DescribeStatus))}."));
        }

        // The profile is enforced HERE rather than on every save, so a half-finished profile
        // can be parked and returned to.
        var outstanding = OrganisationMappingConfig.OutstandingProfileFields(tenant);
        if (outstanding.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.ProfileIncomplete(
                "Complete the organisation profile before submitting it for approval.",
                [.. outstanding.Select(field => new ValidationError(field, "This field is required before submission."))]));
        }

        // ---- The evidence -------------------------------------------------------------------
        //
        // ONLY THE TEXT FIELDS WERE CHECKED. An Organisation could fill in its address and submit
        // with NO DOCUMENTS AT ALL, and the screen the reviewer opens is called Registration
        // Verification - so they were being asked to verify a registration against nothing, and
        // the Documents tab on both sides read "No documents have been uploaded yet."
        //
        // The registration certificate is named specifically rather than counting to one, because
        // "at least one document" is satisfied by uploading a logo. It is the document that
        // evidences the thing being verified; the rest support it.
        var documents = await tenants.GetDocumentsAsync(tenant.Id, cancellationToken);

        var hasRegistration = documents.Any(document =>
            document.DocumentType == TenantDocumentType.RegistrationCertificate);

        if (!hasRegistration)
        {
            return Result.Failure<OutcomeResponse>(Error.ProfileIncomplete(
                documents.Count == 0
                    ? "Attach your registration certificate before submitting for approval. "
                      + "No documents have been uploaded yet."
                    : "Attach your registration certificate before submitting for approval.",
                [new ValidationError(
                    "Documents",
                    "A registration certificate is required before submission.")]));
        }

        var transition = await TransitionAsync(
            tenant, target, command.Request.ExpectedVersion,
            reason: null, notes: command.Request.Notes, cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        var now = clock.UtcNow;
        tenant.SubmittedAtUtc = now;
        tenant.SubmittedByUserId = currentUser.UserId;

        if (isResubmission)
        {
            tenant.ResubmissionCount += 1;
            // The old rejection is cleared, so the screen does not show a stale refusal
            // beside a fresh submission.
            tenant.RejectionReason = null;
        }

        await audit.WriteAsync(
            isResubmission ? AuditActionCodes.TenantResubmitted : AuditActionCodes.TenantProfileSubmitted,
            nameof(Tenant), tenant.Id, tenant.Name,
            new { tenant.ResubmissionCount }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyReviewersAsync(tenant, cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Your organisation has been submitted for approval. You will be told the outcome by e-mail.",
            OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        StartOrganisationReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var transition = await TransitionAsync(
            tenant, TenantStatus.UnderReview, command.Request.ExpectedVersion,
            reason: null, notes: "Review started.", cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        tenant.ReviewStartedAtUtc = clock.UtcNow;
        tenant.ReviewedByUserId = currentUser.UserId;

        await audit.WriteAsync(
            AuditActionCodes.TenantReviewStarted, nameof(Tenant), tenant.Id, tenant.Name,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Review started.", OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    // =================================================================================
    // Decision
    // =================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ReviewOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        // A rejection without a reason is a dead end rather than a decision, so it is refused
        // here as well as in the validator.
        if (!request.Approved && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Give a reason so the organisation can correct and resubmit.",
                    [new ValidationError(nameof(request.Reason), "A reason is required when rejecting.")]));
        }

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var businessUnit = await businessUnits.GetByIdAsync(tenant.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<OutcomeResponse>(Error.Dependency("The platform is not configured."));
        }

        var target = request.Approved ? TenantStatus.Approved : TenantStatus.Rejected;

        var transition = await TransitionAsync(
            tenant, target, request.ExpectedVersion, request.Reason, request.Notes, cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        if (request.Approved)
        {
            tenant.ApprovedAtUtc = now;
            tenant.ApprovedByUserId = currentUser.UserId;
            tenant.RejectionReason = null;

            // Approved means accepted; Active means switched on. Usually the same moment, but
            // kept separable so an Organisation can be approved now and go live on a date.
            if (request.ActivateImmediately)
            {
                var activation = await TransitionAsync(
                    tenant, TenantStatus.Active, tenant.Version,
                    reason: null, notes: "Activated on approval.", cancellationToken);

                if (activation.IsSuccess)
                {
                    tenant.ActivatedAtUtc = now;
                }
            }
        }
        else
        {
            tenant.RejectedAtUtc = now;
            tenant.RejectedByUserId = currentUser.UserId;
            tenant.RejectionReason = request.Reason;
        }

        await audit.WriteAsync(
            request.Approved ? AuditActionCodes.TenantApproved : AuditActionCodes.TenantRejected,
            nameof(Tenant), tenant.Id, tenant.Name,
            new { request.Approved, request.Reason, request.ActivateImmediately },
            request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyOutcomeAsync(tenant, businessUnit, request.Approved, request.Reason, cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            request.Approved
                ? tenant.Status == TenantStatus.Active
                    ? "Organisation approved and activated."
                    : "Organisation approved. Activate it when you are ready."
                : "Organisation rejected. The administrator has been told why.",
            OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ActivateOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var transition = await TransitionAsync(
            tenant, TenantStatus.Active, command.Request.ExpectedVersion,
            reason: null, command.Request.Comment, cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        tenant.ActivatedAtUtc = clock.UtcNow;
        tenant.SuspendedAtUtc = null;
        tenant.SuspensionReason = null;

        await audit.WriteAsync(
            AuditActionCodes.TenantActivated, nameof(Tenant), tenant.Id, tenant.Name,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Organisation activated. Its users can now sign in.",
            OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        SuspendOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var businessUnit = await businessUnits.GetByIdAsync(tenant.BusinessUnitId, cancellationToken);

        var transition = await TransitionAsync(
            tenant, TenantStatus.Suspended, command.Request.ExpectedVersion,
            command.Request.Reason, notes: null, cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        tenant.SuspendedAtUtc = clock.UtcNow;
        tenant.SuspensionReason = command.Request.Reason;

        await audit.WriteAsync(
            AuditActionCodes.TenantSuspended, nameof(Tenant), tenant.Id, tenant.Name,
            new { command.Request.Reason }, command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Existing sessions are NOT killed here. Suspension stops new sign-ins; the
        // Organisation check on each request is what stops the ones already open, and doing
        // it that way means no sweep has to succeed for the suspension to take effect.
        if (businessUnit is not null)
        {
            var admin = await FindPrimaryAdminAsync(tenant.Id, cancellationToken);
            if (admin is not null)
            {
                await notifications.SendOrganisationSuspendedAsync(
                    tenant, businessUnit, admin, command.Request.Reason, cancellationToken);
            }
        }

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Organisation suspended. Its users can no longer sign in.",
            OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ReactivateOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var transition = await TransitionAsync(
            tenant, TenantStatus.Active, command.Request.ExpectedVersion,
            reason: null, command.Request.Notes, cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        tenant.SuspendedAtUtc = null;
        tenant.SuspensionReason = null;
        tenant.ActivatedAtUtc = clock.UtcNow;

        await audit.WriteAsync(
            AuditActionCodes.TenantActivated, nameof(Tenant), tenant.Id, tenant.Name,
            new { Reactivated = true }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Organisation reactivated.", OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ArchiveOrganisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        var transition = await TransitionAsync(
            tenant, TenantStatus.Archived, command.Request.ExpectedVersion,
            command.Request.Reason, notes: null, cancellationToken);

        if (transition.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(transition.Error!);
        }

        tenant.ArchivedAtUtc = clock.UtcNow;

        await audit.WriteAsync(
            AuditActionCodes.TenantArchived, nameof(Tenant), tenant.Id, tenant.Name,
            new { command.Request.Reason }, command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Organisation archived.", OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    // =================================================================================
    // Settings
    // =================================================================================

    /// <summary>
    /// The Organisation security policy.
    ///
    /// AN ORGANISATION MAY TIGHTEN, NOT LOOSEN. Every value is clamped against the platform
    /// floor, so "manage your own settings" cannot become a way to disable lockout or drop the
    /// password length to four. The clamp is silent rather than an error: the caller asked for
    /// something weaker and got the floor, which is the safe reading.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateOrganisationSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantNotFound());
        }

        if (tenant.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (request.DefaultMfaRequirement.HasValue)
        {
            tenant.DefaultMfaRequirement = request.DefaultMfaRequirement.Value;
        }

        if (request.MaximumFailedAccessAttempts.HasValue)
        {
            // Fewer attempts is stricter, so the platform value is the CEILING here.
            tenant.MaximumFailedAccessAttempts = Math.Clamp(
                request.MaximumFailedAccessAttempts.Value, 3, _security.MaximumFailedAccessAttempts);
        }

        if (request.LockoutDurationMinutes.HasValue)
        {
            // A longer lockout is stricter, so the platform value is the FLOOR.
            tenant.LockoutDurationMinutes = Math.Max(
                _security.LockoutMinutes, request.LockoutDurationMinutes.Value);
        }

        if (request.PasswordMinimumLength.HasValue)
        {
            tenant.PasswordMinimumLength = Math.Clamp(
                request.PasswordMinimumLength.Value, _security.PasswordMinimumLength, 64);
        }

        if (request.PasswordExpiryDays.HasValue)
        {
            tenant.PasswordExpiryDays = Math.Max(0, request.PasswordExpiryDays.Value);
        }

        if (request.SessionIdleTimeoutMinutes.HasValue)
        {
            // A shorter idle timeout is stricter, so the platform value is the ceiling.
            tenant.SessionIdleTimeoutMinutes = Math.Clamp(
                request.SessionIdleTimeoutMinutes.Value, 5, _security.SessionIdleTimeoutMinutes);
        }

        await audit.WriteAsync(
            AuditActionCodes.TenantUpdated, nameof(Tenant), tenant.Id, tenant.Name,
            new
            {
                tenant.DefaultMfaRequirement,
                tenant.MaximumFailedAccessAttempts,
                tenant.LockoutDurationMinutes,
                tenant.PasswordMinimumLength,
                tenant.SessionIdleTimeoutMinutes
            },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            tenant.Id, tenant.Status.ToString(), tenant.Version,
            "Organisation settings saved.", OrganisationMappingConfig.PermittedActionsFor(tenant)));
    }

    // =================================================================================
    // The one place a status changes
    // =================================================================================

    /// <summary>
    /// Moves the Organisation, or explains why it cannot move.
    ///
    /// Three checks, in this order, because each is cheaper and more specific than the next:
    /// terminal state, optimistic version, legal transition. The history row is appended here
    /// so the timeline cannot be incomplete — there is no other way to change the status.
    /// </summary>
    private async Task<Result> TransitionAsync(
        Tenant tenant,
        TenantStatus target,
        long expectedVersion,
        string? reason,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (tenant.Version != expectedVersion)
        {
            return Result.Failure(Error.Concurrency());
        }

        if (!tenant.CanTransitionTo(target))
        {
            var allowed = Tenant.AllowedTransitionsFrom(tenant.Status);

            return Result.Failure(Error.InvalidTransition(
                allowed.Count == 0
                    ? $"An organisation that is {OrganisationMappingConfig.DescribeStatus(tenant.Status).ToLowerInvariant()} cannot be changed."
                    : $"An organisation that is {OrganisationMappingConfig.DescribeStatus(tenant.Status).ToLowerInvariant()} "
                      + $"can only move to: {string.Join(", ", allowed.Select(OrganisationMappingConfig.DescribeStatus))}."));
        }

        var from = tenant.Status;
        tenant.Status = target;

        await tenants.AddStatusHistoryAsync(new TenantStatusHistory
        {
            BusinessUnitId = tenant.BusinessUnitId,
            TenantId = tenant.Id,
            FromStatus = from,
            ToStatus = target,
            OccurredAtUtc = clock.UtcNow,
            ActorUserId = currentUser.UserId,
            ActorDisplayName = currentUser.DisplayName,
            Reason = reason,
            Notes = notes,
            CorrelationId = currentUser.CorrelationId
        }, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// The Organisation first administrator: the person onboarding e-mails are addressed to.
    /// </summary>
    private Task<User?> FindPrimaryAdminAsync(Guid tenantId, CancellationToken cancellationToken) =>
        users.FindTenantAdminAsync(tenantId, cancellationToken);

    private async Task NotifyReviewersAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var businessUnit = await businessUnits.GetByIdAsync(tenant.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return;
        }

        var reviewerEmail = businessUnit.SupportEmail ?? businessUnit.ContactEmail;
        if (string.IsNullOrWhiteSpace(reviewerEmail))
        {
            return;
        }

        // PLATFORM HOST, deliberately: the reviewer is a platform administrator, and the
        // Organisation under review is identified by the id in the path rather than by the host.
        var reviewUrl = _client.PlatformUrl(
            $"/app/administration/organisation/registration-verification/{tenant.Id}");

        await notifications.SendOrganisationAwaitingReviewAsync(
            tenant, businessUnit, [reviewerEmail], reviewUrl, cancellationToken);
    }

    private async Task NotifyOutcomeAsync(
        Tenant tenant, BusinessUnit businessUnit, bool approved, string? reason,
        CancellationToken cancellationToken)
    {
        var admin = await FindPrimaryAdminAsync(tenant.Id, cancellationToken);
        if (admin is null)
        {
            return;
        }

        var host = $"{tenant.Subdomain}.{businessUnit.RootDomain}";
        var organisationUrl = _client.TenantUrl(host, _client.SignInPath);

        if (approved)
        {
            await notifications.SendOrganisationApprovedAsync(
                tenant, businessUnit, admin, organisationUrl, cancellationToken);
        }
        else
        {
            // THE ORGANISATION'S OWN HOST, not the platform's. This was the platform base URL,
            // which sent a rejected Organisation's administrator to a host where their
            // Organisation does not resolve — so the person told to correct their details
            // could not reach the page holding them.
            var resubmitUrl = _client.TenantUrl(host, _client.OrganisationOnboardingPath);

            await notifications.SendOrganisationRejectedAsync(
                tenant, businessUnit, admin, reason ?? "No reason was given.", resubmitUrl, cancellationToken);
        }
    }
}
