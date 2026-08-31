using System.Globalization;
using System.Text.RegularExpressions;

namespace YDot.IAM.Domain.ValueObjects;

/// <summary>
/// A validated, normalised e-mail address.
///
/// NORMALISATION IS THE POINT. "Asha.Joseph@Example.ORG" and "asha.joseph@example.org" are
/// the same mailbox, and if one is stored as typed then the scoped-unique index does not
/// actually stop a duplicate. Everything is lower-cased and trimmed on construction, and
/// the normalised form is what goes into the column and the index.
///
/// SCOPED UNIQUE, NOT GLOBALLY UNIQUE. Section 6 of the brief: the same address may exist
/// in several Organisations as several separate users, but never twice inside one. This
/// type does not enforce that — it cannot see the database — it only guarantees that two
/// equal addresses produce two equal strings, which is what makes the index work.
/// </summary>
public sealed partial record EmailValue
{
    private EmailValue(string value) => Value = value;

    public string Value { get; }

    public string LocalPart => Value[..Value.IndexOf('@', StringComparison.Ordinal)];

    public string Domain => Value[(Value.IndexOf('@', StringComparison.Ordinal) + 1)..];

    /// <summary>Returns null rather than throwing, so a validator can turn it into a field message.</summary>
    public static EmailValue? TryParse(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalised = candidate.Trim().ToLowerInvariant();

        return normalised.Length <= 320 && EmailPattern().IsMatch(normalised)
            ? new EmailValue(normalised)
            : null;
    }

    public static EmailValue Parse(string candidate) =>
        TryParse(candidate) ?? throw new ArgumentException($"'{candidate}' is not a valid e-mail address.", nameof(candidate));

    /// <summary>
    /// Masked for display: "as***@example.org". Used wherever an address has to be shown to
    /// somebody who is not its owner, such as the MFA destination on the challenge screen.
    /// </summary>
    public string Masked()
    {
        var local = LocalPart;
        var visible = local.Length <= 2 ? local : local[..2];

        return string.Create(CultureInfo.InvariantCulture, $"{visible}***@{Domain}");
    }

    public override string ToString() => Value;

    public static implicit operator string(EmailValue email) => email.Value;

    // Deliberately permissive: this rejects the obviously malformed, and delivery is the
    // only real proof that an address exists. A stricter pattern rejects valid addresses.
    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex EmailPattern();
}
