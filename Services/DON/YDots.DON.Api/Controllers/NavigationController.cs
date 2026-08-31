using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Features.Navigation.Queries.GetDonorMenu;
using YDots.DON.Application.Features.ReferenceData.Queries.GetReferenceData;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// The role-aware menu and the reference-data catalogues.
///
/// The menu endpoint is what makes role-based navigation work: the Angular shell calls it once
/// after sign-in and renders exactly the entries it receives. It only needs DON.View, because
/// what a caller may actually reach is decided per entry from the permission claims in their
/// token — and rechecked by [HasPermission] when they open the route.
/// </summary>
[Route("api/v1/donors")]
[Authorize]
public sealed class NavigationController : ApiControllerBase
{
    /// <summary>GET the menu entries this caller may see, plus their sensitive-field flags.</summary>
    [HttpGet("menu")]
    [HasPermission(PermissionCodes.DonView)]
    [ProducesResponseType(typeof(ApiResponse<DonorMenuResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMenu(
        [FromServices] GetDonorMenuQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetDonorMenuQuery(), cancellationToken));

    /// <summary>GET every enum catalogue the eight screens draw their selectors from.</summary>
    [HttpGet("reference-data")]
    [HasPermission(PermissionCodes.DonView)]
    [ProducesResponseType(typeof(ApiResponse<ReferenceDataResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReferenceData(
        [FromServices] ReferenceDataQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetReferenceDataQuery(), cancellationToken));

    /// <summary>GET the scope-aware campaign autocomplete.</summary>
    [HttpGet("reference-data/campaigns")]
    [HasPermission(PermissionCodes.DonView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CampaignLookupResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchCampaigns(
        [FromQuery] string? search,
        [FromQuery] int maximumRows,
        [FromServices] ReferenceDataQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new SearchCampaignsQuery(search, maximumRows), cancellationToken));

    /// <summary>GET the scope-aware lead autocomplete, used by the follow-up planner.</summary>
    [HttpGet("reference-data/leads")]
    [HasPermission(PermissionCodes.LeadWorkQueueView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeadLookupResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchLeads(
        [FromQuery] string? search,
        [FromQuery] int maximumRows,
        [FromServices] ReferenceDataQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new SearchLeadsQuery(search, maximumRows), cancellationToken));
}
