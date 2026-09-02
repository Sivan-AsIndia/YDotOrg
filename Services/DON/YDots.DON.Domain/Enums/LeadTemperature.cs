namespace YDots.DON.Domain.Enums;

/// <summary>
/// How warm a lead is, as the Lead Work Queue and My Leads screens show it.
///
/// TEMPERATURE AND DONATION POTENTIAL REPLACE FORMAL QUALIFICATION in this module. The older
/// model asked whether a lead was "qualified" - one yes/no gate that a fundraiser had to argue
/// their way through - and the operational flow replaced it with two independent readings: how
/// engaged this person is now, and how much they might give. A lead can be Hot and Low, or Cold
/// and High, and those are genuinely different pieces of work; one combined score hid that.
///
/// SET BY THE PERSON WORKING THE LEAD, not computed. It is a judgement recorded after a
/// conversation, which is why it is stored rather than derived - unlike lead health, which is
/// arithmetic over the record and is therefore computed fresh on every read.
/// </summary>
public enum LeadTemperature
{
    Cold = 1,
    Warm = 2,
    Hot = 3
}
