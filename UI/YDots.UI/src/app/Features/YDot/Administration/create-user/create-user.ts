import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { UserAdminApiService } from '../../../../Service/user-admin-api.service';
import { LookupItem } from '../../../../Shared/models/api-response.model';
import {
  CreateUserRequest,
  EnumOption,
  EnumOptionsResponse,
  ReferenceDataResponse,
} from '../../../../Shared/models/iam-contract.model';
import { forkJoin } from 'rxjs';

/**
 * IAM-USR-01 — Invite or create a user.
 *
 * WHAT THIS SCREEN PRODUCES
 * -------------------------
 * A real account and a real invitation e-mail. Pressing "Create and send invitation" calls the
 * API, which creates the record, generates a single-use token, stores only its hash, and e-mails
 * a link to `/auth/invitation?token=…`. That link is the first step of the activation stepper.
 *
 * So the loop is: create here → e-mail → activate there → the directory shows the account as
 * Active, with MFA reading **Enrolled** if they added a second factor.
 *
 * WHY THE DROPDOWNS ARE LOADED, NOT TYPED
 * ---------------------------------------
 * Roles and organisation units are identifiers, not words. The previous version collected the
 * *name* of a role from a text list and stored it in a browser cache, which is why nothing it
 * created ever existed on the server. Every option here comes from
 * `GET /users/invite-or-create-user-guided-flow`, already filtered to what this administrator is
 * allowed to grant.
 */
