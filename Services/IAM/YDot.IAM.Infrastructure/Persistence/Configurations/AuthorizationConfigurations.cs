using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// Permissions, role-permission grants, data scopes and the segregation-of-duties rules.
///
/// The split is worth noticing: <c>iam_permissions</c> is GLOBAL — one catalogue for the whole
/// platform — while everything else here is Tenant-owned. That is the shape section 46 of the
/// brief asks for: a permission is a fact about what the software can do, and which roles
/// carry it is a decision that belongs to each Organisation.
/// </summary>
public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("iam_permissions");

        builder.HasKey(permission => permission.Id);

        // GLOBALLY unique, not per Organisation. These codes are compiled into the sibling
        // services and written into tokens, so one code means one thing platform-wide.
        builder.HasIndex(permission => permission.Code)
            .HasDatabaseName("ix_iam_permissions_code")
            .IsUnique();

        builder.HasIndex(permission => new { permission.ModuleCode, permission.GroupCode })
            .HasDatabaseName("ix_iam_permissions_module_group");

        builder.Property(permission => permission.Code).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.Name).HasMaxLength(120).IsRequired();
        builder.Property(permission => permission.Description).HasMaxLength(500);
        builder.Property(permission => permission.ModuleCode).HasMaxLength(20).IsRequired();
        builder.Property(permission => permission.GroupCode).HasMaxLength(60);

        builder.Property(permission => permission.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(permission => permission.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(permission => permission.Version).IsConcurrencyToken();
    }
}

