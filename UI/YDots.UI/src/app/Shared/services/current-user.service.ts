import { Injectable, computed, inject } from '@angular/core';
import { AuthTokenService } from './auth-token.service';

/**
 * The roles the screens name. Derived from the token's roles, never chosen locally.
 *
 * FOUR NAMES, NOT SIX. The platform's role catalogue was cut from fourteen job-shaped roles to
 * four authority-shaped ones - Super Admin, Organisation Administrator, Approver, Initiator - so
 * 'Campaign Manager', 'Campaign Owner', 'Finance Officer' and 'Auditor' are names no token can
 * carry any more. Leaving them in this union would not have failed to compile; it would have left
 * every comparison against them silently false, which is the worse outcome.
 */
export type CampaignRole =
  | 'Super Admin'
  | 'Organisation Administrator'
  | 'Approver'
  | 'Initiator'
  | 'Donor'
  | 'User';

/**
 * The screen-level permission codes the campaign screens ask about.
 *
 * THEY ARE NOT THE SERVER'S CODES, and that mismatch is the whole reason this file needed
 * rewriting. The screens ask about `cam.campaign-register.view`; the API enforces
 * `cam.campaigns.view`. Neither list ever contained a member of the other, so every check here
 * would have failed against a real token - which is precisely why the previous version could
 * only work against a mock profile.
 */
export type CamPermissionCode = string;

