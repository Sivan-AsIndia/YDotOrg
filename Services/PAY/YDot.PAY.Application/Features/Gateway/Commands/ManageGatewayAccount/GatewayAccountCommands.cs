using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Gateway.DTOs;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Features.Gateway.Commands.ManageGatewayAccount;

/// <summary>Creates or updates the Organisation's gateway account.</summary>
public sealed record UpsertGatewayAccountCommand(UpsertGatewayAccountRequest Request);

/// <summary>Reads the Organisation's gateway accounts.</summary>
public sealed record GetGatewayAccountsQuery;

/// <summary>
/// The per-Organisation gateway configuration.
///
/// THIS IS WHERE THE MODULE BECOMES GENUINELY TENANT-SPECIFIC. Each charity's donations settle
/// into its OWN merchant account; a shared account would pool every organisation's income into
/// one payout, which is a legal problem rather than a data one.
///
/// CHANGING THE MERCHANT ID IS AUDITED UNDER ITS OWN CODE. Whoever can do it can redirect every
/// future donation to a different account, so it is the first thing anybody would look for if
/// money started arriving somewhere unexpected - and burying it in a general "account updated"
/// row would make it hard to find.
///
/// NO SECRET PASSES THROUGH HERE. The request carries the REFERENCE to a key already in the
/// secret store; a merchant secret in a request body ends up in a request log, a proxy buffer
/// and an exception message.
/// </summary>
public sealed class GatewayAccountCommandHandler(
    IGatewayAccountRepository accounts,
    IAuditWriter audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<GatewayAccountCommandHandler> logger)
{
    public async Task<Result<GatewayAccountResponse>> HandleAsync(
        UpsertGatewayAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<GatewayAccountResponse>(Error.TenantSelectionRequired());
        }

        var existing = await accounts.GetActiveForTenantAsync(cancellationToken);

        if (existing is null)
        {
            var created = new PaymentGatewayAccount
            {
                TenantId = tenantContext.RequireTenantId(),
                BusinessUnitId = tenantContext.BusinessUnitId,
                GatewayName = request.GatewayName.Trim(),
                MerchantId = request.MerchantId.Trim(),
                SettlementCurrencyCode = request.SettlementCurrencyCode.Trim().ToUpperInvariant(),
                ApiKeyReference = Clean(request.ApiKeyReference),
                WebhookSecretReference = Clean(request.WebhookSecretReference),
                IsTestMode = request.IsTestMode,
                IsActive = request.IsActive,
                ReturnUrl = Clean(request.ReturnUrl),
                WebhookUrl = Clean(request.WebhookUrl),
                PaymentLinkValidityMinutes = request.PaymentLinkValidityMinutes,
                EnabledMethods = Clean(request.EnabledMethods),
                Notes = Clean(request.Notes)
            };

            await accounts.AddAsync(created, cancellationToken);

            await audit.WriteAsync(
                AuditActionCodes.GatewayAccountCreated,
                nameof(PaymentGatewayAccount),
                created.Id,
                new { created.GatewayName, created.MerchantId, created.IsTestMode },
                cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return created.ToResponse(PermittedActions());
        }

        if (request.ExpectedVersion.HasValue && existing.Version != request.ExpectedVersion.Value)
        {
            return Result.Failure<GatewayAccountResponse>(Error.Concurrency());
        }

        var previousMerchantId = existing.MerchantId;
        var newMerchantId = request.MerchantId.Trim();

        existing.GatewayName = request.GatewayName.Trim();
        existing.MerchantId = newMerchantId;
        existing.SettlementCurrencyCode = request.SettlementCurrencyCode.Trim().ToUpperInvariant();
        existing.ApiKeyReference = Clean(request.ApiKeyReference) ?? existing.ApiKeyReference;
        existing.WebhookSecretReference =
            Clean(request.WebhookSecretReference) ?? existing.WebhookSecretReference;
        existing.IsTestMode = request.IsTestMode;
        existing.IsActive = request.IsActive;
        existing.ReturnUrl = Clean(request.ReturnUrl);
        existing.WebhookUrl = Clean(request.WebhookUrl);
        existing.PaymentLinkValidityMinutes = request.PaymentLinkValidityMinutes;
        existing.EnabledMethods = Clean(request.EnabledMethods);
        existing.Notes = Clean(request.Notes);

        var payoutChanged = !string.Equals(previousMerchantId, newMerchantId, StringComparison.Ordinal);

        await audit.WriteAsync(
            payoutChanged
                ? AuditActionCodes.GatewayPayoutDestinationChanged
                : AuditActionCodes.GatewayAccountUpdated,
            nameof(PaymentGatewayAccount),
            existing.Id,
            new
            {
                existing.GatewayName,
                PreviousMerchantId = payoutChanged ? previousMerchantId : null,
                NewMerchantId = payoutChanged ? newMerchantId : null,
                existing.IsTestMode
            },
            cancellationToken: cancellationToken);

        if (payoutChanged)
        {
            logger.LogWarning(
                "The payout destination for organisation {TenantId} changed from {Previous} to "
                + "{Current}, by user {UserId}.",
                existing.TenantId, previousMerchantId, newMerchantId, currentUser.UserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return existing.ToResponse(PermittedActions());
    }

    public async Task<Result<IReadOnlyList<GatewayAccountResponse>>> HandleAsync(
        GetGatewayAccountsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await accounts.GetAllForTenantAsync(cancellationToken);

        return Result.Success<IReadOnlyList<GatewayAccountResponse>>(
            [.. all.Select(account => account.ToResponse(PermittedActions()))]);
    }

    private IReadOnlyList<string> PermittedActions()
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.GatewayView))
        {
            actions.Add("View");
        }

        if (currentUser.HasPermission(PermissionCodes.GatewayManage))
        {
            actions.Add("Edit");
        }

        return actions;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Manual mapping for the gateway account.</summary>
public static class GatewayAccountMappingConfig
{
    /// <summary>
    /// A gateway account as the configuration screen shows it.
    ///
    /// THE SECRET REFERENCES BECOME BOOLEANS. Showing that a key IS configured is useful;
    /// returning the reference itself would put a pointer to the secret into the browser's
    /// memory, its cache and its dev tools for no benefit.
    /// </summary>
    public static GatewayAccountResponse ToResponse(
        this PaymentGatewayAccount account, IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new GatewayAccountResponse(
            account.Id,
            account.TenantId,
            account.GatewayName,
            account.MerchantId,
            account.SettlementCurrencyCode,
            !string.IsNullOrWhiteSpace(account.ApiKeyReference),
            !string.IsNullOrWhiteSpace(account.WebhookSecretReference),
            account.IsTestMode,
            account.IsActive,
            account.ReturnUrl,
            account.WebhookUrl,
            account.PaymentLinkValidityMinutes,
            string.IsNullOrWhiteSpace(account.EnabledMethods)
                ? []
                : [.. account.EnabledMethods.Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            account.Notes,
            account.CreatedAtUtc,
            account.UpdatedAtUtc,
            account.Version,
            permittedActions);
    }
}
