namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// Every permission code IAM owns, plus the platform-level codes that govern BusinessUnit
/// and Organisation administration.
///
/// THESE STRINGS ARE A CONTRACT. They are seeded into the permission table, attached to
/// roles, written into tokens as <c>permission</c> claims, and compiled into
/// <c>[HasPermission(...)]</c> attributes. Once published, a code may be retired but never
/// renamed: a renamed code silently unreachable is far worse than a retired one that is
/// visibly gone.
///
/// PLATFORM CODES ARE DIFFERENT. Everything under <see cref="Platform"/> is marked
/// <c>IsPlatformOnly</c> in the catalogue, which means the seeder refuses to attach it to a
/// Tenant role no matter what a role edit asks for. That is what stops a TenantAdmin being
/// handed the ability to create or approve Organisations.
/// </summary>
public static class PermissionCodes
{
    /// <summary>Section-level view permission. Every IAM screen requires it as a baseline.</summary>
    public const string IamView = "IAM.View";

    /// <summary>
    /// The section codes the OTHER services own, named here because the menu catalogue in this
    /// assembly has to reference them.
    ///
    /// They are literals rather than a project reference for the reason the whole
    /// <c>ModulePermissionCatalogue</c> exists: IAM cannot depend on CAM or DON - they depend on
    /// it - so the shared strings live on this side and the other services mirror them.
    /// </summary>
    public const string SectionCam = "CAM.View";

    public const string SectionDon = "DON.View";

    public const string SectionPay = "PAY.View";

    // ---- Users --------------------------------------------------------------------------
    public const string UsersView = "iam.users.view";
    public const string UsersCreate = "iam.users.create";
    public const string UsersEdit = "iam.users.edit";
    public const string UsersSubmit = "iam.users.submit";
    public const string UsersApprove = "iam.users.approve";
    public const string UsersCancel = "iam.users.cancel";
    public const string UsersArchive = "iam.users.archive";
    public const string UsersExport = "iam.users.export";
    public const string UsersInvite = "iam.users.invite";
    public const string UsersSuspend = "iam.users.suspend";
    public const string UsersReactivate = "iam.users.reactivate";
    public const string UsersDeactivate = "iam.users.deactivate";
    public const string UsersResetPassword = "iam.users.reset-password";
    public const string UsersUnlock = "iam.users.unlock";
    public const string UsersBulkAdminister = "iam.users.bulk-administer";
    public const string UsersChangeLoginIdentifier = "iam.users.change-login-identifier";

    /// <summary>Unmasks e-mail and mobile in the directory, detail and export views.</summary>
    public const string UsersViewSensitiveContact = "iam.users.view-sensitive-contact";

    // ---- Roles ---------------------------------------------------------------------------
    public const string RolesView = "iam.roles.view";
    public const string RolesCreate = "iam.roles.create";
    public const string RolesEdit = "iam.roles.edit";
    public const string RolesDelete = "iam.roles.delete";
    public const string RolesActivate = "iam.roles.activate";
    public const string RolesDeactivate = "iam.roles.deactivate";
    public const string RolesAssignPermissions = "iam.roles.assign-permissions";
    public const string RolesAssignUsers = "iam.roles.assign-users";
    public const string RolesManageIncompatibility = "iam.roles.manage-incompatibility";
    public const string RolesExport = "iam.roles.export";

    // ---- Permissions -----------------------------------------------------------------------
    public const string PermissionsView = "iam.permissions.view";
    public const string PermissionsAssign = "iam.permissions.assign";
    public const string PermissionsRevoke = "iam.permissions.revoke";
    public const string PermissionsExport = "iam.permissions.export";

    // ---- Menu -------------------------------------------------------------------------------
    public const string MenusView = "iam.menus.view";
    public const string MenusConfigure = "iam.menus.configure";
    public const string MenusMapRoles = "iam.menus.map-roles";

