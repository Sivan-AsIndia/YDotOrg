using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Application.Features.LeadCapture.DTOs;
using YDots.DON.Application.Features.Leads.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.LeadCapture.Commands.CaptureLead;

/// <summary>SCR-DON-002 Save. Creates the draft lead and, when the toggle is on, its consent rows.</summary>
public sealed record SaveLeadCommand(CreateLeadRequest Request);

/// <summary>SCR-DON-002 Save on an existing draft.</summary>
public sealed record UpdateLeadCommand(Guid LeadId, UpdateLeadRequest Request);

/// <summary>SCR-DON-002 Deduplicate. Read-only: it reports candidates, it never merges anything.</summary>
public sealed record DeduplicateLeadCommand(Guid LeadId);

/// <summary>SCR-DON-002 Submit. Promotes the draft into the work queue. Idempotent.</summary>
public sealed record SubmitLeadCommand(Guid LeadId, TransitionRequest Request);

/// <summary>SCR-DON-002 Delete unused draft. Only for a draft with no downstream reference.</summary>
public sealed record DeleteLeadDraftCommand(Guid LeadId, ReasonRequest Request);

/// <summary>
/// SCR-DON-002 Bulk upload. Creates many leads from an uploaded file's rows.
///
/// THE DOCUMENT ASKS FOR IT BY NAME - Lead Capture "also shows a Bulk upload leads area" - and
/// the screen had no endpoint behind that area at all: `importBulkUpload()` carried the comment
/// "TODO: wire to the bulk-import API" and set the status to Imported after a 600ms timer, so a
/// person could upload two hundred leads, see "Imported", and have created nothing.
/// </summary>
public sealed record BulkImportLeadsCommand(BulkLeadImportRequest Request);

