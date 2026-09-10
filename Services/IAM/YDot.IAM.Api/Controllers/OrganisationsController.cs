using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.Commands.ManageOrganisation;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Queries.OrganisationQueries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Controllers;

/// <summary>
/// Organisations — called Tenants in the schema, Organisations everywhere a person can see.
///
/// THIS CONTROLLER HAS TWO HALVES WITH DIFFERENT AUDIENCES, and the split is the whole point:
///
/// <code>
/// /api/v1/organisations        PLATFORM. SuperAdmin only. Create, review, approve, suspend.
/// /api/v1/organisations/mine   TENANT.   The TenantAdmin editing their OWN organisation.
/// </code>
///
/// The second half takes NO id. The Organisation comes from the request context, so a
/// TenantAdmin has nothing in the URL to change in order to reach somebody else. That is the
/// simplest possible protection and it is why the two halves are separate routes rather than
/// one route with a permission check.
/// </summary>
[Route("api/v1/organisations")]
[Authorize]
public sealed class OrganisationsController(
    CreateOrganisationCommandHandler create,
    OrganisationLifecycleCommandHandler lifecycle,
    OrganisationAssetCommandHandler assets,
    OrganisationQueryHandler queries,
    OrganisationStructureCommandHandler structure,
    OrganisationStructureQueryHandler structureQueries,
    IDateTimeProvider clock,
    ILogger<OrganisationsController> logger) : ApiControllerBase
{
    // =================================================================================
    // Platform: SuperAdmin administering every organisation
    // =================================================================================

    [HttpGet]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<OrganisationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] TenantSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching organisations.");

        var result = await queries.HandleAsync(new SearchOrganisationsQuery(filter), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation search failed.");
        }

        return FromResult(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetOrganisationAsync))]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganisationAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting organisation. {OrganisationId}", id);

        var result = await queries.HandleAsync(new GetOrganisationQuery(id), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get organisation. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpGet("statistics")]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting organisation statistics.");

        var result = await queries.HandleAsync(new GetOrganisationStatisticsQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get organisation statistics.");
        }

        return FromResult(result);
    }

    /// <summary>Everything sitting on the SuperAdmin desk, oldest first.</summary>
    [HttpGet("awaiting-review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAwaitingReviewAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting organisations awaiting review.");

        var result = await queries.HandleAsync(new GetOrganisationsAwaitingReviewQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get organisations awaiting review.");
        }

        return FromResult(result);
    }

    /// <summary>
    /// Checks whether a web address is free.
    ///
    /// Answers only "available or not" and never lists what is taken, so it cannot be walked
    /// to enumerate the platform customers.
    /// </summary>
    [HttpPost("check-subdomain")]
    [HasPermission(PermissionCodes.Platform.TenantsCreate)]
    [ProducesResponseType(typeof(ApiResponse<CheckSubdomainResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckSubdomainAsync(
        [FromBody] CheckSubdomainRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking organisation subdomain availability.");

        var result = await create.HandleAsync(new CheckSubdomainQuery(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation subdomain availability check failed.");
        }

        return FromResult(result);
    }

    /// <summary>
    /// Creates an Organisation and invites its first administrator.
    ///
    /// One call creates the Organisation, its host, its roles, its default navigation, the
    /// TenantAdmin user and the invitation — because an Organisation missing any of those is
    /// not usable.
    /// </summary>
    [HttpPost]
    [HasPermission(PermissionCodes.Platform.TenantsCreate)]
    [ProducesResponseType(typeof(ApiResponse<CreateOrganisationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrganisationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating organisation.");

        var result = await create.HandleAsync(new CreateOrganisationCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation creation failed.");
            return FromResult(result);
        }

        logger.LogInformation("Organisation created successfully. {OrganisationId}", result.Value!.TenantId);

        return CreatedFromResult(result, nameof(GetOrganisationAsync), new { id = result.Value!.TenantId },
            "Organisation created and the administrator invited.");
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [HasPermission(PermissionCodes.Platform.TenantsInviteAdmin)]
    [ProducesResponseType(typeof(ApiResponse<CreateOrganisationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendInvitationAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resending organisation administrator invitation. {OrganisationId}", id);

        var result = await create.HandleAsync(new ResendOrganisationInvitationCommand(id), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to resend organisation administrator invitation. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation administrator invitation resent successfully. {OrganisationId}", id);
        }

        return FromResult(result, "Invitation re-sent.");
    }

    // ---- Review and decision ------------------------------------------------------------

    [HttpPost("{id:guid}/start-review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartReviewAsync(
        Guid id, [FromBody] StartOrganisationReviewRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting organisation review. {OrganisationId}", id);

        var result = await lifecycle.HandleAsync(new StartOrganisationReviewCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to start organisation review. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation review started successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Approves or rejects. A rejection must carry a reason, so the TenantAdmin can act on it.
    /// </summary>
    [HttpPost("{id:guid}/review")]
    [HasPermission(PermissionCodes.Platform.TenantsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReviewAsync(
        Guid id, [FromBody] ReviewOrganisationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reviewing organisation. {OrganisationId}", id);

        var result = await lifecycle.HandleAsync(new ReviewOrganisationCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation review failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation reviewed successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.Platform.TenantsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] TransitionRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Activating organisation. {OrganisationId}", id);

        var result = await lifecycle.HandleAsync(new ActivateOrganisationCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation activation failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation activated successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(PermissionCodes.Platform.TenantsSuspend)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuspendAsync(
        Guid id, [FromBody] SuspendOrganisationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Suspending organisation. {OrganisationId}", id);

        var result = await lifecycle.HandleAsync(new SuspendOrganisationCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation suspension failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation suspended successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(PermissionCodes.Platform.TenantsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id, [FromBody] ReactivateOrganisationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reactivating organisation. {OrganisationId}", id);

        var result = await lifecycle.HandleAsync(new ReactivateOrganisationCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation reactivation failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation reactivated successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(PermissionCodes.Platform.TenantsArchive)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArchiveAsync(
        Guid id, [FromBody] ArchiveOrganisationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Archiving organisation. {OrganisationId}", id);

        var result = await lifecycle.HandleAsync(new ArchiveOrganisationCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation archive failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation archived successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    // ---- Hosts ----------------------------------------------------------------------------

    [HttpGet("{id:guid}/domains")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationDomainResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDomainsAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting organisation domains. {OrganisationId}", id);

        var result = await queries.GetDomainsAsync(id, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get organisation domains. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/domains")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDomainResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddDomainAsync(
        Guid id, [FromBody] AddOrganisationDomainRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Adding organisation domain. {OrganisationId}", id);

        var result = await assets.HandleAsync(new AddOrganisationDomainCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to add organisation domain. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation domain added successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/domains/verify")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDomainResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyDomainAsync(
        Guid id, [FromBody] VerifyOrganisationDomainRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Verifying organisation domain. {OrganisationId}", id);

        var result = await assets.HandleAsync(new VerifyOrganisationDomainCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation domain verification failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation domain verified successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpDelete("{id:guid}/domains/{domainId:guid}")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveDomainAsync(
        Guid id, Guid domainId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Removing organisation domain. {OrganisationId} {DomainId}", id, domainId);

        var result = await assets.HandleAsync(new RemoveOrganisationDomainCommand(id, domainId), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to remove organisation domain. {OrganisationId} {DomainId}", id, domainId);
        }
        else
        {
            logger.LogInformation("Organisation domain removed successfully. {OrganisationId} {DomainId}", id, domainId);
        }

        return FromResult(result);
    }

    // ---- Documents ---------------------------------------------------------------------------

    [HttpGet("{id:guid}/documents")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationDocumentResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocumentsAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting organisation documents. {OrganisationId}", id);

        var result = await queries.GetDocumentsAsync(id, clock.UtcNow, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get organisation documents. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    [HttpPost("{id:guid}/documents/review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDocumentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewDocumentAsync(
        Guid id, [FromBody] ReviewOrganisationDocumentRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reviewing organisation document. {OrganisationId}", id);

        var result = await assets.HandleAsync(new ReviewOrganisationDocumentCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation document review failed. {OrganisationId}", id);
        }
        else
        {
            logger.LogInformation("Organisation document reviewed successfully. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    /// <summary>The Organisation lifecycle timeline.</summary>
    [HttpGet("{id:guid}/timeline")]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationTimelineResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimelineAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting organisation timeline. {OrganisationId}", id);

        var result = await queries.GetTimelineAsync(id, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get organisation timeline. {OrganisationId}", id);
        }

        return FromResult(result);
    }

    // =================================================================================
    // Tenant: the TenantAdmin managing their OWN organisation. No id anywhere.
    //
    // [AllowedWhileOnboarding] marks the five that an unapproved Organisation cannot finish
    // onboarding without: read the profile, save it, attach documents, list them, submit.
    // Settings, departments and units deliberately carry no such mark - they are running an
    // Organisation, which is work that starts once it has been approved.
    // =================================================================================

    /// <summary>
    /// The caller own Organisation.
    ///
    /// Resolved from the request context, so there is no id to tamper with.
    /// </summary>
    [HttpGet("mine")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMineAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting current organisation.");

        var result = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get current organisation.");
        }

        return FromResult(result);
    }

    /// <summary>
    /// Saves the Organisation profile.
    ///
    /// Partial saves are allowed. Completeness is enforced at SUBMISSION, so a half-finished
    /// profile can be parked rather than losing work.
    /// </summary>
    [HttpPut("mine")]
    [HasPermission(PermissionCodes.OrganisationEdit)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMineAsync(
        [FromBody] UpdateOrganisationProfileRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating current organisation profile.");

        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            logger.LogWarning("Failed to resolve current organisation before profile update.");
            return FromResult(current);
        }

        var result = await lifecycle.HandleAsync(
            new UpdateOrganisationProfileCommand(current.Value!.Id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation profile update failed. {OrganisationId}", current.Value.Id);
        }
        else
        {
            logger.LogInformation("Organisation profile updated successfully. {OrganisationId}", current.Value.Id);
        }

        return FromResult(result);
    }

    /// <summary>Submits the profile for SuperAdmin approval.</summary>
    [HttpPost("mine/submit")]
    [HasPermission(PermissionCodes.OrganisationSubmit)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitMineAsync(
        [FromBody] SubmitOrganisationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Submitting current organisation for approval.");

        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            logger.LogWarning("Failed to resolve current organisation before submission.");
            return FromResult(current);
        }

        var result = await lifecycle.HandleAsync(
            new SubmitOrganisationCommand(current.Value!.Id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation submission failed. {OrganisationId}", current.Value.Id);
        }
        else
        {
            logger.LogInformation("Organisation submitted for approval successfully. {OrganisationId}", current.Value.Id);
        }

        return FromResult(result);
    }

    // REMOVED: POST mine/documents.
    //
    // It registered a document from a JSON body and took the storage path FROM THE CALLER. That
    // was harmless only while no object store existed - the row pointed at nothing, which is why
    // reviewers could never open anything. Now that files are really stored, a caller-supplied
    // path is a cross-tenant write: change the string, land in another Organisation's prefix.
    //
    // Uploading now goes through OrganisationDocumentsController, which takes multipart, derives
    // the path from the Organisation and document ids, and hashes what it actually stored.

    [HttpGet("mine/documents")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [AllowedWhileOnboarding]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationDocumentResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyDocumentsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting current organisation documents.");

        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            logger.LogWarning("Failed to resolve current organisation before getting documents.");
            return FromResult(current);
        }

        var result = await queries.GetDocumentsAsync(current.Value!.Id, clock.UtcNow, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get current organisation documents. {OrganisationId}", current.Value.Id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// The Organisation security policy.
    ///
    /// An Organisation may TIGHTEN these but never loosen them below the platform floor — the
    /// handler clamps every value.
    /// </summary>
    [HttpPut("mine/settings")]
    [HasPermission(PermissionCodes.OrganisationManageSettings)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMySettingsAsync(
        [FromBody] UpdateOrganisationSettingsRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating current organisation settings.");

        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            logger.LogWarning("Failed to resolve current organisation before settings update.");
            return FromResult(current);
        }

        var result = await lifecycle.HandleAsync(
            new UpdateOrganisationSettingsCommand(current.Value!.Id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation settings update failed. {OrganisationId}", current.Value.Id);
        }
        else
        {
            logger.LogInformation("Organisation settings updated successfully. {OrganisationId}", current.Value.Id);
        }

        return FromResult(result);
    }

    // =================================================================================
    // Departments and organisation units
    //
    // TWO SEPARATE HIERARCHIES, deliberately. A department is what somebody DOES (Fundraising,
    // Finance); a unit is where they SIT (Head office, Southern region). Most organisations need
    // both, and collapsing them into one tree forces a choice that has to be undone later - a
    // fundraiser in the southern office belongs to Fundraising AND to Southern, and neither is
    // child of the other.
    //
    // Both are the caller's OWN Organisation's, so none of these takes an Organisation id.
    // =================================================================================

    [HttpGet("mine/departments")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DepartmentResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDepartmentsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting current organisation departments.");

        var result = await structureQueries.HandleAsync(new GetDepartmentsQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get current organisation departments.");
        }

        return FromResult(result);
    }

    [HttpPost("mine/departments")]
    [HasPermission(PermissionCodes.OrganisationManageDepartments)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDepartmentAsync(
        [FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating organisation department.");

        var result = await structure.HandleAsync(new CreateDepartmentCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation department creation failed.");
        }
        else
        {
            logger.LogInformation("Organisation department created successfully.");
        }

        return FromResult(result);
    }

    [HttpPut("mine/departments/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageDepartments)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDepartmentAsync(
        Guid id, [FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating organisation department. {DepartmentId}", id);

        var result = await structure.HandleAsync(new UpdateDepartmentCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation department update failed. {DepartmentId}", id);
        }
        else
        {
            logger.LogInformation("Organisation department updated successfully. {DepartmentId}", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Removes a department.
    ///
    /// Refused while anybody is still in it, or while another department sits under it - the
    /// alternative is orphaning people or a subtree. Setting it to inactive is the way to retire
    /// one that still has history attached, which is nearly always what was actually wanted.
    /// </summary>
    [HttpDelete("mine/departments/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageDepartments)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDepartmentAsync(
        Guid id, [FromBody] DeleteStructureRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting organisation department. {DepartmentId}", id);

        var result = await structure.HandleAsync(new DeleteDepartmentCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation department deletion failed. {DepartmentId}", id);
        }
        else
        {
            logger.LogInformation("Organisation department deleted successfully. {DepartmentId}", id);
        }

        return FromResult(result);
    }

    [HttpGet("mine/units")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationUnitResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnitsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting current organisation units.");

        var result = await structureQueries.HandleAsync(new GetOrganisationUnitsQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get current organisation units.");
        }

        return FromResult(result);
    }

    [HttpPost("mine/units")]
    [HasPermission(PermissionCodes.OrganisationManageUnits)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationUnitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUnitAsync(
        [FromBody] CreateOrganisationUnitRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating organisation unit.");

        var result = await structure.HandleAsync(new CreateOrganisationUnitCommand(request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation unit creation failed.");
        }
        else
        {
            logger.LogInformation("Organisation unit created successfully.");
        }

        return FromResult(result);
    }

    [HttpPut("mine/units/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageUnits)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationUnitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUnitAsync(
        Guid id, [FromBody] UpdateOrganisationUnitRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating organisation unit. {UnitId}", id);

        var result = await structure.HandleAsync(new UpdateOrganisationUnitCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation unit update failed. {UnitId}", id);
        }
        else
        {
            logger.LogInformation("Organisation unit updated successfully. {UnitId}", id);
        }

        return FromResult(result);
    }

    [HttpDelete("mine/units/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageUnits)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUnitAsync(
        Guid id, [FromBody] DeleteStructureRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting organisation unit. {UnitId}", id);

        var result = await structure.HandleAsync(new DeleteOrganisationUnitCommand(id, request), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Organisation unit deletion failed. {UnitId}", id);
        }
        else
        {
            logger.LogInformation("Organisation unit deleted successfully. {UnitId}", id);
        }

        return FromResult(result);
    }

    // =================================================================================
    // BusinessUnit
    // =================================================================================

    [HttpGet("/api/v1/business-units/current")]
    [HasPermission(PermissionCodes.Platform.BusinessUnitsView)]
    [ProducesResponseType(typeof(ApiResponse<BusinessUnitResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBusinessUnitAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting current business unit.");

        var result = await queries.HandleAsync(new GetBusinessUnitQuery(), cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Failed to get current business unit.");
        }

        return FromResult(result);
    }
}