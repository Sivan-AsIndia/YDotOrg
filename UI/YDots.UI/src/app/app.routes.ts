import { Routes } from '@angular/router';
import { MainlayoutComponent } from './Shared/mainlayout/mainlayout';

import { ApplayoutComponent } from './Shared/applayout/applayout';
import { UserSecurityComponent } from './Features/YDot/Administration/user-security/user-security';
import { LoginIdentifierChangeComponent } from './Features/YDot/Administration/login-identifier-change/login-identifier-change';
import { BulkUserAdministrationComponent } from './Features/YDot/Administration/bulk-user-administration/bulk-user-administration';
import { UserDirectoryComponent } from './Features/YDot/Administration/user-directory/user-directory';
import { CreateUserComponent } from './Features/YDot/Administration/create-user/create-user';
import { UserProfileComponent } from './Features/YDot/Administration/user-profile/user-profile';
import { UserDetailsComponent } from './Features/YDot/Administration/user-details/user-details';
import { RoleCatalogueComponent } from './Features/YDot/Administration/role-catalogue/role-catalogue';
import { AccessRequestComponent } from './Features/YDot/Administration/access-request/access-request';
import { AccessReviewCampaignComponent } from './Features/YDot/Administration/access-review/access-review';
import { MySecurityComponent } from './Features/YDot/Administration/my-security/my-security';
import { MfaEnrollmentComponent } from './Features/YDot/Administration/my-security/mfa-enrollment/mfa-enrollment';
import { ExecutiveDashboardComponent } from './Features/YDot/Workspace/executive-dashboard/executive-dashboard';
import { GlobalSearchComponent } from './Features/YDot/Workspace/global-search/global-search';
import { WorkSpaceComponent } from './Features/YDot/Workspace/my-workspace/work-space';
import { NotificationCentreComponent } from './Features/YDot/Workspace/notification-centre/notification-centre';
import { RoleAwareApplicationShellComponent } from './Features/YDot/Workspace/role-aware-application-shell/role-aware-application-shell';
import { SavedViewBuilderComponent } from './Features/YDot/Workspace/saved-view-builder/saved-view-builder';
import { StandardListPageComponent } from './Features/YDot/Workspace/standard-list-page/standard-list-page';
import { StandardRecordDetailComponent } from './Features/YDot/Workspace/standard-record-detail/standard-record-detail';
import { LeadWorkQueueComponent } from './Features/YDot/Donors and Leads/lead-work-queue/lead-work-queue';
import { LeadCaptureComponent } from './Features/YDot/Donors and Leads/lead-capture/lead-capture';
import { Donor360Component } from './Features/YDot/Donors and Leads/donor-360/donor-360';
import { DuplicateReviewComponent } from './Features/YDot/Donors and Leads/duplicate-review/duplicate-review';
import { ConsentPreferenceCentreComponent } from './Features/YDot/Donors and Leads/consent-preference-centre/consent-preference-centre';
import { AssignmentBoardComponent } from './Features/YDot/Donors and Leads/assignment-board/assignment-board';
import { DonorIdentityVerificationComponent } from './Features/YDot/Donors and Leads/donor-identity-verification/donor-identity-verification';
import { FollowUpPlannerComponent } from './Features/YDot/Donors and Leads/follow-up-planner/follow-up-planner';
import { FinanceWorkbenchComponent } from './Features/YDot/Finance/finance-workbench/finance-workbench';
import { SettlementBatchDetailComponent } from './Features/YDot/Finance/settlement-batch-detail/settlement-batch-detail';
import { OfflineDonationEntryComponent } from './Features/YDot/Finance/offline-donation-entry/offline-donation-entry';
import { ReconciliationWorkspaceComponent } from './Features/YDot/Finance/reconciliation-workspace/reconciliation-workspace';
import { FinanceExceptionCaseComponent } from './Features/YDot/Finance/finance-exception-case/finance-exception-case';
import { PeriodCampaignCloseComponent } from './Features/YDot/Finance/period-campaign-close/period-campaign-close';
import { MakerCheckerReviewComponent } from './Features/YDot/Finance/maker-checker-review/maker-checker-review';
import { FinancialCorrectionOrReversalComponent } from './Features/YDot/Finance/financial-correction-or-reversal/financial-correction-or-reversal';
import { DonationIntentDetailComponent } from './Features/YDot/Donations and Payments/donation-intent-detail/donation-intent-detail';
import { PaymentEventQueueComponent } from './Features/YDot/Donations and Payments/payment-event-queue/payment-event-queue';
import { PaymentVerificationPageComponent } from './Features/YDot/Donations and Payments/payment-verification-page/payment-verification-page';
import { PublicDonationInitiationComponent } from './Features/YDot/Donations and Payments/public-donation-initiation/public-donation-initiation';
import { PaymentSupportAndSafeRetryComponent } from './Features/YDot/Donations and Payments/payment-support-and-safe-retry/payment-support-and-safe-retry';
import { ReceiptCorrectionAndReissueComponent } from './Features/YDot/Donations and Payments/receipt-correction-and-reissue/receipt-correction-and-reissue';
import { ReceiptRegisterComponent } from './Features/YDot/Donations and Payments/receipt-register/receipt-register';
import { DonationRegisterComponent } from './Features/YDot/Donations and Payments/donation-register/donation-register';
import { GatewayConfigurationComponent } from './Features/YDot/Donations and Payments/gateway-configuration/gateway-configuration';
import { RefundAndChargebackCaseComponent } from './Features/YDot/Donations and Payments/refund-and-chargeback-case/refund-and-chargeback-case';
import { UnifiedInboxComponent } from './Features/YDot/Communications/unified-inbox/unified-inbox';
import { CommunicationExceptionQueueComponent } from './Features/YDot/Communications/communication-exception-queue/communication-exception-queue';
import { ComplaintCaseComponent } from './Features/YDot/Communications/complaint-case/complaint-case';
import { ConversationDetailComponent } from './Features/YDot/Communications/conversation-detail/conversation-detail';
import { TemplateCatalogueComponent } from './Features/YDot/Communications/template-catalogue/template-catalogue';
import { OutboundMessageComposerComponent } from './Features/YDot/Communications/outbound-message-composer/outbound-message-composer';
import { SlaPolicyCalendarComponent } from './Features/YDot/Communications/sla-policy-calendar/sla-policy-calendar';
import { SuppressionAndContactRestrictionComponent } from './Features/YDot/Communications/suppression-and-contact-restriction/suppression-and-contact-restriction';
import { InventoryOverviewComponent } from './Features/YDot/Inventory/inventory-overview/inventory-overview';
import { BatchLedgerComponent } from './Features/YDot/Inventory/batch-ledger/batch-ledger';
import { StockMovementFormComponent } from './Features/YDot/Inventory/stock-movement-form/stock-movement-form';
import { ReservationManagerComponent } from './Features/YDot/Inventory/reservation-manager/reservation-manager';
import { StockCountSessionComponent } from './Features/YDot/Inventory/stock-count-session/stock-count-session';
import { InventoryExceptionQueueComponent } from './Features/YDot/Inventory/inventory-exception-queue/inventory-exception-queue';
import { WarehouseTransferComponent } from './Features/YDot/Inventory/warehouse-transfer/warehouse-transfer';
import { StockAdjustmentApprovalComponent } from './Features/YDot/Inventory/stock-adjustment-approval/stock-adjustment-approval';
import { AccountUnavailableComponent } from './Features/YDot/Auth/Auth/account-unavailable/account-unavailable';
import { EmailverifyComponent } from './Features/YDot/Auth/Auth/emailverify/emailverify';
import { ForgotpasswordComponent } from './Features/YDot/Auth/Auth/forgotpassword/forgotpassword';
import { MfaChallengeComponent } from './Features/YDot/Auth/Auth/mfa-challenge/mfa-challenge';
import { RegisterComponent } from './Features/YDot/Auth/Auth/register-activation/register';
import { ResetPasswordComponent } from './Features/YDot/Auth/Auth/reset-password/reset-password';
import { LoginComponent } from './Features/YDot/Auth/Auth/signin/login';
import { ReauthenticateComponent } from './Features/YDot/Auth/Auth/reauthenticate/reauthenticate';
import { TostepVerifyComponent } from './Features/YDot/Auth/Auth/tostep-verify/tostep-verify';
import { DashboardComponent } from './Features/YDot/dashboard/dashboard';
import { anonymousOnlyGuard, authGuard } from './Shared/guards/auth.guard';
import {
  organisationContextGuard,
  platformScopeGuard,
  requirePermission,
  superAdminGuard,
} from './Shared/guards/permission.guard';
import { SelectOrganisationComponent } from './Features/YDot/Auth/Auth/select-organisation/select-organisation';
import { AccessDeniedComponent } from './Features/YDot/Shared/access-denied/access-denied';
import { PageNotFoundComponent } from './Features/YDot/Shared/page-not-found/page-not-found';
import { AuditTrailComponent } from './Features/YDot/Administration/audit-trail/audit-trail';
import { MenuMappingComponent } from './Features/YDot/Administration/menu-mapping/menu-mapping';
import { OrganisationStructureComponent } from './Features/YDot/Administration/organisation-structure/organisation-structure';
import { BusinessUnitComponent } from './Features/YDot/Platform/business-unit/business-unit';
import { MenuCatalogueComponent } from './Features/YDot/Platform/menu-catalogue/menu-catalogue';
import { PermissionCatalogueComponent } from './Features/YDot/Platform/permission-catalogue/permission-catalogue';
import { CampaignDetailComponent } from './Features/YDot/Campaigns/campaign-detail/campaign-detail';
import { CampaignReadinessChecklistComponent } from './Features/YDot/Campaigns/campaign-readiness-checklist/campaign-readiness-checklist';
import { CampaignRegisterComponent } from './Features/YDot/Campaigns/campaign-register/campaign-register';
import { CampaignWizardComponent } from './Features/YDot/Campaigns/campaign-wizard/campaign-wizard';
import { TrackingAssetManagerComponent } from './Features/YDot/Campaigns/tracking-asset-manager/tracking-asset-manager';
import { PauseResumeCloseCampaignComponent } from './Features/YDot/Campaigns/pause-resume-and-close-campaign/pause-resume-and-close-campaign';
import { CityComponent } from './Features/YDot/Masters/city/city';
import { CountryComponent } from './Features/YDot/Masters/country/country';
import { CurrencyComponent } from './Features/YDot/Masters/currency/currency';
import { StateComponent } from './Features/YDot/Masters/state/state';
import { TimeZoneComponent } from './Features/YDot/Masters/time-zone/time-zone';
import { OrganisationDirectoryComponent } from './Features/YDot/Organisation/organisation-directory/organisation-directory';
import { OrganisationDetailComponent } from './Features/YDot/Organisation/organisation-detail/organisation-detail';
import { OrganisationSetupWizardComponent } from './Features/YDot/Organisation/organisation-setup-wizard/organisation-setup-wizard';
import { RegistrationVerificationComponent } from './Features/YDot/Organisation/registration-verification/registration-verification';
import { CommunicationTimelineComponent } from './Features/YDot/Donors and Leads/communication-timeline/communication-timeline';
import { DonationHistoryComponent } from './Features/YDot/Donors and Leads/donation-history/donation-history';
import { DonorListComponent } from './Features/YDot/Donors and Leads/donor-list/donor-list';
import { FollowUpExecutionComponent } from './Features/YDot/Donors and Leads/follow-up-execution/follow-up-execution';
import { FollowUpQueueComponent } from './Features/YDot/Donors and Leads/follow-up-queue/follow-up-queue';
import { MyLeadsComponent } from './Features/YDot/Donors and Leads/my-leads/my-leads';
import { GlobalSearchComponent as DonorGlobalSearchComponent } from './Features/YDot/Donors and Leads/global-search/global-search';



