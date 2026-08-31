using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The global navigation catalogue, seeded into <c>MenuDefinition</c> on first run.
///
/// WHY IT IS CODE RATHER THAN DATA. These rows describe what the deployed software actually
/// contains, and the routes have to match the Angular router or the sidebar produces dead
/// links. Keeping the list beside the permission codes means a new screen is added in one
/// place and the seeder reconciles it on the next start, rather than needing a hand-written
/// migration per screen.
///
/// THE ROUTES ARE REAL. Every <c>Route</c> below corresponds to a path in the client
/// <c>app.routes.ts</c>. A node with a null route is a grouping header that expands but does
/// not navigate.
///
/// PLATFORM NODES ARE MARKED. Anything with <c>IsPlatformOnly</c> is only ever returned to
/// SuperAdmin, however a Tenant configures its roles.
/// </summary>
/// WHAT IS SWITCHED OFF, AND WHY. Finance, Communications and Supply carry
/// <c>IsEnabledByDefault: false</c>: no FIN, COM or INV service exists, so every screen behind
/// them would open on nothing. Three Workspace screens are off for a sharper reason - they call
/// no endpoint at all and render invented names, which is the last thing that should appear in
/// a demonstration. Turning a branch back on is a one-word edit here, or a TenantMenu override
/// per Organisation.
///
/// NOTE THE TWO PARENTS THAT STAY ON. "Money" is the parent of the PAY branch and "Workspace"
/// is the parent of Global Search, so disabling either would take a working module down with
/// the dead one.
public static class MenuCatalogue
{
    /// <summary>One row of the catalogue, flattened. <c>ParentCode</c> builds the tree.</summary>
    public sealed record MenuSeed(
        string Code,
        string Name,
        string? ParentCode,
        MenuLevel Level,
        string ModuleCode,
        string? Route,
        string? Icon,
        string? RequiredPermissionCode,
        int DisplayOrder,
        bool IsPlatformOnly = false,
        bool IsEnabledByDefault = true,
        bool IsMandatory = false);

    // ---- Top-level menu codes, referenced as parents below --------------------------------
    public const string Dashboard = "DASHBOARD";
    public const string Workspace = "WORKSPACE";
    public const string Platform = "PLATFORM";
    public const string Administration = "ADMINISTRATION";
    public const string Organisation = "ORGANISATION";
    public const string Fundraising = "FUNDRAISING";
    public const string Money = "MONEY";
    public const string Communications = "COMMUNICATIONS";
    public const string Supply = "SUPPLY";
    public const string Masters = "MASTERS";

