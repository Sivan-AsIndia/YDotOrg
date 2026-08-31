using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Services;

/// <summary>
/// Enrolling a second factor.
///
/// WHY THIS IS A SERVICE AND NOT A HANDLER. Enrolment happens in two places that look nothing
/// alike from the outside:
///
///   • the security page, where somebody already signed in adds a factor; and
///   • the activation screen, where somebody holding an invitation adds one BEFORE they have a
///     session at all.
///
/// The rules are identical in both — generate the secret once, create the method Pending, prove
/// it with a real code, only then make it usable — and they are exactly the rules nobody should
/// be re-implementing. The difference between the two callers is only *how the user was
/// identified*: a token in one case, an invitation in the other. So the caller resolves the
/// user; everything after that happens here, once.
///
/// THE PENDING STAGE IS THE POINT. A method that has never produced a working code is not a
/// second factor, it is a way to lock yourself out — which matters most on the activation path,
/// where the very next thing the person does is sign in with it.
/// </summary>
public interface IMfaEnrolmentService
{
    /// <summary>
    /// Starts an enrolment and returns everything the setup panel needs.
    ///
    /// THE SHARED SECRET COMES BACK EXACTLY ONCE, here, so it can be scanned or typed. It is
    /// never readable again through any endpoint.
    ///
    /// The caller is responsible for saving: this method stages the changes and issues the
    /// confirmation code where one is needed, but does not commit, so an enrolment and whatever
    /// else the calling flow is doing land in the same transaction.
    /// </summary>
    Task<Result<MfaEnrolmentResponse>> BeginAsync(
        User user,
        Tenant? tenant,
        BusinessUnit businessUnit,
        MfaMethodType methodType,
        string? label,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirms an enrolment by checking a code the new method produced.
    ///
    /// Only on success does the method become Active and MFA become enabled on the account. The
    /// first confirmed method is made primary automatically, so there is always something to
    /// challenge without the person having to nominate one.
    /// </summary>
    Task<Result<MfaMethod>> ConfirmAsync(
        User user,
        Guid methodId,
        string code,
        CancellationToken cancellationToken);
}
