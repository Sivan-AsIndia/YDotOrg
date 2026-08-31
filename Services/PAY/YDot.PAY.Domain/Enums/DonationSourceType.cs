namespace YDot.PAY.Domain.Enums;

/// <summary>
/// How the donor arrived, from section 27 of the module brief.
///
/// THIS IS WHAT MAKES ATTRIBUTION ANSWERABLE. Section 22 requires every entry channel to reuse
/// the same payment decision, which means the channel itself has to be recorded on the intent -
/// otherwise "where did this donation come from?" has no answer once the money is in.
/// </summary>
public enum DonationSourceType
{
    /// <summary>A Fundraiser captured the lead and a Lead Owner worked it.</summary>
    FundraiserLead = 0,

    /// <summary>Scanned from a printed campaign QR code.</summary>
    QrCode = 1,

    /// <summary>The organisation's own website donation page.</summary>
    Website = 2,

    /// <summary>A direct donation link, with no campaign tracking attached.</summary>
    DirectLink = 3,

    /// <summary>A link in an e-mail.</summary>
    Email = 4,

    /// <summary>A social media post or profile link.</summary>
    Social = 5,

    /// <summary>A campaign tracking link, which carries full UTM attribution.</summary>
    CampaignLink = 6,

    /// <summary>Recorded by staff on the donor's behalf - a cheque, a bank transfer, cash.</summary>
    OfflineEntry = 7
}
