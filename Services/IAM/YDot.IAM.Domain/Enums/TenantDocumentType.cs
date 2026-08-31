namespace YDot.IAM.Domain.Enums;

/// <summary>
/// The documents a TenantAdmin uploads before submitting the Organisation for approval.
/// <see cref="Other"/> exists so an unusual jurisdiction does not block onboarding.
/// </summary>
public enum TenantDocumentType
{
    RegistrationCertificate = 0,
    TaxExemptionCertificate = 1,
    PanCard = 2,
    GstCertificate = 3,
    AddressProof = 4,
    BankProof = 5,
    TrustDeed = 6,
    AnnualReport = 7,
    AuthorisedSignatoryProof = 8,
    Logo = 9,
    Other = 10
}
