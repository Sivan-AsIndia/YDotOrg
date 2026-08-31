namespace YDots.DON.Domain.Enums;

/// <summary>What kind of conversation a DonorInteraction row records.</summary>
public enum InteractionType
{
    Call = 1,
    Email = 2,
    Sms = 3,
    WhatsApp = 4,
    Meeting = 5,
    Note = 6,
    Visit = 7
}