/**
 * The acting session, as the campaign screens read it.
 *
 * WHAT THIS REPLACES, AND WHY IT MATTERED. This was a mock session: a hard-coded list of five
 * invented people, one of them selected as "the campaign manager, so the create → submit →
 * approval flow is the demonstrated default", and a `setProfile(key)` switcher described as
 * dev-only that shipped in the production bundle. Thirty-two call sites across the campaign
 * screens asked it for permissions. Four consequences followed:
 *
 *   - EVERY CAMPAIGN SCREEN'S BUTTONS CAME FROM A CONSTANT. What a person could do had nothing
 *     to do with who they were; it depended on which mock profile the array happened to hold.
 *   - THE PROFILE SWITCHER WAS REACHABLE. Anything calling `setProfile('super-admin')` granted
 *     itself every campaign permission on screen. The server would still have refused the calls,
 *     but the UI would have offered them all.
 *   - `reference()` RETURNED AN INVENTED USER ID, and that value was written into records as the
 *     person who submitted or approved something.
 *   - THE CODES DID NOT MATCH THE API'S ANYWAY, so this was never going to work against a real
 *     token without the mapping below.
 *
 * IT NOW READS THE TOKEN and maps the screens' vocabulary onto the codes the server enforces. The
 * mapping is the honest part: a screen asking about "the campaign register" is asking about
 * campaigns, and saying so in one place beats renaming thirty-two call sites and every template
 * that goes with them.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly tokens = inject(AuthTokenService);

  /**
   * The screens' codes onto the server's.
   *
   * A SCREEN CODE WITH NO SERVER COUNTERPART MAPS TO THE NEAREST REAL ONE rather than to `true`.
   * `cam.attribution-explorer.view-donor-identity` has no dedicated permission, so it maps to the
   * donor-identity permission DON actually enforces - which is the right answer, because that is
   * the data being revealed.
   */
  private static readonly PermissionMap: Readonly<Record<string, readonly string[]>> = {
    // ---- Campaign register, detail, wizard and lifecycle ------------------------------------
    'cam.campaign-register.view': ['cam.campaigns.view'],
    'cam.campaign-register.create': ['cam.campaigns.create'],
    'cam.campaign-register.export': ['cam.campaigns.export'],
    'cam.campaign-register.delete-draft': ['cam.campaigns.delete-draft'],

    'cam.campaign-detail.view': ['cam.campaigns.view'],
    'cam.campaign-detail.operate-according-to-lifecycle': [
      'cam.campaigns.activate',
      'cam.campaigns.pause',
      'cam.campaigns.resume',
    ],

    'cam.campaign-wizard.view': ['cam.campaigns.view'],
    'cam.campaign-wizard.save-draft': ['cam.campaigns.create', 'cam.campaigns.edit'],
    'cam.campaign-wizard.validate': ['cam.campaigns.view'],
    'cam.campaign-wizard.submit': ['cam.campaigns.submit'],
    'cam.campaign-wizard.delete-draft': ['cam.campaigns.delete-draft'],

    'cam.campaign.view': ['cam.campaigns.view'],
    'cam.campaign.create': ['cam.campaigns.create'],
    'cam.campaign.edit': ['cam.campaigns.edit'],
    'cam.campaign.approve': ['cam.campaigns.approve'],
    'cam.campaign.activate': ['cam.campaigns.activate'],
    'cam.campaign.schedule': ['cam.campaigns.activate'],
    'cam.campaign.pause': ['cam.campaigns.pause'],
    'cam.campaign.resume': ['cam.campaigns.resume'],
    'cam.campaign.stop': ['cam.campaigns.pause'],
    'cam.campaign.close': ['cam.campaigns.close'],
    'cam.campaign.cancel': ['cam.campaigns.close'],
    'cam.campaign.delete': ['cam.campaigns.delete-draft'],

    'cam.pause-resume-and-close-campaign.view': ['cam.campaigns.view'],
    'cam.pause-resume-and-close-campaign.activate': ['cam.campaigns.activate'],
    'cam.pause-resume-and-close-campaign.pause': ['cam.campaigns.pause'],
    'cam.pause-resume-and-close-campaign.resume': ['cam.campaigns.resume'],
    'cam.pause-resume-and-close-campaign.request-close': ['cam.campaigns.request-close'],
    'cam.pause-resume-and-close-campaign.approve-close': ['cam.campaigns.close'],
    'cam.pause-resume-and-close-campaign.cancel-draft': ['cam.campaigns.delete-draft'],

    // ---- Tracking assets --------------------------------------------------------------------
    'cam.tracking-asset-manager.view': ['cam.tracking-assets.view'],
    'cam.tracking-asset-manager.generate': ['cam.tracking-assets.create'],
    'cam.tracking-asset-manager.replace': ['cam.tracking-assets.edit'],
    'cam.tracking-asset-manager.approve': ['cam.tracking-assets.approve'],
    'cam.tracking-asset-manager.disable': ['cam.tracking-assets.deactivate'],

    // Testing a link is a read: it follows the asset's own URL and reports what came back.
    'cam.tracking-asset-manager.test': ['cam.tracking-assets.view'],

    // ---- Readiness --------------------------------------------------------------------------
    'cam.campaign-readiness-checklist.view': ['cam.readiness.view'],
    'cam.campaign-readiness-checklist.validate-readiness': ['cam.readiness.view'],
    'cam.campaign-readiness-checklist.assign-blocker': ['cam.readiness.manage-blockers'],
    'cam.campaign-readiness-checklist.request-approval': ['cam.campaigns.submit'],
    'cam.campaign-readiness-checklist.approve-launch': ['cam.campaigns.approve'],
    'cam.campaign-readiness-checklist.return-to-draft': ['cam.readiness.return-to-draft'],

    // ---- Budget and targets -----------------------------------------------------------------
    'cam.budget-and-target-plan.view': ['cam.budget-plans.view'],
    'cam.budget-and-target-plan.allocate': ['cam.budget-plans.allocate'],
    'cam.budget-and-target-plan.revise': ['cam.budget-plans.revise'],
    'cam.budget-and-target-plan.submit': ['cam.budget-plans.submit'],
    'cam.budget-and-target-plan.approve': ['cam.budget-plans.approve'],

    // ---- Attribution -------------------------------------------------------------------------
    'cam.attribution-explorer.view': ['cam.attribution.view'],
    'cam.attribution-explorer.request-correction': ['cam.attribution.request-correction'],

    // Revealing a donor's identity is DON's permission, because it is DON's data.
    'cam.attribution-explorer.view-donor-identity': ['don.donors.view-sensitive-contact'],

    // THERE IS NO DELETE. The explorer's delete action was removed - a donation is a record of
    // money that moved - so this maps to the export permission the row's other actions need,
    // and the screen refuses the action regardless.
    'cam.attribution-explorer.delete-draft': ['cam.attribution.export'],
  };

  /**
   * The acting session's user id.
   *
   * THE REAL ONE, from the token. It used to be an invented 'USR-0099', and that value was
   * written into records as the person who submitted or approved something.
   */
  readonly reference = computed(() => this.tokens.user()?.id ?? '');

  readonly current = computed(() => ({
    key: this.tokens.user()?.id ?? '',
    reference: this.tokens.user()?.id ?? '',
    name: this.tokens.displayName() || 'Signed-in user',
    role: this.role(),
    permissions: [] as readonly string[],
  }));

  /**
   * The role the screens display.
   *
   * DERIVED FROM THE TOKEN'S ROLES, in the order the screens treat as most privileged. It is a
   * LABEL rather than a decision: nothing here gates an action, because the permission codes do
   * that and a person can hold campaign permissions under any role name an organisation invents.
   */
  readonly role = computed<CampaignRole>(() => {
    const roles = this.tokens.roles().map((role) => role.toUpperCase());

    if (this.tokens.isSuperAdmin()) {
      return 'Super Admin';
    }

    if (roles.includes('TENANT_ADMIN')) {
      return 'Organisation Administrator';
    }

    // APPROVER BEFORE INITIATOR, because the order here is most-privileged-first and somebody
    // holding both should be labelled by the authority they carry rather than the work they do.
    if (roles.includes('APPROVER')) {
      return 'Approver';
    }

    if (roles.includes('INITIATOR')) {
      return 'Initiator';
    }

    // LAST, AND BELOW EVERY STAFF ROLE. Somebody who gives AND works here - a volunteer, an
    // employee - is labelled by the authority they carry over the Organisation's records, not by
    // the fact that they have also donated. Holding both is legitimate; only the order of these
    // checks decides which one a screen shows.
    if (roles.includes('DONOR')) {
      return 'Donor';
    }

    return 'User';
  });

  /** Every campaign permission the token actually carries. */
  readonly permissions = computed<readonly string[]>(() =>
    this.tokens.permissions().filter((code) => code.startsWith('cam.')),
  );

  readonly isSuperAdmin = computed(() => this.tokens.isSuperAdmin());

  /**
   * The signed-in person's own name and address, for a form that should not ask them again.
   *
   * FROM THE TOKEN, so they cost nothing and cannot fail halfway. Empty strings for an anonymous
   * visitor, which is what the donation form checks before it prefills anything.
   */
  readonly displayName = computed(() => this.tokens.displayName());

  readonly email = computed(() => this.tokens.email());

  /**
   * The organisation this session is operating in.
   *
   * FOR THE SCOPE LABELS THE CAMPAIGN SCREENS SHOW. Those were literals - one of them named a
   * company belonging to nobody on the platform - and a scope label that can be wrong is worse
   * than none: it tells an operator they are working inside somebody else's records.
   */
  readonly organisationName = computed(() => this.tokens.organisationName());

  /**
   * Whether this caller may approve a campaign.
   *
   * IT ASKS ABOUT THE PERMISSION, not the role name. The old version compared the role against
   * two strings, so an organisation that called its approvers anything else had nobody who could
   * approve - and the server, which checks the permission, would have disagreed with the screen.
   */
  readonly canApproveCampaigns = computed(() =>
    this.tokens.hasAnyPermission('cam.campaigns.approve'),
  );

  /**
   * Whether the caller holds a screen-level capability.
   *
   * SUPERADMIN PASSES, matching what IAM, CAM, DON and PAY all do server-side: the platform root
   * reaches every module without being individually assigned every permission in it.
   *
   * AN UNMAPPED CODE IS REFUSED rather than allowed. A capability nobody thought to map is one
   * nobody has decided about, and defaulting those to "permitted" is how an unreviewed action
   * ends up on screen.
   */
  hasPermission(code: CamPermissionCode): boolean {
    if (this.tokens.isSuperAdmin()) {
      return true;
    }

    const mapped = CurrentUserService.PermissionMap[code];

    if (!mapped) {
      // Not a screen code: it may already be a server code, in which case ask directly.
      return this.tokens.hasAnyPermission(code);
    }

    return this.tokens.hasAnyPermission(...mapped);
  }

  /**
   * Whether the caller holds ANY of several capabilities.
   *
   * FOR CONTROLS THAT STAND FOR MORE THAN ONE SERVER PERMISSION. "Operate according to lifecycle"
   * is one button and seven separately enforced transitions; offering it to somebody who may
   * pause but not approve is right, and requiring all seven would hide it from nearly everybody.
   */
  hasAnyPermission(...codes: CamPermissionCode[]): boolean {
    return codes.some((code) => this.hasPermission(code));
  }

  /**
   * Retained so the two screens that still call it compile; it does nothing.
   *
   * SWITCHING WHO YOU ARE IS NOT SOMETHING A SCREEN DOES. This changed the active mock profile,
   * and calling it with 'super-admin' granted every campaign permission in the interface. Who the
   * caller is comes from the token, and changing that means signing in as somebody else.
   */
  setProfile(_key: string): void {
    // Intentionally empty. See the note above.
  }
}
