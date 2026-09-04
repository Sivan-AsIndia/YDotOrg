using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Mappings;
using YDot.IAM.Domain.Entities.Configuration;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways.Commands.ManagePaymentGatewayConfiguration;

/// <summary>Creates or updates one Organisation's gateway configuration.</summary>
public sealed record UpsertPaymentGatewayConfigurationCommand(
    UpsertPaymentGatewayConfigurationRequest Request);

/// <summary>Turns a configuration on or off. Activating one stands the others down.</summary>
public sealed record ChangePaymentGatewayStatusCommand(
    Guid ConfigurationId, ChangePaymentGatewayStatusRequest Request);

/// <summary>Deletes a configuration. Refused while it is the active one.</summary>
public sealed record DeletePaymentGatewayConfigurationCommand(
    Guid ConfigurationId, DeletePaymentGatewayConfigurationRequest Request);

/// <summary>Reaches the provider with the stored credentials and records what came back.</summary>
public sealed record TestPaymentGatewayConfigurationCommand(Guid ConfigurationId);

/// <summary>
/// Payment gateway configuration: the screen where an Organisation says where its donations
/// settle.
///
/// THREE THINGS ARE TRUE OF EVERY PATH THROUGH THIS CLASS, and they are the reasons it is longer
/// than a CRUD handler would be.
///
/// 1. A CREDENTIAL IS SEALED BEFORE IT REACHES A COLUMN and is never returned, never logged and
///    never written to an audit row. The only thing that leaves is the four-character hint.
///
/// 2. A NULL CREDENTIAL FIELD MEANS "LEAVE IT ALONE", NOT "CLEAR IT". The form cannot send back
///    a key it was never given, so every save of an existing row arrives with nulls in those
///    three fields. Treating null as a clear would wipe a working merchant credential every time
///    somebody corrected a typo in the display name - and the failure would not show up until
///    the next donation.
///
/// 3. AT MOST ONE ACTIVE CONFIGURATION PER ORGANISATION PER ENVIRONMENT. PAY asks for "the
///    active configuration"; two would make the answer arbitrary, which for a settlement account
///    means donations landing in whichever merchant account the database happened to order
///    first.
///
/// EVERY CHANGE IS WRITTEN TWICE, deliberately. Once to the configuration's own change log,
/// which is the per-field before-and-after the screen renders, and once to the platform audit
/// trail, which is what an auditor reads across every module. Both are written in the same
/// transaction as the change, so neither can be missing from a change that happened.
/// </summary>
public sealed class PaymentGatewayConfigurationCommandHandler(
    IPaymentGatewayConfigurationRepository configurations,
    ITenantRepository tenants,
    IPaymentSecretProtector protector,
    IPaymentGatewayConnectivityTester tester,
    IAuditService audit,
    PaymentGatewayScope scope,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<PaymentGatewayConfigurationCommandHandler> logger)
{
    public async Task<Result<PaymentGatewayConfigurationResponse>> HandleAsync(
        UpsertPaymentGatewayConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (request.Provider == PaymentGatewayProvider.None)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(Error.Validation(
                "Choose a payment gateway.",
                [new ValidationError(
                    "provider",
                    "A configuration has to name the provider it belongs to.")]));
        }

        var resolvedTenant = scope.ResolveWriteTenant(request.TenantId);
        if (resolvedTenant.IsFailure)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(resolvedTenant.Error!);
        }

        var tenantId = resolvedTenant.Value;

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(
                Error.TenantNotFound("That organisation was not found."));
        }

        var existing = await LoadForWriteAsync(request, tenantId, cancellationToken);

        return existing is null
            ? await CreateAsync(request, tenant.Id, tenant.Name, cancellationToken)
            : await UpdateAsync(request, existing, tenant.Name, cancellationToken);
    }

    /// <summary>
    /// Finds the row this save is about, by id when the form has one and otherwise by the natural
    /// key.
    ///
    /// THE NATURAL-KEY LOOKUP IS WHAT MAKES THIS AN UPSERT. A form that has never saved has no
    /// id, and creating a second Razorpay-sandbox row for an Organisation that already has one
    /// would leave two rows fighting over the same provider account.
    /// </summary>
    private async Task<PaymentGatewayConfiguration?> LoadForWriteAsync(
        UpsertPaymentGatewayConfigurationRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        if (request.Id is { } id && id != Guid.Empty)
        {
            return scope.IsSuperAdmin
                ? await configurations.GetAcrossTenantsAsync(id, cancellationToken)
                : await configurations.GetAsync(id, cancellationToken);
        }

        return await configurations.GetByProviderAsync(
            tenantId, request.Provider, request.Environment, cancellationToken);
    }

    private async Task<Result<PaymentGatewayConfigurationResponse>> CreateAsync(
        UpsertPaymentGatewayConfigurationRequest request,
        Guid tenantId,
        string organisationName,
        CancellationToken cancellationToken)
    {
        var configuration = new PaymentGatewayConfiguration
        {
            TenantId = tenantId,
            BusinessUnitId = scope.BusinessUnitId,
            Provider = request.Provider,
            Environment = request.Environment,
            DisplayName = Trim(request.DisplayName),
            MerchantId = Trim(request.MerchantId),
            WebhookUrl = Trim(request.WebhookUrl),
            SubscribedEvents = Join(request.SubscribedEvents),
            SettlementCurrencyCode = NormaliseCurrency(request.SettlementCurrencyCode),
            ReturnUrl = Trim(request.ReturnUrl),
            PaymentLinkValidityMinutes = request.PaymentLinkValidityMinutes,
            EnabledMethods = Join(request.EnabledMethods),
            Notes = Trim(request.Notes),
            IsActive = request.IsActive
        };

        ApplyCredential(configuration, request.ApiKey, CredentialSlot.ApiKey);
        ApplyCredential(configuration, request.SecretKey, CredentialSlot.SecretKey);
        ApplyCredential(configuration, request.WebhookSecret, CredentialSlot.WebhookSecret);

        var usable = EnsureUsable(configuration);
        if (usable.IsFailure)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(usable.Error!);
        }

        await configurations.AddAsync(configuration, cancellationToken);

        if (configuration.IsActive)
        {
            await StandDownOthersAsync(configuration, cancellationToken);
        }

        var log = new PaymentGatewayChangeLog(currentUser, clock);

        log.Summary(
            PaymentGatewayConfigurationAction.Created,
            $"{configuration.Provider} / {configuration.Environment}"
            + (configuration.IsActive ? " (active)" : " (inactive)"));

        await configurations.AddAuditAsync(
            log.For(configuration, request.Reason), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.PaymentGatewayConfigured,
            nameof(PaymentGatewayConfiguration),
            configuration.Id,
            $"{configuration.Provider} ({configuration.Environment}) - {organisationName}",
            new
            {
                Provider = configuration.Provider.ToString(),
                Environment = configuration.Environment.ToString(),
                configuration.MerchantId,
                configuration.IsActive,
                HasApiKey = configuration.ApiKeyCipher is not null,
                configuration.HasSecretKey,
                configuration.HasWebhookSecret,
                OrganisationId = configuration.TenantId
            },
            request.Reason,
            cancellationToken);

        logger.LogInformation(
            "Payment gateway {Provider} ({Environment}) configured for organisation {TenantId}. "
            + "Active: {IsActive}.",
            configuration.Provider,
            configuration.Environment,
            configuration.TenantId,
            configuration.IsActive);

        return configuration.ToResponse(organisationName, null, scope.PermittedActions(configuration.IsActive));
    }

    private async Task<Result<PaymentGatewayConfigurationResponse>> UpdateAsync(
        UpsertPaymentGatewayConfigurationRequest request,
        PaymentGatewayConfiguration configuration,
        string organisationName,
        CancellationToken cancellationToken)
    {
        // THE VERSION CHECK IS NOT OPTIONAL ON AN UPDATE. Without it, two administrators with the
        // screen open would silently overwrite each other, and on this screen the thing being
        // overwritten is which merchant account the money reaches.
        if (request.ExpectedVersion is not { } expected)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(Error.Validation(
                "This configuration already exists. Reload the screen and try again.",
                [new ValidationError(
                    "expectedVersion",
                    "An update has to say which version of the record it is changing.")]));
        }

        if (configuration.Version != expected)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(Error.Concurrency(
                "Somebody else changed this gateway configuration while you had it open. "
                + "Reload the screen to see their change before saving yours."));
        }

        var log = new PaymentGatewayChangeLog(currentUser, clock);

        // ---- The plain fields. Every one recorded before and after. ------------------------------
        log.Field(PaymentGatewayFields.Provider, configuration.Provider, request.Provider);
        log.Field(PaymentGatewayFields.Environment, configuration.Environment, request.Environment);
        log.Field(PaymentGatewayFields.DisplayName, configuration.DisplayName, Trim(request.DisplayName));
        log.Field(PaymentGatewayFields.MerchantId, configuration.MerchantId, Trim(request.MerchantId));
        log.Field(PaymentGatewayFields.WebhookUrl, configuration.WebhookUrl, Trim(request.WebhookUrl));
        log.Field(
            PaymentGatewayFields.SubscribedEvents,
            configuration.SubscribedEvents,
            request.SubscribedEvents is null ? configuration.SubscribedEvents : Join(request.SubscribedEvents));
        log.Field(
            PaymentGatewayFields.SettlementCurrencyCode,
            configuration.SettlementCurrencyCode,
            NormaliseCurrency(request.SettlementCurrencyCode));
        log.Field(PaymentGatewayFields.ReturnUrl, configuration.ReturnUrl, Trim(request.ReturnUrl));
        log.Field(
            PaymentGatewayFields.PaymentLinkValidityMinutes,
            configuration.PaymentLinkValidityMinutes,
            request.PaymentLinkValidityMinutes);
        log.Field(
            PaymentGatewayFields.EnabledMethods,
            configuration.EnabledMethods,
            request.EnabledMethods is null ? configuration.EnabledMethods : Join(request.EnabledMethods));
        log.Field(PaymentGatewayFields.IsActive, configuration.IsActive, request.IsActive);
        log.Field(PaymentGatewayFields.Notes, configuration.Notes, Trim(request.Notes));

        var wasActive = configuration.IsActive;

        configuration.Provider = request.Provider;
        configuration.Environment = request.Environment;
        configuration.DisplayName = Trim(request.DisplayName);
        configuration.MerchantId = Trim(request.MerchantId);
        configuration.WebhookUrl = Trim(request.WebhookUrl);
        configuration.SettlementCurrencyCode = NormaliseCurrency(request.SettlementCurrencyCode);
        configuration.ReturnUrl = Trim(request.ReturnUrl);
        configuration.PaymentLinkValidityMinutes = request.PaymentLinkValidityMinutes;
        configuration.Notes = Trim(request.Notes);
        configuration.IsActive = request.IsActive;

        // Null means "the form did not carry this list", which is how a partial save arrives.
        if (request.SubscribedEvents is not null)
        {
            configuration.SubscribedEvents = Join(request.SubscribedEvents);
        }

        if (request.EnabledMethods is not null)
        {
            configuration.EnabledMethods = Join(request.EnabledMethods);
        }

        // ---- The credentials. See point 2 in the class comment for why null is left alone. -------
        ApplyCredentialWithLog(configuration, request.ApiKey, CredentialSlot.ApiKey, log);
        ApplyCredentialWithLog(configuration, request.SecretKey, CredentialSlot.SecretKey, log);
        ApplyCredentialWithLog(configuration, request.WebhookSecret, CredentialSlot.WebhookSecret, log);

        var usable = EnsureUsable(configuration);
        if (usable.IsFailure)
        {
            return Result.Failure<PaymentGatewayConfigurationResponse>(usable.Error!);
        }

        if (configuration.IsActive)
        {
            await StandDownOthersAsync(configuration, cancellationToken);
        }

        if (log.HasEntries)
        {
            await configurations.AddAuditAsync(log.For(configuration, request.Reason), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var credentialsChanged = log.Entries.Any(
            entry => entry.Action == PaymentGatewayConfigurationAction.CredentialsRotated);

        await audit.WriteAsync(
            credentialsChanged
                ? AuditActionCodes.PaymentGatewayCredentialsRotated
                : AuditActionCodes.PaymentGatewayUpdated,
            nameof(PaymentGatewayConfiguration),
            configuration.Id,
            $"{configuration.Provider} ({configuration.Environment}) - {organisationName}",
            new
            {
                Provider = configuration.Provider.ToString(),
                Environment = configuration.Environment.ToString(),
                configuration.MerchantId,
                WasActive = wasActive,
                configuration.IsActive,
                CredentialsChanged = credentialsChanged,

                // The field names only. The values are in the change log, masked; repeating them
                // here would put the same information in two places and only one of them redacts.
                ChangedFields = log.Entries
                    .Where(entry => entry.FieldName is not null)
                    .Select(entry => entry.FieldName)
                    .ToArray(),
                OrganisationId = configuration.TenantId
            },
            request.Reason,
            cancellationToken);

        return configuration.ToResponse(organisationName, null, scope.PermittedActions(configuration.IsActive));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangePaymentGatewayStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var loaded = await LoadInScopeAsync(command.ConfigurationId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var configuration = loaded.Value!;

        if (configuration.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (configuration.IsActive == request.IsActive)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                request.IsActive
                    ? "That gateway is already active."
                    : "That gateway is already inactive."));
        }

        // ACTIVATING WITHOUT A KEY WOULD LOOK LIKE SUCCESS AND REFUSE EVERY DONATION. The screen
        // would say "active", the sidebar would say nothing was wrong, and every donor would meet
        // PAYMENT_GATEWAY_NOT_CONFIGURED - which reads as a platform fault rather than as an
        // unfinished setup.
        if (request.IsActive)
        {
            var usable = EnsureUsable(configuration);
            if (usable.IsFailure)
            {
                return Result.Failure<OutcomeResponse>(usable.Error!);
            }
        }

        var log = new PaymentGatewayChangeLog(currentUser, clock);
        log.Field(PaymentGatewayFields.IsActive, configuration.IsActive, request.IsActive);

        configuration.IsActive = request.IsActive;

        if (configuration.IsActive)
        {
            await StandDownOthersAsync(configuration, cancellationToken);
        }

        await configurations.AddAuditAsync(log.For(configuration, request.Reason), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            request.IsActive
                ? AuditActionCodes.PaymentGatewayActivated
                : AuditActionCodes.PaymentGatewayDeactivated,
            nameof(PaymentGatewayConfiguration),
            configuration.Id,
            $"{configuration.Provider} ({configuration.Environment})",
            new
            {
                Provider = configuration.Provider.ToString(),
                Environment = configuration.Environment.ToString(),
                configuration.IsActive,
                OrganisationId = configuration.TenantId
            },
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(
            configuration.Id,
            configuration.IsActive ? "Active" : "Inactive",
            configuration.Version,
            configuration.IsActive
                ? "Gateway activated. New donations will be taken through it."
                : "Gateway deactivated. No new donations will be taken through it.",
            scope.PermittedActions(configuration.IsActive));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeletePaymentGatewayConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "Say why this gateway configuration is being removed.",
                [new ValidationError(
                    "reason",
                    "A reason is required. Removing where an organisation's donations settle is "
                    + "not something a later reader should have to guess at.")]));
        }

        var loaded = await LoadInScopeAsync(command.ConfigurationId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var configuration = loaded.Value!;

        if (configuration.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // THE TWO-STEP IS THE POINT. Deleting the row donations are currently flowing through
        // stops every payment for that Organisation the moment it commits, and an accidental
        // click should not be able to do that. Standing it down first is a visible, reversible
        // act with its own audit row.
        if (configuration.IsActive)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This gateway is currently taking donations. Deactivate it first, then delete it."));
        }

        var log = new PaymentGatewayChangeLog(currentUser, clock);

        log.Summary(
            PaymentGatewayConfigurationAction.Deleted,
            $"{configuration.Provider} / {configuration.Environment}");

        // WRITTEN BEFORE THE DELETE, and pointing at a row that will not exist a line later. That
        // is deliberate and it is why ConfigurationId is not a cascading foreign key: the record
        // of who removed an organisation's merchant configuration has to outlive the row.
        await configurations.AddAuditAsync(log.For(configuration, request.Reason), cancellationToken);

        var snapshot = new
        {
            Provider = configuration.Provider.ToString(),
            Environment = configuration.Environment.ToString(),
            configuration.MerchantId,
            configuration.WebhookUrl,
            OrganisationId = configuration.TenantId
        };

        configurations.Remove(configuration);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.PaymentGatewayDeleted,
            nameof(PaymentGatewayConfiguration),
            configuration.Id,
            $"{configuration.Provider} ({configuration.Environment})",
            snapshot,
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(
            configuration.Id, "Deleted", configuration.Version, "Gateway configuration deleted.", []);
    }

    /// <summary>
    /// The Test button.
    ///
    /// IT UNSEALS THE CREDENTIALS AND THEY GO NO FURTHER THAN THE TESTER. Nothing on the way back
    /// carries them: the outcome holds a message, an optional provider reference and a duration,
    /// all of which are safe to store on the row and show on screen.
    ///
    /// THE RESULT IS PERSISTED whether it passed or failed, because "last tested: failed, three
    /// weeks ago" is exactly what somebody needs to see when donations stop working - and it is
    /// the thing nobody remembers to write down.
    /// </summary>
    public async Task<Result<PaymentGatewayTestResultResponse>> HandleAsync(
        TestPaymentGatewayConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadInScopeAsync(command.ConfigurationId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<PaymentGatewayTestResultResponse>(loaded.Error!);
        }

        var configuration = loaded.Value!;

        var apiKey = protector.Unprotect(configuration.ApiKeyCipher);
        var secretKey = protector.Unprotect(configuration.SecretKeyCipher);

        var outcome = string.IsNullOrWhiteSpace(apiKey)
            ? GatewayTestOutcome.Fail(
                "No API key is stored for this gateway, so there is nothing to test with.")
            : await tester.TestAsync(configuration, apiKey, secretKey, cancellationToken);

        var testedAt = clock.UtcNow;

        configuration.LastTestedAtUtc = testedAt;
        configuration.LastTestSucceeded = outcome.Succeeded;
        configuration.LastTestMessage = Truncate(outcome.Message, 500);

        var log = new PaymentGatewayChangeLog(currentUser, clock);

        log.Summary(
            PaymentGatewayConfigurationAction.Tested,
            outcome.Succeeded ? "Passed" : $"Failed: {Truncate(outcome.Message, 200)}");

        await configurations.AddAuditAsync(log.For(configuration, null), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.PaymentGatewayTested,
            nameof(PaymentGatewayConfiguration),
            configuration.Id,

            // The overload that records an OUTCOME rather than assuming success: a failed test
            // is exactly the row somebody needs when donations stop, and one recorded as
            // "succeeded" would be worse than none.
            outcome.Succeeded ? AuditResult.Succeeded : AuditResult.Failed,
            $"{configuration.Provider} ({configuration.Environment})",
            metadata: new
            {
                Provider = configuration.Provider.ToString(),
                Environment = configuration.Environment.ToString(),
                outcome.Succeeded,
                outcome.Message,
                outcome.Reference,
                outcome.DurationMilliseconds,
                OrganisationId = configuration.TenantId
            },
            cancellationToken: cancellationToken);

        return new PaymentGatewayTestResultResponse(
            configuration.Id,
            configuration.Provider.ToString(),
            configuration.Environment.ToString(),
            outcome.Succeeded,
            outcome.Message,
            outcome.Reference,
            outcome.DurationMilliseconds,
            testedAt);
    }

    // =============================================================================================
    // Shared
    // =============================================================================================

    private async Task<Result<PaymentGatewayConfiguration>> LoadInScopeAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var configuration = scope.IsSuperAdmin
            ? await configurations.GetAcrossTenantsAsync(id, cancellationToken)
            : await configurations.GetAsync(id, cancellationToken);

        return configuration is null
            ? Result.Failure<PaymentGatewayConfiguration>(
                Error.NotFound("That gateway configuration was not found."))
            : configuration;
    }

    /// <summary>
    /// Refuses a configuration that could not take a payment.
    ///
    /// CHECKED ON SAVE AND AGAIN ON ACTIVATE rather than only at the point of use, because the
    /// point of use is a donor's browser. A misconfigured row that is caught here costs an
    /// administrator one more field; caught there it costs a donation.
    /// </summary>
    private static Result EnsureUsable(PaymentGatewayConfiguration configuration)
    {
        if (!configuration.IsActive)
        {
            // An inactive row is a draft. Half-finished is exactly what it is for.
            return Result.Success();
        }

        var problems = new List<ValidationError>();

        if (configuration.ApiKeyCipher is null)
        {
            problems.Add(new ValidationError(
                "apiKey", "An active gateway needs its API key, or no donation can be taken."));
        }

        if (!configuration.HasSecretKey
            && configuration.Provider is not PaymentGatewayProvider.HostedCheckout)
        {
            problems.Add(new ValidationError(
                "secretKey",
                "An active gateway needs its secret key. Without it the provider will refuse "
                + "every request as unauthenticated."));
        }

        if (string.IsNullOrWhiteSpace(configuration.SettlementCurrencyCode))
        {
            problems.Add(new ValidationError(
                "settlementCurrencyCode",
                "Name the currency this merchant account settles in. A campaign asking for a "
                + "different one cannot be paid out."));
        }

        return problems.Count == 0
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "This gateway cannot be made active yet.", problems));
    }

    /// <summary>
    /// Stands every other active row in the same environment down.
    ///
    /// IN THE SAME TRANSACTION AS THE ACTIVATION, which is why it is here and not a second call
    /// from the controller: a failure between the two would leave an Organisation with two active
    /// gateways and PAY choosing between them by row order.
    /// </summary>
    private async Task StandDownOthersAsync(
        PaymentGatewayConfiguration configuration, CancellationToken cancellationToken)
    {
        var others = await configurations.GetOtherActiveAsync(
            configuration.TenantId, configuration.Environment, configuration.Id, cancellationToken);

        foreach (var other in others)
        {
            other.IsActive = false;

            var log = new PaymentGatewayChangeLog(currentUser, clock);
            log.Field(PaymentGatewayFields.IsActive, true, false);

            await configurations.AddAuditAsync(
                log.For(
                    other,
                    $"Stood down automatically when {configuration.Provider} "
                    + $"({configuration.Environment}) was made active."),
                cancellationToken);

            logger.LogInformation(
                "Gateway configuration {OtherId} was deactivated because {ConfigurationId} became "
                + "the active {Environment} gateway for organisation {TenantId}.",
                other.Id,
                configuration.Id,
                configuration.Environment,
                configuration.TenantId);
        }
    }

    private enum CredentialSlot
    {
        ApiKey,
        SecretKey,
        WebhookSecret
    }

    /// <summary>
    /// Applies one credential field, honouring the null-means-leave-alone rule.
    ///
    /// null           the form did not carry this field. Nothing changes.
    /// empty string   an explicit clear. The stored credential is removed.
    /// anything else  sealed and stored, with its hint alongside.
    /// </summary>
    private void ApplyCredential(
        PaymentGatewayConfiguration configuration, string? value, CredentialSlot slot)
    {
        if (value is null)
        {
            return;
        }

        var trimmed = value.Trim();
        var cleared = trimmed.Length == 0;

        switch (slot)
        {
            case CredentialSlot.ApiKey:
                configuration.ApiKeyCipher = cleared ? null : protector.Protect(trimmed);
                configuration.ApiKeyHint = cleared ? null : protector.Hint(trimmed);
                break;

            case CredentialSlot.SecretKey:
                configuration.SecretKeyCipher = cleared ? null : protector.Protect(trimmed);
                configuration.HasSecretKey = !cleared;
                break;

            case CredentialSlot.WebhookSecret:
                configuration.WebhookSecretCipher = cleared ? null : protector.Protect(trimmed);
                configuration.HasWebhookSecret = !cleared;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown credential slot.");
        }
    }

    /// <summary>The same, with a change-log row recorded from the before and after state.</summary>
    private void ApplyCredentialWithLog(
        PaymentGatewayConfiguration configuration,
        string? value,
        CredentialSlot slot,
        PaymentGatewayChangeLog log)
    {
        if (value is null)
        {
            return;
        }

        var (fieldName, had) = slot switch
        {
            CredentialSlot.ApiKey => (PaymentGatewayFields.ApiKey, configuration.ApiKeyCipher is not null),
            CredentialSlot.SecretKey => (PaymentGatewayFields.SecretKey, configuration.HasSecretKey),
            CredentialSlot.WebhookSecret =>
                (PaymentGatewayFields.WebhookSecret, configuration.HasWebhookSecret),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown credential slot.")
        };

        ApplyCredential(configuration, value, slot);

        var has = slot switch
        {
            CredentialSlot.ApiKey => configuration.ApiKeyCipher is not null,
            CredentialSlot.SecretKey => configuration.HasSecretKey,
            _ => configuration.HasWebhookSecret
        };

        // The hint is only ever produced for the API key: it is the one an operator matches
        // against a provider dashboard. A hint of a secret would be a fragment of a secret.
        var hint = slot == CredentialSlot.ApiKey ? configuration.ApiKeyHint : null;

        log.Credential(fieldName, had, has, hint);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormaliseCurrency(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "INR" : value.Trim().ToUpperInvariant();

    /// <summary>
    /// A comma-separated list from a set of codes, or null for an empty one.
    ///
    /// Ordered and de-duplicated so that re-posting the same selection in a different order does
    /// not read as a change in the log.
    /// </summary>
    private static string? Join(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var cleaned = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return cleaned.Length == 0 ? null : string.Join(',', cleaned);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 3)] + "...";
}