/// <summary>One permission granted to one role, inside one Organisation.</summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("iam_role_permissions");

        builder.HasKey(grant => grant.Id);

        // A role holds a permission at most once, whether as an allow or a deny.
        builder.HasIndex(grant => new { grant.TenantId, grant.RoleId, grant.PermissionId })
            .HasDatabaseName("ix_iam_role_permissions_unique")
            .IsUnique();

        // The sign-in path reads every permission for a set of roles, so this index is on the
        // hot path for authentication rather than for a screen.
        builder.HasIndex(grant => new { grant.RoleId, grant.PermissionCode })
            .HasDatabaseName("ix_iam_role_permissions_role_code");

        builder.Property(grant => grant.PermissionCode).HasMaxLength(100).IsRequired();
        builder.Property(grant => grant.Notes).HasMaxLength(1000);
        builder.Property(grant => grant.Version).IsConcurrencyToken();

        builder.HasOne(grant => grant.Role)
            .WithMany(role => role.RolePermissions)
            .HasForeignKey(grant => grant.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT rather than CASCADE: a permission that some role still carries must not be
        // deletable, because doing so would silently strip access with no audit trail.
        builder.HasOne(grant => grant.Permission)
            .WithMany(permission => permission.RolePermissions)
            .HasForeignKey(grant => grant.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Narrowing scopes granted to one user inside their Organisation.</summary>
public sealed class UserDataScopeConfiguration : IEntityTypeConfiguration<UserDataScope>
{
    public void Configure(EntityTypeBuilder<UserDataScope> builder)
    {
        builder.ToTable("iam_user_data_scopes");

        builder.HasKey(scope => scope.Id);

        builder.HasIndex(scope => new { scope.TenantId, scope.UserId })
            .HasDatabaseName("ix_iam_user_data_scopes_tenant_user");

        // One live grant per (user, type, value). Filtered over the unrevoked rows, so a
        // scope can be revoked and granted again later without colliding with its own history.
        builder.HasIndex(scope => new { scope.UserId, scope.ScopeType, scope.ScopeValue })
            .HasDatabaseName("ix_iam_user_data_scopes_active")
            .IsUnique()
            .HasFilter("revoked_at_utc IS NULL");

        builder.Property(scope => scope.ScopeValue).HasMaxLength(200).IsRequired();
        builder.Property(scope => scope.DisplayLabel).HasMaxLength(200);
        builder.Property(scope => scope.RevocationReason).HasMaxLength(500);

        builder.Property(scope => scope.ScopeType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(scope => scope.Version).IsConcurrencyToken();

        builder.HasOne(scope => scope.User)
            .WithMany(user => user.DataScopes)
            .HasForeignKey(scope => scope.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table =>
            table.HasCheckConstraint(
                "ck_iam_user_data_scopes_window",
                "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc"));
    }
}

/// <summary>Segregation-of-duties rules: two roles that may not be held together.</summary>
public sealed class RoleIncompatibilityConfiguration : IEntityTypeConfiguration<RoleIncompatibility>
{
    public void Configure(EntityTypeBuilder<RoleIncompatibility> builder)
    {
        builder.ToTable("iam_role_incompatibilities");

        builder.HasKey(rule => rule.Id);

        builder.HasIndex(rule => new { rule.TenantId, rule.RoleId, rule.ConflictingRoleId })
            .HasDatabaseName("ix_iam_role_incompatibilities_unique")
            .IsUnique();

        builder.Property(rule => rule.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(rule => rule.Version).IsConcurrencyToken();

        // NoAction on both sides. Cascade would be ambiguous - two paths from iam_roles to
        // the same row - and PostgreSQL rejects that outright. The role delete handler
        // refuses while a role still has holders, and the rules are cleaned up explicitly.
        builder.HasOne(rule => rule.Role)
            .WithMany()
            .HasForeignKey(rule => rule.RoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(rule => rule.ConflictingRole)
            .WithMany()
            .HasForeignKey(rule => rule.ConflictingRoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ToTable(table =>
            // A role conflicting with itself would make it unassignable and is always a
            // data-entry mistake.
            table.HasCheckConstraint(
                "ck_iam_role_incompatibilities_not_self",
                "role_id <> conflicting_role_id"));
    }
}

/// <summary>The global navigation catalogue.</summary>
public sealed class MenuDefinitionConfiguration : IEntityTypeConfiguration<MenuDefinition>
{
    public void Configure(EntityTypeBuilder<MenuDefinition> builder)
    {
        builder.ToTable("iam_menu_definitions");

        builder.HasKey(menu => menu.Id);

        builder.HasIndex(menu => menu.Code)
            .HasDatabaseName("ix_iam_menu_definitions_code")
            .IsUnique();

        builder.HasIndex(menu => new { menu.ParentMenuId, menu.DisplayOrder })
            .HasDatabaseName("ix_iam_menu_definitions_parent_order");

        builder.Property(menu => menu.Code).HasMaxLength(80).IsRequired();
        builder.Property(menu => menu.Name).HasMaxLength(160).IsRequired();
        builder.Property(menu => menu.Description).HasMaxLength(500);
        builder.Property(menu => menu.ModuleCode).HasMaxLength(20).IsRequired();
        builder.Property(menu => menu.Route).HasMaxLength(300);
        builder.Property(menu => menu.Icon).HasMaxLength(80);
        builder.Property(menu => menu.RequiredPermissionCode).HasMaxLength(100);
        builder.Property(menu => menu.BadgeKey).HasMaxLength(80);

        builder.Property(menu => menu.Level).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(menu => menu.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(menu => menu.Version).IsConcurrencyToken();

        // Restrict, so deleting a Menu that still has Submenus is refused rather than
        // silently taking a whole branch of the navigation with it.
        builder.HasOne(menu => menu.Parent)
            .WithMany(menu => menu.Children)
            .HasForeignKey(menu => menu.ParentMenuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>One Organisation decision about one catalogue node.</summary>
public sealed class TenantMenuConfiguration : IEntityTypeConfiguration<TenantMenu>
{
    public void Configure(EntityTypeBuilder<TenantMenu> builder)
    {
        builder.ToTable("iam_tenant_menus");

        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.TenantId, item.MenuDefinitionId })
            .HasDatabaseName("ix_iam_tenant_menus_unique")
            .IsUnique();

        builder.Property(item => item.DisplayNameOverride).HasMaxLength(160);
        builder.Property(item => item.IconOverride).HasMaxLength(80);
        builder.Property(item => item.Notes).HasMaxLength(500);

        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken();

        builder.HasOne(item => item.MenuDefinition)
            .WithMany(menu => menu.TenantMenus)
            .HasForeignKey(item => item.MenuDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Which roles inside an Organisation see which node.</summary>
public sealed class RoleMenuConfiguration : IEntityTypeConfiguration<RoleMenu>
{
    public void Configure(EntityTypeBuilder<RoleMenu> builder)
    {
        builder.ToTable("iam_role_menus");

        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.TenantId, item.RoleId, item.MenuDefinitionId })
            .HasDatabaseName("ix_iam_role_menus_unique")
            .IsUnique();

        // At most one landing page per role, or sign-in would have to pick arbitrarily.
        builder.HasIndex(item => item.RoleId)
            .HasDatabaseName("ix_iam_role_menus_landing")
            .IsUnique()
            .HasFilter("is_landing_page = TRUE");

        builder.Property(item => item.Notes).HasMaxLength(500);
        builder.Property(item => item.Version).IsConcurrencyToken();

        builder.HasOne(item => item.Role)
            .WithMany(role => role.RoleMenus)
            .HasForeignKey(item => item.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.MenuDefinition)
            .WithMany(menu => menu.RoleMenus)
            .HasForeignKey(item => item.MenuDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Departments: what somebody does.</summary>
public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("iam_departments");

        builder.HasKey(department => department.Id);

        builder.HasIndex(department => new { department.TenantId, department.Code })
            .HasDatabaseName("ix_iam_departments_tenant_code")
            .IsUnique();

        builder.Property(department => department.Code).HasMaxLength(50).IsRequired();
        builder.Property(department => department.Name).HasMaxLength(200).IsRequired();
        builder.Property(department => department.Description).HasMaxLength(1000);

        builder.Property(department => department.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(department => department.Version).IsConcurrencyToken();

        builder.HasOne(department => department.Parent)
            .WithMany(department => department.Children)
            .HasForeignKey(department => department.ParentDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Organisation units: where somebody is.</summary>
public sealed class OrganisationUnitConfiguration : IEntityTypeConfiguration<OrganisationUnit>
{
    public void Configure(EntityTypeBuilder<OrganisationUnit> builder)
    {
        builder.ToTable("iam_organisation_units");

        builder.HasKey(unit => unit.Id);

        builder.HasIndex(unit => new { unit.TenantId, unit.Code })
            .HasDatabaseName("ix_iam_organisation_units_tenant_code")
            .IsUnique();

        builder.Property(unit => unit.Code).HasMaxLength(50).IsRequired();
        builder.Property(unit => unit.Name).HasMaxLength(200).IsRequired();
        builder.Property(unit => unit.Description).HasMaxLength(1000);
        builder.Property(unit => unit.UnitType).HasMaxLength(80);
        builder.Property(unit => unit.AddressLine1).HasMaxLength(250);
        builder.Property(unit => unit.AddressLine2).HasMaxLength(250);
        builder.Property(unit => unit.City).HasMaxLength(120);
        builder.Property(unit => unit.State).HasMaxLength(120);
        builder.Property(unit => unit.Country).HasMaxLength(120);
        builder.Property(unit => unit.PostalCode).HasMaxLength(20);
        builder.Property(unit => unit.ContactEmail).HasMaxLength(320);
        builder.Property(unit => unit.ContactPhone).HasMaxLength(30);
        builder.Property(unit => unit.TimeZone).HasMaxLength(80);

        builder.Property(unit => unit.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(unit => unit.Version).IsConcurrencyToken();

        builder.HasOne(unit => unit.Parent)
            .WithMany(unit => unit.Children)
            .HasForeignKey(unit => unit.ParentUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