@Component({
  selector: 'app-create-user',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './create-user.html',
  styleUrl: './create-user.css',
})
export class CreateUserComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(UserAdminApiService);
  private readonly tokens = inject(AuthTokenService);

  // ---- Screen state ---------------------------------------------------------------------------
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');
  readonly view = signal<ReferenceDataResponse | null>(null);

  /** The enumerations the form renders as dropdowns, with their display labels. */
  readonly enums = signal<EnumOptionsResponse | null>(null);

  readonly activeStep = signal(0);
  readonly steps = ['Identity', 'Organisation', 'Access', 'Review'];
  readonly submitted = signal(false);

  /**
   * Set once the account exists, so the screen can confirm rather than offer Create again.
   *
   * `invitationStatus` is the server's word, not ours: `Sent` means the relay accepted the
   * message, `Pending` means it did not. Reporting the button the administrator pressed instead
   * of what actually happened would tell them an e-mail is on its way when none was ever sent —
   * and they would go on waiting for it.
   */
  readonly createdUser = signal<{
    code: string;
    displayName: string;
    email: string;
    requestedInvite: boolean;
    invitationStatus: string;
  } | null>(null);

  /** True only when the relay actually accepted the invitation. */
  readonly invitationDelivered = computed(() => this.createdUser()?.invitationStatus === 'Sent');

  /** The invitation was asked for, but the relay refused it. */
  readonly invitationStuck = computed(() => {
    const created = this.createdUser();
    return Boolean(created?.requestedInvite) && created?.invitationStatus !== 'Sent';
  });

  // ---- The form ------------------------------------------------------------------------------
  readonly form = signal({
    // Identity
    accountCategory: 'Employee',
    title: '',
    firstName: '',
    middleName: '',
    lastName: '',
    displayName: '',
    preferredName: '',
    email: '',
    username: '',
    mobileCountryCode: '+91',
    mobileNumber: '',
    employeeNumber: '',

    // Organisation
    engagementType: 'FullTime',
    organisationUnitId: '',
    departmentId: '',
    designation: '',
    workLocation: '',
    preferredLanguage: 'en-GB',
    timeZoneId: 'UTC',

    // Access
    primaryRoleId: '',
    dataScopeType: 'Organisation',
    accessStartsAt: new Date().toISOString().slice(0, 10),
    accessEndsAt: '',
    businessJustification: '',

    // Security
    mfaRequirement: 'Optional',
    sendInvitationNow: true,
    welcomeMessage: '',
  });

  /** Result of the live "is this taken?" check on e-mail and username. */
  readonly identityCheck = signal<{ checking: boolean; message: string; ok: boolean; suggestions: string[] } | null>(null);

  /**
   * Field-level errors returned by the API, keyed by field name.
   *
   * The API answers a rejected create with `{ message, errors: [{ field, message }] }`. The
   * summary line is deliberately vague — "Review the highlighted fields before continuing" — on
   * the assumption that the client highlights them. Throwing `errors` away, as this component
   * first did, left that sentence pointing at nothing: a dead end with no way to discover which
   * field the server disliked.
   */
  readonly serverErrors = signal<Record<string, string>>({});

  /**
   * Which step each field belongs to, so a rejection can send the person to the right one.
   * Without this they land on Review, told to fix a field that is three steps back.
   */
  private static readonly FIELD_STEP: Record<string, number> = {
    accountCategory: 0, title: 0, firstName: 0, middleName: 0, lastName: 0, displayName: 0,
    preferredName: 0, email: 0, username: 0, alternateEmail: 0,
    mobileCountryCode: 0, mobileNumber: 0, employeeNumber: 0,

    engagementType: 1, organisationId: 1, organisationUnitId: 1, departmentId: 1,
    designation: 1, managerUserId: 1, workLocation: 1, preferredLanguage: 1, timeZoneId: 1,

    primaryRoleId: 2, additionalRoleIds: 2, dataScopeType: 2, dataScopes: 2,
    accessStartsAtUtc: 2, accessEndsAtUtc: 2, accessReviewDueAtUtc: 2,
    approverUserId: 2, businessJustification: 2,

    credentialSetupMethod: 3, temporaryPassword: 3, mfaRequirement: 3,
    preferredMfaMethod: 3, sendInvitationNow: 3, welcomeMessage: 3,
  };

  /** The server's complaint about one field, if it made one. */
  errorFor(field: string): string | null {
    return this.serverErrors()[field] ?? null;
  }

  /** Every server error, with the step each belongs to, for the summary banner. */
  readonly serverErrorList = computed(() =>
    Object.entries(this.serverErrors()).map(([field, message]) => ({
      field,
      message,
      step: CreateUserComponent.FIELD_STEP[field] ?? 0,
      stepName: this.steps[CreateUserComponent.FIELD_STEP[field] ?? 0],
    })),
  );

  // ---- Options -------------------------------------------------------------------------------
  //
  // TWO SOURCES, AND THE DISTINCTION MATTERS. Reference data is this ORGANISATION'S records -
  // its roles, departments, units and managers, each a GUID. The enum payload is the PRODUCT'S
  // fixed vocabulary - account categories, engagement types - each a stable name. Reading one
  // from the other is how a dropdown ends up permanently empty.
  readonly organisationUnits = computed<LookupItem[]>(() => this.view()?.organisationUnits ?? []);
  readonly departments = computed<LookupItem[]>(() => this.view()?.departments ?? []);
  readonly roles = computed<LookupItem[]>(() => this.view()?.roles ?? []);

  /**
   * The role being granted, by name.
   *
   * FOR THE REVIEW STEP, which listed the name, the e-mail, the username, the account type, the
   * access dates, the two-step policy and the justification - and not the two fields that decide
   * what the account can actually DO. "Check before you send" was missing the thing worth
   * checking.
   */
  readonly primaryRoleName = computed(() => {
    const chosen = String(this.form().primaryRoleId ?? '');

    if (!chosen) {
      return 'Not chosen';
    }

    return this.roles().find((role) => String(role.id) === chosen)?.name ?? chosen;
  });

  /** The data scope being granted, by its display name rather than its enum value. */
  readonly dataScopeName = computed(() => {
    const chosen = String(this.form().dataScopeType ?? '');

    if (!chosen) {
      return 'Not chosen';
    }

    return this.dataScopeTypes().find((scope) => String(scope.value) === chosen)?.label ?? chosen;
  });
  readonly managers = computed<LookupItem[]>(() => this.view()?.managers ?? []);

  readonly accountCategories = computed<EnumOption[]>(() => this.enums()?.accountCategories ?? []);
  readonly engagementTypes = computed<EnumOption[]>(() => this.enums()?.engagementTypes ?? []);
  readonly dataScopeTypes = computed<EnumOption[]>(() => this.enums()?.dataScopeTypes ?? []);
  readonly mfaRequirements = computed<EnumOption[]>(() => this.enums()?.mfaRequirements ?? []);

  readonly initials = computed(() => {
    const name = this.form().displayName.trim();
    if (!name) {
      return '?';
    }

    const parts = name.split(' ').filter(Boolean);
    return (parts.length > 1 ? parts[0][0] + parts[1][0] : parts[0].slice(0, 2)).toUpperCase();
  });

  // ---- Step validity ---------------------------------------------------------------------------

  /**
   * Fields that become mandatory for the chosen account category.
   *
   * MIRRORS THE SERVER'S VALIDATOR, and is a courtesy rather than a control: the API refuses a
   * create that breaks the same rule, so the worst this can do if it drifts is let somebody
   * press Create and be told one step later. It is written out here rather than fetched because
   * the rule is two lines long, and an endpoint whose whole payload is "employees need a staff
   * number" is more moving parts than the rule deserves.
   *
   * The rule: an EMPLOYEE has a staff number, because payroll and the directory both key on it.
   * A volunteer, a contractor or an external party does not.
   */
  readonly conditionalFields = computed<string[]>(() => {
    const form = this.form();

    // CASE-INSENSITIVE, AND IT HAS TO BE. The form holds the server's own enum value, which is
    // "Employee" with a capital E - this compared it against lower-case "employee", so the test
    // was never true and the field was never marked required. The server, meanwhile, refuses the
    // create without it. The person filled in the form, saw no asterisk and no validation, and
    // was rejected on submit for a field nothing had asked them for.
    const isEmployee = form.accountCategory?.toLowerCase() === 'employee';

    // The server narrows it further: a staff number is expected of full and part-time employees,
    // not of somebody on a contract, an internship or an external engagement. Mirrored here so
    // the form asks for exactly what the API will insist on - no more, and no less.
    const engagement = form.engagementType?.toLowerCase();
    const isSalaried = engagement === 'fulltime' || engagement === 'parttime';

    return isEmployee && isSalaried ? ['employeeNumber'] : [];
  });

  readonly employeeNumberRequired = computed(() =>
    this.conditionalFields().some((field) => field.toLowerCase().includes('employee')),
  );

  readonly mobileRequired = computed(() =>
    this.conditionalFields().some((field) => field.toLowerCase().includes('mobile')),
  );

  readonly identityComplete = computed(() => {
    const f = this.form();

    const core = Boolean(
      f.firstName.trim() && f.lastName.trim() && f.displayName.trim() && f.email.trim() && f.username.trim(),
    );

    const conditional =
      (!this.employeeNumberRequired() || Boolean(f.employeeNumber.trim())) &&
      (!this.mobileRequired() || Boolean(f.mobileNumber.trim()));

    return core && conditional;
  });

  /**
   * The Organisation step has nothing that must be answered.
   *
   * Organisation unit USED TO BE REQUIRED HERE, and the API has always treated it as optional -
   * OrganisationUnitId is a nullable Guid defaulting to null. A brand-new Organisation has no
   * units yet, so the only choice in the list was "Choose…", the step could never be
   * completed, and NO USER COULD BE CREATED AT ALL until somebody guessed that a unit had to be
   * built first. The one requirement the client added by itself was the one that blocked the
   * screen.
   *
   * Units and departments are a reporting structure. They are worth filling in, and the hint
   * beside the empty list says where to create them, but nobody should be unable to invite their
   * first colleague for want of an org chart.
   */
  readonly organisationComplete = computed(() => true);

  readonly accessComplete = computed(() => {
    const f = this.form();
    // The API insists on a justification of at least ten characters: granting access without a
    // recorded reason is exactly what an access review later has no answer for.
    return Boolean(f.primaryRoleId && f.businessJustification.trim().length >= 10);
  });

  readonly canSubmit = computed(
    () => this.identityComplete() && this.organisationComplete() && this.accessComplete() && !this.submitting(),
  );

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    // Both in one go: the form is unusable until BOTH have arrived, so waiting for the pair
    // is honest about that rather than rendering half a form and filling the rest in later.
    forkJoin({
      reference: this.api.getFormReferenceData(),
      enums: this.api.getEnumOptions(),
    }).subscribe({
      next: ({ reference, enums }) => {
        this.loading.set(false);
        this.view.set(reference);
        this.enums.set(enums);

        // Defaults come from what the server actually offers, rather than from strings guessed
        // here that may not exist in this Organisation.
        this.form.update((current) => ({
          ...current,
          organisationUnitId: reference.organisationUnits?.[0]?.id ?? '',
        }));
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadFailed.set(true);
        this.errorMessage.set(error.message);
      },
    });
  }

  // =========================================================================================
  // Form helpers
  // =========================================================================================

  update<K extends keyof ReturnType<typeof this.form>>(key: K, value: ReturnType<typeof this.form>[K]): void {
    this.form.update((current) => ({ ...current, [key]: value }));
    this.errorMessage.set('');

    // Editing a field the server complained about clears that complaint, so a stale message
    // cannot sit under a value the person has already corrected.
    const errors = this.serverErrors();
    if (errors[key as string]) {
      const { [key as string]: _removed, ...rest } = errors;
      this.serverErrors.set(rest);
    }
  }

  /**
   * The country codes the form offers.
   *
   * A plain text box here was the cause of a rejected create that the screen could not explain:
   * the API requires the international form (+91), a typed "91" was accepted by the browser, and
   * the failure only surfaced on the very last step. A fixed list cannot produce an invalid value.
   */
  /**
   * Time zones and dialling prefixes from the GlobalMaster catalogue.
   *
   * THIS FORM NEVER ASKS FOR A COUNTRY, which is exactly the case the brief calls out. The
   * cascade is therefore used in its uncountried mode: `geo.timeZones()` holds the FULL zone
   * catalogue from the first render, with no country to link to and nothing thrown. See
   * `GeoMasterService.getTimeZones`.
   *
   * The zone list here used to be five hard-coded <option> elements, one of which - the "UTC"
   * this form still defaults to - existed nowhere in the database.
   */
  protected readonly geo = createGeoCascade();

  /**
   * The dialling prefixes, derived from the countries rather than listed by hand.
   *
   * Was ten literals. A country added on the Masters screen now brings its prefix with it, and
   * the list cannot drift from the country dropdown on the next form along. Duplicates are
   * collapsed - Canada and the United States both dial +1 - and the order follows the country
   * sort order, so +91 stays first.
   */
  protected readonly countryCodes = computed(() => [
    ...new Set(
      this.geo
        .countries()
        .map((country) => country.phoneCountryCode)
        .filter((code): code is string => !!code),
    ),
  ]);

  /** Fills the display name from the first and last name, until somebody edits it themselves. */
  onNameChanged(): void {
    const f = this.form();
    const suggested = [f.firstName.trim(), f.lastName.trim()].filter(Boolean).join(' ');

    if (suggested && !f.displayName.trim()) {
      this.update('displayName', suggested);
    }
  }

  /** Suggests a username from the e-mail, which is what most people would type anyway. */
  onEmailChanged(): void {
    const f = this.form();

    if (f.email.includes('@') && !f.username.trim()) {
      this.update('username', f.email.split('@')[0].toLowerCase());
    }

    this.checkIdentity();
  }

  /**
   * Asks the server whether the e-mail or username is already taken, without naming the owner.
   *
   * NO ORGANISATION IS SENT. The check runs inside whichever Organisation the token names, which
   * is exactly the scope the uniqueness rule uses: the same address may exist in another
   * Organisation and that is not a clash. Passing an id from the browser would let the check be
   * aimed at somebody else's Organisation and answer a question about their people.
   */
  checkIdentity(): void {
    const f = this.form();

    if (!f.email.trim() && !f.username.trim()) {
      return;
    }

    this.identityCheck.set({ checking: true, message: '', ok: false, suggestions: [] });

    this.api
      .checkIdentity({
        email: f.email.trim() || undefined,
        username: f.username.trim() || undefined,
      })
      .subscribe({
      next: (outcome) =>
        this.identityCheck.set({
          checking: false,
          message: outcome.message ?? '',
          ok: outcome.isAvailable === true,
          suggestions: outcome.suggestions ?? [],
        }),
      // A failed check must not block the form. The server validates again on create, so the
      // worst case is finding out one step later rather than here.
      error: () => this.identityCheck.set(null),
    });
  }

  // =========================================================================================
  // Navigation
  // =========================================================================================

  goToStep(index: number): void {
    // Forward movement is gated on the current step being complete; going back never is, so a
    // half-finished form is never a trap.
    if (index <= this.activeStep() || this.isStepComplete(this.activeStep())) {
      this.activeStep.set(index);
      this.submitted.set(false);
    }
  }

  nextStep(): void {
    if (!this.isStepComplete(this.activeStep())) {
      this.submitted.set(true);
      return;
    }

    this.activeStep.update((step) => Math.min(step + 1, this.steps.length - 1));
    this.submitted.set(false);
  }

  previousStep(): void {
    this.activeStep.update((step) => Math.max(step - 1, 0));
  }

  isStepComplete(index: number): boolean {
    switch (index) {
      case 0: return this.identityComplete();
      case 1: return this.organisationComplete();
      case 2: return this.accessComplete();
      default: return true;
    }
  }

  isFieldInvalid(field: string): boolean {
    // A field the server rejected is invalid whatever the local rules think — its rules are the
    // ones that actually decide whether the create succeeds.
    if (this.errorFor(field)) {
      return true;
    }

    if (!this.submitted()) {
      return false;
    }

    const f = this.form();

    switch (field) {
      case 'firstName': return !f.firstName.trim();
      case 'lastName': return !f.lastName.trim();
      case 'displayName': return !f.displayName.trim();
      case 'email': return !f.email.trim();
      case 'username': return !f.username.trim();
      case 'primaryRoleId': return !f.primaryRoleId;
      case 'businessJustification': return f.businessJustification.trim().length < 10;
      // Only invalid when the chosen account category actually demands them.
      case 'employeeNumber': return this.employeeNumberRequired() && !f.employeeNumber.trim();
      case 'mobileNumber': return this.mobileRequired() && !f.mobileNumber.trim();
      default: return false;
    }
  }

  // =========================================================================================
  // Submit
  // =========================================================================================

  /** Creates the account and sends the invitation e-mail. */
  createAndInvite(): void {
    this.submit(true);
  }

  /** Creates the account and leaves the invitation for later. */
  createWithoutInvite(): void {
    this.submit(false);
  }

  private submit(sendInvitation: boolean): void {
    this.submitted.set(true);

    if (!this.canSubmit()) {
      this.errorMessage.set('Some required details are still missing. Check each step above.');
      return;
    }

    // The Organisation comes from the token, never from this form - see checkIdentity above.
    if (!this.tokens.tenant()?.tenantId && !this.tokens.isSuperAdmin()) {
      this.errorMessage.set('Your session is not operating in an organisation. Sign in again.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    const f = this.form();

    // NO ORGANISATION FIELD. It comes from the signed token; an id from this form would let
    // the request be aimed at somebody else's Organisation.
    const request: CreateUserRequest = {
      firstName: f.firstName.trim(),
      middleName: f.middleName || null,
      lastName: f.lastName.trim(),
      displayName: f.displayName.trim(),
      email: f.email.trim().toLowerCase(),
      username: f.username.trim().toLowerCase() || null,
      employeeNumber: f.employeeNumber || null,
      mobileCountryCode: f.mobileNumber ? f.mobileCountryCode : null,
      mobileNumber: f.mobileNumber || null,

      accountCategory: f.accountCategory as CreateUserRequest['accountCategory'],
      engagementType: f.engagementType as CreateUserRequest['engagementType'],
      organisationUnitId: f.organisationUnitId || null,
      departmentId: f.departmentId || null,
      designation: f.designation || null,
      managerUserId: null,

      // Dates go over the wire as UTC instants, not as the local strings the pickers produce.
      accessStartsAtUtc: f.accessStartsAt ? new Date(f.accessStartsAt).toISOString() : null,
      accessEndsAtUtc: f.accessEndsAt ? new Date(f.accessEndsAt).toISOString() : null,

      mfaRequirement: f.mfaRequirement as CreateUserRequest['mfaRequirement'],
      roleIds: f.primaryRoleId ? [f.primaryRoleId] : [],
      dataScopes: [],

      // The person sets their own password from the e-mailed link, so no temporary password is
      // ever generated, written down, or sent anywhere.
      credentialSetupMethod: 'invitationLink',
      sendInvitation,
      invitationMessage: f.welcomeMessage || null,
    };

    this.api.createUser(request).subscribe({
      next: (user) => {
        this.submitting.set(false);

        this.createdUser.set({
          code: user.code ?? '',
          displayName: user.displayName ?? '',
          email: user.email ?? '',
          requestedInvite: sendInvitation,
          invitationStatus: user.invitationSent ? 'Sent' : 'NotSent',
        });

        // `invitationSent` reports what actually happened, not what was asked for. Delivery can
        // fail after the account is safely created, and saying "invitation sent" when it was not
        // is how somebody waits three days for an e-mail that never left.
        const delivered = user.invitationSent === true;

        this.toast.show(
          'Account created',
          !sendInvitation
            ? `${user.displayName} was created. No invitation was sent yet.`
            : delivered
              ? `${user.displayName} has been e-mailed an invitation link.`
              : `${user.displayName} was created, but the invitation e-mail could not be sent.`,
          delivered || !sendInvitation ? 'success' : 'warning',
        );
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.errorMessage.set(error.message);
        this.applyServerErrors(error);
      },
    });
  }

  /**
   * Turns the API's `errors` array into per-field messages and jumps to the earliest step that
   * has one, so "review the highlighted fields" actually has something highlighted to look at.
   */
  private applyServerErrors(error: Error): void {
    // The interceptor copies the envelope's `errors` onto the error object. Reading it through a
    // cast keeps that contract in one place rather than widening the type everywhere.
    const details = (error as { validationErrors?: { field: string; message: string }[] }).validationErrors ?? [];

    if (details.length === 0) {
      this.serverErrors.set({});
      return;
    }

    const mapped: Record<string, string> = {};
    for (const detail of details) {
      // The API camel-cases its field names; guard anyway so an unexpected casing still shows.
      const key = detail.field.charAt(0).toLowerCase() + detail.field.slice(1);
      mapped[key] = detail.message;
    }

    this.serverErrors.set(mapped);
    this.submitted.set(true);

    const firstStep = Math.min(...Object.keys(mapped).map((f) => CreateUserComponent.FIELD_STEP[f] ?? 0));
    if (Number.isFinite(firstStep)) {
      this.activeStep.set(firstStep);
    }
  }

  // =========================================================================================
  // After creation
  // =========================================================================================

  createAnother(): void {
    this.createdUser.set(null);
    this.activeStep.set(0);
    this.submitted.set(false);
    this.identityCheck.set(null);

    this.form.update((current) => ({
      ...current,
      title: '',
      firstName: '',
      middleName: '',
      lastName: '',
      displayName: '',
      preferredName: '',
      email: '',
      username: '',
      mobileNumber: '',
      employeeNumber: '',
      designation: '',
      businessJustification: '',
      welcomeMessage: '',
    }));
  }

  goBack(): void {
    void this.router.navigate(['/app/administration/access/user-directory']);
  }
}
