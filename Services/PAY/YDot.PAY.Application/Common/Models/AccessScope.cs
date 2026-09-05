namespace YDot.PAY.Application.Common.Models;

/// <summary>
/// The narrowing a read service applies WITHIN one Organisation.
///
/// IT IS NOT THE ORGANISATION BOUNDARY. That is enforced underneath by the DbContext query
/// filter and cannot be widened from here.
///
/// THERE ARE NOW TWO KINDS OF NARROWING AND THEY ARE NOT INTERCHANGEABLE:
///
///   <see cref="IsOwnRecordsOnly"/>     records this USER created. A member of staff whose data
///                                     scope says "own" sees the donations they took.
///   <see cref="IsDonorSelfService"/>   records about this DONOR. Somebody who gave.
///
/// USING THE FIRST FOR A DONOR WOULD SHOW THEM NOTHING, which is the trap worth naming. A
/// donation is created before its donor has an account - a lead scans a QR code, pays, and only
/// then is converted, given a login and invited - so the row's <c>CreatedByUserId</c> is not
/// theirs and never will be. Filtering a donor on "records you created" returns an empty page to
/// somebody looking at their own giving history, and an empty page reads as a broken account.
/// The donor filter matches on the DONOR'S E-MAIL, which is on the intent, the donation and the
/// receipt because the donor typed it when they gave.
///
/// THE E-MAIL MATCH IS THE KNOWN LIMIT OF THIS. A donor who gave under one address and signs in
/// under another sees only the first one's gifts. That is the safe direction to be wrong in -
/// it withholds a record rather than exposing somebody else's - and it is why the donor
/// directory matches on "Organisation AND normalised e-mail" too.
/// </summary>
public sealed record AccessScope(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<string> DataScopes,

    /// <summary>
    /// The signed-in donor's e-mail, normalised, and only when <see cref="IsDonorSelfService"/>.
    ///
    /// NULL FOR EVERY STAFF CALLER, so a filter written against it cannot silently narrow a
    /// fundraiser's register to whoever they happen to share an address with.
    /// </summary>
    string? DonorEmail = null,

    /// <summary>
    /// True when the caller is a donor looking at their own giving, and nothing else.
    ///
    /// SET FROM THE ROLE RATHER THAN FROM A DATA-SCOPE ROW, deliberately. A scope row is
    /// provisioning that somebody has to remember to create, and the one time it is forgotten a
    /// donor sees every other donor's giving. The role is issued by the same command that
    /// creates the account, so the two cannot come apart.
    /// </summary>
    bool IsDonorSelfService = false)
{
    public static readonly AccessScope Empty = new(Guid.Empty, Guid.Empty, []);

    /// <summary>Records this USER created. See the note on the class about donors.</summary>
    public bool IsOwnRecordsOnly =>
        DataScopes.Contains("own", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the donor filter can actually be applied.
    ///
    /// A DONOR WITH NO E-MAIL ON THEIR TOKEN IS NARROWED TO NOTHING rather than widened to
    /// everything - see how the read services use this. It should not happen, since an account
    /// cannot be created without an address, but "the filter could not be built" must never
    /// resolve to "show them everything".
    /// </summary>
    public bool HasDonorIdentity => !string.IsNullOrWhiteSpace(DonorEmail);
}
