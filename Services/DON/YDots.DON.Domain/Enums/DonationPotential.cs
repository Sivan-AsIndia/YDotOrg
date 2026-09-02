namespace YDots.DON.Domain.Enums;

/// <summary>
/// How much this lead might realistically give, as the Lead Work Queue shows it.
///
/// THE OTHER HALF OF THE QUALIFICATION REPLACEMENT - see <see cref="LeadTemperature"/>. Kept
/// separate from temperature on purpose: a major donor who has gone quiet is High and Cold, and
/// collapsing the two into one number would rank them alongside a small, enthusiastic giver and
/// tell the fundraiser nothing useful about either.
///
/// A BAND, NOT AN AMOUNT. The figure a lead might give is a guess until a donation exists, and
/// storing a guessed rupee value invites it to be summed into a total somebody then trusts.
/// </summary>
public enum DonationPotential
{
    Low = 1,
    Medium = 2,
    High = 3
}
