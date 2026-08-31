using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for IAM, and also its unit of work.
///
/// IT IS AN IdentityDbContext, NOT A PLAIN DbContext. That is what makes the seven
/// IdentityCore tables real:
///
/// <code>
/// iam_users         &lt;- User            : IdentityUser&lt;Guid&gt;
/// iam_roles         &lt;- Role            : IdentityRole&lt;Guid&gt;
/// iam_user_roles    &lt;- UserRole        : IdentityUserRole&lt;Guid&gt;
/// iam_user_claims   &lt;- UserClaimEntry  : IdentityUserClaim&lt;Guid&gt;
/// iam_role_claims   &lt;- RoleClaimEntry  : IdentityRoleClaim&lt;Guid&gt;
/// iam_user_logins   &lt;- UserLogin       : IdentityUserLogin&lt;Guid&gt;
/// iam_user_tokens   &lt;- UserToken       : IdentityUserToken&lt;Guid&gt;
/// </code>
///
/// The generic arguments below name our customised types in place of the framework defaults,
/// which is how the tables gain TenantId, Code, audit columns and the rest while UserManager,
/// RoleManager and SignInManager keep working against them unchanged. The default
/// <c>AspNetUsers</c>-style names are all replaced with <c>iam_*</c> in
/// <c>IdentityConfigurations</c>.
///
/// IT SHARES ONE DATABASE WITH THE OTHER SERVICES, which is why it needs its own migrations
/// history table. With the default <c>__EFMigrationsHistory</c>, IAM and DON would read and
/// write the same list: EF would see the other service migration ids, report them as pending
/// or unknown, and <c>dotnet ef migrations list</c> would be wrong for both. The tables
/// themselves never clash because IAM owns <c>iam_*</c> and DON owns <c>don_*</c>.
///
/// TWO THINGS HAPPEN AUTOMATICALLY ON EVERY SAVE, so no handler has to remember them:
/// audit columns are stamped, and Tenant ownership is stamped from the request context.
/// And on every READ, a global query filter keeps one Organisation out of another.
/// </summary>
public class IamDbContext(
    DbContextOptions<IamDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IdentityDbContext<
        User,             // TUser
        Role,             // TRole
        Guid,             // TKey
        UserClaimEntry,   // TUserClaim
        UserRole,         // TUserRole
        UserLogin,        // TUserLogin
        RoleClaimEntry,   // TRoleClaim
        UserToken>(options), IUnitOfWork
{
    /// <summary>
    /// Exposed so the query filters can read the current Organisation, and so the
    /// configurations can build the filter expressions against it.
    /// </summary>
    internal ITenantContext TenantContext => tenantContext;

    // ---- Tenancy: the root of the model -------------------------------------------------

    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    public DbSet<TenantDocument> TenantDocuments => Set<TenantDocument>();

    public DbSet<TenantDocumentSubmission> TenantDocumentSubmissions => Set<TenantDocumentSubmission>();

    public DbSet<TenantStatusHistory> TenantStatusHistory => Set<TenantStatusHistory>();

    // ---- Authorisation ----------------------------------------------------------------------

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleIncompatibility> RoleIncompatibilities => Set<RoleIncompatibility>();

    public DbSet<UserDataScope> UserDataScopes => Set<UserDataScope>();

    // ---- Navigation -----------------------------------------------------------------------------

    public DbSet<MenuDefinition> MenuDefinitions => Set<MenuDefinition>();

    public DbSet<TenantMenu> TenantMenus => Set<TenantMenu>();

    public DbSet<RoleMenu> RoleMenus => Set<RoleMenu>();

    // ---- Organisation structure -------------------------------------------------------------------

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<OrganisationUnit> OrganisationUnits => Set<OrganisationUnit>();

    // ---- Sessions and credentials -------------------------------------------------------------------

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    public DbSet<RecoveryToken> RecoveryTokens => Set<RecoveryToken>();

    public DbSet<SignInAttempt> SignInAttempts => Set<SignInAttempt>();

    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();

    // ---- MFA --------------------------------------------------------------------------------------------

    public DbSet<MfaMethod> MfaMethods => Set<MfaMethod>();

    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    // ---- Governance ---------------------------------------------------------------------------------------

    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();

    public DbSet<AccessReview> AccessReviews => Set<AccessReview>();

    public DbSet<AccessReviewCampaign> AccessReviewCampaigns => Set<AccessReviewCampaign>();

    public DbSet<LoginIdentifierChangeRequest> LoginIdentifierChangeRequests =>
        Set<LoginIdentifierChangeRequest>();

    public DbSet<BulkOperation> BulkOperations => Set<BulkOperation>();

    public DbSet<BulkOperationItem> BulkOperationItems => Set<BulkOperationItem>();

    public DbSet<ProtectedActionDraft> ProtectedActionDrafts => Set<ProtectedActionDraft>();

    // ---- Global masters: migrated in from the standalone GlobalMaster service ------------------------------
    //
    // These six are ITenantScoped rather than ITenantOwned, so the filter they receive is
    // "mine OR platform" instead of "mine". That is what lets one seeded ISO catalogue serve
    // every Organisation while each still keeps its own private additions to itself. See
    // GlobalMasterEntity for the full argument.

    public DbSet<Country> Countries => Set<Country>();

    public DbSet<StateProvince> StateProvinces => Set<StateProvince>();

    public DbSet<City> Cities => Set<City>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<TimeZoneDefinition> TimeZones => Set<TimeZoneDefinition>();

    /// <summary>
    /// The country-to-time-zone links.
    ///
    /// Exposed as its own set rather than reached only through <c>Country.CountryTimeZones</c>
    /// because the read side queries it directly: "the zones for country X" is one join off this
    /// table, whereas loading a tracked Country aggregate to answer a dropdown is not.
    /// </summary>
    public DbSet<CountryTimeZone> CountryTimeZones => Set<CountryTimeZone>();

    /// <summary>
    /// The language catalogue — the sixth master, and the last field on the setup wizard, user
    /// creation and lead capture that was still driven by literals in the browser bundle.
    /// </summary>
    public DbSet<Language> Languages => Set<Language>();

    /// <summary>
    /// The country-to-language links, exposed directly for the same reason
    /// <see cref="CountryTimeZones"/> is: "the languages for country X" is one join off this
    /// table, where loading a tracked Country aggregate to fill a dropdown is not.
    /// </summary>
    public DbSet<CountryLanguage> CountryLanguages => Set<CountryLanguage>();

    // ---- Platform ------------------------------------------------------------------------------------------

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MUST run first. It builds the IdentityCore model — keys, the join-table
        // relationships, the default indexes — and everything below either extends or
        // deliberately replaces what it sets up.
        base.OnModelCreating(modelBuilder);

        // One IEntityTypeConfiguration per table, all in Persistence/Configurations.
        // IdentityConfigurations is the one that renames the seven Identity tables and
        // swaps their global unique indexes for Tenant-scoped ones.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IamDbContext).Assembly);

        // Applied last, because a filter has to be attached after the entity is fully
        // configured or EF quietly drops it.
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// THE READ-SIDE HALF OF THE ISOLATION.
    ///
    /// Every entity marked <see cref="ITenantOwned"/> gets a filter of
    /// "TenantId == current", and every entity marked <see cref="ITenantScoped"/> gets
    /// "TenantId == current OR TenantId IS NULL" — the second so the global SuperAdmin user
    /// and the platform roles stay reachable while an Organisation is selected.
    ///
    /// The filters read <c>tenantContext</c> through a captured reference rather than a
    /// constant, so the same compiled model serves every request and each one filters to its
    /// own Organisation.
    ///
    /// THIS IS DEFENCE IN DEPTH, NOT THE ONLY CONTROL. A filter stops a forgotten Where
    /// clause leaking rows; it does not authorise anything, and <c>IgnoreQueryFilters()</c>
    /// walks straight past it. Handlers still check, and the few legitimate global reads go
    /// through the explicit path in <c>GlobalQueryExtensions</c> rather than reaching for
    /// IgnoreQueryFilters ad hoc.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Owned types and shadow entities have no filterable root of their own.
            if (entityType.IsOwned() || clrType.IsAbstract)
            {
                continue;
            }

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                TenantQueryFilter.ApplyStrict(modelBuilder, clrType, this);
            }
            else if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                TenantQueryFilter.ApplyScoped(modelBuilder, clrType, this);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantOwnership();
        StampAuditColumns();
        return base.SaveChangesAsync(cancellationToken);
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);

    /// <summary>
    /// THE WRITE-SIDE HALF OF THE ISOLATION.
    ///
    /// A new Tenant-owned row gets its TenantId and BusinessUnitId from the request context,
    /// never from the caller. That is section 47 of the brief made structural: even if a
    /// request body carried a TenantId and a handler foolishly copied it onto the entity, it
    /// is overwritten here before it reaches the database.
    ///
    /// An existing row can never have its owner changed. Silently reverting the attempt
    /// rather than throwing keeps a legitimate save working while making the reassignment
    /// impossible; the audit trail is where a deliberate attempt shows up.
    /// </summary>
    private void StampTenantOwnership()
    {
        var tenantId = tenantContext.TenantId;
        var businessUnitId = tenantContext.BusinessUnitId;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.TenantId == Guid.Empty && tenantId.HasValue)
                    {
                        entry.Entity.TenantId = tenantId.Value;
                    }

                    if (entry.Entity.BusinessUnitId == Guid.Empty && businessUnitId != Guid.Empty)
                    {
                        entry.Entity.BusinessUnitId = businessUnitId;
                    }

                    break;

                case EntityState.Modified:
                    // Ownership is immutable. Revert any attempt to move a row between
                    // Organisations back to what the database already holds.
                    entry.Property(nameof(ITenantOwned.TenantId)).IsModified = false;
                    entry.Property(nameof(ITenantOwned.BusinessUnitId)).IsModified = false;
                    break;
            }
        }

        // ITenantScoped rows (User, Role and the Identity join tables) are stamped only when
        // the caller left it unset AND an Organisation is resolved. A null TenantId here is
        // meaningful — it is what makes somebody a global user — so it is never filled in by
        // default.
        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added
                && entry.Entity.BusinessUnitId == Guid.Empty
                && businessUnitId != Guid.Empty)
            {
                entry.Entity.BusinessUnitId = businessUnitId;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(ITenantScoped.TenantId)).IsModified = false;
                entry.Property(nameof(ITenantScoped.BusinessUnitId)).IsModified = false;
            }

            // TenantKey is derived, never authored. Recomputing it on every add AND every
            // modify is what guarantees it cannot drift out of step with TenantId — and a
            // drift here would be serious, because TenantKey is what the composite foreign
            // keys on iam_user_roles actually enforce against.
            //
            // Guid.Empty stands for "the platform", so the global SuperAdmin and the platform
            // roles share one scope and can only ever be paired with each other.
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.TenantKey = entry.Entity.TenantId ?? Guid.Empty;
            }
        }
    }

    /// <summary>
    /// Fills in who did it and when, and moves the concurrency version forward.
    ///
    /// Keyed on <see cref="IAuditable"/> rather than on a base class, which is what lets the
    /// Identity-derived entities — <c>User</c>, <c>Role</c>, <c>UserRole</c>, which cannot
    /// extend <c>AuditEntity</c> because <c>IdentityUser&lt;Guid&gt;</c> already supplies
    /// <c>Id</c> — be stamped by exactly the same loop as everything else.
    /// </summary>
    private void StampAuditColumns()
    {
        var now = clock.UtcNow;
        var actorId = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedByUserId = entry.Entity.CreatedByUserId == Guid.Empty
                        ? actorId
                        : entry.Entity.CreatedByUserId;
                    entry.Entity.Version = 1;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = actorId;
                    entry.Entity.Version += 1;
                    break;
            }
        }
    }
}
