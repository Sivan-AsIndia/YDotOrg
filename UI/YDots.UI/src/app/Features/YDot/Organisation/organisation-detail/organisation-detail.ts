import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import {
  DocumentSubmissionsComponent,
  DocumentSubmissionsMode,
} from '../../../../Shared/document-submissions/document-submissions';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Observable, Subject, takeUntil } from 'rxjs';
import { OrganisationApiService } from '../../../../Service/organisation-api.service';
import { apiErrorMessage, apiFieldErrors } from '../../../../Shared/models/api-response.model';
import {
  MfaRequirement,
  OrganisationDetailResponse,
  TenantDocumentType,
} from '../../../../Shared/models/iam-contract.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';

type Tab = 'profile' | 'documents' | 'settings' | 'domains' | 'timeline';

/**
 * One Organisation: its profile, its documents, its settings, its history.
 *
 * ONE COMPONENT, TWO AUDIENCES — AND THE DIFFERENCE IS THE URL
 * ------------------------------------------------------------
 * With an id in the route (`/organisation/details/:id`) this is SuperAdmin looking at somebody
 * else's Organisation: read-mostly, with the lifecycle actions.
 *
 * Without one (`/organisation/details`) it is a TenantAdmin editing THEIR OWN, and every call it
 * makes goes to `/organisations/mine`, which takes no id at all. That is deliberate: a TenantAdmin
 * has nothing in the URL to change in order to reach another Organisation. The two paths are
 * separate endpoints rather than one endpoint with a permission check, because "there is no
 * parameter" is a stronger guarantee than "the parameter is checked".
 *
 * WHY THE BUTTONS COME FROM THE SERVER
 * ------------------------------------
 * `permittedActions` is computed server-side from the Organisation's lifecycle state and the
 * caller's permissions. Rendering from it means the actions offered and the actions the API will
 * accept cannot drift apart — which is what happens the moment a component starts deciding for
 * itself that "submitted means show Approve".
 */
