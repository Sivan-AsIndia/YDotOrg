namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// Seals and unseals a merchant credential on its way to and from the database.
///
/// WHY THE APPLICATION LAYER ONLY EVER SEES THIS INTERFACE. The command handler has to turn the
/// key somebody typed into something safe to store, and it must not know or care whether that
/// is AES-GCM today and a cloud KMS next quarter. The implementation lives in Infrastructure
/// with the algorithm and the key material; everything above it deals in "sealed" and "not
/// sealed".
///
/// THE THREAT THIS ADDRESSES, PRECISELY. A database dump - a stolen backup, an SQL injection, a
/// production restore into a test environment - yields ciphertext and nothing else, because the
/// sealing key is in the deployment's configuration and not in any table. It does NOT defend
/// against an attacker who already has both the database and the application's configuration;
/// nothing stored-and-retrievable can.
///
/// <see cref="Hint"/> IS PART OF THE SAME CONTRACT, not a convenience bolted on beside it.
/// Producing the masked form next to the sealing means the screen never needs the plaintext to
/// show an operator which key is in the box.
/// </summary>
public interface IPaymentSecretProtector
{
    /// <summary>
    /// Seals a credential. Returns null for null or blank input, which is how "the caller left
    /// this field alone" travels through the handler without a special case.
    /// </summary>
    string? Protect(string? plaintext);

    /// <summary>
    /// Unseals a credential, or returns null when the input is not something this protector
    /// sealed - a value from a different key, or a column written before the scheme existed.
    ///
    /// NULL RATHER THAN AN EXCEPTION. A configuration whose credential cannot be unsealed is a
    /// configuration that cannot take a payment, which is a business outcome the callers already
    /// handle; throwing would turn it into a 500 on a donation page.
    /// </summary>
    string? Unprotect(string? cipherText);

    /// <summary>
    /// The masked form shown on screen: enough to recognise a key, never enough to use one.
    ///
    /// Keeps the provider's prefix, which is the half that answers the question actually being
    /// asked - a Razorpay key reading <c>rzp_live_</c> in an Organisation that believes it is in
    /// sandbox is exactly what an operator is looking for.
    /// </summary>
    string? Hint(string? plaintext);
}
