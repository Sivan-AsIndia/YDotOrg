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

        // TRACKING ASSETS AND READINESS CHECKLIST HAVE COME OFF THE SIDEBAR.
        //
        // THE SAME TEST AS THE FOUR RELATIONSHIP SCREENS BELOW: "is there already a way in", not
        // "is the screen wanted". Both are about ONE campaign and say nothing without one - a
        // readiness checklist with no campaign in it is a list of checks against nothing, and the
        // asset manager reached cold is every asset in the Organisation with no reason to be
        // looking at any of them. Each now has a way in from the campaign it belongs to:
        //
        //   Readiness Checklist   Campaign Register row action ("Readiness"), carrying ?ref
        //   Tracking Assets       Campaign detail header ("Tracking assets"), carrying ?campaign
        //
        // WITHDRAWN, NOT DELETED. The routes, the screens, their permissions and the CAM
        // endpoints behind them are untouched, and the start-up reconciliation retires each
        // definition and drops the row from every Organisation that already holds it - so this
        // reaches databases that have already run, not only fresh ones. Restoring one is
        // restoring its line.
        //
        // WHAT REMAINS UNDER CAMPAIGNS is the pair you arrive at cold: the register, and the way
        // to add to it.

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

        // FOLLOW-UP QUEUE BELONGS IN THE SIDEBAR, and it is the exception to the rule the four
        // withdrawn screens below follow.
        //
        // THAT RULE IS "IS THERE ALREADY A WAY IN", and the reason behind it is that a screen
        // about ONE record says nothing when it is reached cold. This one is not about one
        // record: it is a person's own list of the follow-up calls and e-mails they owe, which is
        // exactly the kind of thing somebody opens first thing in the morning without having
        // clicked a donor to get there - the same shape as Lead Work Queue above it.
        //
        // It was reachable only from My Leads, Follow-up Planner and Follow-up Execution, all of
        // which are places you arrive at AFTER choosing a record, so the queue could only be found
        // by somebody who had already stopped needing it.
        new("FR_FOLLOW_UP_QUEUE", "Follow-up Queue", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/follow-up-queue", "clock", "don.follow-up-planner.view", 20),

        // LEAD CAPTURE HAS COME OFF THE SIDEBAR, on the same test as the four screens below: the
        // way in already exists. Lead Work Queue carries a "Create Lead" button on both its
        // populated and its empty state, and that is the context the form belongs in - a capture
        // form reached cold is one whose result nobody is waiting for.
        //
        // WITHDRAWN, NOT DELETED. The route, the screen, `don.lead-capture.view` and the DON
        // endpoints are untouched; the start-up reconciliation retires the definition and drops
        // the row from every Organisation that already holds it. Restoring it is restoring the
        // one line.

        // DONOR 360 HAS NO MENU ENTRY, DELIBERATELY. It is not a screen anybody navigates to
        // cold - it is what opens when a donor is clicked in the Donor List, and it needs a
        // donor id to show anything at all. Offering it in the sidebar gave people a link to a
        // Donor 360 with no donor in it.
        //
        // WITHDRAWN, NOT DELETED. The route, the screen, its permissions and the DON endpoints
        // behind them are all untouched and still work; only the sidebar row is gone. The
        // start-up reconciliation retires the definition and removes the row from every
        // Organisation that already holds it, so this reaches existing databases rather than
        // only fresh ones. Restoring it is restoring this one line.

        new("FR_DONOR_LIST", "Donor List", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/donor-list", "list", "don.donors.view", 40),

        // FOUR RELATIONSHIP SCREENS HAVE COME OFF THE SIDEBAR: Consent Centre, Assignment Board,
        // Follow-up Planner and Identity Verification.
        //
        // THE TEST WAS "IS THERE ALREADY A WAY IN", not "is the screen wanted". Each of the four
        // is opened from the record it is about, which is the only context in which it says
        // anything - a consent centre with no donor in it, or an assignment board reached cold
        // rather than from the lead being assigned, is a screen a person has to re-establish
        // their place in. The four have these ways in:
        //
        //   Consent Centre         Donor 360, Donor List, Identity Verification
        //   Assignment Board       Lead Work Queue (assign, and reassign)
        //   Follow-up Planner      Donor 360
        //   Identity Verification  Donor 360
        //
        // WHAT REMAINS IS THE SET YOU ARRIVE AT COLD: the two lead screens, the donor register,
        // and Duplicate Review.
        //
        // DUPLICATE REVIEW STAYS, and it is the exception worth reading. Nothing links to it -
        // Donor 360's "Duplicate links" row opens its own Documents tab, not this screen - so
        // removing the row would leave a working screen with no way to reach it at all. It comes
        // off the sidebar the moment something opens it, and not before.
        //
        // WITHDRAWN, NOT DELETED, like the campaign nodes above: the routes, screens, permissions
        // and DON endpoints are untouched. The start-up reconciliation retires each definition and
        // drops the row from every Organisation that already holds it, so this reaches databases
        // that have already run rather than fresh ones only. Restoring one is restoring its line.

        new("FR_DUPLICATE_REVIEW", "Duplicate Review", "FR_RELATIONSHIPS", MenuLevel.ChildSubMenu, "DON",
            "/app/fundraising/relationships/duplicate-review", "copy", "don.duplicate-review.view", 80),

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
        // FOUR CHILDREN, AND THE DONATION FLOW DOCUMENT NAMES ALL FOUR - in this order, with
        // these words. Public Donation Initiation, Payment Queue, Support & Retry, Receipt
        // Register. Nothing else belongs under this parent.
        //
        // RECEIPT CORRECTION IS NOT ONE OF THEM ANY MORE. It was seeded here pointing at
        // /app/donations/receipt-correction on the strength of the document's subtitle - "donor
        // form -> payment -> receipt -> correction" - but the guide has no section for the screen
        // and its Quick Reference Summary names four. The Angular route is gone, so leaving the
        // node would put an item in the sidebar that resolves to page-not-found, which is the one
        // failure the note below is about. Dropping the code here retires the row on the next
        // start for every Organisation that already has it, overrides and role mappings intact.
        //
        // CORRECTING A RECEIPT IS STILL SOMETHING THE SERVER DOES. `pay.receipts.correct` and
        // POST /receipts/{id}/correct stay exactly as they are - a receipt-domain operation does
        // not need a menu node to exist.
        //
        // WHAT WAS HERE BEFORE AND IS NOT ANY MORE. Donation Register, Donation Intents, Refunds
        // and Chargebacks and Payment Gateway were all seeded as child nodes pointing at
        // /app/donations/donation-register, /app/donations/donation-intent-detail,
        // /app/donations/refund-and-chargeback-case and /app/donations/gateway-configuration -
        // four routes the Angular router no longer declares. A seeded node whose route does not
        // resolve is worse than a missing one: the item renders, someone clicks it, and the
        // application shows page-not-found for a screen the sidebar promised. The seeder retires
        // a code the catalogue drops rather than deleting it, so removing them here withdraws
        // them from every existing Organisation on the next start without taking any override or
        // role mapping with them.
        //
        // ALSO NOTE WHAT WAS MISSING. Public Donation Initiation - the entry form, the first
        // screen in the document's flow and the only one a donor ever sees - had no menu node at
        // all, so no amount of permission would put it in a sidebar.
        //
        // EVERY NODE CARRIES ITS PERMISSION. They were all null once, which meant the whole Money
        // branch appeared in every sidebar and every item in it led to a screen that answered
        // 403. A menu that shows somebody a page they cannot open is worse than one that hides
        // it: they raise a ticket about a broken screen rather than asking for access.
        new("MN_DONATIONS", "Donations and Payments", Money, MenuLevel.SubMenu, "PAY",
            null, "dollar-sign", "PAY.View", 20),

        // STEP 1 OF THE FLOW. The same component the donor reaches anonymously through the QR
        // code; inside the panel it renders the internal reference view the document shows in
        // Fig 2. The permission is CREATE rather than VIEW because the only thing this screen
        // does is start a donation - which, on the role matrix, is TENANT_ADMIN and INITIATOR.
        new("MN_PUBLIC_DONATION", "Public Donation Initiation", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/public-donation-initiation", "heart", "pay.intents.create", 10),

        // STEP 3. Fail and Pending only - a success goes straight to its receipt and never
        // appears here, which is what makes this a work list rather than a log.
        new("MN_PAYMENT_QUEUE", "Payment Queue", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/payment-event-queue", "inbox", "pay.payments.view-events", 20),

        // STEP 4. Where a retry that failed a second time goes. Safe-retry is the permission
        // because retrying an attempt whose outcome nobody has confirmed is what charges a donor
        // twice.
        new("MN_PAYMENT_SUPPORT", "Support & Retry", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/payment-support-and-safe-retry", "life-buoy", "pay.payments.safe-retry", 30),

        // STEP 5. Every receipt this organisation has issued, with the running totals the
        // document shows across the top of Fig 5.
        new("MN_RECEIPT_REGISTER", "Receipt Register", "MN_DONATIONS", MenuLevel.ChildSubMenu, "PAY",
            "/app/donations/receipt-register", "file-text", "pay.receipts.view", 50),

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