/// <summary>
/// The lead capture write side, including the embedded consent block.
///
/// Consent is written as real Consent rows, one per permitted channel, not as a flag on the
/// lead. That is what makes the consent history in SCR-DON-005 and the channel check on the
/// follow-up planner work from a single source.
/// </summary>
public sealed class LeadCaptureCommandHandler(
    ILeadRepository leadRepository,
    ICampaignRepository campaignRepository,
    IConsentRepository consentRepository,
    IDonorRepository donorRepository,
    IIdempotencyRepository idempotencyRepository,
    IReferenceNumberGenerator referenceNumbers,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings)
{
    private const string SubmitEndpoint = "POST /api/v1/donors/lead-capture/{id}/submit";

    /// <summary>
    /// The most leads one upload may carry.
    ///
    /// A BOUND RATHER THAN A LIMIT ANYBODY WILL HIT. It exists so a mis-generated file of a
    /// million rows fails immediately with a sentence, rather than holding a database
    /// transaction open until the request times out.
    /// </summary>
    private const int MaximumBulkImportRows = 1000;

    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        SaveLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        var campaign = await campaignRepository.GetByIdAsync(request.CampaignId, cancellationToken);
        if (campaign is null || campaign.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<LeadDetailResponse>(Error.Validation(
                "Review Campaign. Choose a campaign inside your scope.",
                [new ValidationError(nameof(request.CampaignId), "Choose a campaign from the list.")]));
        }

        if (campaign.Status == CampaignStatus.Closed)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"Campaign {campaign.Code} is closed and cannot accept new leads."));
        }

        var reference = await referenceNumbers.NextLeadReferenceAsync(cancellationToken);
        var lead = request.ToEntity(reference, currentUser.OrganisationId);

        lead.OwnerUserId = request.OwnerUserId ?? currentUser.UserId;
        lead.OwnerName = request.OwnerName?.Trim() ?? currentUser.DisplayName;
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

        // Record the safe duplicate summary at save time so the panel has something to show
        // without the person having to press Deduplicate first.
        var candidates = await FindCandidatesAsync(lead, cancellationToken);
        lead.DuplicateCandidateSummary = BuildSafeSummary(candidates);

        leadRepository.Add(lead);

        if (request.Consent?.CollectConsent == true)
        {
            AddConsentRows(lead, request.Consent);
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadCreated, nameof(Lead), lead.Id, AuditResult.Succeeded,
                $"{lead.LeadReference} captured from source {lead.Source}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);
        lead.Campaign = campaign;

        return Result.Success(lead.ToDetailResponse(currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents));
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        UpdateLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken);

        var scopeFailure = CheckScope(lead);
        if (scopeFailure is not null)
        {
            return Result.Failure<LeadDetailResponse>(scopeFailure);
        }

        if (command.Request.ExpectedVersion != lead!.Version)
        {
            return Result.Failure<LeadDetailResponse>(Error.Concurrency());
        }

        if (lead.Status is LeadStatus.Converted or LeadStatus.Closed or LeadStatus.Suppressed)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                $"A lead in state {lead.Status} can no longer be edited."));
        }

        var campaign = await campaignRepository.GetByIdAsync(command.Request.CampaignId, cancellationToken);
        if (campaign is null || campaign.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<LeadDetailResponse>(Error.Validation(
                "Review Campaign. Choose a campaign inside your scope.",
                [new ValidationError(nameof(command.Request.CampaignId), "Choose a campaign from the list.")]));
        }

        command.Request.ApplyUpdate(lead);
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

        if (command.Request.Consent?.CollectConsent == true)
        {
            AddConsentRows(lead, command.Request.Consent);
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadUpdated, nameof(Lead), lead.Id, AuditResult.Succeeded,
                $"{lead.LeadReference} updated from version {command.Request.ExpectedVersion}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);
        lead.Campaign = campaign;

        return Result.Success(lead.ToDetailResponse(currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents));
    }

    public async Task<Result<DeduplicateResultResponse>> HandleAsync(
        DeduplicateLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken);

        var scopeFailure = CheckScope(lead);
        if (scopeFailure is not null)
        {
            return Result.Failure<DeduplicateResultResponse>(scopeFailure);
        }

        var candidates = await FindCandidatesAsync(lead!, cancellationToken);
        lead!.DuplicateCandidateSummary = BuildSafeSummary(candidates);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadDeduplicated, nameof(Lead), lead.Id, AuditResult.Succeeded,
                $"{candidates.Count} candidate(s) found for {lead.LeadReference}."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeduplicateResultResponse(
            lead.Id,
            lead.LeadReference,
            candidates.Count,
            candidates.Count == 0 ? ScreenState.Success : ScreenState.Duplicate,
            candidates.Count == 0
                ? "No matching records were found."
                : "Possible matches were found. Review them before you submit.",
            candidates));
    }

    public async Task<Result<LeadDetailResponse>> HandleAsync(
        SubmitLeadCommand command,
        CancellationToken cancellationToken = default)
    {
        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken);

        var scopeFailure = CheckScope(lead);
        if (scopeFailure is not null)
        {
            return Result.Failure<LeadDetailResponse>(scopeFailure);
        }

        if (command.Request.ExpectedVersion is > 0 && command.Request.ExpectedVersion != lead!.Version)
        {
            return Result.Failure<LeadDetailResponse>(Error.Concurrency());
        }

        // Idempotent by contract: a second submit with the same key returns the same record
        // rather than a conflict, which is what "Execute idempotently" in 4.2.3 asks for.
        if (!string.IsNullOrWhiteSpace(currentUser.IdempotencyKey))
        {
            var replay = await idempotencyRepository.FindAsync(currentUser.IdempotencyKey, SubmitEndpoint, cancellationToken);
            if (replay is not null && replay.ResourceId == lead!.Id)
            {
                var consented = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);
                return Result.Success(lead.ToDetailResponse(currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consented));
            }
        }

        if (!lead!.IsDraft)
        {
            return Result.Failure<LeadDetailResponse>(Error.InvalidTransition(
                "This lead has already been submitted to the work queue."));
        }

        lead.IsDraft = false;
        lead.Status = lead.OwnerUserId is null ? LeadStatus.New : LeadStatus.Assigned;
        lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

        if (!string.IsNullOrWhiteSpace(currentUser.IdempotencyKey))
        {
            idempotencyRepository.Add(new IdempotencyRecord
            {
                OrganisationId = currentUser.OrganisationId,
                Key = currentUser.IdempotencyKey,
                Endpoint = SubmitEndpoint,
                ResourceId = lead.Id,
                ResourceReference = lead.LeadReference
            });
        }

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadSubmitted, nameof(Lead), lead.Id, AuditResult.Succeeded,
                command.Request.Comment ?? $"{lead.LeadReference} submitted to the work queue."),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);

        return Result.Success(lead.ToDetailResponse(currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteLeadDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var lead = await leadRepository.GetWithAssignmentsAsync(command.LeadId, cancellationToken);

        var scopeFailure = CheckScope(lead);
        if (scopeFailure is not null)
        {
            return Result.Failure<OutcomeResponse>(scopeFailure);
        }

        // "Draft with no downstream reference". A consent row or an assignment is exactly that
        // kind of reference, so either one turns delete into cancel.
        if (!lead!.IsDraft || lead.Status != LeadStatus.New)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Permanent delete is only available for an unsubmitted draft. Use Close instead."));
        }

        var consents = await consentRepository.GetForLeadAsync(lead.Id, cancellationToken);
        if (consents.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This draft already carries consent evidence and cannot be deleted. Use Close instead."));
        }

        if (lead.Assignments.Count > 0 || lead.ConvertedDonorId is not null)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This draft is already referenced by an assignment or a donor record and cannot be deleted."));
        }

        var reference = lead.LeadReference;
        leadRepository.Remove(lead);

        await auditWriter.WriteAsync(
            new AuditEntry(AuditActionCodes.LeadDraftDeleted, nameof(Lead), lead.Id, AuditResult.Succeeded,
                command.Request.Reason.Trim()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            reference,
            "Deleted",
            clock.UtcNow,
            "The unused draft lead was permanently deleted.",
            "Return to the lead work queue",
            null,
            currentUser.CorrelationId));
    }

    /// <summary>
    /// Turns the consent toggle into one immutable Consent row per channel. A channel the
    /// person did not tick is recorded as Withdrawn rather than left out, because "not asked"
    /// and "asked and refused" are different facts and only one of them permits contact later.
    /// </summary>
    private void AddConsentRows(Lead lead, LeadConsentRequest request)
    {
        var effective = request.ConsentDateUtc ?? clock.UtcNow;

        var decisions = new (ConsentChannel Channel, bool Granted)[]
        {
            (ConsentChannel.Email, request.EmailConsent),
            (ConsentChannel.Sms, request.SmsConsent),
            (ConsentChannel.WhatsApp, request.WhatsAppConsent),
            (ConsentChannel.PhoneCall, request.PhoneCallConsent)
        };

        foreach (var (channel, granted) in decisions)
        {
            consentRepository.Add(new Consent
            {
                LeadId = lead.Id,
                OrganisationId = currentUser.OrganisationId,
                Name = $"{channel} consent - {lead.LeadReference}",
                Description = request.ConsentNotes?.Trim(),
                Status = granted ? ConsentStatus.Active : ConsentStatus.Withdrawn,
                Purpose = request.Purpose?.Trim() ?? "Fundraising communication",
                Channel = channel,
                ConsentState = granted ? ConsentState.Granted : ConsentState.Withdrawn,
                NoticeVersion = _settings.CurrentNoticeVersion,
                EvidenceSource = request.ConsentSource?.Trim() ?? "Lead capture form",
                EvidenceReference = request.ConsentEvidenceReference?.Trim(),
                EffectiveAtUtc = effective,
                CapturedByUserId = currentUser.UserId,
                CapturedByName = currentUser.DisplayName
            });
        }
    }

    /// <summary>
    /// Looks for both existing leads and existing donors that could be the same person. The
    /// response never names them: only a category, a confidence and a route to the comparison.
    /// </summary>
    private async Task<List<DuplicateCandidateResponse>> FindCandidatesAsync(Lead lead, CancellationToken cancellationToken)
    {
        var results = new List<DuplicateCandidateResponse>();

        var leadMatches = await leadRepository.FindDuplicateCandidatesAsync(
            currentUser.OrganisationId, lead.EmailAddress, lead.MobileNumber,
            lead.FirstName, lead.LastName, lead.Id, cancellationToken);

        foreach (var match in leadMatches)
        {
            results.Add(new DuplicateCandidateResponse(
                match.Id,
                "Lead",
                DescribeMatch(lead.EmailAddress, match.EmailAddress, lead.MobileNumber, match.MobileNumber),
                ConfidenceFor(lead.EmailAddress, match.EmailAddress, lead.MobileNumber, match.MobileNumber).ToString(),
                "An existing lead in your scope shares a contact detail or name with this record.",
                $"{ScreenRoutes.LeadWorkQueue}?leadId={match.Id}"));
        }

        var donorMatches = await donorRepository.FindDuplicateCandidatesAsync(
            currentUser.OrganisationId, lead.EmailAddress, lead.MobileNumber,
            LeadMappingConfig.BuildDisplayName(lead), null, cancellationToken);

        foreach (var match in donorMatches)
        {
            results.Add(new DuplicateCandidateResponse(
                match.Id,
                "Donor",
                DescribeMatch(lead.EmailAddress, match.PrimaryEmail, lead.MobileNumber, match.PrimaryPhone),
                ConfidenceFor(lead.EmailAddress, match.PrimaryEmail, lead.MobileNumber, match.PrimaryPhone).ToString(),
                "An existing donor in your scope shares a contact detail or name with this record.",
                $"{ScreenRoutes.Donor360}?donorId={match.Id}"));
        }

        return results;
    }

    private static string DescribeMatch(string? email, string? otherEmail, string? phone, string? otherPhone)
    {
        if (Matches(email, otherEmail))
        {
            return "Same e-mail address";
        }

        return Matches(phone, otherPhone) ? "Same mobile number" : "Similar name";
    }

    private static IdentityConfidence ConfidenceFor(string? email, string? otherEmail, string? phone, string? otherPhone) =>
        Matches(email, otherEmail) || Matches(phone, otherPhone)
            ? IdentityConfidence.High
            : IdentityConfidence.Low;

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? BuildSafeSummary(IReadOnlyList<DuplicateCandidateResponse> candidates) =>
        candidates.Count == 0
            ? null
            : $"{candidates.Count} possible match(es): "
              + string.Join(", ", candidates.Select(candidate => $"{candidate.CandidateType} - {candidate.MatchCategory}").Distinct());

    private Error? CheckScope(Lead? lead)
    {
        if (lead is null || lead.OrganisationId != currentUser.OrganisationId)
        {
            return Error.NotFound("That lead was not found inside your scope.");
        }

        return currentUser.Scope.IsOwnRecordsOnly && lead.OwnerUserId != currentUser.UserId
            ? Error.NotFound("That lead was not found inside your scope.")
            : null;
    }

    /// <summary>
    /// Creates a lead per row, reporting each one separately.
    ///
    /// PARTIAL SUCCESS IS THE NORMAL CASE. A file of two hundred leads with three bad rows should
    /// create a hundred and ninety-seven and name the three; refusing the whole file would make
    /// somebody fix a spreadsheet by trial and error. So each row is validated on its own and a
    /// rejection never stops the ones after it.
    ///
    /// ONE SaveChanges FOR THE FILE, not one per row. Two hundred round trips would take long
    /// enough for the request to time out, and a row rejected for a bad campaign is rejected
    /// before anything is added rather than rolled back afterwards.
    /// </summary>
    public async Task<Result<BulkLeadImportResponse>> HandleAsync(
        BulkImportLeadsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (request.Rows.Count == 0)
        {
            return Result.Failure<BulkLeadImportResponse>(Error.Validation(
                "The uploaded file contained no rows.",
                [new ValidationError(nameof(request.Rows), "Add at least one lead row.")]));
        }

        if (request.Rows.Count > MaximumBulkImportRows)
        {
            return Result.Failure<BulkLeadImportResponse>(Error.Validation(
                $"A bulk upload takes at most {MaximumBulkImportRows} leads at a time. The file had {request.Rows.Count}.",
                [new ValidationError(nameof(request.Rows), "Split the file and upload it in parts.")]));
        }

        // THE CAMPAIGN LIST IS FETCHED ONCE. A row names a campaign the way a person typed it, so
        // every row needs the same lookup - doing it per row would be one query each.
        var campaigns = await campaignRepository.GetActiveAsync(currentUser.OrganisationId, cancellationToken);

        var byCode = campaigns
            .GroupBy(campaign => campaign.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var byName = campaigns
            .GroupBy(campaign => campaign.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var defaultCampaign = request.DefaultCampaignId is Guid defaultId
            ? campaigns.FirstOrDefault(campaign => campaign.Id == defaultId)
            : null;

        var defaultSource = string.IsNullOrWhiteSpace(request.DefaultSource)
            ? "Bulk Upload"
            : request.DefaultSource.Trim();

        var results = new List<BulkLeadImportRowResult>(request.Rows.Count);
        var imported = 0;

        foreach (var row in request.Rows)
        {
            var firstName = row.FirstName?.Trim();

            if (string.IsNullOrWhiteSpace(firstName))
            {
                results.Add(new BulkLeadImportRowResult(row.RowNumber, false, null,
                    "First name is required."));
                continue;
            }

            // A LEAD NOBODY CAN CONTACT IS NOT A LEAD. The same rule the single-capture form
            // enforces: at least one of mobile or e-mail.
            if (string.IsNullOrWhiteSpace(row.MobileNumber) && string.IsNullOrWhiteSpace(row.EmailAddress))
            {
                results.Add(new BulkLeadImportRowResult(row.RowNumber, false, null,
                    "Give a mobile number or an e-mail address so the lead can be contacted."));
                continue;
            }

            var campaign = ResolveCampaign(row.CampaignNameOrCode, byCode, byName) ?? defaultCampaign;

            if (campaign is null)
            {
                results.Add(new BulkLeadImportRowResult(row.RowNumber, false, null,
                    string.IsNullOrWhiteSpace(row.CampaignNameOrCode)
                        ? "No campaign was given and no default campaign was chosen for the upload."
                        : $"Campaign '{row.CampaignNameOrCode.Trim()}' was not found among your active campaigns."));
                continue;
            }

            if (campaign.Status == CampaignStatus.Closed)
            {
                results.Add(new BulkLeadImportRowResult(row.RowNumber, false, null,
                    $"Campaign {campaign.Code} is closed and cannot accept new leads."));
                continue;
            }

            var reference = await referenceNumbers.NextLeadReferenceAsync(cancellationToken);

            var lead = new CreateLeadRequest
            {
                FirstName = firstName,
                LastName = row.LastName?.Trim(),
                MobileNumber = row.MobileNumber?.Trim(),
                EmailAddress = row.EmailAddress?.Trim(),
                PreferredLanguage = row.PreferredLanguage?.Trim(),
                City = row.City?.Trim(),
                CampaignId = campaign.Id,
                Source = string.IsNullOrWhiteSpace(row.Source) ? defaultSource : row.Source.Trim(),
                Notes = row.Notes?.Trim(),
            }.ToEntity(reference, currentUser.OrganisationId);

            lead.OwnerUserId = null;
            lead.OwnerName = null;
            lead.SlaState = LeadMappingConfig.CalculateSlaState(lead.NextActionDueUtc, clock.UtcNow, _settings);

            // UPLOADED LEADS ARE NOT DRAFTS. The document's flow sends them straight to the Lead
            // Work Queue for assignment - a draft would be invisible there, which is the opposite
            // of why somebody uploads a file.
            lead.IsDraft = false;

            leadRepository.Add(lead);
            imported++;

            results.Add(new BulkLeadImportRowResult(row.RowNumber, true, reference, null));
        }

        if (imported > 0)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.LeadCreated, nameof(Lead), Guid.Empty, AuditResult.Succeeded,
                    $"Bulk upload created {imported} of {request.Rows.Count} leads."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var rejected = results.Count(result => !result.Imported);

        return Result.Success(new BulkLeadImportResponse(
            request.Rows.Count,
            imported,
            rejected,
            results,
            rejected == 0
                ? $"All {imported} leads were created."
                : $"{imported} of {request.Rows.Count} leads were created. {rejected} rows were rejected and are listed below."));
    }

    /// <summary>
    /// Finds the campaign a row names.
    ///
    /// CODE FIRST, THEN NAME, AND NEITHER IS FUZZY. An earlier version of the assignment board
    /// matched campaign names word-by-word by prefix, which could resolve "Clean Water 2026" to
    /// the wrong campaign entirely. A row that does not match exactly is rejected and named, so
    /// the person fixes their spreadsheet rather than discovering months later that two hundred
    /// leads were attributed to the wrong appeal.
    /// </summary>
    private static Campaign? ResolveCampaign(
        string? nameOrCode,
        IReadOnlyDictionary<string, Campaign> byCode,
        IReadOnlyDictionary<string, Campaign> byName)
    {
        if (string.IsNullOrWhiteSpace(nameOrCode))
        {
            return null;
        }

        var key = nameOrCode.Trim();

        return byCode.TryGetValue(key, out var byCodeMatch)
            ? byCodeMatch
            : byName.TryGetValue(key, out var byNameMatch) ? byNameMatch : null;
    }
}
