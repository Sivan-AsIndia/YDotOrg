import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, switchMap, takeUntil } from 'rxjs';
import { OrganisationApiService } from '../../../../Service/organisation-api.service';
import { apiErrorMessage, apiFieldErrors } from '../../../../Shared/models/api-response.model';
import {
  CheckSubdomainResponse,
  CreateOrganisationResponse,
  MfaRequirement,
} from '../../../../Shared/models/iam-contract.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';

type WizardStep = 'organisation' | 'address' | 'administrator' | 'review' | 'done';

/**
 * Creating an Organisation, and inviting its first administrator.
 *
 * WHAT ONE PRESS OF "CREATE" ACTUALLY DOES
 * ----------------------------------------
 * A single API call creates the Organisation, reserves its host, seeds its roles and its default
 * navigation, creates the TenantAdmin account, and sends the invitation. It is one call on
 * purpose: an Organisation missing any of those is not usable, and a half-created one is worse
 * than none at all because somebody has to work out what is missing before it can be fixed.
 *
 * THE WEB ADDRESS IS THE PART PEOPLE GET WRONG
 * --------------------------------------------
 * The subdomain is permanent and is how the Organisation is identified on every future sign-in:
 * ten1.ngoplanet.com resolves to that Organisation and nothing else. So it is checked live as it
 * is typed, against the server, including the reserved words the platform keeps for itself. The
 * check answers only "free or not" and never lists what is taken, so it cannot be walked to
 * enumerate the platform's customers.
 *
 * WHY THE ADMINISTRATOR'S E-MAIL MAY ALREADY EXIST ELSEWHERE
 * ----------------------------------------------------------
 * The same address may administer several Organisations, and that is a normal arrangement rather
 * than a clash: users are per-Organisation, so the account created here is a NEW one that happens
 * to share an address with accounts elsewhere. What is refused is the same address twice inside
 * one Organisation.
 */