    // ---- User security, devices and sessions --------------------------------------------------
    public const string UserSecurityView = "iam.user-security.view";
    public const string UserSecurityRevokeSession = "iam.user-security.revoke-session";
    public const string UserSecurityRevokeDevice = "iam.user-security.revoke-device";
    public const string UserSecurityResetMfa = "iam.user-security.reset-mfa";
    public const string UserSecurityForceSignOut = "iam.user-security.force-sign-out";

    // ---- Access requests -----------------------------------------------------------------------
    public const string AccessRequestsView = "iam.access-requests.view";
    public const string AccessRequestsCreate = "iam.access-requests.create";
    public const string AccessRequestsSubmit = "iam.access-requests.submit";
    public const string AccessRequestsApprove = "iam.access-requests.approve";
    public const string AccessRequestsReject = "iam.access-requests.reject";
    public const string AccessRequestsWithdraw = "iam.access-requests.withdraw";

    // ---- Access reviews -------------------------------------------------------------------------
    public const string AccessReviewsView = "iam.access-reviews.view";
    public const string AccessReviewsCreate = "iam.access-reviews.create";
    public const string AccessReviewsDecide = "iam.access-reviews.decide";
    public const string AccessReviewsCancel = "iam.access-reviews.cancel";
    public const string AccessReviewsExport = "iam.access-reviews.export";

    // ---- Audit -----------------------------------------------------------------------------------
    public const string AuditView = "iam.audit.view";
    public const string AuditExport = "iam.audit.export";

    /// <summary>Reads the sensitive audit rows. Separate because they can contain more detail.</summary>
    public const string AuditViewSensitive = "iam.audit.view-sensitive";

    // ---- Organisation profile, from inside the Organisation ----------------------------------------
    public const string OrganisationView = "iam.organisation.view";
    public const string OrganisationEdit = "iam.organisation.edit";
    public const string OrganisationSubmit = "iam.organisation.submit";
    public const string OrganisationUploadDocument = "iam.organisation.upload-document";
    public const string OrganisationManageSettings = "iam.organisation.manage-settings";
    public const string OrganisationManageDepartments = "iam.organisation.manage-departments";
    public const string OrganisationManageUnits = "iam.organisation.manage-units";

    // ---- Payment gateway configuration ----------------------------------------------------------
    //
    // THE MOST CONSEQUENTIAL SET OF CODES IN THIS FILE, and it is worth being blunt about why:
    // they decide which merchant account an Organisation's donations settle into - which is to
    // say, whose bank account the money reaches.
    //
    // THEY BELONG TO ADMINISTRATORS ONLY. SuperAdmin holds every code by virtue of scope and
    // TenantAdmin by GrantsAllTenantPermissions; INITIATOR and APPROVER are excluded explicitly
    // in RoleAccessProfiles.AdministratorOnlyCodes rather than by accident of the action
    // derivation, which would otherwise hand "manage" to the maker as an ordinary operate verb.
    //
    // ALL FOUR ARE SENSITIVE, INCLUDING VIEW. The view response carries no secret - only a
    // four-character hint - but it does say which provider takes an Organisation's money and
    // where its webhook points, and that is worth an enhanced audit row on its own.
    public const string PaymentGatewaysView = "iam.payment-gateways.view";
    public const string PaymentGatewaysManage = "iam.payment-gateways.manage";
    public const string PaymentGatewaysDelete = "iam.payment-gateways.delete";

    /// <summary>Presses Test, which reaches out to the provider with the stored credentials.</summary>
    public const string PaymentGatewaysTest = "iam.payment-gateways.test";

