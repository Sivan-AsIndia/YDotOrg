namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Where the browser client lives, and which origins may call this API.
///
/// AllowCredentials cannot be combined with a wildcard origin, and the refresh cookie needs
/// credentials, so the origins are listed explicitly rather than opened up.
/// </summary>
public sealed class ClientAppSettings
{
    public const string SectionName = "ClientAppSettings";

    /// <summary>Root of the Angular app, used to build links in e-mails.</summary>
    public string BaseUrl { get; set; } = "http://localhost:6701";

    /// <summary>Browser origins permitted by CORS.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    // ---- Client paths that e-mail links point at. Kept here so a route change is a
    // ---- configuration edit rather than a string hunt through the notification service.
    public string SignInPath { get; set; } = "/auth/sign-in";

    public string InvitationPath { get; set; } = "/auth/invitation";

    public string ResetPasswordPath { get; set; } = "/auth/reset-password";

    public string EmailVerifyPath { get; set; } = "/auth/email-verify";

    public string OrganisationOnboardingPath { get; set; } = "/app/administration/organisation/details";

    /// <summary>
    /// Where an access-request notification sends the approver.
    ///
    /// THESE TWO WERE BUILT INLINE in the governance handlers, which is what this class exists to
    /// prevent: a route rename would have left the e-mail pointing at a page that no longer exists,
    /// and nothing would have failed to build. The reminder still arrives, the approver still
    /// clicks, and they land on a 404 while an access request waits.
    /// </summary>
    public string AccessRequestPath { get; set; } = "/app/administration/access/access-request-and-approval";

    /// <summary>Where an access-review reminder sends the reviewer.</summary>
    public string AccessReviewPath { get; set; } = "/app/administration/access/access-review-campaign";
}
