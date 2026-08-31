using FluentValidation;
using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.Features.AssignmentBoard.DTOs;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Application.Features.Leads.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.AssignmentBoard.Commands.RouteLeads;

/// <summary>SCR-DON-006 Assign. Gives an unowned lead an owner.</summary>
public sealed record AssignFromBoardCommand(AssignmentRequest Request);

/// <summary>SCR-DON-006 Reassign. Moves an owned lead to somebody else.</summary>
public sealed record ReassignFromBoardCommand(AssignmentRequest Request);

/// <summary>SCR-DON-006 Bulk route. Moves many leads at once, reporting each one separately.</summary>
public sealed record BulkRouteCommand(BulkRouteRequest Request);

/// <summary>
/// The assignment board write side.
///
/// Assign and Reassign look almost identical, and that is on purpose: they differ only in
/// whether the lead already had an owner. Keeping them apart lets the permission model treat
/// "give somebody work" and "take work away from somebody" as two separate rights.
/// </summary>
public sealed class AssignmentBoardCommandHandler(
    ILeadRepository leadRepository,
    IConsentRepository consentRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        AssignFromBoardCommand command,
        CancellationToken cancellationToken = default) =>
        await ApplyAssignmentAsync(command.Request, expectOwned: false, AuditActionCodes.AssignmentAssigned, cancellationToken);

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        ReassignFromBoardCommand command,
        CancellationToken cancellationToken = default) =>
        await ApplyAssignmentAsync(command.Request, expectOwned: true, AuditActionCodes.AssignmentReassigned, cancellationToken);

    public async Task<Result<BulkRouteResultResponse>> HandleAsync(
        BulkRouteCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;
        var requestedIds = request.LeadIds.Distinct().ToList();

        if (requestedIds.Count == 0)
        {
            return Result.Failure<BulkRouteResultResponse>(Error.Validation(
                "Select at least one lead before routing.",
                [new ValidationError(nameof(request.LeadIds), "Select at least one row.")]));
        }

        if (requestedIds.Count > _settings.BulkRouteMaximumItems)
        {
            return Result.Failure<BulkRouteResultResponse>(Error.Validation(
                $"A bulk route may cover at most {_settings.BulkRouteMaximumItems} leads. Narrow the selection and try again.",
                [new ValidationError(nameof(request.LeadIds), $"Select no more than {_settings.BulkRouteMaximumItems} rows.")]));
        }

        var leads = await leadRepository.GetByIdsAsync(requestedIds, cancellationToken);
        var effective = request.EffectiveAtUtc ?? clock.UtcNow;
        var items = new List<BulkRouteItemResponse>(requestedIds.Count);
        var routed = 0;

        foreach (var leadId in requestedIds)
        {
            var lead = leads.FirstOrDefault(candidate => candidate.Id == leadId);

            // Every skip is reported with its own reason. Silent skipping is exactly what
            // UI section 6.2 forbids for a bulk action.
            if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
            {
                items.Add(new BulkRouteItemResponse(leadId, null, false, "Not found inside your scope."));
                continue;
            }

            if (lead.Status is LeadStatus.Converted or LeadStatus.Closed or LeadStatus.Suppressed)
            {
                items.Add(new BulkRouteItemResponse(leadId, lead.LeadReference, false,
                    $"State {lead.Status} cannot be routed."));
                continue;
            }

            if (lead.OwnerUserId == request.NewOwnerUserId)
            {
                items.Add(new BulkRouteItemResponse(leadId, lead.LeadReference, false,
                    "Already owned by the selected person."));
                continue;
            }

            ApplyOwnerChange(lead, request.NewOwnerUserId, request.NewOwnerName, request.TeamCode,
                request.AssignmentReason, effective, isBulkRoute: true);

            routed++;
            items.Add(new BulkRouteItemResponse(leadId, lead.LeadReference, true, "Routed."));
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.AssignmentBulkRouted, nameof(Lead), null, AuditResult.Succeeded,
                $"{routed} of {requestedIds.Count} lead(s) routed to {request.NewOwnerName}. {request.AssignmentReason.Trim()}"),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var skipped = requestedIds.Count - routed;

        return Result.Success(new BulkRouteResultResponse(
            requestedIds.Count,
            routed,
            skipped,
            items,
            skipped == 0
                ? $"All {routed} lead(s) were routed to {request.NewOwnerName}."
                : $"{routed} lead(s) routed, {skipped} skipped. Review the per-record outcome below.",
            skipped == 0 ? ScreenState.Success : ScreenState.Validation));
    }

    private async Task<Result<LeadDetailResponse>> ApplyAssignmentAsync(
        AssignmentRequest request,
        bool expectOwned,
        string auditActionCode,
        CancellationToken cancellationToken)
    {
        var lead = await leadRepository.GetByIdAsync(request.LeadId, cancellationToken);

        if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<LeadDetailResponse>(Error.NotFound("That lead was not found inside your scope."));
        }

        if (request.ExpectedVersion is > 0 && request.ExpectedVersion != lead.Version)
        {
            return Result.Failure<LeadDetailResponse>(Error.Concurrency());
        }

        if (lead.Status is LeadStatus.Converted or LeadStatus.Closed or LeadStatus.Suppressed)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"A lead in state {lead.Status} can no longer be routed."));
        }

        if (expectOwned && lead.OwnerUserId is null)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                "This lead has no owner yet. Use Assign rather than Reassign."));
        }

        if (!expectOwned && lead.OwnerUserId is not null)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                "This lead already has an owner. Use Reassign rather than Assign."));
        }

        if (lead.OwnerUserId == request.NewOwnerUserId)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                "That person already owns this lead."));
        }

        var effective = request.EffectiveAtUtc ?? clock.UtcNow;

        ApplyOwnerChange(lead, request.NewOwnerUserId, request.NewOwnerName, request.TeamCode,
            request.AssignmentReason, effective, isBulkRoute: false);

        await auditWriter.WriteAsync(
            new AuditEntry(auditActionCode, nameof(Lead), lead.Id, AuditResult.Succeeded,
                request.AssignmentReason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);

        return Result.Success(lead.ToDetailResponse(
            currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents));
    }

    /// <summary>
    /// Moves ownership and writes the history row. Both single and bulk routing go through
    /// here so an ownership change can never happen without leaving a trail.
    /// </summary>
    private void ApplyOwnerChange(
        Lead lead,
        Guid newOwnerUserId,
        string newOwnerName,
        string? teamCode,
        string reason,
        DateTimeOffset effectiveAtUtc,
        bool isBulkRoute)
    {
        var previousOwnerId = lead.OwnerUserId;
        var previousOwnerName = lead.OwnerName;

        lead.OwnerUserId = newOwnerUserId;
        lead.OwnerName = newOwnerName.Trim();
        lead.TeamCode = string.IsNullOrWhiteSpace(teamCode) ? lead.TeamCode : teamCode.Trim();
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

        if (lead.Status == LeadStatus.New)
        {
            lead.Status = LeadStatus.Assigned;
        }

        leadRepository.AddAssignment(new LeadAssignment
        {
            OrganisationId = lead.OrganisationId,
            LeadId = lead.Id,
            PreviousOwnerUserId = previousOwnerId,
            PreviousOwnerName = previousOwnerName,
            NewOwnerUserId = newOwnerUserId,
            NewOwnerName = newOwnerName.Trim(),
            AssignmentReason = reason.Trim(),
            EffectiveAtUtc = effectiveAtUtc,
            AssignedByUserId = currentUser.UserId,
            IsBulkRoute = isBulkRoute
        });
    }
}