    /// <summary>
    /// The global master catalogue: Country, StateProvince, City, Currency and TimeZone.
    ///
    /// MIGRATED FROM THE STANDALONE GlobalMaster SERVICE, which is why these codes are here
    /// rather than in <c>ModulePermissionCatalogue</c> — that file is for the codes OTHER
    /// services enforce, and IAM now enforces these itself.
    ///
    /// THEY ARE TENANT-ASSIGNABLE, NOT PLATFORM-ONLY, and the distinction is worth stating
    /// because the data is shared. Holding <c>gm.countries.create</c> lets an Organisation add
    /// a country OF ITS OWN — a row stamped with its TenantId that only it can see. It never
    /// lets it touch a platform row: <c>GlobalMasterEntity.IsPlatformRow</c> is checked in
    /// every write handler and refuses the edit for anybody but SuperAdmin, whatever
    /// permissions the caller holds.
    /// </summary>
    public static class GlobalMaster
    {
        /// <summary>Section-level view permission. Every Masters screen requires it as a baseline.</summary>
        public const string Section = "GM.View";

        // ---- Countries -----------------------------------------------------------------
        public const string CountriesView = "gm.countries.view";
        public const string CountriesCreate = "gm.countries.create";
        public const string CountriesEdit = "gm.countries.edit";
        public const string CountriesDelete = "gm.countries.delete";
        public const string CountriesActivate = "gm.countries.activate";
        public const string CountriesDeactivate = "gm.countries.deactivate";
        public const string CountriesExport = "gm.countries.export";

        // ---- States and provinces ----------------------------------------------------------
        public const string StatesView = "gm.states.view";
        public const string StatesCreate = "gm.states.create";
        public const string StatesEdit = "gm.states.edit";
        public const string StatesDelete = "gm.states.delete";
        public const string StatesActivate = "gm.states.activate";
        public const string StatesDeactivate = "gm.states.deactivate";
        public const string StatesExport = "gm.states.export";

        // ---- Cities ------------------------------------------------------------------------------
        public const string CitiesView = "gm.cities.view";
        public const string CitiesCreate = "gm.cities.create";
        public const string CitiesEdit = "gm.cities.edit";
        public const string CitiesDelete = "gm.cities.delete";
        public const string CitiesActivate = "gm.cities.activate";
        public const string CitiesDeactivate = "gm.cities.deactivate";
        public const string CitiesExport = "gm.cities.export";

        // ---- Currencies ------------------------------------------------------------------------------
        public const string CurrenciesView = "gm.currencies.view";
        public const string CurrenciesCreate = "gm.currencies.create";
        public const string CurrenciesEdit = "gm.currencies.edit";
        public const string CurrenciesDelete = "gm.currencies.delete";
        public const string CurrenciesActivate = "gm.currencies.activate";
        public const string CurrenciesDeactivate = "gm.currencies.deactivate";
        public const string CurrenciesExport = "gm.currencies.export";

        // ---- Time zones ------------------------------------------------------------------------------
        public const string TimeZonesView = "gm.timezones.view";
        public const string TimeZonesCreate = "gm.timezones.create";
        public const string TimeZonesEdit = "gm.timezones.edit";
        public const string TimeZonesDelete = "gm.timezones.delete";
        public const string TimeZonesActivate = "gm.timezones.activate";
        public const string TimeZonesDeactivate = "gm.timezones.deactivate";
        public const string TimeZonesExport = "gm.timezones.export";

        /// <summary>
        /// The five read codes. Handy for the roles that need the catalogue for their pickers
        /// and nothing more, which is most of them.
        /// </summary>
        public static readonly IReadOnlyList<string> ReadOnly =
        [
            Section, CountriesView, StatesView, CitiesView, CurrenciesView, TimeZonesView
        ];

        public static readonly IReadOnlyList<string> All =
        [
            Section,
            CountriesView, CountriesCreate, CountriesEdit, CountriesDelete, CountriesActivate,
            CountriesDeactivate, CountriesExport,
            StatesView, StatesCreate, StatesEdit, StatesDelete, StatesActivate,
            StatesDeactivate, StatesExport,
            CitiesView, CitiesCreate, CitiesEdit, CitiesDelete, CitiesActivate,
            CitiesDeactivate, CitiesExport,
            CurrenciesView, CurrenciesCreate, CurrenciesEdit, CurrenciesDelete, CurrenciesActivate,
            CurrenciesDeactivate, CurrenciesExport,
            TimeZonesView, TimeZonesCreate, TimeZonesEdit, TimeZonesDelete, TimeZonesActivate,
            TimeZonesDeactivate, TimeZonesExport
        ];

