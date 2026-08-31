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
    IDateTimeProvider clock) : ApiControllerBase
{
    // =================================================================================
    // Platform: SuperAdmin administering every organisation
    // =================================================================================

    [HttpGet]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<OrganisationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] TenantSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchOrganisationsQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = nameof(GetOrganisationAsync))]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDetailResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganisationAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetOrganisationQuery(id), cancellationToken));

    [HttpGet("statistics")]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatisticsAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetOrganisationStatisticsQuery(), cancellationToken));

    /// <summary>Everything sitting on the SuperAdmin desk, oldest first.</summary>
    [HttpGet("awaiting-review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationListItemResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAwaitingReviewAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetOrganisationsAwaitingReviewQuery(), cancellationToken));

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
        [FromBody] CheckSubdomainRequest request, CancellationToken cancellationToken) =>
        FromResult(await create.HandleAsync(new CheckSubdomainQuery(request), cancellationToken));

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
        var result = await create.HandleAsync(new CreateOrganisationCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(result, nameof(GetOrganisationAsync), new { id = result.Value!.TenantId },
                "Organisation created and the administrator invited.");
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [HasPermission(PermissionCodes.Platform.TenantsInviteAdmin)]
    [ProducesResponseType(typeof(ApiResponse<CreateOrganisationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendInvitationAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(
            await create.HandleAsync(new ResendOrganisationInvitationCommand(id), cancellationToken),
            "Invitation re-sent.");

    // ---- Review and decision ------------------------------------------------------------

    [HttpPost("{id:guid}/start-review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartReviewAsync(
        Guid id, [FromBody] StartOrganisationReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new StartOrganisationReviewCommand(id, request), cancellationToken));

    /// <summary>
    /// Approves or rejects. A rejection must carry a reason, so the TenantAdmin can act on it.
    /// </summary>
    [HttpPost("{id:guid}/review")]
    [HasPermission(PermissionCodes.Platform.TenantsApprove)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReviewAsync(
        Guid id, [FromBody] ReviewOrganisationRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new ReviewOrganisationCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [HasPermission(PermissionCodes.Platform.TenantsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateAsync(
        Guid id, [FromBody] TransitionRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new ActivateOrganisationCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(PermissionCodes.Platform.TenantsSuspend)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuspendAsync(
        Guid id, [FromBody] SuspendOrganisationRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new SuspendOrganisationCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(PermissionCodes.Platform.TenantsActivate)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id, [FromBody] ReactivateOrganisationRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new ReactivateOrganisationCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(PermissionCodes.Platform.TenantsArchive)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArchiveAsync(
        Guid id, [FromBody] ArchiveOrganisationRequest request, CancellationToken cancellationToken) =>
        FromResult(await lifecycle.HandleAsync(
            new ArchiveOrganisationCommand(id, request), cancellationToken));

    // ---- Hosts ----------------------------------------------------------------------------

    [HttpGet("{id:guid}/domains")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationDomainResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDomainsAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.GetDomainsAsync(id, cancellationToken));

    [HttpPost("{id:guid}/domains")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDomainResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddDomainAsync(
        Guid id, [FromBody] AddOrganisationDomainRequest request, CancellationToken cancellationToken) =>
        FromResult(await assets.HandleAsync(
            new AddOrganisationDomainCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/domains/verify")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDomainResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyDomainAsync(
        Guid id, [FromBody] VerifyOrganisationDomainRequest request, CancellationToken cancellationToken) =>
        FromResult(await assets.HandleAsync(
            new VerifyOrganisationDomainCommand(id, request), cancellationToken));

    [HttpDelete("{id:guid}/domains/{domainId:guid}")]
    [HasPermission(PermissionCodes.Platform.TenantsManageDomains)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveDomainAsync(
        Guid id, Guid domainId, CancellationToken cancellationToken) =>
        FromResult(await assets.HandleAsync(
            new RemoveOrganisationDomainCommand(id, domainId), cancellationToken));

    // ---- Documents ---------------------------------------------------------------------------

    [HttpGet("{id:guid}/documents")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationDocumentResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocumentsAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.GetDocumentsAsync(id, clock.UtcNow, cancellationToken));

    [HttpPost("{id:guid}/documents/review")]
    [HasPermission(PermissionCodes.Platform.TenantsReview)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationDocumentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewDocumentAsync(
        Guid id, [FromBody] ReviewOrganisationDocumentRequest request, CancellationToken cancellationToken) =>
        FromResult(await assets.HandleAsync(
            new ReviewOrganisationDocumentCommand(id, request), cancellationToken));

    /// <summary>The Organisation lifecycle timeline.</summary>
    [HttpGet("{id:guid}/timeline")]
    [HasPermission(PermissionCodes.Platform.TenantsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationTimelineResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimelineAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.GetTimelineAsync(id, cancellationToken));

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
    public async Task<IActionResult> GetMineAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken));

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
        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            return FromResult(current);
        }

        return FromResult(await lifecycle.HandleAsync(
            new UpdateOrganisationProfileCommand(current.Value!.Id, request), cancellationToken));
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
        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            return FromResult(current);
        }

        return FromResult(await lifecycle.HandleAsync(
            new SubmitOrganisationCommand(current.Value!.Id, request), cancellationToken));
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
        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            return FromResult(current);
        }

        return FromResult(await queries.GetDocumentsAsync(current.Value!.Id, clock.UtcNow, cancellationToken));
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
        var current = await queries.HandleAsync(new GetMyOrganisationQuery(), cancellationToken);

        if (current.IsFailure)
        {
            return FromResult(current);
        }

        return FromResult(await lifecycle.HandleAsync(
            new UpdateOrganisationSettingsCommand(current.Value!.Id, request), cancellationToken));
    }

    // =================================================================================
    // Departments and organisation units
    //
    // TWO SEPARATE HIERARCHIES, deliberately. A department is what somebody DOES (Fundraising,
    // Finance); a unit is where they SIT (Head office, Southern region). Most organisations need
    // both, and collapsing them into one tree forces a choice that has to be undone later - a
    // fundraiser in the southern office belongs to Fundraising AND to Southern, and neither is a
    // child of the other.
    //
    // Both are the caller's OWN Organisation's, so none of these takes an Organisation id.
    // =================================================================================

    [HttpGet("mine/departments")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DepartmentResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDepartmentsAsync(CancellationToken cancellationToken) =>
        FromResult(await structureQueries.HandleAsync(new GetDepartmentsQuery(), cancellationToken));

    [HttpPost("mine/departments")]
    [HasPermission(PermissionCodes.OrganisationManageDepartments)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDepartmentAsync(
        [FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await structure.HandleAsync(new CreateDepartmentCommand(request), cancellationToken));

    [HttpPut("mine/departments/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageDepartments)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDepartmentAsync(
        Guid id, [FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await structure.HandleAsync(
            new UpdateDepartmentCommand(id, request), cancellationToken));

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
        Guid id, [FromBody] DeleteStructureRequest request, CancellationToken cancellationToken) =>
        FromResult(await structure.HandleAsync(
            new DeleteDepartmentCommand(id, request), cancellationToken));

    [HttpGet("mine/units")]
    [HasPermission(PermissionCodes.OrganisationView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganisationUnitResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnitsAsync(CancellationToken cancellationToken) =>
        FromResult(await structureQueries.HandleAsync(
            new GetOrganisationUnitsQuery(), cancellationToken));

    [HttpPost("mine/units")]
    [HasPermission(PermissionCodes.OrganisationManageUnits)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationUnitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUnitAsync(
        [FromBody] CreateOrganisationUnitRequest request, CancellationToken cancellationToken) =>
        FromResult(await structure.HandleAsync(
            new CreateOrganisationUnitCommand(request), cancellationToken));

    [HttpPut("mine/units/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageUnits)]
    [ProducesResponseType(typeof(ApiResponse<OrganisationUnitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUnitAsync(
        Guid id, [FromBody] UpdateOrganisationUnitRequest request, CancellationToken cancellationToken) =>
        FromResult(await structure.HandleAsync(
            new UpdateOrganisationUnitCommand(id, request), cancellationToken));

    [HttpDelete("mine/units/{id:guid}")]
    [HasPermission(PermissionCodes.OrganisationManageUnits)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUnitAsync(
        Guid id, [FromBody] DeleteStructureRequest request, CancellationToken cancellationToken) =>
        FromResult(await structure.HandleAsync(
            new DeleteOrganisationUnitCommand(id, request), cancellationToken));

    // =================================================================================
    // BusinessUnit
    // =================================================================================

    [HttpGet("/api/v1/business-units/current")]
    [HasPermission(PermissionCodes.Platform.BusinessUnitsView)]
    [ProducesResponseType(typeof(ApiResponse<BusinessUnitResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBusinessUnitAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetBusinessUnitQuery(), cancellationToken));
}
