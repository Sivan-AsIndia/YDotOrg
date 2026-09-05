namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// One concrete payment provider, as opposed to the thing that CHOOSES between them.
///
/// WHY A MARKER INTERFACE RATHER THAN JUST <see cref="IPaymentGateway"/>. The router is itself an
/// <see cref="IPaymentGateway"/> - that is the whole point of it, since the command handlers must
/// not know a router exists - so a router that asked the container for every
/// <c>IPaymentGateway</c> would be asked to construct itself. This interface splits the two
/// populations: adapters are registered as <c>IPaymentGatewayAdapter</c>, the router is registered
/// as <c>IPaymentGateway</c>, and the cycle cannot form.
///
/// WHAT IT BUYS. Adding Stripe or PayPal becomes a new class implementing this interface plus one
/// registration line. No change to the router, no change to any handler, and no change to the
/// configuration screen - an Organisation that types the provider's name on IAM's payment gateway
/// configuration page is routed to it because <c>GatewayName</c> matches, not because anybody
/// edited a switch statement.
///
/// <see cref="IPaymentGateway.GatewayName"/> IS THE KEY, and it must equal the string IAM stores
/// in <c>PaymentGatewayConfig.Provider</c>, compared case-insensitively.
/// </summary>
public interface IPaymentGatewayAdapter : IPaymentGateway;