        /// <summary>
        /// The codes that write an enhanced audit row. Deleting a master is here because a
        /// deleted city cannot be recovered from the row that referenced it.
        /// </summary>
        public static readonly IReadOnlyList<string> Sensitive =
        [
            CountriesDelete, CountriesExport, StatesDelete, StatesExport,
            CitiesDelete, CitiesExport, CurrenciesDelete, CurrenciesExport,
            TimeZonesDelete, TimeZonesExport
        ];
    }

    /// <summary>
    /// Platform-level codes. Every one of these is seeded with <c>IsPlatformOnly = true</c>,
    /// which is what prevents a Tenant role from ever carrying one.
    /// </summary>
    public static class Platform
    {
        // ---- BusinessUnit ------------------------------------------------------------------
        public const string BusinessUnitsView = "platform.business-units.view";
        public const string BusinessUnitsCreate = "platform.business-units.create";
        public const string BusinessUnitsEdit = "platform.business-units.edit";
        public const string BusinessUnitsManageSettings = "platform.business-units.manage-settings";

        // ---- Organisations/Tenants ------------------------------------------------------------
        public const string TenantsView = "platform.organisations.view";
        public const string TenantsCreate = "platform.organisations.create";
        public const string TenantsEdit = "platform.organisations.edit";
        public const string TenantsReview = "platform.organisations.review";
        public const string TenantsApprove = "platform.organisations.approve";
        public const string TenantsReject = "platform.organisations.reject";
        public const string TenantsActivate = "platform.organisations.activate";
        public const string TenantsSuspend = "platform.organisations.suspend";
        public const string TenantsArchive = "platform.organisations.archive";
        public const string TenantsInviteAdmin = "platform.organisations.invite-admin";
        public const string TenantsManageDomains = "platform.organisations.manage-domains";
        public const string TenantsExport = "platform.organisations.export";

        /// <summary>Entering an Organisation operating context. The Tenant switcher.</summary>
        public const string TenantsSelect = "platform.organisations.select";

        // ---- Global catalogue --------------------------------------------------------------------
        public const string PermissionCatalogueManage = "platform.permission-catalogue.manage";
        public const string MenuCatalogueManage = "platform.menu-catalogue.manage";
        public const string PlatformAuditView = "platform.audit.view";

        public static readonly IReadOnlyList<string> All =
        [
            BusinessUnitsView, BusinessUnitsCreate, BusinessUnitsEdit, BusinessUnitsManageSettings,
            TenantsView, TenantsCreate, TenantsEdit, TenantsReview, TenantsApprove, TenantsReject,
            TenantsActivate, TenantsSuspend, TenantsArchive, TenantsInviteAdmin, TenantsManageDomains,
            TenantsExport, TenantsSelect,
            PermissionCatalogueManage, MenuCatalogueManage, PlatformAuditView
        ];
    }

