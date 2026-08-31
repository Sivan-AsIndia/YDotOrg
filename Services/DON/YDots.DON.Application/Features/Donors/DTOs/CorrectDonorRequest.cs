namespace YDots.DON.Application.Features.Donors.DTOs;

/// <summary>
/// SCR-DON-003 Correct body. Every field is optional: send only what actually changed and the
/// rest is left alone. The correction reason is the one thing that is always required, because
/// a correction without a recorded why is not a correction, it is an untracked edit.
/// </summary>
public sealed class CorrectDonorRequest
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? OrganisationName { get; set; }

    /// <summary>
    /// Contact details, which Donor 360 offers on its correction form.
    ///
    /// THEY WERE MISSING, and the workspace sent them anyway: the correction call posts
    /// primaryEmail and primaryPhone, System.Text.Json discarded both as unknown members, and
    /// the screen reported the correction as saved. Correcting a mistyped e-mail on a donor
    /// record - the single most ordinary correction there is - did nothing at all.
    /// </summary>
    public string? PrimaryEmail { get; set; }

    public string? PrimaryPhone { get; set; }

    /// <summary>
    /// Who is accountable for the relationship.
    ///
    /// The id is what the record stores and every ownership query matches on; the name travels
    /// with it so the grid can print an owner without a second call to IAM. Send neither and the
    /// existing owner is left alone.
    /// </summary>
    public Guid? RelationshipOwnerUserId { get; set; }

    public string? RelationshipOwnerName { get; set; }

    public string? PreferredLanguage { get; set; }

    public bool? DoNotContact { get; set; }

    public string? Notes { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string CorrectionReason { get; set; } = string.Empty;

    public long ExpectedVersion { get; set; }
}