    /// <summary>
    /// The whole catalogue. Order within the list does not matter; <c>DisplayOrder</c> and
    /// <c>ParentCode</c> decide the shape.
    /// </summary>
    public static readonly IReadOnlyList<MenuSeed> All =
    [
        // ============ Dashboard =============================================================
        new(Dashboard, "Dashboard", null, MenuLevel.Menu, "CORE",
            "/app/dashboard", "grid", null, 10, IsMandatory: true),

        // ============ Platform: SuperAdmin only ==============================================
        // This whole branch is invisible to every Tenant user, which is what keeps
        // Organisation administration out of a TenantAdmin sidebar.
        new(Platform, "Platform", null, MenuLevel.Menu, "PLATFORM",
            null, "globe", PermissionCodes.Platform.TenantsView, 20, IsPlatformOnly: true),

        new("PLATFORM_ORGANISATIONS", "Organisations", Platform, MenuLevel.SubMenu, "PLATFORM",
            "/app/administration/organisation/directory", "building",
            PermissionCodes.Platform.TenantsView, 10, IsPlatformOnly: true),

        new("PLATFORM_ORG_CREATE", "Create Organisation", "PLATFORM_ORGANISATIONS",
            MenuLevel.ChildSubMenu, "PLATFORM",
            "/app/administration/organisation/setup-wizard", "plus-circle",
            PermissionCodes.Platform.TenantsCreate, 10, IsPlatformOnly: true),

        new("PLATFORM_ORG_APPROVALS", "Approval Queue", "PLATFORM_ORGANISATIONS",
            MenuLevel.ChildSubMenu, "PLATFORM",
            "/app/administration/organisation/registration-verification", "check-circle",
            PermissionCodes.Platform.TenantsReview, 20, IsPlatformOnly: true),

        new("PLATFORM_BUSINESS_UNIT", "Business Unit", Platform, MenuLevel.SubMenu, "PLATFORM",
            "/app/platform/business-unit", "briefcase",
            PermissionCodes.Platform.BusinessUnitsView, 20, IsPlatformOnly: true),

        new("PLATFORM_PERMISSION_CATALOGUE", "Permission Catalogue", Platform, MenuLevel.SubMenu,
            "PLATFORM", "/app/platform/permission-catalogue", "key",
            PermissionCodes.Platform.PermissionCatalogueManage, 30, IsPlatformOnly: true),

        new("PLATFORM_MENU_CATALOGUE", "Menu Catalogue", Platform, MenuLevel.SubMenu, "PLATFORM",
            "/app/platform/menu-catalogue", "list", PermissionCodes.Platform.MenuCatalogueManage,
            40, IsPlatformOnly: true),

        new("PLATFORM_AUDIT", "Platform Audit", Platform, MenuLevel.SubMenu, "PLATFORM",
            "/app/platform/audit", "shield", PermissionCodes.Platform.PlatformAuditView,
            50, IsPlatformOnly: true),

        // ============ Administration (IAM) =====================================================
        //
        // THE HEADING CARRIES NO PERMISSION, and that is deliberate. It used to require
        // IAM.View, which quietly defeated the one node underneath it that is meant to be
        // universal: ADMIN_MY_SECURITY is marked IsMandatory with no permission of its own,
        // because everybody must be able to change their own password and manage their second
        // factor. But a child whose parent was filtered out is never reached by BuildTree, so
        // the parent's gate hid it anyway - and DONOR_PORTAL_USER, the one seeded role without
        // IAM.View, had no way to reach My Security at all.
        //
        // Dropping the gate costs nothing, because every OTHER child names its own permission
        // and filter 8 removes the heading entirely when none of them survives. A grouping
        // header should never be stricter than its most permissive child.
        new(Administration, "Administration", null, MenuLevel.Menu, "IAM",
            null, "settings", null, 30),

        // ---- Access and identity -------------------------------------------------------------
        new("ADMIN_ACCESS", "Access and Identity", Administration, MenuLevel.SubMenu, "IAM",
            null, "users", PermissionCodes.UsersView, 10),

        new("ADMIN_USER_DIRECTORY", "User Directory", "ADMIN_ACCESS", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/user-directory", "list",
            PermissionCodes.UsersView, 10),

        new("ADMIN_CREATE_USER", "Invite or Create User", "ADMIN_ACCESS", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/create-user", "user-plus",
            PermissionCodes.UsersCreate, 20),

        new("ADMIN_USER_PROFILE", "User Profile and Access", "ADMIN_ACCESS", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/user-profile-and-access", "user",
            PermissionCodes.UsersView, 30),

        new("ADMIN_ROLE_CATALOGUE", "Roles and Permissions", "ADMIN_ACCESS", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/role-and-permission-catalogue", "shield",
            PermissionCodes.RolesView, 40),

        new("ADMIN_MENU_MAPPING", "Menu Mapping", "ADMIN_ACCESS", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/menu-mapping", "sliders",
            PermissionCodes.MenusView, 50),

        new("ADMIN_BULK_USERS", "Bulk User Administration", "ADMIN_ACCESS", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/users/bulk-actions", "layers",
            PermissionCodes.UsersBulkAdminister, 60),

        // ---- Governance ------------------------------------------------------------------------
        new("ADMIN_GOVERNANCE", "Access Governance", Administration, MenuLevel.SubMenu, "IAM",
            null, "clipboard", PermissionCodes.AccessRequestsView, 20),

        new("ADMIN_ACCESS_REQUESTS", "Access Requests", "ADMIN_GOVERNANCE", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/access-request-and-approval", "inbox",
            PermissionCodes.AccessRequestsView, 10),

        new("ADMIN_ACCESS_REVIEWS", "Access Reviews", "ADMIN_GOVERNANCE", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/access-review-campaign", "check-square",
            PermissionCodes.AccessReviewsView, 20),

        new("ADMIN_ACCESS_PREVIEW", "Access Preview", "ADMIN_GOVERNANCE", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/access/access-preview", "eye",
            PermissionCodes.PermissionsView, 30),

        new("ADMIN_AUDIT", "Audit Trail", "ADMIN_GOVERNANCE", MenuLevel.ChildSubMenu, "IAM",
            "/app/administration/audit", "file-text", PermissionCodes.AuditView, 40),

        // ---- My account. Mandatory: everybody needs somewhere to manage their own security. ----
        new("ADMIN_MY_SECURITY", "My Security", Administration, MenuLevel.SubMenu, "IAM",
            "/app/administration/access/my-security", "lock", null, 30, IsMandatory: true),

        // ============ Organisation (the Tenant own profile) =======================================
        new(Organisation, "Organisation", null, MenuLevel.Menu, "IAM",
            null, "home", PermissionCodes.OrganisationView, 40),

        new("ORG_PROFILE", "Organisation Profile", Organisation, MenuLevel.SubMenu, "IAM",
            "/app/administration/organisation/details", "info",
            PermissionCodes.OrganisationView, 10),

        new("ORG_DEPARTMENTS", "Departments", Organisation, MenuLevel.SubMenu, "IAM",
            "/app/administration/organisation/departments", "git-branch",
            PermissionCodes.OrganisationManageDepartments, 20),

        new("ORG_UNITS", "Branches and Units", Organisation, MenuLevel.SubMenu, "IAM",
            "/app/administration/organisation/units", "map-pin",
            PermissionCodes.OrganisationManageUnits, 30),

        new("ORG_SETTINGS", "Organisation Settings", Organisation, MenuLevel.SubMenu, "IAM",
            "/app/administration/organisation/settings", "sliders",
            PermissionCodes.OrganisationManageSettings, 40),

        // ============ Workspace ======================================================================
        new(Workspace, "Workspace", null, MenuLevel.Menu, "UX", null, "layout", null, 50),

        new("WS_MY_WORKSPACE", "My Workspace", Workspace, MenuLevel.SubMenu, "UX",
            "/app/workspace/my-workspace", "home", null, 10, IsEnabledByDefault: false),

        new("WS_EXECUTIVE_DASHBOARD", "Executive Dashboard", Workspace, MenuLevel.SubMenu, "UX",
            "/app/workspace/executive-dashboard", "trending-up", null, 20, IsEnabledByDefault: false),

        // SWITCHED OFF, for the same reason as its three siblings above. The component injects
        // no service of any kind - it renders invented records held in component state - and it
        // additionally carried NO permission, so it was the one screen in the whole catalogue
        // that appeared in EVERY sidebar: Standard User, Volunteer and Donor Portal User
        // included. Fabricated data behind an ungated menu item is the worst combination in the
        // build, and it was the only thing keeping the Workspace branch alive.
        //
        // Turning it back on is this one word, once the screen talks to a real search endpoint.
        new("WS_GLOBAL_SEARCH", "Global Search", Workspace, MenuLevel.SubMenu, "UX",
            "/app/workspace/global-search", "search", null, 30, IsEnabledByDefault: false),

        new("WS_NOTIFICATIONS", "Notification Centre", Workspace, MenuLevel.SubMenu, "UX",
            "/app/workspace/notification-centre", "bell", null, 40, IsEnabledByDefault: false),

        // ============ Fundraising ======================================================================
        //
        // Owned by the CAM and DON services. Listed here because IAM is the only service that
        // knows the caller's full permission set and therefore the only one that can build a
        // menu spanning every module.
        //
        // EVERY NODE NOW NAMES A PERMISSION. They all carried null, which meant the whole
        // Fundraising branch appeared in every sidebar - including a Volunteer's and a Donor
        // Portal user's - and then answered 403 on the click, because the endpoints behind them
        // do check. A menu that offers a screen the caller cannot open is worse than no menu.
        //
        // A PARENT NAMES THE SECTION CODE, a child names the code its own screen needs. That
        // way losing one screen collapses one row rather than hiding the branch that holds the
        // others.
        new(Fundraising, "Fundraising", null, MenuLevel.Menu, "CAM",
            null, "heart", PermissionCodes.SectionCam, 60),

        new("FR_CAMPAIGNS", "Campaigns", Fundraising, MenuLevel.SubMenu, "CAM",
            null, "flag", "cam.campaigns.view", 10),

        new("FR_CAMPAIGN_REGISTER", "Campaign Register", "FR_CAMPAIGNS", MenuLevel.ChildSubMenu, "CAM",
            "/app/fundraising/campaigns/campaign-register", "list", "cam.campaigns.view", 10),

        // The wizard requires CREATE, not view: somebody who may read the register but not add
        // to it should not be offered a Create Campaign link.
        new("FR_CAMPAIGN_WIZARD", "Create Campaign", "FR_CAMPAIGNS", MenuLevel.ChildSubMenu, "CAM",
            "/app/fundraising/campaigns/campaign-wizard", "plus-circle", "cam.campaigns.create", 20),

        new("FR_TRACKING_ASSETS", "Tracking Assets", "FR_CAMPAIGNS", MenuLevel.ChildSubMenu, "CAM",
            "/app/fundraising/campaigns/tracking-asset-manager", "tag", "cam.tracking-assets.view", 30),

        // NEW. The readiness checklist is a working screen in the campaign module - it is what
        // stands between a campaign and going live - and it had no menu entry at all.
        new("FR_CAMPAIGN_READINESS", "Readiness Checklist", "FR_CAMPAIGNS", MenuLevel.ChildSubMenu, "CAM",
            "/app/fundraising/campaigns/campaign-readiness-checklist", "check-square",
            "cam.readiness.view", 40),

        // THESE TWO ARE NOW ENABLED, and each guards on its OWN permission.
        //
        // They were disabled because they had no service behind them - the screens read seeded
        // arrays, so putting them in a sidebar would have offered people figures that came from a
        // bundle. Both now have a CAM slice, its own tables and its own permissions, so the reason
        // for hiding them has gone.
        //
        // THE GUARD CHANGED FROM cam.campaigns.view, which was the wrong permission twice over: it
        // let anybody who could see a campaign open its budgets, and it meant a finance officer with
        // no campaign permissions could not reach the budget screen at all.
        // ATTRIBUTION EXPLORER AND BUDGET AND TARGETS ARE NOT OFFERED YET.
        //
        // Withdrawn rather than deleted: the screens, their permissions and the CAM endpoints
        // behind them all still exist and still work. Only the way in is gone, so bringing them
        // back is restoring these two lines and nothing else.
        //
        // They are not merely disabled by default, because a disabled node can still be switched
        // on per Organisation from Menu Mapping - and an operator switching on a screen that is
        // not ready is exactly what this is meant to prevent. The reconciliation on start-up
        // removes the rows from every Organisation that already holds them.

        new("FR_RELATIONSHIPS", "Donors and Leads", Fundraising, MenuLevel.SubMenu, "DON",
            null, "users", PermissionCodes.SectionDon, 20),

        new("FR_LEAD_QUEUE", "Lead Work Queue", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/lead-work-queue", "inbox", "don.lead-work-queue.view", 10),

        new("FR_LEAD_CAPTURE", "Lead Capture", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/lead-capture", "user-plus", "don.lead-capture.view", 20),

        new("FR_DONOR_360", "Donor 360", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/donor-360", "user", "don.donor-360.view", 30),

        new("FR_DONOR_LIST", "Donor List", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/donor-list", "list", "don.donors.view", 40),

        new("FR_CONSENT", "Consent Centre", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/consent-and-preference-centre", "check-circle",
            "don.consent-and-preference-centre.view", 50),

        new("FR_ASSIGNMENT", "Assignment Board", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/assignment-board", "shuffle", "don.assignment-board.view", 60),

        new("FR_FOLLOW_UP", "Follow-up Planner", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/follow-up-planner", "calendar", "don.follow-up-planner.view", 70),

        new("FR_DUPLICATE_REVIEW", "Duplicate Review", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/duplicate-review", "copy", "don.duplicate-review.view", 80),

        new("FR_IDENTITY_VERIFICATION", "Identity Verification", "FR_RELATIONSHIPS",
            MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/donor-identity-verification", "shield",
            "don.donor-identity-verification.view", 90),

        // ============ Money ==============================================================================
        new(Money, "Money", null, MenuLevel.Menu, "FIN", null, "credit-card", null, 70),

        new("MN_FINANCE", "Finance", Money, MenuLevel.SubMenu, "FIN", null, "book", null, 10, IsEnabledByDefault: false),

        // THESE TWO CARRIED NO PERMISSION, so the Finance branch appeared in every sidebar -
        // a Volunteer's included - and every item in it led to a screen they could not use. The
        // note below on the PAY nodes explains why that is worse than hiding them.
        //
        // Both are guarded on PAY's reconciliation permission, which is what the work on these
        // screens actually amounts to: matching what the gateway reported against what the bank
        // received. Finance has no service of its own, so borrowing PAY's permission is honest -
        // inventing a `fin.*` code that nothing enforces would not be.
        new("MN_FINANCE_WORKBENCH", "Finance Workbench", "MN_FINANCE", MenuLevel.ChildSubMenu, "FIN",
            "/app/money/finance/finance-workbench", "layout", "pay.donations.reconcile", 10, IsEnabledByDefault: false),

        new("MN_RECONCILIATION", "Reconciliation", "MN_FINANCE", MenuLevel.ChildSubMenu, "FIN",
            "/app/money/finance/reconciliation-workspace", "refresh-cw", "pay.donations.reconcile", 20, IsEnabledByDefault: false),

        // Recording a gift with no gateway to corroborate it. It lives under Finance because
        // that is who does it, but the permission is PAY's - the module that owns the donation
        // it creates.
        new("MN_OFFLINE_DONATION", "Offline Donation Entry", "MN_FINANCE", MenuLevel.ChildSubMenu, "FIN",
            "/app/money/finance/offline-donation-entry", "edit",
            "pay.donations.record-offline", 30, IsEnabledByDefault: false),

        // ---- Donations and payments (PAY) ---------------------------------------------------
        //
        // EVERY NODE BELOW NOW CARRIES ITS PERMISSION. They were all null, which meant the whole
        // Money branch appeared in every sidebar - including a Volunteer's - and every item in it
        // led to a screen that answered 403. A menu that shows somebody a page they cannot open
        // is worse than one that hides it: they raise a ticket about a broken screen rather than
        // asking for access.
        new("MN_DONATIONS", "Donations and Payments", Money, MenuLevel.SubMenu, "PAY",
            null, "dollar-sign", "PAY.View", 20),

        new("MN_DONATION_REGISTER", "Donation Register", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/donation-register", "list", "pay.donations.view", 10),

        new("MN_DONATION_INTENTS", "Donation Intents", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/donation-intent-detail", "clock", "pay.intents.view", 20),

        new("MN_RECEIPT_REGISTER", "Receipt Register", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/receipt-register", "file-text", "pay.receipts.view", 30),

        // Section 23. Separate from the event queue because they answer different questions:
        // this one is "which donors are stuck?", the queue is "what did the gateway tell us?".
        new("MN_PAYMENT_SUPPORT", "Payment Support", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/payment-support-and-safe-retry", "life-buoy", "pay.payments.safe-retry", 40),

        new("MN_PAYMENT_QUEUE", "Payment Event Queue", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/payment-event-queue", "inbox", "pay.payments.view-events", 50),

        new("MN_REFUNDS", "Refunds and Chargebacks", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/refund-and-chargeback-case", "corner-up-left", "pay.refunds.view", 60),

        // THE MOST CONSEQUENTIAL SCREEN IN THE MODULE: it decides which merchant account an
        // organisation's donations settle into. It was additionally off by default; that second
        // gate is gone because an Organisation administrator has to be able to configure their
        // own gateway. The permission is the guard, and it is a real one, so a Volunteer or a
        // Fundraising Officer never sees this screen.
        //
        // WHO ACTUALLY HOLDS pay.gateway.view: TENANT_ADMIN (via GrantsAllTenantPermissions)
        // and AUDITOR, who needs to see which account the money settles into to audit it.
        //
        // NOT PAYMENT_OPERATIONS - the note on RoleCodes.PaymentOperations explains why:
        // choosing the merchant account is a different kind of decision from processing the
        // payments that reach it. An earlier version of this comment claimed the opposite and
        // was wrong in both directions; it is corrected here because the sentence was being
        // read as a statement of the seeded grant.
        new("MN_GATEWAY_CONFIG", "Payment Gateway", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/gateway-configuration", "credit-card", "pay.gateway.view", 70),

        // ============ Communications =========================================================================
        new(Communications, "Communications", null, MenuLevel.Menu, "COM", null, "message-circle", null, 80, IsEnabledByDefault: false),

        new("CM_INBOX", "Unified Inbox", Communications, MenuLevel.SubMenu, "COM",
            "/app/communications/unified-inbox", "inbox", null, 10, IsEnabledByDefault: false),

        new("CM_TEMPLATES", "Template Catalogue", Communications, MenuLevel.SubMenu, "COM",
            "/app/communications/template-catalogue", "file", null, 20, IsEnabledByDefault: false),

        new("CM_COMPOSER", "Outbound Composer", Communications, MenuLevel.SubMenu, "COM",
            "/app/communications/outbound-message-composer", "send", null, 30, IsEnabledByDefault: false),

        // ============ Supply =================================================================================
        new(Supply, "Supply", null, MenuLevel.Menu, "INV", null, "package", null, 90, IsEnabledByDefault: false),

        new("SP_INVENTORY_OVERVIEW", "Inventory Overview", Supply, MenuLevel.SubMenu, "INV",
            "/app/supply/inventory/inventory-overview", "grid", null, 10, IsEnabledByDefault: false),

        new("SP_BATCH_LEDGER", "Batch Ledger", Supply, MenuLevel.SubMenu, "INV",
            "/app/supply/inventory/batch-ledger", "book", null, 20, IsEnabledByDefault: false),

        new("SP_STOCK_MOVEMENT", "Stock Movement", Supply, MenuLevel.SubMenu, "INV",
            "/app/supply/inventory/stock-movement-form", "truck", null, 30, IsEnabledByDefault: false),

        // ============ Masters ==================================================================================
        //
        // The global master catalogue, served by IAM since GlobalMaster was merged into it.
        // EVERY NODE NOW NAMES A PERMISSION. They used to carry null, which meant the whole
        // branch appeared in every sidebar including a Volunteer's - and then answered 403 on
        // the click, because the endpoints behind them do check. A menu that offers a screen
        // the caller cannot open is worse than no menu at all.
        new(Masters, "Masters", null, MenuLevel.Menu, "GM",
            null, "database", PermissionCodes.GlobalMaster.Section, 100),

        new("MS_COUNTRY", "Country", Masters, MenuLevel.SubMenu, "GM",
            "/app/masters/country", "flag", PermissionCodes.GlobalMaster.CountriesView, 10),

        new("MS_STATE", "State", Masters, MenuLevel.SubMenu, "GM",
            "/app/masters/state", "map", PermissionCodes.GlobalMaster.StatesView, 20),

        new("MS_CITY", "City", Masters, MenuLevel.SubMenu, "GM",
            "/app/masters/city", "map-pin", PermissionCodes.GlobalMaster.CitiesView, 30),

        new("MS_CURRENCY", "Currency", Masters, MenuLevel.SubMenu, "GM",
            "/app/masters/currency", "dollar-sign", PermissionCodes.GlobalMaster.CurrenciesView, 40),

        new("MS_TIMEZONE", "Time Zone", Masters, MenuLevel.SubMenu, "GM",
            "/app/masters/timezone", "clock", PermissionCodes.GlobalMaster.TimeZonesView, 50)
    ];

    /// <summary>The nodes only SuperAdmin ever sees.</summary>
    public static IEnumerable<MenuSeed> PlatformNodes => All.Where(node => node.IsPlatformOnly);

    /// <summary>The nodes a new Organisation gets switched on automatically.</summary>
    public static IEnumerable<MenuSeed> TenantDefaultNodes =>
        All.Where(node => !node.IsPlatformOnly && node.IsEnabledByDefault);
}
