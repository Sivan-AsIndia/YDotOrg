using System.Text;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Governance.Mappings;

/// <summary>
/// Shared presentation rules for the governance slice.
///
/// These live here rather than inside a handler because BOTH sides need them: the command
/// handler returns permitted actions after a change, and the read service returns them on
/// every list and detail row. Two copies would drift, and the drift would show up as a button
/// the screen offers and the API then refuses.
/// </summary>
public static class GovernanceMappingConfig
{
    /// <summary>
    /// What an access request STATE allows, for the caller.
    ///
    /// The caller matters here in a way it usually does not: the person who raised a request
    /// may edit or withdraw it but must never decide it, and everybody else sees the opposite.
    /// Passing <see cref="Guid.Empty"/> means "nobody in particular", which yields the
    /// state-only actions.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(AccessRequest request, Guid callerId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isOwnRequest = request.RequestedByUserId == callerId;
        var isSubject = request.RequestedForUserId == callerId;

        return request.Status switch
        {
            AccessRequestStatus.Draft when isOwnRequest => ["View", "Edit", "Submit", "Withdraw"],
            AccessRequestStatus.Draft => ["View"],

            AccessRequestStatus.Submitted when isOwnRequest => ["View", "Withdraw"],

            // Maker and checker: the requester and the subject are both excluded from
            // deciding, so neither is offered a button that would be refused.
            AccessRequestStatus.Submitted when !isSubject => ["View", "Approve", "Reject"],
            AccessRequestStatus.Submitted => ["View"],

            _ => ["View"]
        };
    }

    /// <summary>What an access review state allows.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(AccessReview review, Guid callerId)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (!review.IsOpen)
        {
            return ["View"];
        }

        // Only the assigned reviewer decides. Anybody else with the permission can still see
        // it and cancel it, which is what a supervisor needs.
        return review.ReviewerUserId == callerId
            ? ["View", "Decide", "Cancel"]
            : ["View", "Cancel"];
    }

    /// <summary>What an identifier-change request state allows.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(LoginIdentifierChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Status switch
        {
            LoginIdentifierChangeStatus.Draft => ["View", "Submit", "Cancel"],
            LoginIdentifierChangeStatus.PendingVerification => ["View", "Verify", "Resend", "Cancel"],
            LoginIdentifierChangeStatus.PendingApproval => ["View", "Approve", "Reject", "Cancel"],
            LoginIdentifierChangeStatus.Approved => ["View", "Apply", "Cancel"],
            _ => ["View"]
        };
    }

    /// <summary>What a review campaign state allows.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(AccessReviewCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        return campaign.Status switch
        {
            AccessReviewCampaignStatus.Draft => ["View", "Edit", "Start", "Cancel"],
            AccessReviewCampaignStatus.Active => ["View", "Close", "Cancel"],
            _ => ["View"]
        };
    }

    /// <summary>
    /// "PendingVerification" becomes "Pending verification".
    ///
    /// Done on the server so every screen shows the same wording, rather than each client
    /// inventing its own mapping from an enum name.
    /// </summary>
    public static string Humanise(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);
        builder.Append(value[0]);

        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(value[index]));
            }
            else
            {
                builder.Append(value[index]);
            }
        }

        return builder.ToString();
    }
}
