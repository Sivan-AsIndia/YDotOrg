using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;
using YDot.IAM.Domain.Entities.Configuration;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways.Mappings;

/// <summary>
/// Entity to response, in one place.
///
/// THE REASON THIS IS NOT INLINE IN THE HANDLERS is the same reason the response type carries no
/// credential field: there are four call sites, and a mapping copied four times is a mapping
/// where the fifth copy eventually includes <c>ApiKeyCipher</c> because somebody was matching
/// column names. Here there is one place to look and one place for that mistake to be visible.
/// </summary>
public static class PaymentGatewayMappingConfig
{
    public static PaymentGatewayConfigurationResponse ToResponse(
        this PaymentGatewayConfiguration configuration,
        string? organisationName,
        string? organisationCode,
        IReadOnlyList<string> permittedActions)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var descriptor = PaymentGatewayCatalogue.Find(configuration.Provider);

        return new PaymentGatewayConfigurationResponse(
            configuration.Id,
            configuration.TenantId,
            organisationName,
            organisationCode,
            configuration.Provider.ToString(),
            descriptor?.Name ?? configuration.Provider.ToString(),
            configuration.Environment.ToString(),
            configuration.DisplayName,
            configuration.MerchantId,
            configuration.ApiKeyHint,

            // Presence, never the value. See the entity comment for the whole of the reasoning.
            HasApiKey: configuration.ApiKeyCipher is not null,
            configuration.HasSecretKey,
            configuration.WebhookUrl,
            configuration.HasWebhookSecret,
            Split(configuration.SubscribedEvents),
            configuration.SettlementCurrencyCode,
            configuration.ReturnUrl,
            configuration.PaymentLinkValidityMinutes,
            Split(configuration.EnabledMethods),
            configuration.IsActive,
            descriptor?.HasAdapter ?? false,
            configuration.LastTestedAtUtc,
            configuration.LastTestSucceeded,
            configuration.LastTestMessage,
            configuration.Notes,
            configuration.CreatedAtUtc,
            configuration.UpdatedAtUtc,
            configuration.Version,
            permittedActions);
    }

    public static PaymentGatewayConfigurationAuditResponse ToResponse(
        this PaymentGatewayConfigurationAudit entry, string? organisationName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new PaymentGatewayConfigurationAuditResponse(
            entry.Id,
            entry.ConfigurationId,
            entry.TenantId,
            organisationName,
            entry.Provider.ToString(),
            entry.Environment.ToString(),
            entry.Action.ToString(),
            entry.FieldName,
            PaymentGatewayFields.LabelFor(entry.FieldName),
            entry.OldValue,
            entry.NewValue,
            entry.ActorUserId,
            entry.ActorDisplayName,
            entry.OccurredAtUtc,
            entry.Reason,
            entry.IpAddress,
            entry.CorrelationId);
    }

    /// <summary>The catalogue, in the shape the form binds to.</summary>
    public static PaymentGatewayCatalogueResponse ToCatalogueResponse(string? suggestedWebhookUrl) =>
        new(
            [.. PaymentGatewayCatalogue.Providers.Select(provider => new PaymentGatewayProviderOption(
                provider.Provider.ToString(),
                provider.Name,
                provider.HasAdapter,
                provider.ApiKeyLabel,
                provider.SecretKeyLabel,
                provider.MerchantIdLabel,
                provider.TestKeyPrefix,
                provider.LiveKeyPrefix,
                provider.DocumentationUrl))],
            [.. PaymentGatewayCatalogue.WebhookEvents.Select(
                item => new PaymentGatewayEventOption(item.Code, item.Name, item.Description))],
            [.. PaymentGatewayCatalogue.PaymentMethods.Select(
                item => new PaymentGatewayMethodOption(item.Code, item.Name))],
            suggestedWebhookUrl);

    /// <summary>
    /// A comma-separated column back into a list.
    ///
    /// An empty column becomes an EMPTY LIST, never null: a template that has to guard every
    /// list before iterating it is a template where one guard eventually goes missing.
    /// </summary>
    public static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
