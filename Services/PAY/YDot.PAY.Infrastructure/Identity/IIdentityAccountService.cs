using YDot.PAY.Application.Common.Abstractions.Services;

namespace YDot.PAY.Infrastructure.Identity;

/// <summary>
/// The seam between PAY and IAM for donor accounts.
///
/// IT LIVES IN INFRASTRUCTURE, NOT IN THE APPLICATION LAYER, deliberately. The application layer
/// already has <see cref="IDonorDirectory"/>, which expresses the BUSINESS steps of section 15 -
/// find donor, create donor, create account, invite. This interface is the MECHANISM behind the
/// last two, and how PAY talks to IAM is an infrastructure concern that no handler should be able
/// to see or depend on.
///
/// THE TWO METHODS USE TWO DIFFERENT TRANSPORTS, and the split is the interesting part.
///
///   * THE READ IS DIRECT SQL. Section 12's existing-donor check runs on the PUBLIC donation
///     path, before a donor has committed to anything, and it must be fast and must not depend
///     on another service being up. It reads two columns from one indexed row.
///
///   * THE WRITE IS HTTP. Creating a user means allocating a code, assigning a default role,
///     minting an invitation token and sending an e-mail - IAM's rules, not ours. Writing
///     iam_users from here would be a second service quietly reimplementing all of that, and
///     the first rule change in IAM would leave PAY creating subtly broken accounts.
///
/// A FAILURE ON THE WRITE PATH IS NOT AN ERROR TO THE CALLER. The money is already taken by the
/// time this runs; an invitation that could not be sent is a follow-up task, not a reason to
/// reject a gift that succeeded. Every failure comes back as a result, never an exception.
/// </summary>
public interface IIdentityAccountService
{
    /// <summary>
    /// Whether this e-mail already has a user account in this Organisation.
    ///
    /// SCOPED BY ORGANISATION, always. The same person may hold an account with one charity on
    /// the platform and none with another, and answering globally would tell charity A about
    /// charity B's people.
    /// </summary>
    Task<DonorAccountSummary?> FindDonorAccountAsync(
        Guid tenantId, string normalisedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Section 17: creates the donor's account in IAM and sends the activation invitation.
    ///
    /// NO PASSWORD IS SET OR TRANSMITTED. The account is created in an invited state and the
    /// donor chooses their own password through the activation link.
    /// </summary>
    Task<DonorAccountResult> CreateDonorAccountAndInviteAsync(
        CreateDonorAccountRequest request, CancellationToken cancellationToken);
}

/// <summary>What PAY needs to know about an existing IAM account.</summary>
public sealed record DonorAccountSummary(Guid UserId, bool IsActive, string Status);
