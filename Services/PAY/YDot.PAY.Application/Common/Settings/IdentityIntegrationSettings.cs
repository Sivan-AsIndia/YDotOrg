namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// How PAY reaches IAM to create a donor's account.
///
/// SECTION 17 IS OPTIONAL BY DESIGN, and this class is where that is expressed. A charity may
/// take donations without offering donors a portal account at all, and an installation with no
/// IAM integration configured must still be able to accept money - so <see cref="Enabled"/>
/// defaults to false and the donation path simply records that no account was created.
///
/// THE CREDENTIAL IS A SERVICE ACCOUNT'S, and it needs exactly one permission: iam.users.create.
/// It should not be a person's login, and it should not hold anything more - a compromised PAY
/// process should be able to invite a donor and nothing else.
/// </summary>
public sealed class IdentityIntegrationSettings
{
    public const string SectionName = "IdentityIntegration";

    /// <summary>
    /// Whether to attempt account creation at all.
    ///
    /// FALSE BY DEFAULT. An unconfigured integration that tried anyway would add a failed HTTP
    /// call and a timeout to every successful donation, which is a slow checkout for no benefit.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>IAM's base address, for example https://ydots-iam-api:8080.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The service account PAY signs in as.
    ///
    /// NOT STORED IN appsettings IN ANY REAL ENVIRONMENT - it comes from the secret store or the
    /// environment, like the database password. The property exists so binding works; what fills
    /// it is a deployment concern.
    /// </summary>
    public string ServiceAccountUsername { get; set; } = string.Empty;

    public string ServiceAccountPassword { get; set; } = string.Empty;

    /// <summary>
    /// How long to wait for IAM before giving up.
    ///
    /// DELIBERATELY SHORT. This runs after the money has been captured, so a slow IAM must not
    /// hold the donor on a spinner wondering whether their payment worked. Ten seconds is longer
    /// than a healthy call and short enough that a sick one is abandoned rather than endured.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// The account category donor logins are created under.
    ///
    /// Kept configurable because it decides which menu the donor sees when they sign in, and an
    /// installation may have named its portal category differently.
    /// </summary>
    public string DonorAccountCategory { get; set; } = "DonorPortal";
}