public sealed class AssignmentRequestValidator : AbstractValidator<AssignmentRequest>
{
    public AssignmentRequestValidator()
    {
        RuleFor(request => request.LeadId).NotEmpty().WithMessage("Enter Lead reference.");
        RuleFor(request => request.NewOwnerUserId).NotEmpty().WithMessage("Enter New owner.");

        RuleFor(request => request.NewOwnerName)
            .NotEmpty().WithMessage("Enter New owner.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.");

        RuleFor(request => request.AssignmentReason)
            .NotEmpty().WithMessage("Enter Assignment reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}

public sealed class BulkRouteRequestValidator : AbstractValidator<BulkRouteRequest>
{
    public BulkRouteRequestValidator()
    {
        RuleFor(request => request.LeadIds).NotEmpty().WithMessage("Select at least one lead.");
        RuleFor(request => request.NewOwnerUserId).NotEmpty().WithMessage("Enter New owner.");

        RuleFor(request => request.NewOwnerName)
            .NotEmpty().WithMessage("Enter New owner.")
            .MaximumLength(200).WithMessage("Use no more than 200 characters.");

        RuleFor(request => request.AssignmentReason)
            .NotEmpty().WithMessage("Enter Assignment reason.")
            .Length(10, 2000).WithMessage("Use between 10 and 2,000 characters.");
    }
}
