using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Organisations.Commands.ManageOrganisation;

/// <summary>Adds a host name to an Organisation.</summary>
public sealed record AddOrganisationDomainCommand(Guid TenantId, AddOrganisationDomainRequest Request);

/// <summary>Confirms that the DNS record proving ownership has appeared.</summary>
public sealed record VerifyOrganisationDomainCommand(Guid TenantId, VerifyOrganisationDomainRequest Request);

/// <summary>Removes a host name.</summary>
public sealed record RemoveOrganisationDomainCommand(Guid TenantId, Guid DomainId);

/// <summary>SuperAdmin accepting or rejecting one document.</summary>
public sealed record ReviewOrganisationDocumentCommand(Guid TenantId, ReviewOrganisationDocumentRequest Request);

/// <summary>
/// Organisation hosts and documents.
///
/// THE DOMAIN HALF IS SECURITY-SENSITIVE in a way the document half is not. A host name is
/// what resolves an anonymous sign-in to an Organisation, so adding one wrongly would send
/// somebody credentials to the wrong place. Two things guard it: a platform-wide unique index
/// on the host, and the verification requirement — a custom domain arrives UNVERIFIED and
/// cannot resolve anything until ownership is proved by a DNS record.
/// </summary>
public sealed class OrganisationAssetCommandHandler(
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    ITokenHasher tokenHasher,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OrganisationDomainResponse>> HandleAsync(
        AddOrganisationDomainCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<OrganisationDomainResponse>(Error.TenantNotFound());
        }

        var host = HostNameValue.TryParse(request.HostName);
        if (host is null)
        {
            return Result.Failure<OrganisationDomainResponse>(
                Error.Validation("That web address is not valid.",
                    [new ValidationError(nameof(request.HostName), "Enter a valid host name.")]));
        }

        // Platform-wide uniqueness. A host resolving to two Organisations would mean
        // credentials checked against whichever row came back first.
        if (await tenants.HostNameExistsAsync(host.Value, null, cancellationToken))
        {
            return Result.Failure<OrganisationDomainResponse>(
                Error.Duplicate("That web address is already in use."));
        }

        // A SUBDOMAIN of our own apex is verified on creation, because the platform already
        // controls the apex. Anything else has to be proved.
        var businessUnit = await businessUnits.GetByIdAsync(tenant.BusinessUnitId, cancellationToken);

        var isOwnSubdomain = businessUnit is not null
                             && host.Value.EndsWith("." + businessUnit.RootDomain, StringComparison.Ordinal);

        var isVerified = isOwnSubdomain && request.DomainType == TenantDomainType.Subdomain;

        var domain = new TenantDomain
        {
            BusinessUnitId = tenant.BusinessUnitId,
            TenantId = tenant.Id,
            HostName = host.Value,
            DomainType = request.DomainType,
            // A new primary demotes the old one below; a first domain is primary regardless.
            IsPrimary = request.IsPrimary,
            IsVerified = isVerified,
            VerifiedAtUtc = isVerified ? clock.UtcNow : null,
            VerifiedByUserId = isVerified ? currentUser.UserId : null,
            // The token the Organisation publishes as a DNS TXT record.
            VerificationToken = isVerified ? null : tokenHasher.GenerateToken(16),
            IsActive = true
        };

        if (request.IsPrimary)
        {
            var existing = await tenants.GetPrimaryDomainAsync(tenant.Id, cancellationToken);
            if (existing is not null)
            {
                existing.IsPrimary = false;
            }
        }

        await tenants.AddDomainAsync(domain, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantDomainAdded, nameof(TenantDomain), domain.Id, domain.HostName,
            new { TenantId = tenant.Id, domain.DomainType, domain.IsVerified },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(domain.ToDomainResponse());
    }

    /// <summary>
    /// Marks a host verified.
    ///
    /// The DNS lookup that would prove ownership is not performed here — the platform
    /// administrator confirms it, and the endpoint is theirs alone. Automating the check would
    /// mean this service making outbound DNS queries on a caller-supplied name, which is its
    /// own problem.
    /// </summary>
    public async Task<Result<OrganisationDomainResponse>> HandleAsync(
        VerifyOrganisationDomainCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var domain = await tenants.GetDomainAsync(command.Request.DomainId, cancellationToken);

        if (domain is null || domain.TenantId != command.TenantId)
        {
            return Result.Failure<OrganisationDomainResponse>(
                Error.NotFound("That web address was not found for this organisation."));
        }

        if (domain.IsVerified)
        {
            return Result.Success(domain.ToDomainResponse());
        }

        domain.IsVerified = true;
        domain.VerifiedAtUtc = clock.UtcNow;
        domain.VerifiedByUserId = currentUser.UserId;
        // Cleared once spent: it has no further use and no reason to remain readable.
        domain.VerificationToken = null;

        await audit.WriteAsync(
            AuditActionCodes.TenantDomainVerified, nameof(TenantDomain), domain.Id, domain.HostName,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(domain.ToDomainResponse());
    }

    /// <summary>
    /// Takes a host out of service.
    ///
    /// Deactivated rather than deleted, so the row and its history survive and the host cannot
    /// immediately be claimed by a different Organisation. The primary host cannot be removed
    /// at all — an Organisation with no way to reach it is unusable.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RemoveOrganisationDomainCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var domain = await tenants.GetDomainAsync(command.DomainId, cancellationToken);

        if (domain is null || domain.TenantId != command.TenantId)
        {
            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That web address was not found for this organisation."));
        }

        if (domain.IsPrimary)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "The primary web address cannot be removed. Make another one primary first."));
        }

        domain.IsActive = false;

        await audit.WriteAsync(
            AuditActionCodes.TenantUpdated, nameof(TenantDomain), domain.Id, domain.HostName,
            new { Removed = true }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            domain.Id, "Removed", domain.Version, "That web address has been removed.", []));
    }

    /// <summary>
    /// SuperAdmin accepting or rejecting one document.
    ///
    /// A rejection needs notes, for the same reason an Organisation rejection needs a reason:
    /// the TenantAdmin has to know what to replace and why.
    /// </summary>
    public async Task<Result<OrganisationDocumentResponse>> HandleAsync(
        ReviewOrganisationDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!request.Accepted && string.IsNullOrWhiteSpace(request.Notes))
        {
            return Result.Failure<OrganisationDocumentResponse>(
                Error.Validation("Say what is wrong with the document so it can be replaced.",
                    [new ValidationError(nameof(request.Notes), "Notes are required when rejecting a document.")]));
        }

        var document = await tenants.GetDocumentAsync(request.DocumentId, cancellationToken);

        if (document is null || document.TenantId != command.TenantId)
        {
            return Result.Failure<OrganisationDocumentResponse>(
                Error.NotFound("That document was not found for this organisation."));
        }

        if (document.Status == TenantDocumentStatus.Superseded)
        {
            return Result.Failure<OrganisationDocumentResponse>(Error.InvalidTransition(
                "That document has been replaced by a newer upload. Review the newer one."));
        }

        document.Status = request.Accepted
            ? TenantDocumentStatus.Accepted
            : TenantDocumentStatus.Rejected;

        document.ReviewedAtUtc = now;
        document.ReviewedByUserId = currentUser.UserId;
        document.ReviewNotes = request.Notes;

        await audit.WriteAsync(
            AuditActionCodes.TenantDocumentReviewed, nameof(TenantDocument), document.Id,
            document.FileName,
            new { TenantId = command.TenantId, request.Accepted, request.Notes },
            request.Notes, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(document.ToDocumentResponse(now));
    }
}