    /// <summary>
    /// Codes whose use always writes an enhanced audit row: approvals, exports, anything that
    /// unmasks personal data, and everything that changes somebody access.
    /// </summary>
    public static readonly IReadOnlyList<string> Sensitive =
    [
        UsersCreate, UsersApprove, UsersCancel, UsersArchive, UsersExport, UsersSuspend,
        UsersDeactivate, UsersResetPassword, UsersUnlock, UsersBulkAdminister,
        UsersChangeLoginIdentifier, UsersViewSensitiveContact,
        RolesCreate, RolesEdit, RolesDelete, RolesAssignPermissions, RolesAssignUsers,
        PermissionsAssign, PermissionsRevoke,
        MenusConfigure, MenusMapRoles,
        UserSecurityRevokeSession, UserSecurityRevokeDevice, UserSecurityResetMfa,
        UserSecurityForceSignOut,
        AccessRequestsApprove, AccessRequestsReject,
        AccessReviewsDecide, AccessReviewsCancel, AccessReviewsExport,
        AuditExport, AuditViewSensitive,
        OrganisationEdit, OrganisationSubmit, OrganisationManageSettings,
        PaymentGatewaysView, PaymentGatewaysManage, PaymentGatewaysDelete, PaymentGatewaysTest,
        .. GlobalMaster.Sensitive,
        Platform.BusinessUnitsCreate, Platform.BusinessUnitsEdit, Platform.BusinessUnitsManageSettings,
        Platform.TenantsCreate, Platform.TenantsApprove, Platform.TenantsReject,
        Platform.TenantsActivate, Platform.TenantsSuspend, Platform.TenantsArchive,
        Platform.TenantsInviteAdmin, Platform.TenantsManageDomains, Platform.TenantsExport,
        Platform.TenantsSelect, Platform.PermissionCatalogueManage, Platform.MenuCatalogueManage,
        Platform.PlatformAuditView
    ];

    /// <summary>Every Tenant-level code IAM owns. Platform codes are listed separately.</summary>
    public static readonly IReadOnlyList<string> AllTenant =
    [
        IamView,
        UsersView, UsersCreate, UsersEdit, UsersSubmit, UsersApprove, UsersCancel, UsersArchive,
        UsersExport, UsersInvite, UsersSuspend, UsersReactivate, UsersDeactivate, UsersResetPassword,
        UsersUnlock, UsersBulkAdminister, UsersChangeLoginIdentifier, UsersViewSensitiveContact,
        RolesView, RolesCreate, RolesEdit, RolesDelete, RolesActivate, RolesDeactivate,
        RolesAssignPermissions, RolesAssignUsers, RolesManageIncompatibility, RolesExport,
        PermissionsView, PermissionsAssign, PermissionsRevoke, PermissionsExport,
        MenusView, MenusConfigure, MenusMapRoles,
        UserSecurityView, UserSecurityRevokeSession, UserSecurityRevokeDevice, UserSecurityResetMfa,
        UserSecurityForceSignOut,
        AccessRequestsView, AccessRequestsCreate, AccessRequestsSubmit, AccessRequestsApprove,
        AccessRequestsReject, AccessRequestsWithdraw,
        AccessReviewsView, AccessReviewsCreate, AccessReviewsDecide, AccessReviewsCancel,
        AccessReviewsExport,
        AuditView, AuditExport, AuditViewSensitive,
        OrganisationView, OrganisationEdit, OrganisationSubmit, OrganisationUploadDocument,
        OrganisationManageSettings, OrganisationManageDepartments, OrganisationManageUnits,

        // Payment gateway configuration. Tenant-assignable because the whole point is that an
        // Organisation configures its OWN merchant account; RoleAccessProfiles keeps the four
        // codes to administrators.
        PaymentGatewaysView, PaymentGatewaysManage, PaymentGatewaysDelete, PaymentGatewaysTest,

        // The global master catalogue, migrated in from the standalone GlobalMaster service.
        // Tenant-assignable: the permission governs an Organisation's OWN master rows, never
        // the shared platform ones.
        .. GlobalMaster.All
    ];

    /// <summary>Every code this service knows about, Tenant and platform together.</summary>
    public static IReadOnlyList<string> All => [.. AllTenant, .. Platform.All];

    public static bool IsSensitive(string permissionCode) =>
        Sensitive.Contains(permissionCode, StringComparer.Ordinal);

    public static bool IsPlatformOnly(string permissionCode) =>
        Platform.All.Contains(permissionCode, StringComparer.Ordinal);
}