@Component({
  selector: 'app-organisation-setup-wizard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './organisation-setup-wizard.html',
  styleUrl: './organisation-setup-wizard.css',
})
export class OrganisationSetupWizardComponent implements OnDestroy {
  private readonly api = inject(OrganisationApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  private readonly destroy$ = new Subject<void>();
  private readonly subdomainInput$ = new Subject<string>();

  readonly step = signal<WizardStep>('organisation');
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly created = signal<CreateOrganisationResponse | null>(null);

  // ---- Live subdomain check ------------------------------------------------------------------
  readonly checkingSubdomain = signal(false);
  readonly subdomainCheck = signal<CheckSubdomainResponse | null>(null);

  /**
   * The domain new organisations hang off.
   *
   * FROM THE SERVER, because it is deployment configuration rather than a constant. The template
   * showed a hard-coded '.ngoplanet.com' until the availability check had run, which was wrong
   * everywhere the platform is not deployed on that domain - in Docker the root domain is
   * `localhost`, so the form promised the operator an address that would never resolve.
   */
  readonly rootDomain = signal('');

  // ---- The form ------------------------------------------------------------------------------
  readonly form = signal({
    name: '',
    legalName: '',
    subdomain: '',
    code: '',
    organisationType: '',
    contactPhoneCountryCode: '+91',
    contactPhone: '',
    timeZone: 'Asia/Kolkata',
    defaultCurrency: 'INR',
    defaultCulture: 'en-IN',
    maximumUsers: null as number | null,
    defaultMfaRequirement: 'optional' as MfaRequirement,

    adminFirstName: '',
    adminLastName: '',
    adminEmail: '',
    adminUsername: '',
    invitationMessage: '',
    sendInvitation: true,
  });

  /**
   * The organisation types offered.
   *
   * A free-text field here produces a directory where "NGO", "N.G.O." and "Ngo" are three
   * different things and none of them can be reported on. The list stays short and ends in
   * "Other", which is what makes it tolerable to constrain.
   */
  readonly organisationTypes = [
    'Non-profit / NGO',
    'Charitable organisation',
    'Foundation',
    'Community organisation',
    'Educational organisation',
    'Healthcare organisation',
    'Faith-based organisation',
    'Social welfare organisation',
    'International organisation',
    'Other',
  ];

  readonly mfaOptions: { value: MfaRequirement; label: string; hint: string }[] = [
    {
      value: 'optional',
      label: 'Optional',
      hint: 'People may add a second factor from their security page.',
    },
    {
      value: 'required',
      label: 'Required',
      hint: 'Everyone must enrol a second factor before they can work.',
    },
  ];

  /**
   * Time zone, currency, language and dialling prefix, from the GlobalMaster catalogue.
   *
   * ALL FOUR WERE FREE-TEXT BOXES. The time zone and currency were `<input type="text">`, so
   * "Asia/Calcutta", "IST" and "inr" were all accepted and stored, and nothing downstream could
   * rely on either value. A three-character `maxlength` is not a currency validator, and a
   * ten-character one is not a language validator.
   *
   * This wizard collects no country either, so the zone and language lists are the unfiltered
   * catalogues - the same supported case as user creation, and one the API answers rather than
   * refusing.
   */
  protected readonly geo = createGeoCascade();

  /** Dialling prefixes derived from the country catalogue, deduplicated. Was ten literals. */
  protected readonly countryCodes = computed(() => [
    ...new Set(
      this.geo
        .countries()
        .map((country) => country.phoneCountryCode)
        .filter((code): code is string => !!code),
    ),
  ]);

  // =========================================================================================
  // Step validity
  // =========================================================================================

  readonly subdomainAvailable = computed(() => this.subdomainCheck()?.isAvailable === true);

  readonly organisationStepValid = computed(() => {
    const f = this.form();
    return f.name.trim().length >= 2
      && f.subdomain.trim().length >= 3
      && this.subdomainAvailable()
      && !this.checkingSubdomain();
  });

  readonly addressStepValid = computed(() => true);

  readonly administratorStepValid = computed(() => {
    const f = this.form();
    return f.adminFirstName.trim().length > 0
      && f.adminLastName.trim().length > 0
      && this.looksLikeEmail(f.adminEmail);
  });

  readonly canCreate = computed(
    () => this.organisationStepValid() && this.administratorStepValid() && !this.submitting());

  readonly stepNumber = computed(() => {
    switch (this.step()) {
      case 'organisation': return 1;
      case 'address': return 2;
      case 'administrator': return 3;
      case 'review': return 4;
      default: return 5;
    }
  });

  constructor() {
    // The business unit owns the root domain. Loaded once, so the suffix beside the address field
    // is right before anybody types.
    this.api.getBusinessUnit().subscribe({
      next: (unit) => this.rootDomain.set(unit.rootDomain ?? ''),

      // An empty suffix is honest. Guessing a domain here is what produced the original problem.
      error: () => this.rootDomain.set(''),
    });

    // The availability check runs as the address is typed, not on blur: somebody who types a
    // taken name and moves straight to the next field would otherwise not find out until the
    // whole form is submitted.
    this.subdomainInput$
      .pipe(
        debounceTime(400),
        distinctUntilChanged(),
        switchMap((value) => {
          this.checkingSubdomain.set(true);
          return this.api.checkSubdomain(value);
        }),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: (result) => {
          this.checkingSubdomain.set(false);
          this.subdomainCheck.set(result);
        },
        error: () => {
          this.checkingSubdomain.set(false);
          this.subdomainCheck.set(null);
        },
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // =========================================================================================
  // Form handling
  // =========================================================================================

  update<K extends keyof ReturnType<typeof this.form>>(
    key: K, value: ReturnType<typeof this.form>[K]): void {
    this.form.update((current) => ({ ...current, [key]: value }));
    this.errorMessage.set('');
  }

  /**
   * Keeps the web address to what a host name can actually be.
   *
   * Lower case, letters, digits and hyphens. Doing it as the person types is kinder than
   * rejecting "Hope Foundation" after the fact, and it means what they see is exactly what will
   * be reserved.
   */
  onSubdomainInput(value: string): void {
    const cleaned = value.toLowerCase().replace(/[^a-z0-9-]/g, '').replace(/^-+/, '').slice(0, 63);

    this.update('subdomain', cleaned);
    this.subdomainCheck.set(null);

    if (cleaned.length >= 3) {
      this.subdomainInput$.next(cleaned);
    }
  }

  /** Suggests a web address from the name, which is what most people would have typed anyway. */
  suggestSubdomain(): void {
    const name = this.form().name.trim().toLowerCase();

    if (!name || this.form().subdomain) {
      return;
    }

    this.onSubdomainInput(name.replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 30));
  }

  useSuggestion(suggestion: string): void {
    this.onSubdomainInput(suggestion);
  }

  // =========================================================================================
  // Navigation
  // =========================================================================================

  goTo(step: WizardStep): void {
    this.step.set(step);
    this.errorMessage.set('');
  }

  next(): void {
    switch (this.step()) {
      case 'organisation':
        if (this.organisationStepValid()) {
          this.goTo('address');
        }
        return;

      case 'address':
        this.goTo('administrator');
        return;

      case 'administrator':
        if (this.administratorStepValid()) {
          this.goTo('review');
        }
        return;

      default:
        return;
    }
  }

  back(): void {
    switch (this.step()) {
      case 'address': this.goTo('organisation'); return;
      case 'administrator': this.goTo('address'); return;
      case 'review': this.goTo('administrator'); return;
      default: return;
    }
  }

  // =========================================================================================
  // Create
  // =========================================================================================

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.fieldErrors.set({});

    const f = this.form();

    this.api
      .create({
        name: f.name.trim(),
        subdomain: f.subdomain.trim(),
        adminEmail: f.adminEmail.trim().toLowerCase(),
        adminFirstName: f.adminFirstName.trim(),
        adminLastName: f.adminLastName.trim(),
        code: f.code.trim() || null,
        legalName: f.legalName.trim() || null,
        organisationType: f.organisationType || null,
        contactPhoneCountryCode: f.contactPhone ? f.contactPhoneCountryCode : null,
        contactPhone: f.contactPhone.trim() || null,
        adminUsername: f.adminUsername.trim() || null,
        timeZone: f.timeZone || null,
        defaultCurrency: f.defaultCurrency || null,
        defaultCulture: f.defaultCulture || null,
        maximumUsers: f.maximumUsers,
        defaultMfaRequirement: f.defaultMfaRequirement,
        invitationMessage: f.invitationMessage.trim() || null,
        sendInvitation: f.sendInvitation,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.created.set(result);
          this.step.set('done');

          // `invitationSent` reports what actually happened, not what was asked for: delivery can
          // fail after the Organisation is safely created, and saying "invitation sent" when it
          // was not is how somebody waits three days for an e-mail that never left.
          this.toast.show(
            'Organisation created',
            result.invitationSent
              ? `${result.name} was created and ${result.adminEmail} has been invited.`
              : `${result.name} was created, but the invitation e-mail could not be sent.`,
            result.invitationSent ? 'success' : 'warning');
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(apiErrorMessage(error, 'The organisation could not be created.'));
          this.fieldErrors.set(apiFieldErrors(error));

          // A clash on the web address is worth showing on the step that owns it, rather than
          // leaving somebody on the review page wondering which field to change.
          if (this.fieldErrors()['subdomain']) {
            this.goTo('organisation');
          }
        },
      });
  }