// Attribution Explorer and Budget and Targets have no route for now. Their components remain in
// Features/YDot/Campaigns and their CAM endpoints still answer; only the way in is withdrawn until
// the screens are ready to be offered.
export const routes: Routes = [

  // ===== AUTHENTICATED ROUTES (with sidebar/app layout) =====
  {
    path: 'app',
    component: ApplayoutComponent,
    canActivate: [authGuard],
    children: [
      // /app LANDS ON THE DASHBOARD, which is the one page every role receives.
      //
      // It used to redirect to User Profile and Access. That screen is only offered to the five
      // roles holding iam.users.view, and the endpoint behind it refuses everybody else - even for
      // their OWN record - so the other ten roles landed on "The user profile couldn't be
      // retrieved" whenever anything sent them to /app rather than to a named page.
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },

      // ===== Global masters =====
      //
      // THE FIVE MASTERS MOVED INTO IAM and enforce their own gm.* permissions there. The guards
      // below mirror those codes, so a person without them lands on the access-denied page that
      // explains itself rather than on a grid that renders empty and looks broken.
      //
      // Each screen accepts the VIEW code alone: a person who may read the country list but not
      // edit it still has a reason to open it, and the API withholds the write actions through
      // `permittedActions` rather than through the route.
      {
        path: 'masters/country',
        component: CountryComponent,
        canActivate: [requirePermission('gm.countries.view', 'GM.View')],
      },
      {
        path: 'masters/state',
        component: StateComponent,
        canActivate: [requirePermission('gm.states.view', 'GM.View')],
      },
      {
        path: 'masters/city',
        component: CityComponent,
        canActivate: [requirePermission('gm.cities.view', 'GM.View')],
      },
      {
        path: 'masters/currency',
        component: CurrencyComponent,
        canActivate: [requirePermission('gm.currencies.view', 'GM.View')],
      },
      {
        path: 'masters/timezone',
        component: TimeZoneComponent,
        canActivate: [requirePermission('gm.timezones.view', 'GM.View')],
      },
 
      { path: 'dashboard', component: DashboardComponent },

      // ===== Administration — access and identity =====
      //
      // GUARDED WITH THE CODE THE MENU CATALOGUE ALREADY DECLARES for each screen, so the sidebar
      // and the route agree. Until this was added, these routes carried only the `authGuard` on
      // the parent: the API refused the data with 403, but a person who typed the URL got the
      // screen shell and an inline error instead of the page that explains why they cannot be
      // there. No data was ever exposed — the server has always re-checked every one of these.
      //
      // THE TWO PROFILE ROUTES ARE DELIBERATELY DIFFERENT. With no :userReference the component
      // loads the SIGNED-IN user's own record, which is why it is the landing page and why every
      // role must keep reaching it; guarding that one with iam.users.view would lock ten of the
      // fifteen roles out of their own profile. The parameterised variant loads somebody else's
      // record, and that is an administrative act.
      { path: 'administration/users/:userReference/security', component: UserSecurityComponent, canActivate: [requirePermission('iam.users.view')] },
      { path: 'administration/users/:userReference/login-identifier-change', component: LoginIdentifierChangeComponent, canActivate: [requirePermission('iam.users.view')] },
      { path: 'administration/users/bulk-actions', component: BulkUserAdministrationComponent, canActivate: [requirePermission('iam.users.bulk-administer')] },
      { path: 'administration/access/user-directory', component: UserDirectoryComponent, canActivate: [requirePermission('iam.users.view')] },
      { path: 'administration/access/create-user', component: CreateUserComponent, canActivate: [requirePermission('iam.users.create')] },
      { path: 'administration/access/user-profile-and-access', component: UserProfileComponent },
      { path: 'administration/access/user-profile-and-access/:userReference', component: UserProfileComponent, canActivate: [requirePermission('iam.users.view')] },
      { path: 'administration/access/user-details/:userReference', component: UserDetailsComponent, canActivate: [requirePermission('iam.users.view')] },
      { path: 'administration/access/role-and-permission-catalogue', component: RoleCatalogueComponent, canActivate: [requirePermission('iam.roles.view')] },
      { path: 'administration/access/access-request-and-approval', component: AccessRequestComponent, canActivate: [requirePermission('iam.access-requests.view')] },
      // Self-service: everybody manages their own sign-in security, so no permission gate.
      { path: 'administration/access/my-security', component: MySecurityComponent },
      { path: 'administration/access/my-security/mfa-enrol', component: MfaEnrollmentComponent },
      // IAM-USR-03 — Access preview
      { path: 'administration/access/access-preview', component: AccessReviewCampaignComponent, canActivate: [requirePermission('iam.permissions.view')] },
      { path: 'administration/access/access-review-campaign', component: AccessReviewCampaignComponent, canActivate: [requirePermission('iam.access-reviews.view')] },

      // ===== Workspace pages =====
      { path: 'workspace/my-workspace', component: WorkSpaceComponent },
      { path: 'workspace/executive-dashboard', component: ExecutiveDashboardComponent },
      { path: 'workspace/global-search', component: GlobalSearchComponent },
      { path: 'workspace/standard-list-page', component: StandardListPageComponent },
      { path: 'workspace/standard-record-detail', component: StandardRecordDetailComponent },
      { path: 'workspace/notification-centre', component: NotificationCentreComponent },

      // Campaign 
      // CREATE IS GUARDED SEPARATELY FROM VIEW. A fundraiser who may see the campaign register
      // has no business on the creation wizard, and letting them open it only to be refused on
      // save is a worse experience than not offering it.
      {
        path: 'fundraising/campaigns/campaign-register',
        component: CampaignRegisterComponent,
        canActivate: [requirePermission('cam.campaigns.view')],
      },
      {
        path: 'fundraising/campaigns/campaign-wizard',
        component: CampaignWizardComponent,
        canActivate: [requirePermission('cam.campaigns.create')],
      },
      {
        path: 'fundraising/campaigns/campaign-detail',
        component: CampaignDetailComponent,
        canActivate: [requirePermission('cam.campaigns.view')],
      },
      {
        path: 'fundraising/campaigns/tracking-asset-manager',
        component: TrackingAssetManagerComponent,
        canActivate: [requirePermission('cam.tracking-assets.view')],
      },
      // THE MENU CATALOGUE POINTS AT 'fundraising/campaigns/...' FOR BOTH OF THESE, and these two
      // were registered under a 'cam/' prefix that nothing else in the campaign section uses. The
      // menu entry therefore matched no route, fell through to the wildcard and bounced the
      // operator to the dashboard with no explanation - which is how a screen that exists and
      // works looks broken. The old paths are kept as redirects so an existing bookmark still
      // lands somewhere real.
      {
        path: 'fundraising/campaigns/campaign-readiness-checklist',
        component: CampaignReadinessChecklistComponent,
        canActivate: [requirePermission('cam.readiness.view')],
      },
      {
        path: 'cam/campaign-readiness-checklist',
        redirectTo: 'fundraising/campaigns/campaign-readiness-checklist',
        pathMatch: 'full',
      },
      {
        path: 'fundraising/campaigns/pause-resume-and-close-campaign',
        component: PauseResumeCloseCampaignComponent,
        canActivate: [requirePermission('cam.campaigns.view')],
      },
      {
        path: 'cam/pause-resume-and-close-campaign',
        redirectTo: 'fundraising/campaigns/pause-resume-and-close-campaign',
        pathMatch: 'full',
      },

      // THE MENU ALSO CARRIES A DUPLICATE REVIEW ENTRY. The component exists and is complete; the
      // route was commented out, so the entry led nowhere.
      {
        path: 'fundraising/relationships/duplicate-review',
        component: DuplicateReviewComponent,
        canActivate: [requirePermission('don.duplicate-review.view')],
      },


      // ===== Donors and Leads pages =====
      // { path: 'fundraising/relationships/lead-work-queue', component: LeadWorkQueueComponent },
      // { path: 'fundraising/relationships/lead-capture', component: LeadCaptureComponent },
      // { path: 'fundraising/relationships/donor-360', component: Donor360Component },
      // { path: 'fundraising/relationships/duplicate-review', component: DuplicateReviewComponent },
      // { path: 'fundraising/relationships/consent-and-preference-centre', component: ConsentPreferenceCentreComponent },
      // { path: 'fundraising/relationships/assignment-board', component: AssignmentBoardComponent },
      // { path: 'don/donor-identity-verification', component: DonorIdentityVerificationComponent },
      // { path: 'don/follow-up-planner', component: FollowUpPlannerComponent },

        // ===== Donors and Leads pages =====
        //
        // Each guarded with the don.* code its menu entry declares. These screens carry donor
        // contact detail, consent decisions and identity evidence, so a stale bookmark should land
        // on the page that says why rather than on a grid that renders empty. The API has always
        // refused the underlying calls with 403; the guard is what makes the refusal legible.
        //
        // The aliases are guarded identically to the canonical route they duplicate - a second
        // path to a screen must not be a way around its check.
            { path: 'fundraising/relationships/lead-work-queue', component: LeadWorkQueueComponent, canActivate: [requirePermission('don.lead-work-queue.view')] },
            { path: 'fundraising/relationships/lead-capture', component: LeadCaptureComponent, canActivate: [requirePermission('don.lead-capture.view')] },
            { path: 'fundraising/relationships/donor-360', component: Donor360Component, canActivate: [requirePermission('don.donor-360.view')] },
            // { path: 'fundraising/relationships/duplicate-review', component: DuplicateReviewComponent },
            { path: 'fundraising/relationships/consent-and-preference-centre', component: ConsentPreferenceCentreComponent, canActivate: [requirePermission('don.consent-and-preference-centre.view')] },
            { path: 'fundraising/relationships/assignment-board', component: AssignmentBoardComponent, canActivate: [requirePermission('don.assignment-board.view')] },
            // Canonical donor workflow routes plus compatibility aliases.
            // Existing `/don/...` paths are preserved; relationship aliases ensure
            // navigation/menu configurations using the module route namespace open
            // the same production component rather than falling through.
            { path: 'don/donor-identity-verification', component: DonorIdentityVerificationComponent, canActivate: [requirePermission('don.donor-identity-verification.view')] },
            { path: 'fundraising/relationships/donor-identity-verification', component: DonorIdentityVerificationComponent, canActivate: [requirePermission('don.donor-identity-verification.view')] },
            { path: 'fundraising/relationships/identity-verification', component: DonorIdentityVerificationComponent, canActivate: [requirePermission('don.donor-identity-verification.view')] },
            { path: 'don/follow-up-planner', component: FollowUpPlannerComponent, canActivate: [requirePermission('don.follow-up-planner.view')] },
            { path: 'fundraising/relationships/follow-up-planner', component: FollowUpPlannerComponent, canActivate: [requirePermission('don.follow-up-planner.view')] },
            { path: 'fundraising/relationships/my-leads', component: MyLeadsComponent, canActivate: [requirePermission('don.lead-work-queue.view')] },
            { path: 'fundraising/relationships/communication-timeline', component: CommunicationTimelineComponent, canActivate: [requirePermission('don.donor-360.view')] },
            { path: 'fundraising/relationships/follow-up-queue', component: FollowUpQueueComponent, canActivate: [requirePermission('don.follow-up-planner.view')] },
            { path: 'fundraising/relationships/follow-up-execution', component: FollowUpExecutionComponent, canActivate: [requirePermission('don.follow-up-planner.view')] },
            { path: 'global-search', component: DonorGlobalSearchComponent, canActivate: [requirePermission('don.donors.view')] },
            { path: 'fundraising/relationships/donation-history', component: DonationHistoryComponent, canActivate: [requirePermission('don.donor-360.view')] },
            { path: 'fundraising/relationships/donor-list', component: DonorListComponent, canActivate: [requirePermission('don.donors.view')] },

      // =========================================================================
      // Access denied.
      //
      // Route guards send people here rather than bouncing them to the dashboard, because a
      // silent redirect makes a permission problem look like a broken link — they click the
      // same bookmark tomorrow and nobody ever learns that a permission needs granting.
      // =========================================================================
      { path: 'access-denied', component: AccessDeniedComponent },
      { path: 'page-not-found', component: PageNotFoundComponent },

      // =========================================================================
      // Menu and navigation.
      //
      // What this organisation uses, and what each role sees. The guard is a courtesy: the
      // endpoints re-check the same permission, so somebody who edits their way past it reaches
      // a screen that cannot save.
      // =========================================================================
      {
        path: 'administration/access/menu-mapping',
        component: MenuMappingComponent,
        canActivate: [requirePermission('iam.menus.view', 'iam.menus.configure')],
      },

      // =========================================================================
      // Audit trail. Organisation-scoped: this is the caller's own organisation's history.
      // =========================================================================
      {
        path: 'administration/audit',
        component: AuditTrailComponent,
        canActivate: [requirePermission('iam.audit.view')],
      },

      // ===== Organisation pages =====
      //
      // The three platform screens are gated on the platform.* codes the menu declares. A
      // SuperAdmin's token carries NO permission codes at all - its authority is a flag and a
      // Global scope claim - and requirePermission handles that: hasAnyPermission short-circuits
      // on isSuperAdmin, exactly as the server short-circuits its own permission lookup. So these
      // read as "SuperAdmin only" without hard-coding the role.
      //
      // THEY ALSO CARRY platformScopeGuard, which steps back out of any Organisation the session
      // is standing in. These four are platform screens; reaching one while the token still names
      // an Organisation is what left a TenantAdmin sidebar sitting beside "every organisation on
      // the platform". The guard is on the ROUTE rather than on the links that lead here on
      // purpose - a link can be fixed, but the Back button and a bookmark cannot.
      { path: 'administration/organisation/directory', component: OrganisationDirectoryComponent, canActivate: [platformScopeGuard, requirePermission('platform.organisations.view')] },
      { path: 'administration/organisation/details', component: OrganisationDetailComponent, canActivate: [requirePermission('iam.organisation.view')] },
      { path: 'administration/organisation/details/:id', component: OrganisationDetailComponent, canActivate: [requirePermission('iam.organisation.view')] },
      { path: 'administration/organisation/setup-wizard', component: OrganisationSetupWizardComponent, canActivate: [platformScopeGuard, requirePermission('platform.organisations.create')] },
      { path: 'administration/organisation/registration-verification', component: RegistrationVerificationComponent, canActivate: [platformScopeGuard, requirePermission('platform.organisations.review')] },
      { path: 'administration/organisation/registration-verification/:id', component: RegistrationVerificationComponent, canActivate: [platformScopeGuard, requirePermission('platform.organisations.review')] },

      // The organisation's own settings live on the detail screen, which opens on that tab.
      // A separate component would duplicate the loading, the version handling and the form.
      //
      // Both codes are accepted because the screen serves two audiences: somebody who may read the
      // organisation profile, and the administrator who may change its settings.
      { path: 'administration/organisation/settings', component: OrganisationDetailComponent, canActivate: [requirePermission('iam.organisation.manage-settings', 'iam.organisation.view')] },

      // Two hierarchies, one screen. `mode` tells the component which it is managing, so the
      // routes can be renamed without touching it. See the component for why they are separate
      // trees rather than one.
      {
        path: 'administration/organisation/departments',
        component: OrganisationStructureComponent,
        data: { mode: 'departments' },
        canActivate: [requirePermission(
          'iam.organisation.view', 'iam.organisation.manage-departments')],
      },
      {
        path: 'administration/organisation/units',
        component: OrganisationStructureComponent,
        data: { mode: 'units' },
        canActivate: [requirePermission('iam.organisation.view', 'iam.organisation.manage-units')],
      },

      // =========================================================================
      // Platform.
      //
      // These are genuinely global — they have no meaning inside a single organisation — so
      // they are gated on scope rather than on a permission code, and platformScopeGuard steps
      // back out of any Organisation on the way in. See the Organisation block above.
      // =========================================================================
      {
        path: 'platform/business-unit',
        component: BusinessUnitComponent,
        canActivate: [platformScopeGuard, superAdminGuard],
      },
      {
        path: 'platform/permission-catalogue',
        component: PermissionCatalogueComponent,
        canActivate: [platformScopeGuard, superAdminGuard],
      },
      {
        path: 'platform/menu-catalogue',
        component: MenuCatalogueComponent,
        canActivate: [platformScopeGuard, superAdminGuard],
      },
      {
        path: 'platform/audit',
        component: AuditTrailComponent,
        canActivate: [platformScopeGuard, superAdminGuard],
      },

      // ===== Finance pages =====
      { path: 'money/finance/finance-workbench', component: FinanceWorkbenchComponent },
      { path: 'money/finance/settlement-batch-detail', component: SettlementBatchDetailComponent },
      // These two live under Finance because that is who works them, but the records they write
      // belong to the payments module - so the permission is PAY's.
      {
        path: 'money/finance/offline-donation-entry',
        component: OfflineDonationEntryComponent,
        canActivate: [requirePermission('pay.donations.record-offline')],
      },
      {
        path: 'money/finance/reconciliation-workspace',
        component: ReconciliationWorkspaceComponent,
        canActivate: [requirePermission('pay.donations.reconcile')],
      },
      { path: 'money/finance/finance-exception-case', component: FinanceExceptionCaseComponent },
      { path: 'money/finance/period-campaign-close', component: PeriodCampaignCloseComponent },
      { path: 'fin/maker-checker-review', component: MakerCheckerReviewComponent },
      { path: 'fin/financial-correction-or-reversal', component: FinancialCorrectionOrReversalComponent },

      // ===== Donations and Payments pages =====
      //
      // EVERY ONE OF THESE IS GUARDED except the public donation form, and the exception is the
      // point: a donor with a QR code has no account and no permissions. Requiring one would mean
      // asking somebody to register before they may give money. The API treats that route as
      // anonymous for the same reason, and resolves the organisation from the unguessable
      // reference the donor arrived with rather than from anything they can choose.
      {
        path: 'donations/donation-intent-detail',
        component: DonationIntentDetailComponent,
        canActivate: [requirePermission('pay.intents.view')],
      },
      {
        path: 'donations/donation-intent-detail/:reference',
        component: DonationIntentDetailComponent,
        canActivate: [requirePermission('pay.intents.view')],
      },
      {
        path: 'donations/payment-event-queue',
        component: PaymentEventQueueComponent,
        canActivate: [requirePermission('pay.payments.view-events')],
      },
      {
        path: 'donations/payment-verification',
        component: PaymentVerificationPageComponent,
        canActivate: [requirePermission('pay.payments.verify')],
      },

      // ANONYMOUS ON PURPOSE. See the note above.
      { path: 'donations/public-donation-initiation', component: PublicDonationInitiationComponent },

      {
        path: 'donations/payment-support-and-safe-retry',
        component: PaymentSupportAndSafeRetryComponent,
        canActivate: [requirePermission('pay.payments.safe-retry')],
      },

      // Correction needs the CORRECT permission, not merely the view one: a correction issues a
      // new tax document superseding one a donor may already have claimed on.
      {
        path: 'donations/receipt-correction-and-reissue',
        component: ReceiptCorrectionAndReissueComponent,
        canActivate: [requirePermission('pay.receipts.correct')],
      },
      {
        path: 'donations/receipt-register',
        component: ReceiptRegisterComponent,
        canActivate: [requirePermission('pay.receipts.view')],
      },

      // The register of everything actually received. The menu carried this node with no
      // component behind it, so following it reached a blank page.
      {
        path: 'donations/donation-register',
        component: DonationRegisterComponent,
        canActivate: [requirePermission('pay.donations.view')],
      },

      // WHERE THE MONEY GOES. Guarded on view; the screen itself checks pay.gateway.manage before
      // drawing anything that can change the configuration, so somebody supporting donors can see
      // which gateway is live without being able to switch it.
      {
        path: 'donations/gateway-configuration',
        component: GatewayConfigurationComponent,
        canActivate: [requirePermission('pay.gateway.view')],
      },

      // Either code opens the combined register. Refunds and chargebacks are separately
      // permissioned, and the screen shows whichever half the caller may see.
      {
        path: 'donations/refund-and-chargeback-case',
        component: RefundAndChargebackCaseComponent,
        canActivate: [requirePermission('pay.refunds.view', 'pay.chargebacks.view')],
      },

      // ===== Communications pages =====
      { path: 'communications/unified-inbox', component: UnifiedInboxComponent },
      { path: 'communications/communication-exception-queue', component: CommunicationExceptionQueueComponent },
      { path: 'communications/complaint-case', component: ComplaintCaseComponent },
      { path: 'communications/conversation-detail', component: ConversationDetailComponent },
      { path: 'communications/template-catalogue', component: TemplateCatalogueComponent },
      { path: 'communications/outbound-message-composer', component: OutboundMessageComposerComponent },
      { path: 'communications/sla-policy-calendar', component: SlaPolicyCalendarComponent },
      { path: 'communications/suppression-and-contact-restriction', component: SuppressionAndContactRestrictionComponent },

      // ===== Inventory pages (Section 10) =====
      { path: 'supply/inventory/inventory-overview', component: InventoryOverviewComponent },
      { path: 'supply/inventory/batch-ledger', component: BatchLedgerComponent },
      { path: 'supply/inventory/stock-movement-form', component: StockMovementFormComponent },
      { path: 'supply/inventory/reservation-manager', component: ReservationManagerComponent },
      { path: 'supply/inventory/stock-count-session', component: StockCountSessionComponent },
      { path: 'supply/inventory/inventory-exception-queue', component: InventoryExceptionQueueComponent },
      { path: 'inv/warehouse-transfer', component: WarehouseTransferComponent },
      { path: 'inv/stock-adjustment-approval', component: StockAdjustmentApprovalComponent },

      // ===== UX pages =====
      { path: 'ux/role-aware-application-shell', component: RoleAwareApplicationShellComponent },
      { path: 'ux/saved-view-builder', component: SavedViewBuilderComponent },

      // ===== ANYTHING ELSE INSIDE THE APPLICATION =====
      //
      // IT SAYS SO RATHER THAN REDIRECTING. An unmatched address used to fall through to the
      // top-level wildcard and land a signed-in person on the sign-in form - which reads as "you
      // have been logged out" when what actually happened is that a link was wrong. Two live menu
      // entries did exactly this for months and nobody reported it, because nothing looked broken.
      //
      // THIS ENTRY MUST STAY LAST. Angular matches routes in order, and a wildcard above a real
      // route swallows it.
      { path: '**', component: PageNotFoundComponent },
    ]
  },

  // ===== ROOT-LEVEL REDIRECT (catch URL typed without /app/) =====
  {
    path: 'administration/access/user-directory',
    redirectTo: '/app/administration/access/user-directory',
  },
  {
    path: 'administration/access/user-profile-and-access',
    redirectTo: '/app/administration/access/user-profile-and-access',
  },
  {
    path: 'administration/access/role-and-permission-catalogue',
    redirectTo: '/app/administration/access/role-and-permission-catalogue',
  },
  {
    path: 'administration/access/access-request-and-approval',
    redirectTo: '/app/administration/access/access-request-and-approval',
  },
  {
    path: 'administration/access/my-security',
    redirectTo: '/app/administration/access/my-security',
  },
  {
    path: 'administration/access/my-security/mfa-enrol',
    redirectTo: '/app/administration/access/my-security/mfa-enrol',
  },
  {
    path: 'administration/access/access-preview',
    redirectTo: '/app/administration/access/access-preview',
  },
  {
    path: 'administration/access/access-review-campaign',
    redirectTo: '/app/administration/access/access-review-campaign',
  },
  {
    path: 'administration/users/:userReference/security',
    redirectTo: '/app/administration/users/:userReference/security',
  },
  {
    path: 'administration/users/:userReference/login-identifier-change',
    redirectTo: '/app/administration/users/:userReference/login-identifier-change',
  },
  {
    path: 'administration/users/bulk-actions',
    redirectTo: '/app/administration/users/bulk-actions',
  },
  {
    path: 'administration/access/create-user',
    redirectTo: '/app/administration/access/create-user',
  },

  // ===== PUBLIC/AUTH ROUTES (without sidebar) =====
  //
  // Every screen here is reachable without a session, because each one is a step on the way to
  // getting one. The API still checks the token, the invitation or the challenge on every call,
  // so an open route is not an open door.
  //
  // Note the query-string forms: the e-mails now send /auth/invitation?token=… and
  // /auth/reset-password?token=…, which keeps the token out of the path segment. The older
  // /:token routes are kept so links already in somebody's inbox still open.
  {
    path: '',
    component: MainlayoutComponent,
    children: [
      { path: '', redirectTo: 'auth/sign-in', pathMatch: 'full' },

      // IAM-AUTH-01 — Sign in. anonymousOnlyGuard bounces an already-signed-in person to the
      // dashboard instead of showing them a sign-in form they do not need.
      { path: 'login', redirectTo: 'auth/sign-in', pathMatch: 'full' },
      { path: 'auth/sign-in', component: LoginComponent, canActivate: [anonymousOnlyGuard] },

      // Where sign-in ends for a root user. They are properly authenticated — the token is real
      // — but they belong to no organisation, so every organisation-scoped screen has nothing to
      // show until they say which one they mean. authGuard, not anonymousOnlyGuard: they ARE
      // signed in by the time they arrive.
      {
        path: 'auth/select-organisation',
        component: SelectOrganisationComponent,
        canActivate: [authGuard],
      },

      // IAM-AUTH-02 — Accept invitation and activate account
      { path: 'register', redirectTo: 'auth/invitation', pathMatch: 'full' },
      { path: 'auth/invitation', component: RegisterComponent },
      { path: 'auth/invitation/:token', component: RegisterComponent },

      // IAM-AUTH-03 — Forgot password
      { path: 'forgot-password', redirectTo: 'auth/forgot-password', pathMatch: 'full' },
      { path: 'auth/forgot-password', component: ForgotpasswordComponent },

      // Email verification (used by the login-identifier change flow)
      { path: 'email-verify', component: EmailverifyComponent },
      { path: 'auth/email-verify', component: EmailverifyComponent },

      // IAM-AUTH-04 — Reset password. The same screen handles the reactivation link, which
      // arrives as ?token=…&mode=reactivate.
      { path: 'auth/reset-password', component: ResetPasswordComponent },
      { path: 'auth/reset-password/:token', component: ResetPasswordComponent },

      // IAM-AUTH-05 — MFA challenge. No token in the URL: the challenge is handed over in
      // memory by MfaHandoffService, so it never reaches browser history or a server log.
      { path: 'auth/mfa', component: MfaChallengeComponent },

      // IAM-AUTH-06 — Account unavailable and recovery guidance
      { path: 'auth/account-unavailable', component: AccountUnavailableComponent },

      // IAM-AUTH-07 — Session timeout and reauthentication
      { path: 'auth/reauthenticate', component: ReauthenticateComponent },

      // Legacy two-step verify (redirect to MFA challenge)
      { path: 'tostep-verify', redirectTo: 'auth/mfa', pathMatch: 'full' },
    ]
  },

  // Anything unrecognised goes to sign-in rather than a blank page.
  { path: '**', redirectTo: 'auth/sign-in' },
];