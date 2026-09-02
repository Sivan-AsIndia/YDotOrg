using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Features.CommunicationTimeline.DTOs;
using YDots.DON.Application.Features.CommunicationTimeline.Queries;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// The Communication Timeline - a lead's or a donor's conversation history.
///
/// FOUR SCREENS POINT HERE, and the workflow document names all four: Communicate on the Lead
/// Work Queue and on My Leads, Open Timeline in the lead preview, and View History on the
/// Follow-Up Queue's action menu. Donor 360's communication history is the same page again.
///
/// IT IS A READ. Recording a conversation goes through the lead work queue's Contact command,
/// which already writes the interaction, applies the consent rules and audits the result -
/// duplicating that here would be a second way to write the same row with different checks.
/// </summary>
[Route("api/v1/donors/communication-timeline")]
[Authorize]
public sealed class CommunicationTimelineController : ApiControllerBase
{
    /// <summary>
    /// GET the timeline for a lead, for a donor, or for a lead and the donor it became.
    ///
    /// BOTH IDS ARE ACCEPTED AND EITHER MAY BE OMITTED. The queue screens hold a lead id and
    /// Donor 360 holds a donor id; the handler resolves whichever it is given to both, so a
    /// converted lead's earlier conversations stay on screen after conversion - which is what
    /// the document means by "the converted donor retains the existing owner and Communication
    /// Timeline history".
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.LeadWorkQueueView)]
    [ProducesResponseType(typeof(ApiResponse<CommunicationTimelineResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeline(
        [FromQuery] Guid? leadId,
        [FromQuery] Guid? donorId,
        [FromServices] CommunicationTimelineQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(
            new GetCommunicationTimelineQuery(leadId, donorId), cancellationToken));
}