@Component({
  selector: 'app-organisation-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, DocumentSubmissionsComponent],
  templateUrl: './organisation-detail.html',
  styleUrl: './organisation-detail.css',
})
export class OrganisationDetailComponent implements OnInit, OnDestroy {
  private readonly api = inject(OrganisationApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly tokens = inject(AuthTokenService);

  private readonly destroy$ = new Subject<void>();

  /** Set only on the platform route. Null means "my own organisation". */
  readonly organisationId = signal<string | null>(null);

  readonly organisation = signal<OrganisationDetailResponse | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});

  readonly tab = signal<Tab>('profile');
  readonly editing = signal(false);

  // ---- Profile form -------------------------------------------------------------------------
  readonly form = signal({
    name: '',
    legalName: '',
    registrationNumber: '',
    taxIdentificationNumber: '',
    panNumber: '',
    gstNumber: '',
    organisationType: '',
    establishedOn: '',
    description: '',
    websiteUrl: '',
    contactPersonName: '',
    contactEmail: '',
    contactPhoneCountryCode: '+91',
    contactPhone: '',
    addressLine1: '',
    addressLine2: '',
    city: '',
    state: '',
    country: '',
    postalCode: '',
    timeZone: '',
    defaultCurrency: '',
    defaultCulture: '',
  });

  // ---- Settings form ------------------------------------------------------------------------
  readonly settingsForm = signal({
    defaultMfaRequirement: 'optional' as MfaRequirement,
    maximumFailedAccessAttempts: 5,
    lockoutDurationMinutes: 15,
    passwordMinimumLength: 10,
    passwordExpiryDays: 0,
    sessionIdleTimeoutMinutes: 30,
  });

  readonly submitNotes = signal('');

  readonly documentTypes: { value: TenantDocumentType; label: string }[] = [
    { value: 'registrationCertificate', label: 'Registration certificate' },
    { value: 'taxExemptionCertificate', label: 'Tax exemption certificate' },
    { value: 'panCard', label: 'PAN card' },
    { value: 'gstCertificate', label: 'GST certificate' },
    { value: 'addressProof', label: 'Proof of address' },
    { value: 'bankProof', label: 'Proof of bank account' },
    { value: 'trustDeed', label: 'Trust deed' },
    { value: 'annualReport', label: 'Annual report' },
    { value: 'authorisedSignatoryProof', label: 'Authorised signatory proof' },
    { value: 'logo', label: 'Logo' },
    { value: 'other', label: 'Other' },
  ];

  /**
   * Country, state and city from the GlobalMaster catalogue.
   *
   * All three were free-text boxes with a `maxlength` and nothing else, so "India", "india" and
   * "Inida" were equally acceptable and the stored address could not be grouped or reported on.
   */
  protected readonly geo = createGeoCascade();

  /** Dialling prefixes derived from the country catalogue rather than listed by hand. */
  protected readonly countryCodes = computed(() => [
    ...new Set(
      this.geo
        .countries()
        .map((country) => country.phoneCountryCode)
        .filter((code): code is string => !!code),
    ),
  ]);

  protected onCountryChange(value: string): void {
    // The state and city are cleared with the country, so a saved address cannot end up naming a
    // state that does not exist in the country beside it.
    this.form.update((f) => ({ ...f, country: value, state: '', city: '' }));
    this.geo.selectCountry(value);
  }

  protected onStateChange(value: string): void {
    this.form.update((f) => ({ ...f, state: value, city: '' }));
    this.geo.selectState(value);
  }

  // =========================================================================================
  // Derived
  // =========================================================================================

  /** True when this is the caller's own Organisation rather than one they are administering. */
  readonly isOwnOrganisation = computed(() => this.organisationId() === null);

  /**
   * Which side of the desk the Documents tab is drawn for.
   *
   * WITHOUT AN ID this is the Organisation's own screen, so the submissions component talks to
   * `/organisations/mine/...` - an endpoint that carries no id and therefore cannot be pointed at
   * anybody else. WITH AN ID it is the platform reviewer, and the review endpoints take that id.
   *
   * IT USED TO BE THE LITERAL 'tenant' IN THE TEMPLATE. A SuperAdmin opening
   * /organisation/details/{id} has no Organisation selected, so `/organisations/mine/...` failed
   * with 409 TENANT_SELECTION_REQUIRED and the tab rendered "Select an organisation to continue."
   * above an upload box offering to start a submission on nobody's behalf.
   */
  readonly documentsMode = computed<DocumentSubmissionsMode>(() =>
    this.isOwnOrganisation() ? 'tenant' : 'review');

  readonly permittedActions = computed(() => this.organisation()?.permittedActions ?? []);

  can(action: string): boolean {
    return this.permittedActions().includes(action);
  }

  readonly outstandingFields = computed(() => this.organisation()?.outstandingProfileFields ?? []);
  readonly isProfileComplete = computed(() => this.organisation()?.isProfileComplete === true);

  /**
   * Which boxes the server will actually accept a value from, lower-cased for comparison.
   *
   * NULL, NOT AN EMPTY ARRAY, IS THE "NO RESTRICTION" CASE. An API build that predates the
   * field sends nothing, and treating that as "no field is editable" would grey out the whole
   * form; an API that deliberately closes the profile — while a submission is with a reviewer,
   * or after archival — sends `[]` and means it. The two have to stay distinguishable.
   */
  private readonly editableFieldSet = computed<ReadonlySet<string> | null>(() => {
    const fields = this.organisation()?.editableProfileFields;

    return fields ? new Set(fields.map((field) => field.toLowerCase())) : null;
  });

  /** True when this state limits editing to contact e-mail, telephone and address. */
  readonly isProfileRestricted = computed(() => {
    const fields = this.editableFieldSet();

    return !!fields && fields.size > 0 && !fields.has('name');
  });

  /**
   * Whether one profile box may be typed into.
   *
   * The server decides and this only draws the answer — a disabled box is a courtesy so nobody
   * types a new registration number, saves, and watches it come back unchanged with no
   * explanation.
   */
  canEditField(field: string): boolean {
    const fields = this.editableFieldSet();

    return fields === null || fields.has(field.toLowerCase());
  }

  readonly documents = computed(() => this.organisation()?.documents ?? []);

  /**
   * PAN and GSTIN, checked for SHAPE as they are typed.
   *
   * Both boxes previously carried a `maxlength` and nothing else, on either side: the server's
   * validator asked only for a length too. They are the two identifiers a platform reviewer
   * checks the registration certificate against, so a transposed character is an organisation
   * approved against evidence that does not match its own record - and nothing downstream can
   * tell. The same two patterns are enforced in
   * `UpdateOrganisationProfileRequestValidator`; these exist so the correction happens while the
   * person is still looking at the field.
   *
   * EMPTY IS VALID. Neither is a required profile field, so the rule applies to a value that has
   * actually been entered.
   */
  private static readonly PanPattern = /^[A-Za-z]{5}[0-9]{4}[A-Za-z]$/;
  private static readonly GstPattern = /^[0-9]{2}[A-Za-z]{5}[0-9]{4}[A-Za-z][0-9A-Za-z][Zz][0-9A-Za-z]$/;

  readonly panValid = computed(() => {
    const value = this.form().panNumber.trim();
    return !value || OrganisationDetailComponent.PanPattern.test(value);
  });

  readonly gstValid = computed(() => {
    const value = this.form().gstNumber.trim();
    return !value || OrganisationDetailComponent.GstPattern.test(value);
  });

  /**
   * Files belonging to no grouped submission.
   *
   * These predate submissions, so nothing else lists them. Filtering on the missing submission
   * is also what keeps a grouped file from appearing both in the submissions component and
   * again in the table below it.
   */
  readonly ungroupedDocuments = computed(() =>
    this.documents().filter((document) => !document.submissionId));

  /** Re-reads the organisation after the submissions component changes something. */
  reload(): void {
    this.load();
  }
  readonly domains = computed(() => this.organisation()?.domains ?? []);
  readonly timeline = computed(() => this.organisation()?.timeline ?? []);

  /**
   * Whether the profile may be edited.
   *
   * TWO NAMES FOR ONE THING, and this screen only knew one of them. The server calls it "Edit"
   * while an Organisation is Invited or Active, and "EditProfile" during onboarding -
   * InvitationAccepted, ProfileIncomplete and Rejected. This asked for "Edit" alone, so the
   * button disappeared in exactly the three states where the profile MUST be edited.
   *
   * The visible result was a screen that listed the seven fields still required and offered no
   * way to fill any of them in: the organisation could never be completed, so it could never be
   * submitted, so it could never be approved.
   */
  readonly canEditProfile = computed(
    () => this.isOwnOrganisation() && (this.can('Edit') || this.can('EditProfile')));

  /**
   * Whether it can be sent for approval.
   *
   * "Resubmit" is the same button after a rejection. Omitting it meant an Organisation that had
   * been sent back could be corrected and then not returned - the correction had nowhere to go.
   */
  readonly canSubmit = computed(
    () => this.isOwnOrganisation()
      && (this.can('Submit') || this.can('Resubmit'))
      && this.isProfileComplete());

  /**
   * Whether the Organisation has been approved.
   *
   * The security settings are part of RUNNING an Organisation, and that starts once SuperAdmin
   * has accepted it. Before then the server refuses `PUT /organisations/mine/settings` along
   * with the rest of the Tenant surface, so drawing the tab would offer a form that cannot be
   * saved. Approved rather than Active, deliberately: acceptance is the decision that matters
   * here, and activation is a separate step afterwards.
   */
  readonly isApproved = computed(() => {
    const status = this.organisation()?.status;

    return status === 'approved' || status === 'active';
  });

  readonly isSuperAdmin = computed(() => this.tokens.isSuperAdmin());

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    this.organisationId.set(id);
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    const id = this.organisationId();
    const request: Observable<OrganisationDetailResponse> = id ? this.api.get(id) : this.api.getMine();

    request.pipe(takeUntil(this.destroy$)).subscribe({
      next: (organisation) => {
        this.organisation.set(organisation);
        this.fillForms(organisation);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadFailed.set(true);
        this.errorMessage.set(apiErrorMessage(error, 'This organisation could not be loaded.'));
      },
    });
  }

  private fillForms(organisation: OrganisationDetailResponse): void {
    this.form.set({
      name: organisation.name ?? '',
      legalName: organisation.legalName ?? '',
      registrationNumber: organisation.registrationNumber ?? '',
      taxIdentificationNumber: organisation.taxIdentificationNumber ?? '',
      panNumber: organisation.panNumber ?? '',
      gstNumber: organisation.gstNumber ?? '',
      organisationType: organisation.organisationType ?? '',
      establishedOn: this.toDateInput(organisation.establishedOn),
      description: organisation.description ?? '',
      websiteUrl: organisation.websiteUrl ?? '',
      contactPersonName: organisation.contactPersonName ?? '',
      contactEmail: organisation.contactEmail ?? '',
      contactPhoneCountryCode: organisation.contactPhoneCountryCode ?? '+91',
      contactPhone: organisation.contactPhone ?? '',
      addressLine1: organisation.addressLine1 ?? '',
      addressLine2: organisation.addressLine2 ?? '',
      city: organisation.city ?? '',
      state: organisation.state ?? '',
      country: organisation.country ?? '',
      postalCode: organisation.postalCode ?? '',
      timeZone: organisation.timeZone ?? '',
      defaultCurrency: organisation.defaultCurrency ?? '',
      defaultCulture: organisation.defaultCulture ?? '',
    });

    // Rebuild the cascade from the stored names so the state and city dropdowns open populated
    // and showing what was saved, rather than blank.
    this.geo.restore(organisation.country, organisation.state, organisation.city);

    this.settingsForm.set({
      defaultMfaRequirement: organisation.defaultMfaRequirement ?? 'optional',
      maximumFailedAccessAttempts: organisation.maximumFailedAccessAttempts ?? 5,
      lockoutDurationMinutes: organisation.lockoutDurationMinutes ?? 15,
      passwordMinimumLength: organisation.passwordMinimumLength ?? 10,
      passwordExpiryDays: organisation.passwordExpiryDays ?? 0,
      sessionIdleTimeoutMinutes: organisation.sessionIdleTimeoutMinutes ?? 30,
    });
  }

  // =========================================================================================
  // Profile
  // =========================================================================================

  update<K extends keyof ReturnType<typeof this.form>>(
    key: K, value: ReturnType<typeof this.form>[K]): void {
    this.form.update((current) => ({ ...current, [key]: value }));
  }

  updateSetting<K extends keyof ReturnType<typeof this.settingsForm>>(
    key: K, value: ReturnType<typeof this.settingsForm>[K]): void {
    this.settingsForm.update((current) => ({ ...current, [key]: value }));
  }

  startEditing(): void {
    this.editing.set(true);
    this.errorMessage.set('');
  }

  cancelEditing(): void {
    const organisation = this.organisation();

    if (organisation) {
      this.fillForms(organisation);
    }

    this.editing.set(false);
    this.errorMessage.set('');
    this.fieldErrors.set({});
  }

  /**
   * Saves the profile.
   *
   * PARTIAL SAVES ARE FINE. Completeness is checked at submission, not here, so somebody can
   * fill in half the form, go and find the registration number, and come back to the rest.
   */
  saveProfile(): void {
    const organisation = this.organisation();

    if (!organisation || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.fieldErrors.set({});

    const f = this.form();

    this.api
      .updateMine({
        expectedVersion: organisation.version ?? 0,
        name: f.name.trim() || null,
        legalName: f.legalName.trim() || null,
        registrationNumber: f.registrationNumber.trim() || null,
        taxIdentificationNumber: f.taxIdentificationNumber.trim() || null,
        panNumber: f.panNumber.trim() || null,
        gstNumber: f.gstNumber.trim() || null,
        organisationType: f.organisationType || null,
        establishedOn: f.establishedOn || null,
        description: f.description.trim() || null,
        websiteUrl: f.websiteUrl.trim() || null,
        contactPersonName: f.contactPersonName.trim() || null,
        contactEmail: f.contactEmail.trim() || null,
        contactPhoneCountryCode: f.contactPhone ? f.contactPhoneCountryCode : null,
        contactPhone: f.contactPhone.trim() || null,
        addressLine1: f.addressLine1.trim() || null,
        addressLine2: f.addressLine2.trim() || null,
        city: f.city.trim() || null,
        state: f.state.trim() || null,
        country: f.country.trim() || null,
        postalCode: f.postalCode.trim() || null,
        timeZone: f.timeZone.trim() || null,
        defaultCurrency: f.defaultCurrency.trim() || null,
        defaultCulture: f.defaultCulture.trim() || null,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.editing.set(false);
          this.toast.show('Saved', outcome.message ?? 'The profile has been saved.', 'success');
          this.load();
        },
        error: (error: unknown) => this.handleFailure(error, 'The profile could not be saved.'),
      });
  }

  /**
   * Sends the profile for approval.
   *
   * This is where completeness is enforced, which is why `outstandingProfileFields` is shown on
   * the screen: it is the list of what is still missing, straight from the server, rather than a
   * generic "some fields are required".
   */
  submitForApproval(): void {
    const organisation = this.organisation();

    if (!organisation || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api
      .submitMine({
        expectedVersion: organisation.version ?? 0,
        notes: this.submitNotes().trim() || null,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.submitNotes.set('');
          this.toast.show(
            'Submitted for approval',
            outcome.message ?? 'Your organisation profile is now with the platform team.',
            'success');
          this.load();
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(apiErrorMessage(error, 'The profile could not be submitted.'));
          this.fieldErrors.set(apiFieldErrors(error));
        },
      });
  }

  // =========================================================================================
  // Settings
  // =========================================================================================

  /**
   * Saves the security policy.
   *
   * An Organisation may TIGHTEN these but never loosen them below the platform floor — the
   * server clamps every value, so a request asking for a four-character minimum comes back
   * having been raised rather than accepted. The screen reloads afterwards so what is displayed
   * is what was actually stored.
   */
  saveSettings(): void {
    const organisation = this.organisation();

    if (!organisation || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');

    const s = this.settingsForm();

    this.api
      .updateMySettings({
        expectedVersion: organisation.version ?? 0,
        defaultMfaRequirement: s.defaultMfaRequirement,
        maximumFailedAccessAttempts: s.maximumFailedAccessAttempts,
        lockoutDurationMinutes: s.lockoutDurationMinutes,
        passwordMinimumLength: s.passwordMinimumLength,
        passwordExpiryDays: s.passwordExpiryDays,
        sessionIdleTimeoutMinutes: s.sessionIdleTimeoutMinutes,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.toast.show(
            'Settings saved',
            outcome.message ?? 'The security settings have been updated.',
            'success');
          this.load();
        },
        error: (error: unknown) => this.handleFailure(error, 'The settings could not be saved.'),
      });
  }

  // =========================================================================================
  // Documents
  //
  // The upload form that used to live here is gone. It posted metadata with a storage path
  // the BROWSER invented, which was harmless only while nothing was really stored - and is a
  // cross-tenant write now that files are. Uploading is handled by
  // <app-document-submissions>, which streams the bytes and lets the server derive the path.
  // =========================================================================================

  // =========================================================================================
  // Display helpers
  // =========================================================================================

  statusClass(status: string | undefined): string {
    switch (status) {
      case 'active':
      case 'approved':
        return 'is-good';
      case 'submitted':
      case 'underReview':
      case 'resubmitted':
        return 'is-warn';
      case 'rejected':
      case 'suspended':
        return 'is-error';
      case 'archived':
        return 'is-muted';
      default:
        return 'is-info';
    }
  }

  documentStatusClass(status: string | undefined): string {
    switch (status) {
      case 'accepted': return 'is-good';
      case 'rejected': return 'is-error';
      case 'superseded': return 'is-muted';
      case 'underReview': return 'is-warn';
      default: return 'is-info';
    }
  }

  /** Used by the template to decide whether there is a description worth rendering. */
  description(organisation: OrganisationDetailResponse): boolean {
    return !this.editing() && !!organisation.description;
  }

  goBack(): void {
    void this.router.navigate(['/app/administration/organisation/directory']);
  }

  /** A date-only input needs yyyy-MM-dd; the API sends a full instant. */
  private toDateInput(value: string | null | undefined): string {
    return value ? value.slice(0, 10) : '';
  }

  private handleFailure(error: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(apiErrorMessage(error, fallback));
    this.fieldErrors.set(apiFieldErrors(error));
  }
}