  viewCreated(): void {
    const id = this.created()?.tenantId;

    void this.router.navigate(
      id
        ? ['/app/administration/organisation/details', id]
        : ['/app/administration/organisation/directory']);
  }

  createAnother(): void {
    this.form.set({
      name: '', legalName: '', subdomain: '', code: '', organisationType: '',
      contactPhoneCountryCode: '+91', contactPhone: '',
      timeZone: 'Asia/Kolkata', defaultCurrency: 'INR', defaultCulture: 'en-IN',
      maximumUsers: null, defaultMfaRequirement: 'optional',
      adminFirstName: '', adminLastName: '', adminEmail: '', adminUsername: '',
      invitationMessage: '', sendInvitation: true,
    });

    this.subdomainCheck.set(null);
    this.created.set(null);
    this.fieldErrors.set({});
    this.step.set('organisation');
  }

  /**
   * Enough of a check to catch a typo, and no more.
   *
   * A full RFC 5322 pattern rejects addresses that genuinely work, and the server validates
   * properly anyway. The job here is to stop somebody submitting "j.smith" by accident.
   */
  private looksLikeEmail(value: string): boolean {
    const trimmed = value.trim();
    return trimmed.length > 3 && trimmed.includes('@') && !trimmed.endsWith('@');
  }
}
