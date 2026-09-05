using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Multitenancy;

/// <summary>
/// Fills in the request Organisation, from the token where there is one and from the donation
/// reference where there is not.
///
/// WHERE IT SITS IN THE PIPELINE IS LOAD-BEARING. It must run AFTER <c>UseAuthentication</c>,
/// because it reads claims that only exist once the token is validated, and BEFORE the endpoint,
/// because the query filters read what it sets. In the wrong order it resolves nothing, every
/// filter matches nothing, and every list comes back empty with no error anywhere.
///
/// THE PUBLIC FALLBACK IS WHAT MAKES THIS DIFFERENT FROM CAM'S. Sections 19 to 22 describe a
/// donor with a QR code and no account: their request carries an intent reference in the route
/// and nothing else. Without a fallback every public donation endpoint would run with no
/// Organisation, the filters would return nothing, and the donor would be told their own
/// donation does not exist.
///
/// THE FALLBACK IS DELIBERATELY NARROW - three conditions, all required:
///
///   1. THE REQUEST IS UNAUTHENTICATED. A signed-in caller always keeps their own token's
///      Organisation, so this can never be used to move sideways into another one.
///   2. THE PATH IS ONE OF THE PUBLIC DONATION ROUTES. Nothing else consults it.
///   3. THE REFERENCE RESOLVES TO EXACTLY ONE ROW. It is twelve unguessable characters with a
///      unique index behind it, so the caller is NAMING a record that already belongs to an
///      Organisation, not CHOOSING which Organisation to act in.
///
/// A reference that does not resolve leaves the context empty, and the endpoint answers "not
/// found" - which is also the answer somebody guessing references gets, and tells them nothing.
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    /// <summary>
    /// The route segments whose next segment is a donation reference.
    ///
    /// A WHITELIST RATHER THAN A PATTERN, because the consequence of matching too broadly is
    /// that some other endpoint starts silently resolving an Organisation from a caller-supplied
    /// string.
    /// </summary>
    private static readonly string[] PublicIntentRoutePrefixes =
    [
        "/api/public/donations/",
        "/api/public/donation-intents/",
        "/api/public/payments/"
    ];

    public async Task InvokeAsync(
        HttpContext context, TenantContext tenantContext, PaymentDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(dbContext);

        var principal = context.User;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            ResolveFromToken(context, tenantContext, principal);
        }
        else
        {
            await ResolveFromPublicReferenceAsync(context, tenantContext, dbContext);
        }

        await next(context);
    }

    /// <summary>
    /// The ordinary path: the Organisation comes from claims we signed.
    ///
    /// NOTHING THE CALLER CAN SET IS CONSULTED - no header, no query string, no body. That is
    /// the whole reason an authenticated caller cannot choose which Organisation they operate in.
    /// </summary>
    private void ResolveFromToken(
        HttpContext context, TenantContext tenantContext, ClaimsPrincipal principal)
    {
        // tenant_id first, organisation_id as the fallback. IAM writes the same value into both -
        // the second for the services that predate the tenancy vocabulary - so reading either
        // works, and reading both means PAY keeps working whichever IAM version is deployed.
        var tenantId = ParseGuid(
            Find(principal, ClaimTypeNames.TenantId) ?? Find(principal, ClaimTypeNames.OrganisationId));

        var businessUnitId = ParseGuid(Find(principal, ClaimTypeNames.BusinessUnitId)) ?? Guid.Empty;

        var isSuperAdmin = string.Equals(
            Find(principal, ClaimTypeNames.IsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

        tenantContext.Set(
            tenantId,
            businessUnitId,
            Find(principal, ClaimTypeNames.TenantCode),
            Find(principal, ClaimTypeNames.TenantName),
            isSuperAdmin);

        // A SuperAdmin who has not selected an Organisation is the ordinary case for platform
        // work. A TENANT user with no tenant_id is a token IAM should never have issued, and on a
        // money service that is worth knowing about immediately.
        if (tenantId is null && !isSuperAdmin)
        {
            logger.LogWarning(
                "An authenticated non-super-admin request carried no organisation claim. Every "
                + "organisation-scoped read will return empty. Correlation {CorrelationId}.",
                context.TraceIdentifier);
        }
    }

    /// <summary>
    /// The public path: the Organisation comes from the record the reference names.
    ///
    /// THE LOOKUP IGNORES THE QUERY FILTER, necessarily - there is no Organisation yet, so a
    /// filtered query would return nothing and the resolution could never bootstrap. It selects
    /// two columns from one indexed row, so the cost is negligible even though it runs before
    /// every public donation request.
    /// </summary>
    private async Task ResolveFromPublicReferenceAsync(
        HttpContext context, TenantContext tenantContext, PaymentDbContext dbContext)
    {
        var reference = ExtractPublicReference(context.Request.Path);

        if (string.IsNullOrWhiteSpace(reference))
        {
            // NO REFERENCE YET, WHICH IS THE NORMAL CASE FOR THE FIRST CALL. /initiate creates the
            // intent, so there is nothing to look one up by, and this used to return here and
            // leave the context empty - which meant the endpoint answered
            // TENANT_SELECTION_REQUIRED and A DONATION COULD NEVER BE STARTED AT ALL. Every later
            // call in the flow works, because by then a reference exists; only the first one, the
            // one that matters, could not.
            //
            // The host is the remaining signal, and the right one: a donor arrives at
            // hope.ngoplanet.com because that is the charity they mean to give to. It is the same
            // fact IAM resolves a sign-in from, read from the same table.
            await ResolveFromHostAsync(context, tenantContext, dbContext);

            // THE HOST IS NOT ALWAYS ENOUGH, and where it is not, the campaign is.
            //
            // The donation page is served from whatever origin the app runs on. In the shipped
            // container set that is `localhost:6700`, and `localhost` is a PLATFORM host with no
            // row in iam_tenant_domains - so the host resolved nothing and /initiate answered
            // TENANT_SELECTION_REQUIRED for every donor who reached the page by the documented
            // URL. Only somebody who had already edited their hosts file to reach
            // `ten1.localhost` could give at all. The same gap opens in production behind any
            // front door that terminates on a shared hostname.
            //
            // The body names a campaign or a tracking reference, and each of those already
            // belongs to exactly one Organisation. That is the SAME argument condition 3 above
            // makes for the intent reference: the caller is NAMING a record, not CHOOSING an
            // Organisation. A campaign id that resolves to nothing leaves the context empty and
            // the endpoint refuses, exactly as before.
            if (!tenantContext.HasTenant)
            {
                await ResolveFromInitiationBodyAsync(context, tenantContext, dbContext);
            }

            return;
        }

        var owner = await dbContext.DonationIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(intent => intent.IntentReference == reference)
            .Select(intent => new { intent.TenantId, intent.BusinessUnitId })
            .FirstOrDefaultAsync(context.RequestAborted);

        if (owner is null)
        {
            // Left unresolved on purpose. The endpoint will answer "not found", which is also
            // what somebody guessing references gets - and tells them nothing about whether the
            // reference exists in another charity's books.
            return;
        }

        tenantContext.SetFromPublicContext(owner.TenantId, owner.BusinessUnitId, tenantCode: null);

        logger.LogDebug(
            "Resolved organisation {TenantId} from a public donation reference. Correlation {CorrelationId}.",
            owner.TenantId,
            context.TraceIdentifier);
    }

    /// <summary>
    /// The Organisation that owns this host name.
    ///
    /// ONLY FOR ANONYMOUS PUBLIC ROUTES. An authenticated caller is resolved from claims we signed
    /// and never from anything they can set - which includes the Host header - so this is reached
    /// only when there is no token at all. A donor has no token by definition.
    ///
    /// The host is not a secret and cannot be used to reach another Organisation's data: it names
    /// which charity the donation is FOR, and the donor typed it themselves.
    /// </summary>
    private async Task ResolveFromHostAsync(
        HttpContext context, TenantContext tenantContext, PaymentDbContext dbContext)
    {
        var host = context.Request.Host.Host;

        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        var normalised = host.Trim().ToLowerInvariant();

        // Read straight from the identity tables over the shared database, the same seam CAM uses
        // to read payments. PAY owns no copy of the domain list and must not invent one: two
        // places recording which host belongs to which charity is one place too many when the
        // answer decides where money is credited.
        var owner = await dbContext.Database
            .SqlQuery<TenantHostRow>($"""
                SELECT tenant_id, business_unit_id
                FROM iam_tenant_domains
                WHERE lower(host_name) = {normalised}
                LIMIT 1
                """)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (owner is null)
        {
            logger.LogDebug(
                "No organisation is registered on host {Host}. Correlation {CorrelationId}.",
                normalised, context.TraceIdentifier);

            return;
        }

        tenantContext.SetFromPublicContext(owner.TenantId, owner.BusinessUnitId, tenantCode: null);

        logger.LogDebug(
            "Resolved organisation {TenantId} from host {Host}. Correlation {CorrelationId}.",
            owner.TenantId, normalised, context.TraceIdentifier);
    }

    /// <summary>
    /// The Organisation that owns the campaign the donor is giving to.
    ///
    /// LAST RESORT AND ANONYMOUS-ONLY. It is reached only from the public branch, only for
    /// /initiate, and only when the host named no Organisation - an authenticated caller never
    /// gets here, so this cannot be used to step sideways out of a token's Organisation.
    ///
    /// It buffers the request body to read two fields and rewinds it, so model binding downstream
    /// still sees a complete stream.
    /// </summary>
    private async Task ResolveFromInitiationBodyAsync(
        HttpContext context, TenantContext tenantContext, PaymentDbContext dbContext)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Path.StartsWithSegments("/api/public/donations/initiate",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Guid? campaignId;
        string? trackingReference;

        try
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            using var document = await JsonDocument.ParseAsync(
                context.Request.Body,
                new JsonDocumentOptions { AllowTrailingCommas = true },
                context.RequestAborted);

            campaignId = TryReadGuid(document.RootElement, "campaignId");
            trackingReference = TryReadString(document.RootElement, "trackingReference");
        }
        catch (JsonException)
        {
            // A body that is not JSON is the endpoint's problem to report, not this one's.
            return;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }
        }

        TenantHostRow? owner = null;

        if (campaignId is not null)
        {
            owner = await dbContext.Database
                .SqlQuery<TenantHostRow>($"""
                    SELECT tenant_id, business_unit_id
                    FROM cam_campaigns
                    WHERE id = {campaignId.Value}
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(context.RequestAborted);
        }

        if (owner is null && !string.IsNullOrWhiteSpace(trackingReference))
        {
            var reference = trackingReference.Trim();

            owner = await dbContext.Database
                .SqlQuery<TenantHostRow>($"""
                    SELECT tenant_id, business_unit_id
                    FROM cam_tracking_assets
                    WHERE tracking_reference = {reference}
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(context.RequestAborted);
        }

        if (owner is null)
        {
            logger.LogDebug(
                "A public donation initiation named no resolvable campaign or tracking reference. "
                + "Correlation {CorrelationId}.",
                context.TraceIdentifier);

            return;
        }

        tenantContext.SetFromPublicContext(owner.TenantId, owner.BusinessUnitId, tenantCode: null);

        logger.LogDebug(
            "Resolved organisation {TenantId} from the campaign named on a donation initiation. "
            + "Correlation {CorrelationId}.",
            owner.TenantId, context.TraceIdentifier);
    }

    private static Guid? TryReadGuid(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static string? TryReadString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record TenantHostRow(Guid TenantId, Guid BusinessUnitId);

    /// <summary>
    /// Pulls the reference out of a public donation route.
    ///
    /// It takes only the segment immediately after a whitelisted prefix, and only when that
    /// segment looks like a reference rather than an action word - so
    /// <c>/api/public/donations/initiate</c> resolves nothing and is handled as the ordinary
    /// anonymous POST it is.
    /// </summary>
    private static string? ExtractPublicReference(PathString path)
    {
        var value = path.Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var prefix in PublicIntentRoutePrefixes)
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = value[prefix.Length..];
            var separator = remainder.IndexOf('/', StringComparison.Ordinal);
            var segment = separator < 0 ? remainder : remainder[..separator];

            // ACTION WORDS ARE NOT REFERENCES, and this has to be checked before the shape test
            // rather than left to it. "initiate" is eight ASCII letters, which is exactly what a
            // reference looks like, so the shape test accepted it: every call to /initiate went
            // looking for a donation intent whose reference was the literal word "initiate",
            // found nothing, and gave up - taking the whole public donation flow with it, since
            // /initiate is the call that starts it.
            return IsAction(segment) || !LooksLikeReference(segment) ? null : segment;
        }

        return null;
    }

    /// <summary>
    /// The verbs that appear where a reference otherwise would.
    ///
    /// Kept as a list rather than inferred, so adding a public action is a deliberate edit here
    /// instead of a silent change in what counts as a reference.
    /// </summary>
    /// <summary>
    /// Segments that are the NAME OF A ROUTE rather than a donation reference.
    ///
    /// "initiate" was here first, and the note on the call site explains why: it is eight ASCII
    /// letters, which is exactly the shape a reference has, so the shape test accepted it and the
    /// call that STARTS a donation went looking for an intent called "initiate".
    ///
    /// "campaigns" is the same trap and was found the same way - nine ASCII letters, accepted as
    /// a reference, no intent found, Organisation left unresolved, and the public donation form's
    /// campaign picker silently empty. A route added under /api/public/donations/ whose next
    /// segment is a literal has to be listed here, and the failure when it is not is quiet: the
    /// endpoint answers 200 with nothing in it.
    /// </summary>
    private static readonly HashSet<string> ActionSegments =
        new(StringComparer.OrdinalIgnoreCase) { "initiate", "campaigns" };

    private static bool IsAction(string segment) => ActionSegments.Contains(segment);

    /// <summary>
    /// Whether a segment is shaped like a reference the generator produces.
    ///
    /// Checked BEFORE going to the database, so an unauthenticated request to a public route
    /// cannot be used to fire an arbitrary query per call.
    /// </summary>
    private static bool LooksLikeReference(string segment) =>
        segment.Length is >= 8 and <= 64
        && segment.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static string? Find(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}
