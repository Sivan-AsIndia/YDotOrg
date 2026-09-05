namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Which set of a provider's credentials a configuration holds.
///
/// THIS IS NOT COSMETIC. A Production row moves real money and a Sandbox row moves none, and
/// the two are told apart by nothing but this column and the key prefix that usually agrees
/// with it. Every screen that shows a configuration shows this beside it, because a sandbox
/// row mistaken for a live one is how an organisation reports income it never received - and a
/// live row mistaken for a sandbox one is how somebody tests with a real donor's card.
/// </summary>
public enum PaymentGatewayEnvironment
{
    /// <summary>The provider's test environment. No money moves.</summary>
    Sandbox = 0,

    /// <summary>The live environment. Donations settle into the merchant's real account.</summary>
    Production = 1
}
