namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The maximum stored length of each free-text campaign field.
///
/// WHY THIS EXISTS AS A CONSTANT. The same number has to appear in three places - the create
/// validator, the update validator and the EF column - and when it drifted between them the
/// symptom was a campaign that could be typed but not saved. The wizard's counter said the
/// public description was well inside its limit, the create was refused with "Some of the
/// details are not valid", and nothing on the screen named the field.
///
/// THESE ARE MARKUP LENGTHS, NOT READING LENGTHS, and that distinction is the bug they were
/// sized wrongly for. Public description and Terms and notice are authored in a rich-text
/// editor: the client counts the characters a person can see - its own caps are 2,000 and
/// 20,000 - and then sends the editor's HTML, which is the same words wrapped in paragraph,
/// list and span tags. Pasted text carries the source's inline styling too, so the markup can
/// run to several times the text inside it. A limit set to the reading length therefore refuses
/// content that is comfortably within the limit the person was shown.
///
/// SO EACH IS THE CLIENT'S VISIBLE CAP WITH ROOM FOR THE TAGS AROUND IT. Purpose is the
/// exception and stays as it was: it is sent as plain text, so its limit and the wizard's
/// counter are measuring the same thing.
/// </summary>
public static class CampaignFieldLimits
{
    /// <summary>Plain text on both sides - the wizard sends <c>innerText</c>, not markup.</summary>
    public const int Purpose = 1000;

    /// <summary>Rich text. The wizard shows a 2,000-character counter; this holds its markup.</summary>
    public const int PublicDescription = 8000;

    /// <summary>Rich text. The wizard shows a 20,000-character counter; this holds its markup.</summary>
    public const int TermsAndNotice = 80000;
}
