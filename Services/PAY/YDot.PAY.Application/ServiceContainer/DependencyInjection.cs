using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Application.Features.Donations.Commands.ManageDonation;
using YDot.PAY.Application.Features.Donations.Commands.ManageIntent;
using YDot.PAY.Application.Features.Donations.Queries;
using YDot.PAY.Application.Features.Gateway.Commands.ManageGatewayAccount;
using YDot.PAY.Application.Features.Payments.Commands.ProcessPayment;
using YDot.PAY.Application.Features.Payments.Queries;
using YDot.PAY.Application.Features.Receipts.Commands.ManageReceipt;
using YDot.PAY.Application.Features.Receipts.Queries;
using YDot.PAY.Application.Features.Refunds.Commands.ManageChargeback;
using YDot.PAY.Application.Features.Refunds.Commands.ManageRefund;
using YDot.PAY.Application.Features.Refunds.Queries;

namespace YDot.PAY.Application.ServiceContainer;

/// <summary>
/// Registers everything the application layer owns: the option-pattern settings, the
/// FluentValidation validators, and every CQRS handler.
///
/// NO MediatR. Handlers are registered by hand and injected straight into the controller that
/// uses them, so the list below is also the inventory of what this service can do - and a
/// handler somebody forgot to wire up fails at STARTUP with a clear message rather than at the
/// first request with a container resolution error.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Option pattern -------------------------------------------------------------
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<PaymentSettings>(configuration.GetSection(PaymentSettings.SectionName));
        services.Configure<ClientAppSettings>(configuration.GetSection(ClientAppSettings.SectionName));
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.Configure<IdentityIntegrationSettings>(
            configuration.GetSection(IdentityIntegrationSettings.SectionName));

        // ---- FluentValidation ---------------------------------------------------------------
        //
        // Scanned rather than listed, unlike the handlers: a validator is found by the TYPE it
        // validates, so a missing one degrades to "no validation ran" rather than to a startup
        // failure - and listing them by hand would buy none of the safety that listing the
        // handlers does.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // ---- Donation intents: sections 11 to 14, 19 to 22 -----------------------------------
        services.AddScoped<DonationIntentCommandHandler>();
        services.AddScoped<DonationCommandHandler>();
        services.AddScoped<DonationQueryHandler>();

        // ---- Payment processing: sections 15 to 18, 23 ------------------------------------------
        services.AddScoped<PaymentProcessingCommandHandler>();
        services.AddScoped<PaymentEventQueryHandler>();

        // ---- Receipts: section 24 -------------------------------------------------------------------
        services.AddScoped<ReceiptCommandHandler>();
        services.AddScoped<ReceiptQueryHandler>();

        // ---- Refunds and chargebacks ---------------------------------------------------------------------
        services.AddScoped<RefundCommandHandler>();
        services.AddScoped<ChargebackCommandHandler>();
        services.AddScoped<RefundQueryHandler>();

        // ---- Gateway configuration -------------------------------------------------------------------------
        services.AddScoped<GatewayAccountCommandHandler>();

        return services;
    }
}
