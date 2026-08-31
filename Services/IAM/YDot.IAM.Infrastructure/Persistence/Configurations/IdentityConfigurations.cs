using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// THE CUSTOMISATION OF THE ASP.NET CORE IDENTITY TABLES.
///
/// The brief asks for "IdentityCore tables customised based on application needs in
/// PostgreSQL". This file is where that happens. It does three things to the model the
/// framework built:
///
///   1. RENAMES all seven tables from AspNetUsers, AspNetRoles and so on to iam_*, so IAM
///      sits alongside DON in one shared database with no name collisions and an obvious
///      owner for every table.
///
///   2. REPLACES the framework global unique indexes with Tenant-scoped ones. This is the
///      single most important change in the file, and without it the whole multi-tenant
///      model is impossible — see the note on the user configuration below.
///
///   3. ADDS the YDot columns, constraints and relationships: TenantId, Code, audit
///      columns, the concurrency token, and the check constraint that ties a null TenantId
///      to SuperAdmin.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("iam_users");

        builder.HasKey(user => user.Id);

        // ---- THE INDEX SWAP -------------------------------------------------------------
        //
        // IdentityCore creates UNIQUE indexes named "UserNameIndex" on NormalizedUserName
        // and "EmailIndex" on NormalizedEmail. Those are global, and a global unique e-mail
        // is precisely what this system must NOT have: the brief requires that
        // john@gmail.com exist as a separate person in TEN001 and TEN002.
        //
        // So the framework indexes are dropped by name and replaced with composite ones that
        // lead with TenantId. Same protection, one Organisation at a time.
        builder.Metadata.RemoveIndex(builder.Metadata.FindIndex(
            builder.Metadata.FindProperty(nameof(User.NormalizedUserName))!)!);

        builder.Metadata.RemoveIndex(builder.Metadata.FindIndex(
            builder.Metadata.FindProperty(nameof(User.NormalizedEmail))!)!);

        // Unique per Organisation. The filter excludes the global SuperAdmin rows, which
        // carry a null TenantId and cannot participate in a composite unique index anyway —
        // PostgreSQL treats every NULL as distinct, so they would never collide regardless.
        builder.HasIndex(user => new { user.TenantId, user.NormalizedEmail })
            .HasDatabaseName("ix_iam_users_tenant_email")
            .IsUnique()
            .HasFilter(null);

        builder.HasIndex(user => new { user.TenantId, user.NormalizedUserName })
            .HasDatabaseName("ix_iam_users_tenant_username")
            .IsUnique();

        // Every master carries a unique Code, scoped to the Organisation.
        builder.HasIndex(user => new { user.TenantId, user.Code })
            .HasDatabaseName("ix_iam_users_tenant_code")
            .IsUnique();

        // The global root account. A partial unique index over the single row where
        // TenantId IS NULL, so a second SuperAdmin with the same address cannot be created.
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("ix_iam_users_global_email")
            .IsUnique()
            .HasFilter("tenant_id IS NULL");

        // Search and list indexes.
        builder.HasIndex(user => new { user.TenantId, user.Status })
            .HasDatabaseName("ix_iam_users_tenant_status");

        builder.HasIndex(user => user.BusinessUnitId)
            .HasDatabaseName("ix_iam_users_business_unit");

        // ---- Columns ------------------------------------------------------------------------

        builder.Property(user => user.Code).HasMaxLength(50).IsRequired();
        builder.Property(user => user.EmployeeNumber).HasMaxLength(40);
        builder.Property(user => user.FirstName).HasMaxLength(80).IsRequired();
        builder.Property(user => user.MiddleName).HasMaxLength(80);
        builder.Property(user => user.LastName).HasMaxLength(80).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(user => user.Designation).HasMaxLength(120);
        builder.Property(user => user.MobileCountryCode).HasMaxLength(8);
        builder.Property(user => user.MobileNumber).HasMaxLength(20);
        builder.Property(user => user.LockoutReason).HasMaxLength(300);
        builder.Property(user => user.LastLoginIpAddress).HasMaxLength(64);
        builder.Property(user => user.LastFailedLoginIpAddress).HasMaxLength(64);
        builder.Property(user => user.LastLoginUserAgent).HasMaxLength(400);
        builder.Property(user => user.LastLoginBrowser).HasMaxLength(80);
        builder.Property(user => user.LastLoginOperatingSystem).HasMaxLength(80);
        builder.Property(user => user.LastLoginDeviceIdentifier).HasMaxLength(200);
        builder.Property(user => user.PreferredCulture).HasMaxLength(20);
        builder.Property(user => user.TimeZone).HasMaxLength(80);
        builder.Property(user => user.AvatarUrl).HasMaxLength(500);

        // Enums are stored as readable text, not integers. A DBA reading iam_users should
        // not have to consult a C# file to learn what status 3 means.
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(user => user.AccountCategory).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(user => user.MfaRequirement).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(user => user.PrivilegeLevel).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(user => user.EngagementType).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(user => user.CredentialSetupMethod).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(user => user.LastLoginClientType).HasConversion<string>().HasMaxLength(40).IsRequired();

        // Optimistic concurrency. The UPDATE carries id AND version; zero rows affected
        // surfaces as CONCURRENCY_CONFLICT rather than a silent overwrite.
        builder.Property(user => user.Version).IsConcurrencyToken();

        // ---- Relationships ---------------------------------------------------------------------

        builder.HasOne(user => user.Tenant)
            .WithMany()
            .HasForeignKey(user => user.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(user => user.Department)
            .WithMany(department => department.Users)
            .HasForeignKey(user => user.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(user => user.OrganisationUnit)
            .WithMany(unit => unit.Users)
            .HasForeignKey(user => user.OrganisationUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(user => user.Manager)
            .WithMany()
            .HasForeignKey(user => user.ManagerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The principal side of the composite foreign key on iam_user_roles: it lets that
        // table point at "this user, in this Organisation" rather than merely "this user".
        //
        // NOTE IT IS TenantKey, NOT TenantId. EF makes every key property REQUIRED, so an
        // alternate key over the nullable TenantId would silently emit it as NOT NULL and
        // make SuperAdmin unstorable. TenantKey is the non-null mirror that exists precisely
        // to carry this key - see ITenantScoped.TenantKey.
        builder.HasAlternateKey(user => new { user.TenantKey, user.Id })
            .HasName("ak_iam_users_tenant_key_id");

        builder.Property(user => user.TenantKey).IsRequired();

        // ---- Check constraints: the tenancy invariants, enforced where they cannot be forgotten ----

        builder.ToTable(table =>
        {
            // THE CENTRAL INVARIANT. A null TenantId means "belongs to no Organisation", and
            // the only account allowed to be that is the platform root. Without this, an
            // ordinary user given a null TenantId would become visible in every Organisation
            // through the widened query filter on ITenantScoped.
            table.HasCheckConstraint(
                "ck_iam_users_super_admin_has_no_tenant",
                @"(is_super_admin = TRUE AND tenant_id IS NULL)
                  OR (is_super_admin = FALSE AND tenant_id IS NOT NULL)");

            // A manager cannot be their own manager.
            table.HasCheckConstraint(
                "ck_iam_users_manager_not_self",
                "manager_user_id IS NULL OR manager_user_id <> id");

            // The access window has to be a window.
            table.HasCheckConstraint(
                "ck_iam_users_access_window",
                "access_ends_at_utc IS NULL OR access_ends_at_utc > access_starts_at_utc");
        });
    }
}

/// <summary>Customised <c>AspNetRoles</c>.</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("iam_roles");

        builder.HasKey(role => role.Id);

        // Same swap as on the user table, same reason: IdentityCore makes NormalizedName
        // globally unique, and roles have to be per Organisation. Two Organisations may both
        // have a role called Administrator without any relationship between them.
        builder.Metadata.RemoveIndex(builder.Metadata.FindIndex(
            builder.Metadata.FindProperty(nameof(Role.NormalizedName))!)!);

        builder.HasIndex(role => new { role.TenantId, role.NormalizedName })
            .HasDatabaseName("ix_iam_roles_tenant_name")
            .IsUnique();

        builder.HasIndex(role => new { role.TenantId, role.NormalizedCode })
            .HasDatabaseName("ix_iam_roles_tenant_code")
            .IsUnique();

        builder.HasIndex(role => role.BusinessUnitId)
            .HasDatabaseName("ix_iam_roles_business_unit");

        builder.Property(role => role.Code).HasMaxLength(50).IsRequired();
        builder.Property(role => role.NormalizedCode).HasMaxLength(50).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(500);
        builder.Property(role => role.DisplayTag).HasMaxLength(40);

        builder.Property(role => role.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(role => role.RoleType).HasConversion<string>().HasMaxLength(80).IsRequired();

        builder.Property(role => role.Version).IsConcurrencyToken();

        builder.HasOne(role => role.Tenant)
            .WithMany()
            .HasForeignKey(role => role.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Principal side of the other half of the composite foreign key on iam_user_roles.
        // TenantKey rather than TenantId, for the reason given on the user configuration.
        builder.HasAlternateKey(role => new { role.TenantKey, role.Id })
            .HasName("ak_iam_roles_tenant_key_id");

        builder.Property(role => role.TenantKey).IsRequired();

        builder.ToTable(table =>
            // A platform role belongs to no Organisation; every other kind must name one.
            table.HasCheckConstraint(
                "ck_iam_roles_platform_has_no_tenant",
                "(role_type = 'Platform' AND tenant_id IS NULL) OR (role_type <> 'Platform' AND tenant_id IS NOT NULL)"));
    }
}

/// <summary>
/// Customised <c>AspNetUserRoles</c>: the UserRoleMapping the brief describes.
/// </summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("iam_user_roles");

        // Drop the navigation-less foreign keys the base Identity model created; the
        // Tenant-scoped composites declared below replace them. See IdentityModelCleanup.
        builder.RemoveBaseIdentityRelationships();

        // ---- THE KEY CHANGE ------------------------------------------------------------
        //
        // IdentityUserRole has a composite primary key of (UserId, RoleId). That allows one
        // assignment per pair, which is right, but leaves nowhere to record an assignment
        // that was revoked and later granted again — the second insert would collide with
        // the first.
        //
        // Assignments are history here, not just current state, so the table gets its own
        // surrogate key and the "one live assignment per pair" rule moves to a FILTERED
        // unique index over the active rows only.
        builder.HasKey(assignment => assignment.Id);

        builder.HasIndex(assignment => new { assignment.TenantId, assignment.UserId, assignment.RoleId })
            .HasDatabaseName("ix_iam_user_roles_active_unique")
            .IsUnique()
            .HasFilter("status = 'Active'");

        builder.HasIndex(assignment => new { assignment.TenantId, assignment.UserId })
            .HasDatabaseName("ix_iam_user_roles_tenant_user");

        builder.HasIndex(assignment => new { assignment.TenantId, assignment.RoleId })
            .HasDatabaseName("ix_iam_user_roles_tenant_role");

        builder.Property(assignment => assignment.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(assignment => assignment.RevocationReason).HasMaxLength(300);
        builder.Property(assignment => assignment.Justification).HasMaxLength(1000);
        builder.Property(assignment => assignment.Version).IsConcurrencyToken();

        // ---- THE COMPOSITE FOREIGN KEYS ----------------------------------------------------
        //
        // This is what makes cross-Tenant privilege escalation structurally impossible rather
        // than merely unlikely. The foreign key is on (TenantId, UserId), not UserId alone,
        // so the DATABASE refuses a row pairing a user in TEN001 with the TenantId of TEN002.
        // No handler bug can produce one.
        builder.HasOne(assignment => assignment.User)
            .WithMany(user => user.UserRoles)
            .HasForeignKey(assignment => new { assignment.TenantKey, assignment.UserId })
            .HasPrincipalKey(user => new { user.TenantKey, user.Id })
            .HasConstraintName("fk_iam_user_roles_iam_users_tenant_user")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(assignment => assignment.Role)
            .WithMany(role => role.UserRoles)
            .HasForeignKey(assignment => new { assignment.TenantKey, assignment.RoleId })
            .HasPrincipalKey(role => new { role.TenantKey, role.Id })
            .HasConstraintName("fk_iam_user_roles_iam_roles_tenant_role")
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
            table.HasCheckConstraint(
                "ck_iam_user_roles_effective_window",
                "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc"));
    }
}

/// <summary>Customised <c>AspNetUserClaims</c>.</summary>
public sealed class UserClaimConfiguration : IEntityTypeConfiguration<UserClaimEntry>
{
    public void Configure(EntityTypeBuilder<UserClaimEntry> builder)
    {
        builder.ToTable("iam_user_claims");

        builder.RemoveBaseIdentityRelationships();

        builder.HasIndex(claim => new { claim.TenantId, claim.UserId })
            .HasDatabaseName("ix_iam_user_claims_tenant_user");

        builder.Property(claim => claim.ClaimType).HasMaxLength(200).IsRequired();
        builder.Property(claim => claim.ClaimValue).HasMaxLength(1000);
        builder.Property(claim => claim.Justification).HasMaxLength(1000);

        // Named explicitly. ApplyConfigurationsFromAssembly does not guarantee an order, so
        // if this class runs before UserConfiguration the principal table is still the
        // framework default and the generated constraint name comes out as
        // "fk_iam_user_claims_asp_net_users_user_id". Naming it here makes the result
        // independent of that ordering.
        builder.HasOne(claim => claim.User)
            .WithMany(user => user.Claims)
            .HasForeignKey(claim => claim.UserId)
            .HasConstraintName("fk_iam_user_claims_iam_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Customised <c>AspNetRoleClaims</c>.</summary>
public sealed class RoleClaimConfiguration : IEntityTypeConfiguration<RoleClaimEntry>
{
    public void Configure(EntityTypeBuilder<RoleClaimEntry> builder)
    {
        builder.ToTable("iam_role_claims");

        builder.RemoveBaseIdentityRelationships();

        builder.HasIndex(claim => new { claim.TenantId, claim.RoleId })
            .HasDatabaseName("ix_iam_role_claims_tenant_role");

        builder.Property(claim => claim.ClaimType).HasMaxLength(200).IsRequired();
        builder.Property(claim => claim.ClaimValue).HasMaxLength(1000);
        builder.Property(claim => claim.Description).HasMaxLength(500);

        builder.HasOne(claim => claim.Role)
            .WithMany(role => role.Claims)
            .HasForeignKey(claim => claim.RoleId)
            .HasConstraintName("fk_iam_role_claims_iam_roles_role_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Customised <c>AspNetUserLogins</c>: external sign-in providers.</summary>
public sealed class UserLoginConfiguration : IEntityTypeConfiguration<UserLogin>
{
    public void Configure(EntityTypeBuilder<UserLogin> builder)
    {
        builder.ToTable("iam_user_logins");

        builder.RemoveBaseIdentityRelationships();

        // The framework key is (LoginProvider, ProviderKey), which is globally unique. That
        // is wrong here: the same Google account legitimately maps to a different user in
        // every Organisation it belongs to.
        //
        // TenantId cannot join the primary key, because it is nullable for the global
        // SuperAdmin. UserId takes its place instead, which achieves the same thing - a user
        // is already Tenant-specific, so keying on it makes the link Tenant-specific too, and
        // every column stays non-nullable.
        builder.HasKey(login => new { login.LoginProvider, login.ProviderKey, login.UserId });

        // One live link per provider account per Organisation. Filtered, because the global
        // SuperAdmin rows carry a null TenantId and are excluded rather than colliding.
        builder.HasIndex(login => new { login.TenantId, login.LoginProvider, login.ProviderKey })
            .HasDatabaseName("ix_iam_user_logins_tenant_provider")
            .IsUnique()
            .HasFilter("tenant_id IS NOT NULL");

        builder.HasIndex(login => new { login.TenantId, login.UserId })
            .HasDatabaseName("ix_iam_user_logins_tenant_user");

        builder.Property(login => login.LoginProvider).HasMaxLength(128);
        builder.Property(login => login.ProviderKey).HasMaxLength(256);
        builder.Property(login => login.ProviderDisplayName).HasMaxLength(200);

        builder.HasOne(login => login.User)
            .WithMany(user => user.Logins)
            .HasForeignKey(login => login.UserId)
            .HasConstraintName("fk_iam_user_logins_iam_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Customised <c>AspNetUserTokens</c>: the IdentityCore token store.</summary>
public sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("iam_user_tokens");

        builder.RemoveBaseIdentityRelationships();

        // TenantId is nullable and so cannot join the key; UserId already scopes the row to
        // one Organisation, so the framework key shape is kept as-is.
        builder.HasKey(token => new { token.UserId, token.LoginProvider, token.Name });

        builder.Property(token => token.LoginProvider).HasMaxLength(128);
        builder.Property(token => token.Name).HasMaxLength(128);

        // Token values are secrets. The value converter registered in
        // SecretEncryptionConfiguration encrypts this column at rest.
        builder.Property(token => token.Value).HasMaxLength(2000);

        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .HasConstraintName("fk_iam_user_tokens_iam_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
