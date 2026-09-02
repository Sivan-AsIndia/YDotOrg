using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Seed;

/// <summary>
/// Initialises a fresh database, and reconciles an existing one.
///
/// IT IS IDEMPOTENT, WHICH IS THE WHOLE DESIGN. Every step asks "does this already exist?"
/// before inserting, so the seeder runs on every start without duplicating anything. That is
/// what lets a newly added permission or menu node appear simply by deploying, with no
/// hand-written migration and no manual step somebody forgets.
///
/// THE ORDER MATTERS, because each step depends on the one before:
///
/// <code>
/// 1. BusinessUnit          the root everything hangs off
/// 2. Permissions           the global catalogue, from code
/// 3. Menu definitions      the global navigation, from code
/// 4. Platform role         SUPER_ADMIN, TenantId null
/// 5. SuperAdmin user       TenantId null, rajat.sivan@gmail.com
/// 6. Sample Organisations  three, in three different lifecycle states
/// </code>
///
/// IT WRITES THROUGH THE DbContext WITH FILTERS BYPASSED. There is no request and therefore
/// no ambient Organisation, so every insert names its Organisation explicitly.
/// </summary>
public sealed class IamDbSeeder(
    IamDbContext context,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    IOptions<SeedSettings> seedOptions,
    IOptions<SecuritySettings> securityOptions,
    ILogger<IamDbSeeder> logger)
{
    private readonly SeedSettings _seed = seedOptions.Value;
    private readonly SecuritySettings _security = securityOptions.Value;

    /// <summary>
    /// The three sample Organisations from section 49 of the brief, in three deliberately
    /// different states so every branch of the onboarding flow can be exercised immediately.
    /// </summary>
    private static readonly (bool UsesConfiguredId, string Name, string Subdomain,
        string AdminEmail, string FirstName, string LastName, TenantStatus Status)[] SampleTenants =
    [
        // ---- ONE ORGANISATION, APPROVED AND ACTIVATED -------------------------------------
        //
        // Its administrator account is already usable and holds TENANT_ADMIN, and the two role
        // accounts below - INITIATOR and APPROVER - are seeded into it.
        //
        // IT TAKES ITS ID FROM CONFIGURATION rather than generating one, and that is the single
        // place in this seeder where a fixed identifier is load-bearing. See the note on
        // SeedSettings.SampleOrganisationId: DON stamps its own demonstration donors, leads and
        // campaigns with the matching value, and if the two differ that data is returned to
        // nobody.
        (UsesConfiguredId: true,
            "Hope Foundation", "ten1", "rajat.sivan@yahoo.com", "Rajat", "Sivan",
            TenantStatus.Active),

        // ---- ONE ORGANISATION STILL ON AN OUTSTANDING INVITATION ---------------------------
        //
        // Seeded deliberately, so the Invited half of the lifecycle is testable from the first
        // start rather than only after somebody has driven the setup wizard. It has:
        //
        //   * no password on its administrator, so the account cannot be signed into at all;
        //   * a PENDING UserInvitation whose plaintext token is written to the start-up log,
        //     which is what makes the activation link walkable without a mail relay;
        //   * no role accounts and no completed registration profile, because an organisation
        //     that has not accepted its invitation has no staff and has submitted nothing.
        //
        // Its id is generated at creation. Nothing outside IAM refers to it, so unlike the
        // activated sample above it needs no agreed value.
        (UsesConfiguredId: false,
            "Bright Future Trust", "ten2", "bright.future@example.com", "Ananya", "Desai",
            TenantStatus.Invited)
    ];

    /// <summary>
    /// One demonstration account per Tenant role, for the activated sample Organisation.
    ///
    /// WHY THESE ARE SEEDED RATHER THAN CREATED AFTERWARDS. The demonstration guide lists these
    /// accounts and the password they share. Creating them with a script meant a fresh
    /// `docker compose up` produced an Organisation containing one administrator and nobody else,
    /// so the documented credentials were wrong on any machine that had not run that script -
    /// which is every machine except the one it was written on.
    ///
    /// They are seeded ACTIVE with a password, exactly like the sample administrator, because the
    /// point of them is to be signed into. The invitation flow is demonstrated by creating a
    /// further user through the product rather than by leaving a documented account unusable.
    ///
    /// TWO ACCOUNTS, NOT THIRTEEN, because there are now two roles below the administrator. The
    /// third Organisation role, TENANT_ADMIN, is not listed here: it is held by the sample
    /// Organisation's own administrator, created by SeedSampleTenantAsync with the address from
    /// the SampleTenants table above, and seeding a second holder of it would put two
    /// unrestricted accounts in a demonstration database for no reason.
    ///
    /// THESE ARE REAL ADDRESSES AND NOT example.com ONES, which the thirteen they replace were.
    /// The nominated testers sign in as these two and need the invitation and password-reset mail
    /// to actually arrive. That makes IAM_MAIL_REDIRECT_TO worth a thought before pointing this
    /// environment at anything containing real supporters - see the note on it in .env.
    /// </summary>
    private static readonly (string RoleCode, string Username, string First, string Last,
        string Email)[] RoleAccounts =
    [
        (RoleCodes.Initiator, "initiator", "Rajat", "Sivan", "rajat.sivan@outlook.com"),
        (RoleCodes.Approver, "approver", "Gowri", "Saranya", "gowrisaranya6@gmail.com")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_seed.Enabled)
        {
            logger.LogInformation("Seeding is disabled.");
            return;
        }

        var businessUnit = await SeedBusinessUnitAsync(cancellationToken);

        if (_seed.SeedCatalogues)
        {
            await SeedPermissionsAsync(cancellationToken);
            await SeedMenuDefinitionsAsync(cancellationToken);
        }

        // Saved here so the catalogue rows have ids before anything references them.
        await context.SaveChangesAsync(cancellationToken);

        var platformRole = await SeedPlatformRoleAsync(businessUnit, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await SeedSuperAdminAsync(businessUnit, platformRole, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        if (_seed.SeedSampleTenants)
        {
            foreach (var sample in SampleTenants)
            {
                await SeedSampleTenantAsync(businessUnit, sample, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            // AFTER THE SAVE, DELIBERATELY, AND IN A SEPARATE PASS.
            //
            // SeedRoleAccountsAsync looks the Organisation's roles up from the database to find
            // the one each account should hold. Called from inside SeedSampleTenantAsync it ran
            // BEFORE SaveChangesAsync, when those roles existed only in the change tracker - so
            // the lookup came back empty, every account was skipped as "role not found", and the
            // eleven demonstration logins were silently absent.
            //
            // It only showed up on a FRESH database. On one where the Organisation already
            // existed the roles were already saved, so it worked - which is exactly backwards
            // from where it needed to work, because a colleague starting from nothing is the
            // whole point of seeding them.
            foreach (var sample in SampleTenants.Where(item => item.Status == TenantStatus.Active))
            {
                var tenant = await context.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        item => item.BusinessUnitId == businessUnit.Id
                                && item.Subdomain == sample.Subdomain,
                        cancellationToken);

                if (tenant is not null)
                {
                    await SeedRoleAccountsAsync(
                        tenant, businessUnit, DateTimeOffset.UtcNow, cancellationToken);

                    // Departments and units are seeded HERE rather than inside
                    // SeedSampleTenantAsync, and the reason is the early return at the top of
                    // that method: it exits the moment the Organisation already exists, so
                    // anything it creates can only ever appear on a database that has never
                    // been seeded before. Departments and units are what the Create User form's
                    // two dropdowns read, and an upgraded database left both permanently empty -
                    // a form with an unfillable field, and nothing in the API to explain it.
                    // Run from here it reconciles on every start, so an existing Organisation
                    // gets them too.
                    await SeedOrganisationStructureAsync(
                        tenant, businessUnit, DateTimeOffset.UtcNow, cancellationToken);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // AFTER the Organisations exist, because it reconciles what they hold.
        if (_seed.SeedCatalogues)
        {
            await ReconcileTenantMenusAsync(cancellationToken);

            // MISSING ROLES FIRST, then their grants. The permission reconcile below only fills
            // rows into roles that already exist, so a role ADDED to the blueprint after an
            // Organisation was created reached nobody: the Organisation simply never had it, and
            // every permission that lived only in that role belonged to no role at all on that
            // database. Creating them here first means the grant pass that follows sees them.
            await ReconcileTenantRolesAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            await ReconcileSystemRolePermissionsAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // AFTER the grants, because what a role may SEE is computed from what it may DO.
            await ReconcileRoleMenusAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // LAST, because it reads the roles the three passes above have finished settling.
            await RetireUnknownSystemRolesAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Seeding complete.");
    }

    /// <summary>The root platform entity: www.ngoplanet.com.</summary>
    private async Task<BusinessUnit> SeedBusinessUnitAsync(CancellationToken cancellationToken)
    {
        var code = _seed.BusinessUnitCode.ToUpperInvariant();

        var existing = await context.BusinessUnits
            .FirstOrDefaultAsync(unit => unit.Code == code, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var businessUnit = new BusinessUnit
        {
            Code = code,
            Name = _seed.BusinessUnitName,
            LegalName = _seed.BusinessUnitName,
            RootDomain = _seed.RootDomain.ToLowerInvariant(),
            Status = BusinessUnitStatus.Active,
            ContactEmail = _seed.SuperAdminEmail,
            SupportEmail = _seed.SuperAdminEmail,
            TimeZone = "Asia/Kolkata",
            DefaultCurrency = "INR",
            DefaultCulture = "en-IN",
            Description = "Root business unit for the YDot platform.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.Empty
        };

        await context.BusinessUnits.AddAsync(businessUnit, cancellationToken);

        logger.LogInformation(
            "Seeded business unit {Code} on {RootDomain}.", businessUnit.Code, businessUnit.RootDomain);

        return businessUnit;
    }

    /// <summary>
    /// The global permission catalogue, reconciled from code.
    ///
    /// Every code from <c>PermissionCodes</c> and <c>ModulePermissionCatalogue</c> is checked
    /// and inserted if missing. That is how a permission added in a later release reaches an
    /// existing database — deploy, restart, and it is there.
    /// </summary>
    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Permissions
            .Select(permission => permission.Code)
            .ToListAsync(cancellationToken);

        var known = existing.ToHashSet(StringComparer.Ordinal);
        var added = 0;
        var order = 0;

        // ---- IAM Tenant codes ------------------------------------------------------------
        foreach (var code in PermissionCodes.AllTenant)
        {
            order += 10;

            if (known.Contains(code))
            {
                continue;
            }

            await context.Permissions.AddAsync(BuildPermission(code, order, isPlatformOnly: false), cancellationToken);
            added++;
        }

        // ---- Platform codes. Marked IsPlatformOnly, which is what keeps them off Tenant roles.
        foreach (var code in PermissionCodes.Platform.All)
        {
            order += 10;

            if (known.Contains(code))
            {
                continue;
            }

            await context.Permissions.AddAsync(BuildPermission(code, order, isPlatformOnly: true), cancellationToken);
            added++;
        }

        // ---- The other services codes ---------------------------------------------------------
        //
        // Seeded here because IAM is the only service that can put a claim into a token, so a
        // code that does not exist here is a code the Donors service can never receive.
        foreach (var seed in ModulePermissionCatalogue.AllOtherModules)
        {
            order += 10;

            if (known.Contains(seed.Code))
            {
                continue;
            }

            await context.Permissions.AddAsync(new Permission
            {
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                ModuleCode = seed.ModuleCode,
                GroupCode = seed.GroupCode,
                Action = seed.Action,
                IsSensitive = seed.IsSensitive,
                IsPlatformOnly = seed.IsPlatformOnly,
                Status = PermissionStatus.Active,
                DisplayOrder = order,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);

            added++;
        }

        if (added > 0)
        {
            logger.LogInformation("Seeded {Count} permission(s).", added);
        }
    }

    /// <summary>
    /// Turns a dotted code into a catalogue row.
    ///
    /// The module, group, action and name are all derived from the code itself, so adding a
    /// permission means adding one string to <c>PermissionCodes</c> and nothing else.
    /// </summary>
    private static Permission BuildPermission(string code, int displayOrder, bool isPlatformOnly)
    {
        // THE DERIVATION MOVED OUT OF HERE, into PermissionCodeConventions, and it had to.
        // RoleAccessProfiles decides whether INITIATOR or APPROVER holds a code by asking what
        // action it is, and while this method was the only place that knew, the profile had to
        // guess - so a code this method filed as Approve could land in the maker role, which is
        // the one outcome the two roles exist to prevent. One derivation, two callers, no drift.
        return new Permission
        {
            Code = code,
            Name = PermissionCodeConventions.DeriveName(code),
            Description = null,
            ModuleCode = PermissionCodeConventions.DeriveModule(code),
            GroupCode = PermissionCodeConventions.DeriveGroup(code),
            Action = PermissionCodeConventions.DeriveAction(code),
            IsSensitive = PermissionCodes.IsSensitive(code),
            IsPlatformOnly = isPlatformOnly,
            Status = PermissionStatus.Active,
            DisplayOrder = displayOrder,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.Empty
        };
    }


    /// <summary>The global navigation catalogue, reconciled from <c>MenuCatalogue</c>.</summary>
    private async Task SeedMenuDefinitionsAsync(CancellationToken cancellationToken)
    {
        var existing = await context.MenuDefinitions
            .ToDictionaryAsync(menu => menu.Code, StringComparer.Ordinal, cancellationToken);

        var added = 0;
        var updated = 0;

        // TWO PASSES, because a child cannot resolve its parent id until the parent exists.
        // Ordered by level so parents are always created first.
        foreach (var seed in MenuCatalogue.All.OrderBy(node => node.Level))
        {
            Guid? parentId = null;

            if (!string.IsNullOrWhiteSpace(seed.ParentCode))
            {
                if (!existing.TryGetValue(seed.ParentCode, out var parent))
                {
                    logger.LogWarning(
                        "Menu node {Code} names parent {ParentCode}, which does not exist. Skipped.",
                        seed.Code, seed.ParentCode);

                    continue;
                }

                parentId = parent.Id;
            }

            // RECONCILE, DO NOT SKIP. This used to `continue` the moment a code was already
            // present, which made the catalogue write-once: renaming a screen, correcting a
            // route, tightening a permission or switching a branch off reached a fresh database
            // and no other. Every deployment that had ever started kept the original list
            // forever, and nothing failed loudly enough to notice.
            //
            // Status is deliberately NOT reconciled. The catalogue describes what the software
            // contains; whether an operator has hidden or retired a node is their decision and
            // is not code's to overwrite.
            if (existing.TryGetValue(seed.Code, out var current))
            {
                var changed =
                    current.Name != seed.Name
                    || current.ParentMenuId != parentId
                    || current.Level != seed.Level
                    || current.ModuleCode != seed.ModuleCode
                    || current.Route != seed.Route
                    || current.Icon != seed.Icon
                    || current.RequiredPermissionCode != seed.RequiredPermissionCode
                    || current.DisplayOrder != seed.DisplayOrder
                    || current.IsPlatformOnly != seed.IsPlatformOnly
                    || current.IsEnabledByDefault != seed.IsEnabledByDefault
                    || current.IsMandatory != seed.IsMandatory;

                if (changed)
                {
                    current.Name = seed.Name;
                    current.ParentMenuId = parentId;
                    current.Level = seed.Level;
                    current.ModuleCode = seed.ModuleCode;
                    current.Route = seed.Route;
                    current.Icon = seed.Icon;
                    current.RequiredPermissionCode = seed.RequiredPermissionCode;
                    current.DisplayOrder = seed.DisplayOrder;
                    current.IsPlatformOnly = seed.IsPlatformOnly;
                    current.IsEnabledByDefault = seed.IsEnabledByDefault;
                    current.IsMandatory = seed.IsMandatory;

                    updated++;
                }

                continue;
            }

            var definition = new MenuDefinition
            {
                Code = seed.Code,
                Name = seed.Name,
                ParentMenuId = parentId,
                Level = seed.Level,
                ModuleCode = seed.ModuleCode,
                Route = seed.Route,
                Icon = seed.Icon,
                RequiredPermissionCode = seed.RequiredPermissionCode,
                DisplayOrder = seed.DisplayOrder,
                Status = MenuStatus.Active,
                IsPlatformOnly = seed.IsPlatformOnly,
                IsEnabledByDefault = seed.IsEnabledByDefault,
                IsMandatory = seed.IsMandatory,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = Guid.Empty
            };

            await context.MenuDefinitions.AddAsync(definition, cancellationToken);

            // Added to the lookup immediately so the next node in this same pass can use it
            // as a parent without a round trip.
            existing[seed.Code] = definition;
            added++;
        }

        // ---- Nodes the catalogue no longer contains ------------------------------------------
        //
        // RETIRED, NOT DELETED. Removing a screen from the catalogue has to reach a database that
        // already has it, or withdrawing a screen only ever works on a deployment that has never
        // run - the same write-once gap as the update path above, one level up. A definition that
        // is simply deleted would take an Organisation's own overrides and any role mapping with
        // it, so the row stays and is marked Retired; the menu builder already drops those, and
        // restoring the catalogue entry brings it back exactly as it was.
        var known = MenuCatalogue.All.Select(seed => seed.Code).ToHashSet(StringComparer.Ordinal);
        var retired = 0;

        foreach (var orphan in existing.Values.Where(menu =>
                     !known.Contains(menu.Code) && menu.Status != MenuStatus.Retired))
        {
            orphan.Status = MenuStatus.Retired;
            retired++;
        }

        if (added > 0 || updated > 0 || retired > 0)
        {
            logger.LogInformation(
                "Menu catalogue reconciled: {Added} added, {Updated} updated, {Retired} retired.",
                added, updated, retired);
        }
    }

    /// <summary>
    /// Brings every Organisation's SYSTEM-GENERATED navigation back into line with the catalogue.
    ///
    /// An Organisation does not read the catalogue directly. At creation it is given a
    /// <c>TenantMenu</c> row per enabled node, and that row is what the sidebar is built from —
    /// which is the whole point, because it is the thing an administrator can then override per
    /// Organisation.
    ///
    /// The consequence is that changing the catalogue alone changes nothing for anybody who
    /// already exists. Switching a branch off reached new Organisations only; every existing one
    /// kept an explicit row saying "enabled", and that row wins. The same gap in reverse meant a
    /// newly added screen never appeared for an Organisation created before it.
    ///
    /// ONLY SYSTEM-GENERATED ROWS ARE TOUCHED. The moment an administrator changes a node for
    /// their Organisation, <c>IsSystemGenerated</c> is cleared and this leaves it alone for good
    /// — their decision outranks the default, which is what an override is for.
    /// </summary>
    /// <summary>
    /// Brings each seeded role's permission rows back in line with its definition in code.
    ///
    /// WITHOUT THIS, A PERMISSION ADDED TO A ROLE ONLY EVER REACHES A DATABASE THAT HAS NEVER
    /// BEEN SEEDED. Roles are created inside <c>SeedSampleTenantAsync</c>, which returns at its
    /// first line when the Organisation already exists - so on every upgraded database the roles
    /// keep whatever grant they were first created with, no matter what the definitions now say.
    /// <c>SeedPermissionsAsync</c> already reconciles the catalogue for exactly this reason; the
    /// grants needed the same treatment and did not have it.
    ///
    /// IT ONLY ADDS. A row that is present and no longer in the definition is left alone, because
    /// this cannot tell a stale grant apart from one an administrator added on purpose, and
    /// silently revoking the second to tidy up the first is the worse mistake by a distance.
    ///
    /// SYSTEM ROLES ONLY - <c>IsSystemRole</c>, which is set only by this seeder. A role somebody
    /// created in the Role Catalogue is theirs and is never touched. A blanket-grant role is
    /// skipped too: the flag is the grant, and rows would add nothing.
    /// </summary>
    /// <summary>
    /// Creates any blueprint role an existing Organisation does not have.
    ///
    /// WHY IT IS NEEDED. Roles are copied into an Organisation when it is created, and that copy
    /// is a one-off. Adding a role to <see cref="TenantRoleDefinitions"/> afterwards therefore
    /// reached new Organisations only - so DATA_STEWARD and DONOR_CARE, and the eighteen donor
    /// permissions that live in them, would have existed on a fresh database and nowhere else.
    ///
    /// IT ONLY EVER ADDS. An Organisation that has renamed, re-scoped or deactivated its copy of
    /// a role keeps its own version untouched: the match is on the code, and a role already
    /// present is left exactly as the Organisation left it.
    /// </summary>
    private async Task ReconcileTenantRolesAsync(CancellationToken cancellationToken)
    {
        var tenants = await context.Tenants
            .IgnoreQueryFilters()
            .Select(tenant => new { tenant.Id, tenant.BusinessUnitId, tenant.Code })
            .ToListAsync(cancellationToken);

        if (tenants.Count == 0)
        {
            return;
        }

        var existing = (await context.Roles
                .IgnoreQueryFilters()
                .Where(role => role.TenantId != null)
                .Select(role => new { TenantId = role.TenantId!.Value, role.NormalizedCode })
                .ToListAsync(cancellationToken))
            .GroupBy(role => role.TenantId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.NormalizedCode).ToHashSet(StringComparer.Ordinal));

        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var tenant in tenants)
        {
            var held = existing.TryGetValue(tenant.Id, out var codes)
                ? codes
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var definition in TenantRoleDefinitions.All)
            {
                if (held.Contains(definition.Code))
                {
                    continue;
                }

                await context.Roles.AddAsync(new Role
                {
                    TenantId = tenant.Id,
                    BusinessUnitId = tenant.BusinessUnitId,
                    Code = definition.Code,
                    NormalizedCode = definition.Code,
                    Name = definition.Name,
                    NormalizedName = definition.Name.ToUpperInvariant(),
                    Description = definition.Description,
                    RoleType = RoleType.Tenant,
                    Status = RoleStatus.Active,
                    IsSystemRole = true,
                    IsDefaultRole = definition.IsDefault,
                    GrantsAllTenantPermissions = definition.GrantsAll,
                    IsPrivileged = definition.IsPrivileged,
                    Priority = definition.Priority,
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.Empty
                }, cancellationToken);

                held.Add(definition.Code);
                added++;
            }
        }

        if (added > 0)
        {
            logger.LogInformation(
                "Reconciled Organisation roles: {Added} missing role(s) created across {TenantCount} Organisation(s).",
                added, tenants.Count);
        }
    }

    private async Task ReconcileSystemRolePermissionsAsync(CancellationToken cancellationToken)
    {
        var definitions = TenantRoleDefinitions.All
            .Where(definition => !definition.GrantsAll)
            .ToDictionary(definition => definition.Code, StringComparer.Ordinal);

        if (definitions.Count == 0)
        {
            return;
        }

        var permissions = await context.Permissions
            .Where(permission => permission.Status == PermissionStatus.Active && !permission.IsPlatformOnly)
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);

        var roles = await context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId != null && role.IsSystemRole && !role.GrantsAllTenantPermissions)
            .ToListAsync(cancellationToken);

        if (roles.Count == 0)
        {
            return;
        }

        var roleIds = roles.Select(role => role.Id).ToList();

        // One query for every role's rows rather than one per role: this runs on every start.
        var held = (await context.RolePermissions
                .IgnoreQueryFilters()
                .Where(grant => roleIds.Contains(grant.RoleId))
                .Select(grant => new { grant.RoleId, grant.PermissionCode })
                .ToListAsync(cancellationToken))
            .GroupBy(grant => grant.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.PermissionCode).ToHashSet(StringComparer.Ordinal));

        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var role in roles)
        {
            if (!definitions.TryGetValue(role.NormalizedCode, out var definition))
            {
                continue;
            }

            var current = held.TryGetValue(role.Id, out var codes)
                ? codes
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var permissionCode in definition.PermissionCodes)
            {
                if (current.Contains(permissionCode)
                    || !permissions.TryGetValue(permissionCode, out var permission))
                {
                    continue;
                }

                await context.RolePermissions.AddAsync(new RolePermission
                {
                    // Non-null by the `role.TenantId != null` filter on the query above.
                    TenantId = role.TenantId!.Value,
                    BusinessUnitId = role.BusinessUnitId,
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                    PermissionCode = permission.Code,
                    GrantedAtUtc = now,
                    GrantedByUserId = Guid.Empty,
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.Empty
                }, cancellationToken);

                current.Add(permissionCode);
                added++;
            }
        }

        var removed = await WithdrawSupersededGrantsAsync(roles, cancellationToken);

        if (added > 0 || removed > 0)
        {
            logger.LogInformation(
                "Reconciled system role grants: {Added} permission row(s) added, {Removed} withdrawn, "
                + "across {RoleCount} role(s).",
                added, removed, roles.Count);
        }
    }

    /// <summary>
    /// Removes the grants a system role used to hold and must no longer.
    ///
    /// NARROWING A ROLE DOES NOT REACH AN EXISTING DATABASE ON ITS OWN. The reconciliation above
    /// only adds what a definition is missing - on purpose, so that an administrator's extra
    /// grant to a system role survives a restart - which means a permission removed from a
    /// profile stays in the table, and the role goes on holding it. Every narrowing is therefore
    /// stated explicitly in <see cref="RoleAccessProfiles.WithdrawnGrants"/> and applied here.
    ///
    /// IT IS NOT A DIFF AGAINST THE PROFILE, and must not become one: "delete every row the
    /// profile does not list" would undo an administrator's deliberate customisation silently, on
    /// every start-up, for every Organisation.
    /// </summary>
    private async Task<int> WithdrawSupersededGrantsAsync(
        IReadOnlyCollection<Role> roles, CancellationToken cancellationToken)
    {
        if (RoleAccessProfiles.WithdrawnGrants.Count == 0)
        {
            return 0;
        }

        var removed = 0;

        foreach (var role in roles)
        {
            if (!RoleAccessProfiles.WithdrawnGrants.TryGetValue(role.NormalizedCode, out var codes)
                || codes.Count == 0)
            {
                continue;
            }

            var doomed = await context.RolePermissions
                .IgnoreQueryFilters()
                .Where(grant => grant.RoleId == role.Id && codes.Contains(grant.PermissionCode))
                .ToListAsync(cancellationToken);

            if (doomed.Count == 0)
            {
                continue;
            }

            context.RolePermissions.RemoveRange(doomed);
            removed += doomed.Count;

            logger.LogInformation(
                "Withdrew {Count} superseded grant(s) from role {RoleCode}: {Codes}.",
                doomed.Count, role.NormalizedCode,
                string.Join(", ", doomed.Select(grant => grant.PermissionCode)));
        }

        return removed;
    }

    /// <summary>
    /// Writes the role-to-menu mapping rows the "Menu Mapping" screen edits.
    ///
    /// WHY THIS EXISTS AT ALL, given that permissions already decide what a sidebar shows. Two
    /// reasons, and the second is the important one.
    ///
    /// The first is that the mapping screen opened empty. RoleMenu has always been optional -
    /// "no rows means no restriction" - so nothing had ever written any, and an administrator who
    /// opened Menu Mapping to see which role reached which screen was shown a blank grid and left
    /// to infer the answer from the permission catalogue. The mapping was real but invisible.
    ///
    /// The second is that a blank grid is dangerous to edit. Tick one box in an empty grid and
    /// the save writes exactly one row - and every other node for that role now has a row saying
    /// nothing while one says yes, which reads to a person as "only this one is mapped". Seeding
    /// the full grid means an administrator changes a decision that is already written down
    /// rather than creating the first one by accident.
    ///
    /// IT CANNOT GRANT ANYTHING, and that is what makes it safe to write in bulk. RoleMenu is a
    /// subtractive filter by construction - see the note on the entity - so the worst a wrong row
    /// here can do is hide a screen. IsVisible is set from whether the role actually holds the
    /// node's required permission, so what it writes is a faithful record of what was already
    /// true, not a new decision.
    ///
    /// EXISTING ROWS ARE LEFT ALONE. An administrator who has hidden Payments from Initiator
    /// meant it, and a reconcile that stamped over that on the next restart would be a bug wearing
    /// the clothes of a feature. This adds what is missing and touches nothing else.
    /// </summary>
    private async Task ReconcileRoleMenusAsync(CancellationToken cancellationToken)
    {
        // The nodes an Organisation can map: everything the platform branch does not own, and
        // nothing retired. Disabled-by-default nodes are excluded for the same reason
        // ReconcileTenantMenusAsync excludes them - an Organisation does not hold them, so a row
        // mapping a role to one would describe a screen that is not there.
        var mappable = await context.MenuDefinitions
            .Where(menu => !menu.IsPlatformOnly
                           && menu.IsEnabledByDefault
                           && menu.Status != MenuStatus.Retired)
            .Select(menu => new { menu.Id, menu.Code, menu.RequiredPermissionCode })
            .ToListAsync(cancellationToken);

        if (mappable.Count == 0)
        {
            return;
        }

        var roles = await context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId != null && role.IsSystemRole)
            .Select(role => new
            {
                role.Id,
                TenantId = role.TenantId!.Value,
                role.BusinessUnitId,
                role.NormalizedCode,
                role.GrantsAllTenantPermissions
            })
            .ToListAsync(cancellationToken);

        if (roles.Count == 0)
        {
            return;
        }

        var roleIds = roles.Select(role => role.Id).ToList();

        // What each role may actually do. One query rather than one per role: this runs on every
        // start, and there is a row here for every role in every Organisation.
        var grants = (await context.RolePermissions
                .IgnoreQueryFilters()
                .Where(grant => roleIds.Contains(grant.RoleId) && !grant.IsDenied)
                .Select(grant => new { grant.RoleId, grant.PermissionCode })
                .ToListAsync(cancellationToken))
            .GroupBy(grant => grant.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.PermissionCode).ToHashSet(StringComparer.Ordinal));

        var mapped = (await context.RoleMenus
                .IgnoreQueryFilters()
                .Where(mapping => roleIds.Contains(mapping.RoleId))
                .Select(mapping => new { mapping.RoleId, mapping.MenuDefinitionId })
                .ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.MenuDefinitionId).ToHashSet());

        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var role in roles)
        {
            var held = grants.TryGetValue(role.Id, out var codes)
                ? codes
                : new HashSet<string>(StringComparer.Ordinal);

            var already = mapped.TryGetValue(role.Id, out var existing)
                ? existing
                : [];

            foreach (var node in mappable.Where(node => !already.Contains(node.Id)))
            {
                // TENANT_ADMIN holds everything by flag and owns no RolePermission rows at all,
                // so asking `held` about it would answer no to every node and hide the entire
                // sidebar from the one role that is supposed to see all of it.
                var isVisible = role.GrantsAllTenantPermissions
                                || string.IsNullOrWhiteSpace(node.RequiredPermissionCode)
                                || held.Contains(node.RequiredPermissionCode);

                await context.RoleMenus.AddAsync(new RoleMenu
                {
                    TenantId = role.TenantId,
                    BusinessUnitId = role.BusinessUnitId,
                    RoleId = role.Id,
                    MenuDefinitionId = node.Id,
                    IsVisible = isVisible,

                    // Everybody lands on the dashboard. It is the one node with no permission on
                    // it that every role holds, so it is the only choice that cannot land
                    // somebody on a screen they are then refused.
                    IsLandingPage = node.Code == MenuCatalogue.Dashboard,

                    MappedAtUtc = now,
                    MappedByUserId = Guid.Empty,
                    Notes = isVisible
                        ? "Seeded: this role holds the permission this screen requires."
                        : "Seeded: this role does not hold the permission this screen requires.",
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.Empty
                }, cancellationToken);

                added++;
            }
        }

        if (added > 0)
        {
            logger.LogInformation(
                "Reconciled role menu mapping: {Added} row(s) added across {RoleCount} role(s) "
                + "and {NodeCount} navigable node(s).",
                added, roles.Count, mappable.Count);
        }
    }

    /// <summary>
    /// Deactivates system roles an Organisation still holds that the blueprint no longer defines.
    ///
    /// THIS IS AN UPGRADE PATH AND NOTHING ELSE. A database seeded before the role catalogue was
    /// cut from fourteen roles to three still carries the other eleven - Campaign Manager, Finance
    /// Officer, Data Steward and the rest - and each of them still grants what it always granted.
    /// Leaving them would make the deliverable false on every database except a brand new one:
    /// the Roles screen would list fourteen roles and the seeder would insist there were three.
    ///
    /// IT DEACTIVATES AND DOES NOT DELETE, deliberately. A role may still be assigned to somebody,
    /// and deleting it would strip a person's access with no record of what they used to hold and
    /// no way to put it back if the removal was a mistake. Inactive is reversible, it is visible
    /// on the Roles screen, and the audit trail keeps the assignment - so an administrator can see
    /// what happened and decide where those people should go.
    ///
    /// IT ONLY TOUCHES SYSTEM ROLES. A role an Organisation built for itself is theirs, is not in
    /// the blueprint by definition, and must never be swept up by a reconcile that was aiming at
    /// the platform's own leftovers.
    /// </summary>
    private async Task RetireUnknownSystemRolesAsync(CancellationToken cancellationToken)
    {
        var known = TenantRoleDefinitions.All
            .Select(definition => definition.Code)
            .ToHashSet(StringComparer.Ordinal);

        var stale = await context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId != null
                           && role.IsSystemRole
                           && role.Status == RoleStatus.Active
                           && !known.Contains(role.NormalizedCode))
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return;
        }

        foreach (var role in stale)
        {
            role.Status = RoleStatus.Inactive;
            role.UpdatedAtUtc = DateTimeOffset.UtcNow;
            role.UpdatedByUserId = Guid.Empty;
        }

        logger.LogWarning(
            "Deactivated {Count} system role(s) no longer in the catalogue: {Codes}. Anyone still "
            + "assigned to one keeps the assignment but loses its grants; reassign them to "
            + "INITIATOR or APPROVER.",
            stale.Count,
            string.Join(", ", stale.Select(role => role.NormalizedCode).Distinct(StringComparer.Ordinal)));
    }

    private async Task ReconcileTenantMenusAsync(CancellationToken cancellationToken)
    {
        var definitions = await context.MenuDefinitions.ToListAsync(cancellationToken);

        // What an Organisation should hold: everything enabled by default that is not the
        // platform-only branch.
        var shouldHold = definitions
            .Where(definition => !definition.IsPlatformOnly
                                 && definition.IsEnabledByDefault
                                 && definition.Status != MenuStatus.Retired)
            .Select(definition => definition.Id)
            .ToHashSet();

        var tenants = await context.Tenants
            .IgnoreQueryFilters()
            .Select(tenant => new { tenant.Id, tenant.BusinessUnitId })
            .ToListAsync(cancellationToken);

        var existing = await context.TenantMenus
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        var byTenant = existing
            .GroupBy(menu => menu.TenantId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var removed = 0;
        var restored = 0;

        foreach (var tenant in tenants)
        {
            if (!byTenant.TryGetValue(tenant.Id, out var held))
            {
                // Never provisioned — leave it to whatever creates it, rather than inventing a
                // navigation for an Organisation that may not be ready for one.
                continue;
            }

            foreach (var menu in held.Where(menu => menu.IsSystemGenerated
                                                    && !shouldHold.Contains(menu.MenuDefinitionId)))
            {
                context.TenantMenus.Remove(menu);
                removed++;
            }

            var codesHeld = held.Select(menu => menu.MenuDefinitionId).ToHashSet();

            foreach (var definitionId in shouldHold.Where(id => !codesHeld.Contains(id)))
            {
                await context.TenantMenus.AddAsync(new TenantMenu
                {
                    TenantId = tenant.Id,
                    BusinessUnitId = tenant.BusinessUnitId,
                    MenuDefinitionId = definitionId,
                    IsEnabled = true,
                    Status = MenuStatus.Active,
                    IsSystemGenerated = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty
                }, cancellationToken);

                restored++;
            }
        }

        if (removed > 0 || restored > 0)
        {
            logger.LogInformation(
                "Organisation navigation reconciled across {Tenants} Organisation(s): "
                + "{Removed} row(s) removed, {Restored} row(s) added.",
                tenants.Count, removed, restored);
        }
    }

    /// <summary>
    /// The platform role: TenantId null, held only by SuperAdmin.
    ///
    /// It carries no permission rows. SuperAdmin authority comes from the <c>IsSuperAdmin</c>
    /// flag and the Global scope claim, not from an enumerated list that would go stale the
    /// moment a permission is added.
    /// </summary>
    private async Task<Role> SeedPlatformRoleAsync(BusinessUnit businessUnit, CancellationToken cancellationToken)
    {
        var existing = await context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                role => role.TenantId == null && role.NormalizedCode == RoleCodes.SuperAdmin,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var role = new Role
        {
            TenantId = null,
            BusinessUnitId = businessUnit.Id,
            Code = RoleCodes.SuperAdmin,
            NormalizedCode = RoleCodes.SuperAdmin,
            Name = "Platform Administrator",
            NormalizedName = "PLATFORM ADMINISTRATOR",
            Description = "Root platform role. Unrestricted access across every organisation.",
            RoleType = RoleType.Platform,
            Status = RoleStatus.Active,
            IsSystemRole = true,
            IsPrivileged = true,
            GrantsAllTenantPermissions = true,
            Priority = 1000,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.Empty
        };

        await context.Roles.AddAsync(role, cancellationToken);

        logger.LogInformation("Seeded the platform role.");

        return role;
    }

    /// <summary>
    /// The global root user.
    ///
    /// <c>TenantId</c> is NULL and stays null forever — that is the invariant the whole
    /// tenancy model rests on, and there is a check constraint enforcing it. They are not a
    /// member of any Organisation; they select one to operate in.
    /// </summary>
    private async Task SeedSuperAdminAsync(
        BusinessUnit businessUnit, Role platformRole, CancellationToken cancellationToken)
    {
        var normalisedEmail = _seed.SuperAdminEmail.Trim().ToUpperInvariant();

        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                user => user.TenantId == null && user.NormalizedEmail == normalisedEmail,
                cancellationToken);

        if (existing is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var superAdmin = new User
        {
            TenantId = null,
            BusinessUnitId = businessUnit.Id,
            Code = "SUPERADMIN",
            FirstName = _seed.SuperAdminFirstName,
            LastName = _seed.SuperAdminLastName,
            DisplayName = $"{_seed.SuperAdminFirstName} {_seed.SuperAdminLastName}".Trim(),
            Email = _seed.SuperAdminEmail.Trim().ToLowerInvariant(),
            NormalizedEmail = normalisedEmail,
            UserName = _seed.SuperAdminUsername.Trim().ToLowerInvariant(),
            NormalizedUserName = _seed.SuperAdminUsername.Trim().ToUpperInvariant(),
            EmailConfirmed = true,
            EmailConfirmedAtUtc = now,
            Status = UserStatus.Active,
            AccountCategory = UserAccountCategory.Employee,
            PrivilegeLevel = PrivilegeLevel.SuperAdmin,
            IsSuperAdmin = true,
            IsTenantAdmin = false,
            IsSystemAccount = true,
            MfaRequirement = MfaRequirement.Optional,
            AccessStartsAtUtc = now,
            LockoutEnabled = true,
            CredentialSetupMethod = CredentialSetupMethod.AdministratorSet,
            CreatedAtUtc = now,
            CreatedByUserId = Guid.Empty
        };

        // A password only when one was configured. With none, the account exists but cannot be
        // signed into until a reset link is used - which is the correct production default.
        if (!string.IsNullOrWhiteSpace(_seed.SuperAdminPassword))
        {
            superAdmin.PasswordHash = passwordHasher.Hash(_seed.SuperAdminPassword);
            superAdmin.PasswordChangedAtUtc = now;

            logger.LogWarning(
                "The SuperAdmin account was seeded WITH a configured password. "
                + "Clear SeedSettings:SuperAdminPassword outside development.");
        }
        else
        {
            logger.LogInformation(
                "The SuperAdmin account was seeded with no password. "
                + "Use forgot-password on the platform host to set one.");
        }

        await context.Users.AddAsync(superAdmin, cancellationToken);

        await context.UserRoles.AddAsync(new UserRole
        {
            TenantId = null,
            BusinessUnitId = businessUnit.Id,
            UserId = superAdmin.Id,
            RoleId = platformRole.Id,
            Status = UserRoleAssignmentStatus.Active,
            IsPrimary = true,
            AssignedAtUtc = now,
            AssignedByUserId = Guid.Empty,
            EffectiveFromUtc = now,
            Justification = "Platform root account.",
            CreatedAtUtc = now,
            CreatedByUserId = Guid.Empty
        }, cancellationToken);

        logger.LogInformation("Seeded the SuperAdmin account {Email}.", superAdmin.Email);
    }

    /// <summary>
    /// One sample Organisation, complete with its host, roles, menus, administrator and
    /// invitation.
    ///
    /// The three samples are deliberately left in different lifecycle states so the whole
    /// onboarding flow can be exercised the moment the database comes up: one Active with a
    /// usable administrator, and two sitting on an outstanding invitation.
    /// </summary>
    private async Task SeedSampleTenantAsync(
        BusinessUnit businessUnit,
        (bool UsesConfiguredId, string Name, string Subdomain, string AdminEmail,
            string FirstName, string LastName, TenantStatus Status) sample,
        CancellationToken cancellationToken)
    {
        var existing = await context.Tenants
            .FirstOrDefaultAsync(
                tenant => tenant.BusinessUnitId == businessUnit.Id && tenant.Subdomain == sample.Subdomain,
                cancellationToken);

        if (existing is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var isActive = sample.Status == TenantStatus.Active;

        var count = await context.Tenants
            .CountAsync(tenant => tenant.BusinessUnitId == businessUnit.Id, cancellationToken);

        var code = $"TEN{count + 1:D3}";

        var tenant = new Tenant
        {
            // FROM CONFIGURATION for the activated sample, generated for the rest. See the
            // note on SeedSettings.SampleOrganisationId: DON's demonstration data is stamped
            // with the matching value and is invisible to every caller whose token carries a
            // different one.
            Id = sample.UsesConfiguredId ? _seed.SampleOrganisationId : Guid.NewGuid(),
            BusinessUnitId = businessUnit.Id,
            Code = code,
            Name = sample.Name,
            LegalName = $"{sample.Name} Trust",
            Subdomain = sample.Subdomain,
            Status = sample.Status,
            TimeZone = businessUnit.TimeZone,
            DefaultCurrency = businessUnit.DefaultCurrency,
            DefaultCulture = businessUnit.DefaultCulture,
            DefaultMfaRequirement = MfaRequirement.Optional,
            MaximumFailedAccessAttempts = _security.MaximumFailedAccessAttempts,
            LockoutDurationMinutes = _security.LockoutMinutes,
            PasswordMinimumLength = _security.PasswordMinimumLength,
            SessionIdleTimeoutMinutes = _security.SessionIdleTimeoutMinutes,
            ContactEmail = sample.AdminEmail.ToLowerInvariant(),
            InvitedAtUtc = now,
            CreatedAtUtc = now,
            CreatedByUserId = Guid.Empty
        };

        // The Active sample gets a complete profile, so it is genuinely usable rather than
        // Active-but-incomplete - which is a state the submission rules would not have allowed.
        if (isActive)
        {
            tenant.RegistrationNumber = $"REG-{code}-2026";
            tenant.OrganisationType = "Charitable Trust";
            tenant.ContactPersonName = $"{sample.FirstName} {sample.LastName}";
            tenant.ContactPhoneCountryCode = "+91";
            tenant.ContactPhone = "9876543210";
            tenant.AddressLine1 = "1 Charity Road";
            tenant.City = "Chennai";
            tenant.State = "Tamil Nadu";
            tenant.Country = "India";
            tenant.PostalCode = "600001";
            tenant.EstablishedOn = now.AddYears(-5);
            tenant.InvitationAcceptedAtUtc = now;
            tenant.SubmittedAtUtc = now;
            tenant.ApprovedAtUtc = now;
            tenant.ActivatedAtUtc = now;
        }

        await context.Tenants.AddAsync(tenant, cancellationToken);

        // ---- The host that reaches it -------------------------------------------------------
        await context.TenantDomains.AddAsync(new TenantDomain
        {
            BusinessUnitId = businessUnit.Id,
            TenantId = tenant.Id,
            HostName = $"{sample.Subdomain}.{businessUnit.RootDomain}",
            DomainType = TenantDomainType.Subdomain,
            IsPrimary = true,
            // Verified on creation: the platform already controls the apex domain.
            IsVerified = true,
            VerifiedAtUtc = now,
            IsActive = true,
            CreatedAtUtc = now,
            CreatedByUserId = Guid.Empty
        }, cancellationToken);

        // ---- The lifecycle ladder -------------------------------------------------------------
        await context.TenantStatusHistory.AddAsync(new TenantStatusHistory
        {
            BusinessUnitId = businessUnit.Id,
            TenantId = tenant.Id,
            FromStatus = null,
            ToStatus = sample.Status,
            OccurredAtUtc = now,
            ActorDisplayName = "System",
            Notes = "Seeded sample organisation.",
            CreatedAtUtc = now,
            CreatedByUserId = Guid.Empty
        }, cancellationToken);

        // ---- Its own roles -----------------------------------------------------------------------
        var permissions = await context.Permissions
            .Where(permission => permission.Status == PermissionStatus.Active && !permission.IsPlatformOnly)
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);

        Role? tenantAdminRole = null;

        foreach (var definition in TenantRoleDefinitions.All)
        {
            var role = new Role
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                Code = definition.Code,
                NormalizedCode = definition.Code,
                Name = definition.Name,
                NormalizedName = definition.Name.ToUpperInvariant(),
                Description = definition.Description,
                RoleType = RoleType.Tenant,
                Status = RoleStatus.Active,
                IsSystemRole = true,
                IsDefaultRole = definition.IsDefault,
                GrantsAllTenantPermissions = definition.GrantsAll,
                IsPrivileged = definition.IsPrivileged,
                Priority = definition.Priority,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            };

            await context.Roles.AddAsync(role, cancellationToken);

            if (definition.Code == RoleCodes.TenantAdmin)
            {
                tenantAdminRole = role;
            }

            // A blanket-grant role needs no rows: the flag is the grant.
            if (definition.GrantsAll)
            {
                continue;
            }

            foreach (var permissionCode in definition.PermissionCodes)
            {
                if (!permissions.TryGetValue(permissionCode, out var permission))
                {
                    continue;
                }

                await context.RolePermissions.AddAsync(new RolePermission
                {
                    TenantId = tenant.Id,
                    BusinessUnitId = businessUnit.Id,
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                    PermissionCode = permission.Code,
                    GrantedAtUtc = now,
                    GrantedByUserId = Guid.Empty,
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.Empty
                }, cancellationToken);
            }
        }

        // ---- Its default navigation ---------------------------------------------------------------
        var menuDefinitions = await context.MenuDefinitions
            .Where(menu => !menu.IsPlatformOnly && menu.IsEnabledByDefault)
            .ToListAsync(cancellationToken);

        foreach (var definition in menuDefinitions)
        {
            await context.TenantMenus.AddAsync(new TenantMenu
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                MenuDefinitionId = definition.Id,
                IsEnabled = true,
                Status = MenuStatus.Active,
                IsSystemGenerated = true,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);
        }

        // ---- Its administrator ------------------------------------------------------------------------
        var email = sample.AdminEmail.Trim().ToLowerInvariant();
        var username = email.Split('@')[0];

        var admin = new User
        {
            TenantId = tenant.Id,
            BusinessUnitId = businessUnit.Id,
            Code = "USR-00001",
            FirstName = sample.FirstName,
            LastName = sample.LastName,
            DisplayName = $"{sample.FirstName} {sample.LastName}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            // Active only for the approved sample. The other two are genuinely Invited, with
            // no password at all, so they cannot be signed into until the link is used.
            EmailConfirmed = isActive,
            EmailConfirmedAtUtc = isActive ? now : null,
            Status = isActive ? UserStatus.Active : UserStatus.Invited,
            AccountCategory = UserAccountCategory.Employee,
            PrivilegeLevel = PrivilegeLevel.TenantAdmin,
            IsTenantAdmin = true,
            MfaRequirement = MfaRequirement.Inherited,
            AccessStartsAtUtc = now,
            LockoutEnabled = true,
            CredentialSetupMethod = CredentialSetupMethod.InvitationLink,
            CreatedAtUtc = now,
            CreatedByUserId = Guid.Empty
        };

        if (isActive && !string.IsNullOrWhiteSpace(_seed.SuperAdminPassword))
        {
            // Development convenience only, and only for the already-activated sample: it
            // shares the configured seed password so the flow can be walked without e-mail.
            admin.PasswordHash = passwordHasher.Hash(_seed.SuperAdminPassword);
            admin.PasswordChangedAtUtc = now;
        }

        await context.Users.AddAsync(admin, cancellationToken);

        if (tenantAdminRole is not null)
        {
            await context.UserRoles.AddAsync(new UserRole
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                UserId = admin.Id,
                RoleId = tenantAdminRole.Id,
                Status = UserRoleAssignmentStatus.Active,
                IsPrimary = true,
                AssignedAtUtc = now,
                AssignedByUserId = Guid.Empty,
                EffectiveFromUtc = now,
                Justification = "First administrator of the organisation.",
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);
        }

        // ---- The outstanding invitation, for the two that are not yet activated -----------------------
        if (!isActive)
        {
            var plaintext = tokenHasher.GenerateToken();

            await context.UserInvitations.AddAsync(new UserInvitation
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                UserId = admin.Id,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                InvitationType = InvitationType.TenantAdmin,
                InitialRoleId = tenantAdminRole?.Id,
                TokenHash = tokenHasher.Hash(plaintext),
                Reference = tokenHasher.GenerateReference("INV"),
                ExpiresAtUtc = now.AddDays(_security.InvitationExpiryDays),
                Status = InvitationStatus.Pending,
                InvitedByUserId = Guid.Empty,
                InvitedAtUtc = now,
                InvitationHostName = $"{sample.Subdomain}.{businessUnit.RootDomain}",
                LastSentAtUtc = now,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);

            // Logged so the flow can be walked without a mail relay. The token exists only in
            // this log line and in the hash - it is not recoverable afterwards.
            logger.LogInformation(
                "Seeded organisation {Code} ({Name}) with a PENDING invitation for {Email}. "
                + "Activation token: {Token}",
                code, sample.Name, email, plaintext);
        }
        else
        {
            logger.LogInformation(
                "Seeded organisation {Code} ({Name}), ACTIVE, administrator {Email}.",
                code, sample.Name, email);
        }
    }

    /// <summary>
    /// The Organisation's departments and units.
    ///
    /// These two lists are the entire content of the Department and Organisation Unit dropdowns
    /// on Create User and User Profile. Nothing else fills them - there is no fallback list in
    /// the client - so with the tables empty those two fields render as a select with one blank
    /// option, and the screen looks broken in a way no error message accounts for.
    ///
    /// IDEMPOTENT BY CODE, not by "does the Organisation exist". Each row is added only if no
    /// row with that code is already present in the Organisation, so this runs harmlessly on
    /// every start and adds only what a database is actually missing. Anything an administrator
    /// has since renamed, re-parented or archived is left exactly as they left it.
    /// </summary>
    private async Task SeedOrganisationStructureAsync(
        Tenant tenant, BusinessUnit businessUnit, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // (Code, Name, Description, DisplayOrder)
        (string Code, string Name, string Description, int Order)[] departments =
        [
            ("FIN",  "Finance",              "Receipting, reconciliation and statutory reporting.", 10),
            ("FR",   "Fundraising",          "Campaigns, donor acquisition and major gifts.",       20),
            ("PROG", "Programmes",           "Delivery of the charitable objects on the ground.",   30),
            ("OPS",  "Operations",           "Inventory, procurement and logistics.",               40),
            ("COMM", "Communications",       "Outbound messaging, complaints and supporter care.",  50),
            ("HR",   "People and Culture",   "Recruitment, onboarding and staff records.",          60),
            ("IT",   "Technology",           "Platform administration and information security.",   70)
        ];

        var existingDepartmentCodes = await context.Departments
            .IgnoreQueryFilters()
            .Where(department => department.TenantId == tenant.Id)
            .Select(department => department.Code)
            .ToListAsync(cancellationToken);

        var departmentCodeSet = existingDepartmentCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in departments)
        {
            if (departmentCodeSet.Contains(definition.Code))
            {
                continue;
            }

            await context.Departments.AddAsync(new Department
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                Status = RecordStatus.Active,
                DisplayOrder = definition.Order,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);
        }

        // (Code, Name, UnitType, City, State, DisplayOrder)
        (string Code, string Name, string UnitType, string City, string State, int Order)[] units =
        [
            ("HO",  "Head Office",       "Head Office",     "Chennai",   "Tamil Nadu",    10),
            ("CHN", "Chennai Branch",    "Branch",          "Chennai",   "Tamil Nadu",    20),
            ("BLR", "Bengaluru Branch",  "Branch",          "Bengaluru", "Karnataka",     30),
            ("MUM", "Mumbai Branch",     "Branch",          "Mumbai",    "Maharashtra",   40),
            ("DEL", "Delhi Branch",      "Regional Office", "New Delhi", "Delhi",         50),
            ("WH1", "Central Warehouse", "Warehouse",       "Chennai",   "Tamil Nadu",    60)
        ];

        var existingUnitCodes = await context.OrganisationUnits
            .IgnoreQueryFilters()
            .Where(unit => unit.TenantId == tenant.Id)
            .Select(unit => unit.Code)
            .ToListAsync(cancellationToken);

        var unitCodeSet = existingUnitCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in units)
        {
            if (unitCodeSet.Contains(definition.Code))
            {
                continue;
            }

            await context.OrganisationUnits.AddAsync(new OrganisationUnit
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                Code = definition.Code,
                Name = definition.Name,
                UnitType = definition.UnitType,
                City = definition.City,
                State = definition.State,
                Country = "India",
                TimeZone = tenant.TimeZone,
                Status = RecordStatus.Active,
                DisplayOrder = definition.Order,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);
        }

        logger.LogInformation(
            "Reconciled the structure of {Code}: {DepartmentCount} department(s) and "
            + "{UnitCount} unit(s) added.",
            tenant.Code,
            departments.Length - departmentCodeSet.Count(code =>
                departments.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))),
            units.Length - unitCodeSet.Count(code =>
                units.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>
    /// One account per Tenant role in the activated sample Organisation.
    ///
    /// Only for the ACTIVATED sample. An Organisation still working through onboarding has no
    /// business having a fundraising team already in it, and seeding one would make the
    /// onboarding demonstration look like it had skipped a step.
    /// </summary>
    private async Task SeedRoleAccountsAsync(
        Tenant tenant, BusinessUnit businessUnit, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_seed.RoleAccountPassword))
        {
            logger.LogInformation(
                "No role-account password configured, so the demonstration accounts were skipped.");

            return;
        }

        var roles = await context.Roles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId == tenant.Id)
            .ToDictionaryAsync(role => role.Code, StringComparer.Ordinal, cancellationToken);

        // Whoever is already here, by username. This runs on every start, so it must add only
        // what is missing and leave everything else alone - including a password somebody has
        // since changed.
        var present = await context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenant.Id)
            .Select(user => user.UserName!)
            .ToListAsync(cancellationToken);

        var existingUsernames = present.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var passwordHash = passwordHasher.Hash(_seed.RoleAccountPassword);
        var seeded = 0;

        foreach (var account in RoleAccounts)
        {
            if (existingUsernames.Contains(account.Username))
            {
                continue;
            }

            if (!roles.TryGetValue(account.RoleCode, out var role))
            {
                logger.LogWarning(
                    "Role {RoleCode} does not exist in {Tenant}, so {Username} was skipped.",
                    account.RoleCode, tenant.Code, account.Username);

                continue;
            }

            var email = account.Email.ToLowerInvariant();

            var user = new User
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                Code = $"USR-{present.Count + seeded + 1:D5}",
                FirstName = account.First,
                LastName = account.Last,
                DisplayName = $"{account.First} {account.Last}",
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                UserName = account.Username,
                NormalizedUserName = account.Username.ToUpperInvariant(),
                EmployeeNumber = $"HF-{account.Username.ToUpperInvariant()}",

                // ACTIVE WITH A PASSWORD, because the whole point of these is to be signed into.
                // The invitation flow is demonstrated by creating a further user through the
                // product, not by leaving the documented accounts unusable.
                EmailConfirmed = true,
                EmailConfirmedAtUtc = now,
                Status = UserStatus.Active,
                PasswordHash = passwordHash,
                PasswordChangedAtUtc = now,

                AccountCategory = UserAccountCategory.Employee,
                PrivilegeLevel = PrivilegeLevel.Standard,
                IsTenantAdmin = false,
                MfaRequirement = MfaRequirement.Inherited,
                AccessStartsAtUtc = now,
                LockoutEnabled = true,
                CredentialSetupMethod = CredentialSetupMethod.InvitationLink,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            };

            await context.Users.AddAsync(user, cancellationToken);

            await context.UserRoles.AddAsync(new UserRole
            {
                TenantId = tenant.Id,
                BusinessUnitId = businessUnit.Id,
                UserId = user.Id,
                RoleId = role.Id,
                Status = UserRoleAssignmentStatus.Active,
                IsPrimary = true,
                AssignedAtUtc = now,
                AssignedByUserId = Guid.Empty,
                EffectiveFromUtc = now,
                Justification = "Demonstration account for this role.",
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Empty
            }, cancellationToken);

            seeded++;
        }

        if (seeded > 0)
        {
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Seeded {Count} role account(s) in {Tenant}. They share the configured "
                + "role-account password.", seeded, tenant.Code);
        }
    }
}
