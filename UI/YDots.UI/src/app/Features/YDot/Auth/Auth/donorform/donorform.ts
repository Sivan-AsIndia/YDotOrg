
import { CommonModule } from '@angular/common';
import { Component, Injector, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '../../../Finance/shared/toast.service';
import { CampaignStoreService } from '../../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../../Shared/services/current-user.service';
import { DataService } from '../../../../../Service/data.service';
import { PaymentApiService } from '../../../../../Service/payment-api.service';
import { GatewayCheckoutService } from '../../../../../Shared/services/gateway-checkout.service';
import {
  DonationRoutes,
  destinationAfterPayment,
  payerKind,
} from '../../../../../Shared/services/donation-redirect.policy';
import { apiErrorMessage } from '../../../../../Shared/models/api-response.model';
import {
  CheckoutSession,
  PublicCampaignSummary,
  ConfirmCheckoutRequest,
  CreateDonationIntentRequest,
  DonationIntentResponse,
} from '../../../../../Shared/models/payment.model';

type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

type LifecycleState = 'No record' | 'Submitted' | 'Awaiting payment';

type DonorType = 'Individual' | 'Organisation';
type DedicationType = 'In memory of' | 'In honor of';

interface EffectivePermissions {
  readonly view: boolean; 
  readonly submit: boolean; 
  readonly continueToPayment: boolean; 
}

interface ScopeOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

interface CatalogueOption {
  readonly reference: string;
  readonly label: string;
}
interface ActivityEntry {
  readonly time: string;
  readonly text: string;
}

type RelatedTab = 'Linked' | 'Documents' | 'Activity' | 'Integration' | 'Support' | 'Audit';

interface PublicDonationInitiationConfig {
  readonly pageTitle: string;
  readonly pageSubtitle: string;
  readonly operatingTimeZone: string;
  readonly consentPolicyVersion: string;
  readonly campaigns: readonly ScopeOption[];
  readonly currencies: readonly CatalogueOption[];
  readonly geographies: readonly CatalogueOption[];
  readonly permissions: EffectivePermissions;
  readonly maxDonationAmount: number;
}


/**
 * The campaign states that may receive a donation.
 *
 * See the note on `campaignOptions`. Kept as one list so the two donation forms cannot drift
 * apart about what "an approved campaign" means.
 */
const DonatableCampaignStatuses: readonly string[] = ['Approved', 'Scheduled', 'Active'];

@Component({
  selector: 'app-donorform',
 imports: [CommonModule, FormsModule],
  templateUrl:'./donorform.html',
  styleUrl: './donorform.css',
})
export class DonorformComponent {

  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  private readonly currentUser = inject(CurrentUserService);

  /**
   * The campaign store, resolved ONLY for a signed-in caller.
   *
   * IT IS DELIBERATELY NOT A FIELD INJECTION ANY MORE. CampaignStoreService calls the
   * authenticated campaign API in its own constructor and again every sixty seconds, so
   * injecting it here meant a stranger who scanned a QR code triggered a 401 on page load and
   * another every minute they spent reading the form - on the one screen in the application
   * whose entire purpose is to serve somebody with no account. Resolving it lazily means the
   * anonymous path never constructs it at all.
   *
   * AN ANONYMOUS DONOR LOSES NOTHING BY IT. Their campaign comes from the tracking reference in
   * the link they followed, which the API resolves server-side against the right organisation -
   * which is also the only way it could be resolved safely, since a list here would have to
   * offer every organisation's campaigns to everybody.
   */
  private campaignStoreOrNull(): CampaignStoreService | null {
    if (!this.isInternalView()) {
      return null;
    }

    this.campaignStoreRef ??= this.injector.get(CampaignStoreService);
    return this.campaignStoreRef;
  }

  private campaignStoreRef: CampaignStoreService | null = null;

  /**
   * Whether somebody signed in is looking at this form.
   *
   * THE DIFFERENCE IS THE CAMPAIGN PICKER, not the fields. A fundraiser capturing a donation on
   * a lead's behalf can be offered the campaign list because the campaign API will answer them;
   * a donor arriving from a poster cannot.
   */
  protected readonly isInternalView = computed(() => this.currentUser.reference() !== '');

  /**
   * The payments API. Every method this screen calls is one of the PUBLIC ones - no token, no
   * permission - because the person in front of this form does not have an account yet.
   */
  private readonly payments = inject(PaymentApiService);

  /**
   * Opens whichever provider the ORGANISATION is configured for.
   *
   * THIS IS WHAT REPLACED `key: 'rzp_test_TCwSZidEO9q88a'`. The form used to open Razorpay
   * directly with a test key compiled into the bundle, and with no order behind it - so every
   * organisation's donations went to one test merchant account, the amount charged was whatever
   * the browser said, and an organisation configured for any other provider was still shown
   * Razorpay. The server now creates the order against that organisation's own configured
   * gateway and this opens whatever the session names.
   */
  private readonly checkout = inject(GatewayCheckoutService);

  /**
   * The tracking reference from the QR code or link.
   *
   * IT IS HOW AN ANONYMOUS DONOR GETS A CAMPAIGN, and how the platform knows which organisation
   * the gift belongs to. Nobody signed in means no campaign list to choose from - the campaign
   * register is authenticated - so the link itself carries the attribution and the API resolves
   * it. A form opened with no reference and no session can still be submitted; the API then
   * decides whether it has enough to place the donation.
   */
  protected readonly trackingReference = signal<string>('');

  /**
   * True when the link named the campaign, so the picker is bound and locked.
   *
   * THE FLOW DOCUMENT ASKS FOR EXACTLY THIS: "If the shared link carries a campaign name, that
   * campaign is auto-bound as the default on the donation form and cannot be edited or changed
   * by the donor. If the link does not carry a campaign name, the form shows a Campaign dropdown
   * so the donor can select the campaign themselves."
   */
  protected readonly campaignLockedByLink = signal(false);

  /**
   * A campaign id taken straight from the link, when the link carries one.
   *
   * WHY BOTH A CODE AND AN ID ARE ACCEPTED IN `?campaign=`. A code - CMP-2026-004 - is what a
   * person reads off the campaign register and the friendlier thing to put in a link, but turning
   * it into the identifier the API needs requires the campaign register, and the register is
   * authenticated. An anonymous donor is offered no campaign list at all, so a code alone is
   * unresolvable for exactly the visitor a public donation link exists to serve.
   *
   * AN ID RESOLVES FOR EVERYBODY. It is what the create call takes, and the API resolves the
   * ORGANISATION from it too - so a link carrying one works with no session, no tracking asset
   * and no lookup in the browser. The donor sees no campaign name until the server confirms it,
   * which is the honest state: nothing here can name a campaign it cannot read.
   *
   * IT IS NOT A SECRET AND DOES NOT NEED TO BE. A campaign identifier authorises nothing; the
   * API refuses a donation against a campaign that is not open, whoever names it.
   */
  protected readonly campaignIdFromLink = signal<string | null>(null);

  /** Whether a string is a GUID, and therefore a campaign id rather than a campaign code. */
  private static isGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
  }


  protected readonly pageTitle = signal('Public donation initiation');
  protected readonly pageSubtitle = signal('Collect minimum identity, amount and consent before creating a unique intent.');
  protected readonly operatingTimeZone = signal('Asia/Kolkata · IST (UTC+05:30)');

  protected readonly lifecycleState = signal<LifecycleState>('No record');

  protected readonly lastRefresh = signal('Today, 09:30 AM · IST');

  protected readonly intentReference = signal<string>('');

  /**
   * Set on Submit when the entered name AND payment email/mobile match an
   * existing record in the shared donor list. Such donors may complete the
   * payment on this public form; onPaymentSuccess then navigates them to the
   * login page to sign in instead of staying on the form.
   */
  protected readonly isExistingDonor = signal(false);

  protected readonly owner = computed(() => (this.fullName().trim() ? `${this.fullName().trim()} · Donor` : 'Donor · not yet identified'));

  protected readonly scopeSummary = computed(() =>
    this.selectedCampaign()
      ? `Public intake · ${this.selectedCampaign()!.name} (${this.selectedCampaign()!.context})`
      : 'Public intake · awaiting an eligible campaign or appeal in your active scope',
  );

  protected readonly permissions = signal<EffectivePermissions>({
    view: true,
    submit: true,
    continueToPayment: true,
  });


  /**
   * The campaigns a donation may be started against.
   *
   * EMPTY FOR AN ANONYMOUS DONOR, and that is correct rather than a gap - see
   * campaignStoreOrNull above. Cancelled and Closed campaigns are filtered out because a gift
   * cannot be attributed to one.
   */
  /**
   * The campaigns a donation may be started against.
   *
   * APPROVED AND OPEN ONLY. This filtered out Cancelled and Closed and admitted everything else,
   * which meant Draft and Submitted campaigns - ones nobody has approved yet, and which may
   * never run - were offered to donors as somewhere to send money. The three states admitted
   * here are the ones that have passed approval and can still take a gift:
   *
   *   Approved   signed off, not yet started
   *   Scheduled  signed off, with a start date set
   *   Active     running now
   *
   * Paused and Closing are excluded as well as Draft, Submitted, Closed and Cancelled: a paused
   * campaign has been stopped on purpose, and one that is closing is being wound up. Neither is
   * somewhere to send new money.
   */
  protected readonly campaignOptions = computed<readonly ScopeOption[]>(() => {
    const store = this.campaignStoreOrNull();

    // ANONYMOUS: the public endpoint, which is the case this form is built for. Every row it
    // returns is already open for giving, so there is nothing further to filter - the server
    // applied the same rule the branch below applies to the register.
    if (!store) {
      return this.publicCampaigns().map((campaign) => ({
        reference: campaign.code,
        name: campaign.name,
        context: 'Open for donations',
      }));
    }

    return store
      .all()
      .filter((c) => DonatableCampaignStatuses.includes(c.status))
      .map((c) => ({
        reference: c.code,
        name: c.name,
        context: c.status,
      }));
  });

  /**
   * The appeals an anonymous donor may choose from.
   *
   * WHY THERE ARE TWO SOURCES FOR ONE PICKER. A signed-in fundraiser has the campaign register,
   * which carries status, dates and everything else a staff screen needs. A donor who scanned a
   * QR code has no token, so the register answers them 401 - and the picker they were shown was
   * therefore always empty, reading "No eligible campaign or appeal matches inside your scope"
   * to somebody who has no scope at all and is simply trying to give money.
   *
   * The anonymous endpoint returns the same appeals filtered to the ones actually open for
   * giving, resolved from the host rather than from anything the browser can choose.
   */
  protected readonly publicCampaigns = signal<readonly PublicCampaignSummary[]>([]);

  /**
   * Loads the anonymous picker, for a visitor with no session.
   *
   * SIGNED-IN CALLERS SKIP IT. They have the register, which is richer and already loaded, and
   * asking for both would show every campaign twice.
   *
   * A FAILURE LEAVES AN EMPTY PICKER AND NOTHING ELSE. A donor who arrived with a tracking
   * reference or a campaign on their link can still give without it.
   */
  private loadPublicCampaigns(): void {
    if (this.isInternalView()) {
      return;
    }

    this.payments.getPublicCampaigns().subscribe({
      next: (rows) => {
        this.publicCampaigns.set(rows);

        // RE-MATCHED NOW THE LIST EXISTS. A link naming a campaign arrives before this call
        // returns, so the first attempt had nothing to match against.
        const code = (this.route.snapshot.queryParamMap.get('campaign') ?? '').trim();

        if (code && !this.selectedCampaign()) {
          this.bindCampaignFromCode(code);
        }
      },
      error: () => this.publicCampaigns.set([]),
    });
  }

  protected readonly campaignQuery = signal('');
  protected readonly selectedCampaign = signal<ScopeOption | null>(null);
  protected readonly campaignPickerOpen = signal(false);
  protected readonly campaignResults = computed(() => {
    const q = this.campaignQuery().trim().toLowerCase();
    if (!q) {
      return this.campaignOptions();
    }
    return this.campaignOptions().filter(
      (o) =>
        o.name.toLowerCase().includes(q) ||
        o.reference.toLowerCase().includes(q) ||
        o.context.toLowerCase().includes(q),
    );
  });
  protected selectCampaign(option: ScopeOption): void {
    this.selectedCampaign.set(option);
    this.campaignPickerOpen.set(false);
    this.campaignQuery.set('');
  }
  protected toggleCampaignPicker(): void {
    // LOCKED BY THE LINK IS ALSO LOCKED, not just locked by the lifecycle. A link that names the
    // campaign has already decided which appeal - and which organisation - this gift belongs to,
    // and a donor who could re-point it would be giving to a cause the poster did not advertise.
    if (this.formLocked() || this.campaignLockedByLink()) {
      return;
    }
    this.campaignPickerOpen.update((v) => !v);
  }

  protected readonly donorTypeOptions: readonly DonorType[] = ['Individual', 'Organisation'];
  protected readonly donorType = signal<DonorType>('Individual');
  protected setDonorType(value: string): void {
    if (this.formLocked()) {
      return;
    }
    this.donorType.set(value === 'Organisation' ? 'Organisation' : 'Individual');
    if (this.donorType() === 'Individual') {
      this.organisationName.set('');
    }
  }


  protected readonly organisationName = signal('');

  protected readonly fullName = signal('');

  protected readonly emailOrMobile = signal('');

  /**
   * The donor's mobile number. Its own field now, not half of a shared one.
   *
   * OPTIONAL, AND THAT IS DELIBERATE. E-mail carries the receipt and the account invitation, so
   * it is required; a mobile number is how a fundraiser follows somebody up, which is useful and
   * not a reason to refuse a gift.
   *
   * VALIDATED ONLY WHEN GIVEN. Ten to fifteen digits after punctuation is stripped, which admits
   * an Indian ten-digit number, the same number written +91 XXXXX XXXXX, and international
   * numbers, while rejecting the four digits somebody types when they mean to leave it blank.
   */
  protected readonly mobileNumber = signal('');

  protected readonly mobileInvalid = computed(() => {
    const digits = this.mobileNumber().replace(/\D+/g, '');
    return digits.length > 0 && (digits.length < 10 || digits.length > 15);
  });

  protected readonly emailValid = computed(() => {
    const v = this.emailOrMobile().trim();
    if (!v) {
      return true; 
    }
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v);
  });
  protected readonly emailMasked = computed(() => {
    const v = this.emailOrMobile().trim();
    if (!v) {
      return '';
    }
    const at = v.indexOf('@');
    if (at <= 1) {
      return '•••••';
    }
    return `${v[0]}••••${v.slice(at - 1)}`;
  });

  protected readonly donationAmount = signal<string>('');
  protected readonly amountOverLimitAllowed = signal(false);
  protected readonly maxDonationAmount = signal(500000);
  protected readonly amountInvalid = computed(() => {
    const raw = this.donationAmount().trim();
    if (!raw) {
      return false;
    }
    const n = Number(raw);
    if (Number.isNaN(n) || n < 0) {
      return true;
    }
    return n > this.maxDonationAmount() && !this.amountOverLimitAllowed();
  });
  protected readonly formattedAmount = computed(() => {
    const n = Number(this.donationAmount());
    if (!this.donationAmount() || Number.isNaN(n)) {
      return '';
    }
    const cur = this.currencyLabel(this.currency()) || this.currency();
    return `${cur} ${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  });


  protected readonly currencyCatalogue = signal<readonly CatalogueOption[]>([]);
  protected readonly currency = signal<string>('');
  protected currencyLabel(reference: string): string {
    return this.currencyCatalogue().find((c) => c.reference === reference)?.label.split(' — ')[0] ?? '';
  }
  protected currencyFullLabel(reference: string): string {
    return this.currencyCatalogue().find((c) => c.reference === reference)?.label ?? '';
  }

  protected readonly recurringDonation = signal(false);
  protected readonly recurringFrequencyOptions: readonly CatalogueOption[] = [
    { reference: 'MONTHLY', label: 'Monthly' },
    { reference: 'QUARTERLY', label: 'Quarterly' },
    { reference: 'ANNUALLY', label: 'Annually' },
  ];
  protected readonly recurringFrequency = signal<string>('');
  protected toggleRecurring(checked: boolean): void {
    if (this.formLocked()) {
      return;
    }
    this.recurringDonation.set(checked);
    if (!checked) {
      this.recurringFrequency.set('');
    }
  }

  protected readonly taxReceiptRequired = signal(false);
  protected toggleTaxReceiptRequired(checked: boolean): void {
    if (this.formLocked()) {
      return;
    }
    this.taxReceiptRequired.set(checked);
  }


  protected readonly panOrTaxId = signal<string>('');
  protected readonly panInvalid = computed(() => {
    const raw = this.panOrTaxId().trim();
    if (!raw) {
      return false;
    }
    const n = Number(raw);
    return Number.isNaN(n) || n < 0;
  });
  protected readonly panMasked = computed(() => {
    const v = this.panOrTaxId().trim();
    if (!v) {
      return '';
    }
    return v.length <= 2 ? '••' : `${'•'.repeat(v.length - 2)}${v.slice(-2)}`;
  });

  /**
   * Address — structured address / geography selector; Conditional (4.2.2).
   * Restricted. Use approved administrative geography; preserve entered
   * address text; verify serviceability separately.
   */
  protected readonly geographyCatalogue = signal<readonly CatalogueOption[]>([]);
  protected readonly geography = signal<string>('');
  protected readonly addressText = signal('');
  protected geographyLabel(reference: string): string {
    return this.geographyCatalogue().find((g) => g.reference === reference)?.label ?? '';
  }

  /**
   * Anonymous donation — checkbox; Optional (NEW FIELD). Drives the public
   * recognition preference shown in the summary panel.
   */
  protected readonly anonymousDonation = signal(false);
  protected toggleAnonymous(checked: boolean): void {
    if (this.formLocked()) {
      return;
    }
    this.anonymousDonation.set(checked);
  }

  /**
   * Dedication / tribute — toggle + type + dedicatee name (NEW FIELD group).
   * When enabled, "In memory of / In honor of" and the dedicatee's name
   * become visible and the name becomes required.
   */
  protected readonly dedicationEnabled = signal(false);
  protected readonly dedicationTypeOptions: readonly DedicationType[] = ['In memory of', 'In honor of'];
  protected readonly dedicationType = signal<DedicationType>('In memory of');
  protected readonly dedicateeName = signal('');
  protected toggleDedication(checked: boolean): void {
    if (this.formLocked()) {
      return;
    }
    this.dedicationEnabled.set(checked);
    if (!checked) {
      this.dedicateeName.set('');
    }
  }
  protected setDedicationType(value: string): void {
    if (this.formLocked()) {
      return;
    }
    this.dedicationType.set(value === 'In honor of' ? 'In honor of' : 'In memory of');
  }

  /**
   * Comments / message — optional textarea (NEW FIELD). Free text carried
   * through to the record summary and payment notes; not required.
   */
  protected readonly comments = signal('');
  protected readonly commentsMax = 300;
  protected readonly commentsCount = computed(() => this.comments().trim().length);

  /**
   * Consent acknowledgement — checkbox; Required (4.2.2). Never preselected;
   * records actor, notice or policy version and effective time.
   */
  protected readonly consentPolicyVersion = signal('Privacy Notice v3.2 · Consent Terms v1.4');
  protected readonly consentChecked = signal(false);
  protected readonly consentEffectiveTime = signal<string>('');
  protected toggleConsent(checked: boolean): void {
    if (this.formLocked()) {
      return;
    }
    this.consentChecked.set(checked);
    this.consentEffectiveTime.set(checked ? this.nowLabel() : '');
    if (checked) {
      this.pushActivity(`Consent acknowledged against ${this.consentPolicyVersion()}.`);
    }
  }

  /**
   * Public-recognition preference — read-only text/link/status panel;
   * Read-only (4.2.2). Server-derived and immutable in this view; internal —
   * public only through a separately approved publication field. Now
   * reflects the donor's own Anonymous donation choice where provided.
   */
  protected readonly publicRecognitionPreference = computed(() => {
    if (!this.intentReference()) {
      return '';
    }
    return this.anonymousDonation()
      ? 'Anonymous (donor requested) · not yet approved for public display'
      : 'Named (donor did not request anonymity) · not yet approved for public display';
  });

  /**
   * Payment-link destination — read-only text/link/status panel; Read-only
   * (4.2.2). Server-derived and immutable in this view.
   */
  protected readonly paymentLinkDestination = signal<string>('');

  /**
   * Privacy notice version — read-only text/link/status panel; Read-only;
   * Confidential — visible only within record scope and purpose (4.2.2).
   */
  protected readonly privacyNoticeVersion = computed(() => (this.intentReference() ? this.consentPolicyVersion().split(' · ')[0] : ''));

  // ================= Actions, eligibility and result (4.2.3) =================

  protected readonly formLocked = computed(() => this.lifecycleState() !== 'No record');

  /** Submit (Primary) — eligible with permission, permitted lifecycle state (No record) (4.2.3). */
  protected readonly submitAllowed = computed(
    () => this.permissions().submit && this.lifecycleState() === 'No record' && this.uiState() !== 'no-access',
  );

  /** Review (Page/row) — any authorised state, view permission (4.2.3). */
  protected readonly reviewAllowed = computed(() => this.permissions().view && this.uiState() !== 'no-access');

  /** Continue to payment (Workflow action) — permitted lifecycle state = Submitted (4.2.3). */
  protected readonly continueToPaymentAllowed = computed(
    () =>
      this.permissions().continueToPayment &&
      this.lifecycleState() === 'Submitted' &&
      this.uiState() !== 'no-access',
  );

  /** Review — refreshes only the authorised record and shows the confirmed result, not a toast alone (4.2.3). */
  protected requestReview(): void {
    if (!this.reviewAllowed()) {
      return;
    }
    this.lastRefresh.set(this.nowLabel());
    this.reviewedNote.set(`Reviewed as of ${this.lastRefresh()}. No change to the record outside your effective scope.`);
    this.pushActivity('Record reviewed; no unauthorised change applied.');
  }
  protected readonly reviewedNote = signal<string>('');

  // ----- Required-field validation (4.2.2 Req. + 4.2.6) -----

  /**
   * Required fields for Submit: Campaign or appeal, Donor type, Organisation
   * name (conditional), Email or mobile, Donation amount, Currency,
   * Recurring frequency (conditional), Tax id (conditional on tax receipt),
   * Dedicatee name (conditional on dedication), Consent (4.2.2 Req. = Yes).
   */
  protected readonly validationErrors = computed(() => {
    const errors: { field: string; label: string; message: string }[] = [];
    // A CAMPAIGN OR A TRACKING REFERENCE, NOT NECESSARILY A PICKED CAMPAIGN. An anonymous donor
    // who arrived from a QR code is offered no campaign list - the register is authenticated -
    // so the link itself carries the attribution and the API resolves it. Demanding a selection
    // here would make the form unsubmittable for exactly the person it exists to serve.
    if (!this.selectedCampaign() && !this.trackingReference() && !this.campaignIdFromLink()) {
      errors.push({ field: 'campaign', label: 'Campaign or appeal', message: 'Enter Campaign or appeal.' });
    }
    if (!this.donorType()) {
      errors.push({ field: 'donorType', label: 'Donor type', message: 'Enter Donor type.' });
    }
    if (this.donorType() === 'Organisation' && !this.organisationName().trim()) {
      errors.push({ field: 'organisationName', label: 'Organisation name', message: 'Enter Organisation name.' });
    }
    if (!this.emailOrMobile().trim()) {
      errors.push({ field: 'emailOrMobile', label: 'Email or mobile', message: 'Enter Email or mobile.' });
    } else if (!this.emailValid()) {
      errors.push({
        field: 'emailOrMobile',
        label: 'Email or mobile',
        message: 'Review Email or mobile. The value does not meet the stated format or range.',
      });
    }
    if (!this.donationAmount().trim()) {
      errors.push({ field: 'donationAmount', label: 'Donation amount', message: 'Enter Donation amount.' });
    } else if (this.amountInvalid()) {
      errors.push({
        field: 'donationAmount',
        label: 'Donation amount',
        message: 'Review Donation amount. The value does not meet the stated format or range.',
      });
    }
    if (!this.currency()) {
      errors.push({ field: 'currency', label: 'Currency', message: 'Enter Currency.' });
    }
    if (this.recurringDonation() && !this.recurringFrequency()) {
      errors.push({ field: 'recurringFrequency', label: 'Frequency', message: 'Enter Frequency.' });
    }
    if (this.taxReceiptRequired() && !this.panOrTaxId().trim()) {
      errors.push({ field: 'panOrTaxId', label: 'PAN or tax identifier', message: 'Enter PAN or tax identifier.' });
    } else if (this.panInvalid()) {
      errors.push({
        field: 'panOrTaxId',
        label: 'PAN or tax identifier',
        message: 'Review PAN or tax identifier. The value does not meet the stated format or range.',
      });
    }
    if (this.dedicationEnabled() && !this.dedicateeName().trim()) {
      errors.push({ field: 'dedicateeName', label: 'Dedicatee name', message: 'Enter Dedicatee name.' });
    }
    if (this.mobileInvalid()) {
      errors.push({
        field: 'mobileNumber',
        label: 'Mobile No',
        message: 'Review Mobile No. Enter 10 to 15 digits.',
      });
    }
    if (!this.consentChecked()) {
      errors.push({ field: 'consent', label: 'Consent acknowledgement', message: 'Enter Consent acknowledgement.' });
    }
    return errors;
  });

  protected readonly remainingRequired = computed(() =>
    this.validationErrors()
      .filter((e) => e.message.startsWith('Enter '))
      .map((e) => e.label),
  );

  /**
   * Submit - the only button on this form, and it now goes all the way to the provider.
   *
   * WHAT THIS USED TO DO, because the difference is the whole point of the change. It minted its
   * own reference ("INT-2025-" plus three random digits), fabricated a payment-link URL that
   * pointed nowhere, pushed a row onto an in-memory queue, and opened Razorpay Checkout with a
   * test key compiled into the bundle and NO ORDER behind it. Four consequences followed, and
   * every one of them was real:
   *
   *   - EVERY ORGANISATION'S DONATIONS WENT TO ONE TEST MERCHANT ACCOUNT. The tenant-wise
   *     gateway configuration was read by the server and ignored by the browser.
   *   - THE AMOUNT CHARGED WAS WHATEVER THIS PAGE SAID. With no order held by the provider,
   *     editing one number in a browser console changed what a donor paid.
   *   - NOTHING WAS RECORDED. The donation existed until the tab was refreshed and no further,
   *     so it never reached the payments queue, a receipt or a report.
   *   - THE PAGE DECIDED WHETHER THE PAYMENT SUCCEEDED, which only the provider can know.
   *
   * WHAT IT DOES NOW. The server creates the intent, resolves the organisation and its
   * configured gateway, opens an ORDER against that provider, and hands back a session carrying
   * that organisation's own publishable key. This opens whatever provider the session names, and
   * the SERVER settles the outcome.
   */
  protected requestSubmit(): void {
    // A REOPENED DONATION IS CONTINUED, NOT DUPLICATED. This form has one button. Somebody who
    // arrived on ?intent= - from Retry on the payments queue, or from a failed result page -
    // pressed it meaning "pay the donation I came back for", and creating a second intent for
    // the same gift would leave two records, two payment attempts and, if they paid both, two
    // charges. `continueToPaymentAllowed` is what gates it, and `submitAllowed` below refuses a
    // fresh submission in the same state for the same reason.
    if (this.intentReference()) {
      this.requestContinueToPayment();
      return;
    }

    if (this.validationErrors().length > 0) {
      this.uiState.set('validation');
      this.focusFirstInvalid();
      return;
    }

    if (!this.submitAllowed()) {
      return;
    }

    this.uiState.set('loading');

    this.payments.initiateDonation(this.buildIntentRequest()).subscribe({
      next: (intent) => this.onIntentCreated(intent),
      error: (error: unknown) => {
        this.uiState.set('ready');
        this.pushActivity('The donation could not be started.');
        this.toast.show('Donation not started', apiErrorMessage(error), 'error');
      },
    });
  }

  /**
   * The request body, built from the form exactly once.
   *
   * THE AMOUNT GOES AS A NUMBER, NOT AS MINOR UNITS. Converting to the provider's paise or cents
   * is the server's job; doing it here was how a client-side edit could decide what a donor paid.
   *
   * THE CAMPAIGN GOES AS AN ID, NOT A CODE. The API's campaignId is a Guid, so sending the
   * human-readable code returns a 400 before the handler runs. Where no campaign was picked, the
   * tracking reference from the link resolves it server-side instead.
   */
  private buildIntentRequest(): CreateDonationIntentRequest {
    const campaignRef = this.selectedCampaign()?.reference ?? '';

    return {
      donorName:
        this.donorType() === 'Organisation'
          ? this.organisationName().trim() || 'Donor'
          : this.fullName().trim() || 'Donor',

      // TWO FIELDS, TWO VALUES. This used to sniff one field with a regular expression and send
      // the result as an e-mail or a mobile depending on what it looked like - so a mistyped
      // address travelled as a phone number and the donor got no receipt.
      email: this.emailOrMobile().trim(),
      mobile: this.mobileNumber().trim() || null,
      amount: Number(this.donationAmount()),
      currencyCode: this.currency(),
      // THE LINK'S OWN ID WINS - see `campaignIdFromLink`. This form is the one a lead opens
      // with no session, so it is the branch that matters most here.
      campaignId:
        this.campaignIdFromLink()
        ?? (campaignRef ? this.campaignStoreOrNull()?.apiId(campaignRef) ?? null : null),
      trackingReference: this.trackingReference() || null,
      taxIdentifier: this.panOrTaxId().trim() || null,
      addressLine1: this.addressText().trim() || null,
      addressLine2: this.geographyLabel(this.geography()) || null,

      // Consent is captured BEFORE the intent exists, so it travels with the creation rather
      // than being written over it afterwards.
      consentGiven: this.consentChecked(),
      consentVersion: this.consentPolicyVersion(),
      allowPublicRecognition: !this.anonymousDonation(),
    };
  }

  /**
   * The intent exists. Find out who this is, then pay.
   *
   * THE SERVER DECIDES WHO THIS IS, NOT THIS PAGE. The old code compared the typed address
   * against the literal string 'existing.donor@ydot.org', and separately matched name-plus-
   * contact against a JSON file of donors fetched into the browser - so recognition depended on
   * a file any visitor could read, and on this tab having loaded it. existingDonorMatched is the
   * API's answer, against that organisation's real donor records.
   *
   * NEITHER ANSWER STOPS THE PAYMENT, which is what the flow document asks for: a recognised
   * donor can pay right away, and a lead continues directly on the open form. The answer is
   * remembered because it decides where they go AFTERWARDS, not whether they may give.
   */
  private onIntentCreated(intent: DonationIntentResponse): void {
    this.intentReference.set(intent.intentReference);
    this.lifecycleState.set('Submitted');
    this.isExistingDonor.set(intent.existingDonorMatched === true);

    this.lastOutcome.set({
      action: 'Submit',
      reference: intent.intentReference,
      state: intent.statusDescription || 'Submitted',
      effectiveTime: this.nowLabel(),
      downstream: 'Checkout requested from the configured payment gateway',
      nextAction: 'Complete the payment',
      reason: '',
    });

    this.pushActivity('Submitted. Reference ' + intent.intentReference + ' created.');

    // AN ADDRESS THAT CAN ALREADY SIGN IN GOES TO SIGN IN, NOT TO THE GATEWAY.
    //
    // Section 3 of the flow document: "If the email matches an existing Donor the person is
    // redirected to Login. After logging in, the donor is taken to the Payment page." This form
    // was taking them straight to checkout instead, so somebody who already has an account here -
    // a returning donor, and just as importantly a fundraiser or an administrator typing their
    // own work address - paid as an anonymous stranger, and the gift was recorded against a
    // second identity in an organisation where they already exist.
    //
    // IT IS THE SERVER'S ANSWER, NOT THIS FORM'S. `requiresSignIn` is true when an IAM account
    // exists for the address in this organisation; the browser cannot know that and must not
    // guess. It is deliberately not the same flag as `existingDonorMatched`, which is only true
    // for people who have given before and is false for every member of staff.
    //
    // THE DONATION SURVIVES THE DETOUR. It is already created and already carries the campaign,
    // amount and consent, so the return URL names it and the in-app screen reopens it - the
    // donor confirms and pays once, rather than filling the form in twice.
    if (intent.requiresSignIn) {
      this.uiState.set('success');
      this.pushActivity('This address already has an account; sent to sign in.');

      this.toast.show(
        'Please sign in to continue',
        'This email already has an account with this organisation. Sign in and we will take you '
          + 'straight back to complete your donation.',
        'info',
      );

      void this.router.navigate([DonationRoutes.SignIn], {
        queryParams: {
          returnUrl:
            DonationRoutes.InAppDonation + '?intent=' + encodeURIComponent(intent.intentReference),
        },
      });

      return;
    }

    this.startPayment(intent.intentReference, intent.version);
  }

  /**
   * Continue to payment - opens the ORGANISATION'S configured provider over this page.
   *
   * IT RE-READS THE INTENT FIRST rather than reusing a version it is holding. The version is
   * what stops a double-submitted form opening two attempts against one intent, and two attempts
   * is a donor who can pay twice for one gift.
   */
  protected requestContinueToPayment(): void {
    const reference = this.intentReference();

    if (!this.continueToPaymentAllowed() || !reference) {
      return;
    }

    this.payments.getPublicIntent(reference).subscribe({
      next: (detail) => this.startPayment(detail.intentReference, detail.version),
      error: (error: unknown) =>
        this.toast.show('Payment unavailable', apiErrorMessage(error), 'error'),
    });
  }

  /**
   * Asks the server to open a checkout session, then draws it.
   *
   * IT FALLS BACK RATHER THAN FAILING, twice over: an organisation whose provider cannot draw an
   * in-page checkout is refused by the server, and one whose provider has no browser SDK here is
   * refused by GatewayCheckoutService. Both end at a payment link, which every provider
   * supports. A donation is not worth losing to a missing form.
   */
  private startPayment(intentReference: string, expectedVersion: number): void {
    this.payments.createCheckoutSession(intentReference, { expectedVersion }).subscribe({
      next: (session) => this.openCheckout(session),

      error: () => {
        this.pushActivity('In-page checkout unavailable; requesting a payment link instead.');

        // THE VERSION IS RE-READ, NOT REUSED. The refused session may have written to the intent
        // on its way out, and a link asked for against a version that has moved is refused as a
        // stale double submit - which would turn a recoverable fallback into a dead end.
        this.payments.getPublicIntent(intentReference).subscribe({
          next: (detail) => this.requestPaymentLink(detail.intentReference, detail.version),
          error: (readError: unknown) => {
            this.uiState.set('success');
            this.lifecycleState.set('Submitted');
            this.toast.show('Payment unavailable', apiErrorMessage(readError), 'error');
          },
        });
      },
    });
  }

  /** Draws whichever provider the session names. */
  private openCheckout(session: CheckoutSession): void {
    this.paymentLinkDestination.set('');

    void this.checkout
      .open(session, this.selectedCampaign()?.name || 'Donation', {
        onSucceeded: (confirmation) => this.confirmCheckout(session, confirmation),
        onFailed: () => this.onPaymentFailure(session),
        onDismissed: () => this.onCheckoutDismissed(),
      })
      .then((outcome) => {
        if (outcome === 'opened') {
          // 'Awaiting payment' WHILE THE FORM IS OPEN. If the donor closes it,
          // onCheckoutDismissed puts this back to 'Submitted' so Submit can reopen it.
          this.lifecycleState.set('Awaiting payment');
          this.uiState.set('success');
          this.pushActivity(
            'Checkout opened via ' + session.gatewayName + ' (attempt ' + session.attemptNumber + ').',
          );
          return;
        }

        // NOT SUPPORTED IS NOT AN ERROR. The organisation's provider has a server adapter but no
        // in-page form here, so the donor gets a link - which is what a provider without a
        // browser SDK has always meant.
        if (outcome === 'unsupported') {
          this.pushActivity(
            session.gatewayName + ' has no in-page checkout; sending a payment link instead.',
          );
        } else {
          this.pushActivity('The payment form could not be opened; falling back to a payment link.');
        }

        this.payments.getPublicIntent(session.intentReference).subscribe({
          next: (detail) => this.requestPaymentLink(detail.intentReference, detail.version),
          error: (error: unknown) => {
            // 'Submitted', SO SUBMIT CAN TRY AGAIN. Nothing reached a provider on this path -
            // neither the in-page form nor the link - so the donation is exactly as unpaid as it
            // was, and the one button on this form has to remain able to retry it.
            this.uiState.set('success');
            this.lifecycleState.set('Submitted');
            this.toast.show('Payment form unavailable', apiErrorMessage(error), 'error');
          },
        });
      });
  }

  /**
   * The link route - for a provider with no in-page form, and for a donor who is not at a screen.
   *
   * A FULL NAVIGATION, NOT A NEW TAB. A popup blocker silently swallowing the window is
   * indistinguishable, to the donor, from the button doing nothing at all.
   */
  private requestPaymentLink(intentReference: string, expectedVersion: number): void {
    this.payments.createPaymentLink(intentReference, { expectedVersion }).subscribe({
      next: (link) => {
        this.lifecycleState.set('Awaiting payment');
        this.paymentLinkDestination.set(link.paymentLinkUrl);
        this.uiState.set('success');
        this.pushActivity(
          'Payment link issued via ' + link.gatewayName + ' (attempt ' + link.attemptNumber + ').',
        );
        this.toast.show('Redirecting to payment', 'Opening the secure payment page.', 'success');
        window.location.assign(link.paymentLinkUrl);
      },
      error: (error: unknown) => {
        // THE INTENT SURVIVES A LINK FAILURE. It is recorded and pending, so it appears on the
        // payments queue for an administrator to recover rather than vanishing with the toast.
        this.uiState.set('success');
        this.lifecycleState.set('Submitted');
        this.pushActivity('A payment link could not be issued; the intent remains submitted.');
        this.toast.show('Payment link unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  /**
   * The provider says it is paid. The SERVER decides whether it is.
   *
   * THIS PAGE DOES NOT DECIDE THE OUTCOME AND MUST NOT. What the provider hands the browser is a
   * payment id and a signature over it; only the server holds the secret that proves the
   * signature, and only the server can ask the provider whether the money actually moved. It
   * used to be decided here - updatePaymentEventQueueStatus(..., 'Success') straight from the
   * checkout callback - which meant anything that could call that callback could mark a donation
   * paid.
   *
   * A FAILED CONFIRMATION IS NOT A FAILED PAYMENT, and the donor must never be told it is. The
   * money may well have moved; what failed was our chance to hear about it on this request. Both
   * branches go to the result page, which asks again and keeps asking.
   */
  private confirmCheckout(session: CheckoutSession, confirmation: ConfirmCheckoutRequest): void {
    this.uiState.set('loading');
    this.pushActivity('Payment completed at the gateway; confirming.');

    this.payments.confirmCheckout(session.intentReference, confirmation).subscribe({
      next: () => this.goToResult(session.intentReference),
      error: () => this.goToResult(session.intentReference),
    });
  }

  /**
   * The donor closed the form without paying. Nothing failed; nothing was charged.
   *
   * THE LIFECYCLE GOES BACK TO 'Submitted', NOT ON TO 'Awaiting payment', and the difference is
   * whether this donor can try again. `continueToPaymentAllowed` - which is what Submit checks
   * for a donation that already exists - is true in 'Submitted' and false in 'Awaiting payment',
   * so leaving it on the latter would put somebody who closed the form by accident in front of a
   * button that does nothing. The donation is unpaid either way; 'Submitted' is the honest word
   * for one whose payment was never attempted.
   */
  private onCheckoutDismissed(): void {
    this.uiState.set('success');
    this.lifecycleState.set('Submitted');
    this.pushActivity('The donor closed the payment form without paying.');
    this.toast.show(
      'Payment not completed',
      'The payment form was closed. Your donation is saved - select Submit when you are ready to '
        + 'pay it.',
      'info',
    );
  }

  /** The provider declined. A real outcome, so the donor is taken to it. */
  private onPaymentFailure(session: CheckoutSession): void {
    this.lifecycleState.set('Submitted');
    this.pushActivity('The payment was declined at the gateway.');
    this.goToResult(session.intentReference);
  }

  /**
   * Back to our own application, whatever happened.
   *
   * THE RESULT PAGE IS THE RETURN HALF OF THIS FLOW and the one place the redirect rules live.
   * It reads the intent reference, asks our API to verify with the provider, polls while the
   * answer is still pending, and only then decides where this person belongs - a lead back to
   * this form, a donor to their donations or to the payments queue. Deciding it here, from a
   * browser that has just been told "paid" by a script, is how the old code sent somebody to the
   * sign-in page for a payment that had not settled.
   */
  private goToResult(intentReference: string): void {
    void this.router.navigate(['/give/result'], { queryParams: { intent: intentReference } });
  }

  /** Persistent outcome record (4.2.1 Persistent outcome / 4.2.4 Success). */
  protected readonly lastOutcome = signal<{
    action: string;
    reference: string;
    state: string;
    effectiveTime: string;
    downstream: string;
    nextAction: string;
    reason: string;
  } | null>(null);

  // ================= Related and history (4.2.1) =================
  protected readonly relatedTabs: readonly RelatedTab[] = ['Linked', 'Documents', 'Activity', 'Integration', 'Support', 'Audit'];
  protected readonly activeRelatedTab = signal<RelatedTab>('Linked');
  protected selectRelatedTab(tab: RelatedTab): void {
    this.activeRelatedTab.set(tab);
  }
  protected readonly activityLog = signal<readonly ActivityEntry[]>([]);
  private pushActivity(text: string): void {
    this.activityLog.update((cur) => [{ time: this.nowLabel(), text }, ...cur]);
  }

  // ================= UI states (4.2.4 / 4.2.7) =================
  protected readonly uiState = signal<UiState>('ready');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  /** Return to the editable form, preserving entered values (4.2.4 / 4.2.7). */
  protected backToForm(): void {
    this.uiState.set('ready');
  }

  /** Focus the first invalid field and preserve the primary action after correction (4.2.4 Validation / 4.2.6). */
  private focusFirstInvalid(): void {
    const first = this.validationErrors()[0];
    if (!first) {
      return;
    }
    queueMicrotask(() => {
      const el = document.getElementById(`fld-${first.field}`);
      el?.focus();
    });
  }

  // ================= Helpers =================
  private nowLabel(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  constructor() {
    this.readLinkContext();
    this.loadPublicCampaigns();

    if (!this.route.snapshot.queryParamMap.get('intent')) {
      this.resetDonationFields();
    }

    this.loadConfig();

    // AND AGAIN ON EVERY LATER ARRIVAL. A lead sent back to this form after paying reaches this
    // route with no `intent`, and where the component is not rebuilt the finished gift would
    // still be on screen - campaign, amount and a ticked consent.
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((current) => {
      if (this.intentReference() && !current.get('intent')) {
        this.resetDonationFields();
      }
    });
  }

  /**
   * Clears the gift, and keeps who the donor is.
   *
   * WHY IT RUNS ON ARRIVAL RATHER THAN ON DEPARTURE. A donor returning from a completed payment
   * lands on this route, and where they were already on it - which is exactly the confirmed-donor
   * case, since the destination is this page - Angular treats the navigation as one to the same
   * URL and does not rebuild the component. Every signal keeps its value, so the campaign, the
   * amount and the ticked consent from the gift just made are all still on screen, and the page
   * reads as though the donation never went through.
   *
   * WHAT IT CLEARS IS THE DECISION, NOT THE PERSON. Campaign, amount, consent, the reference and
   * the lifecycle all go. Name, e-mail and mobile stay: for a signed-in donor they come from the
   * account and clearing them would only mean re-prefilling them a line later, and for an
   * anonymous one they are the only things worth keeping if they choose to give again.
   */
  private resetDonationFields(): void {
    this.selectedCampaign.set(null);
    this.campaignQuery.set('');
    this.campaignPickerOpen.set(false);
    this.donationAmount.set('');
    this.consentChecked.set(false);
    this.consentEffectiveTime.set('');
    this.intentReference.set('');
    this.paymentLinkDestination.set('');
    this.isExistingDonor.set(false);
    this.lifecycleState.set('No record');
    this.lastOutcome.set(null);
    this.uiState.set('ready');
  }


  /**
   * Reads what the QR code or shared link is carrying.
   *
   * TWO THINGS ARRIVE ON THE QUERY STRING AND THEY DO DIFFERENT JOBS.
   *
   *   ref / tracking - the TRACKING REFERENCE. It is how an anonymous donor gets a campaign at
   *     all, and it is also what tells the platform which organisation the gift belongs to. The
   *     API resolves it; nothing here can, and nothing here should - a value the browser could
   *     choose would let a stranger point a donation at any organisation on the platform.
   *
   *   campaign - a campaign CODE, for a link built by the tracking asset manager that names one.
   *     Where it is present the picker is bound to it and locked, which is what the flow
   *     document asks for: "that campaign is auto-bound as the default on the donation form and
   *     cannot be edited or changed by the donor". Where it is absent the picker stays open so a
   *     signed-in fundraiser can choose - an anonymous donor sees no list, because the campaign
   *     register is authenticated, and their campaign comes from the tracking reference instead.
   *
   *   intent - an existing donation being continued, from the payments queue or from a result
   *     page that sent somebody back to pay.
   */
  private readLinkContext(): void {
    const params = this.route.snapshot.queryParamMap;

    this.trackingReference.set(params.get('ref') ?? params.get('tracking') ?? '');

    const campaignCode = (params.get('campaign') ?? '').trim();

    if (campaignCode) {
      // BOUND AS SOON AS THE REGISTER ANSWERS. The store loads asynchronously for a signed-in
      // caller and stays empty for an anonymous one, so the match is attempted now and again
      // after load rather than once and only once.
      this.bindCampaignFromCode(campaignCode);
      this.campaignLockedByLink.set(true);

      if (DonorformComponent.isGuid(campaignCode)) {
        this.campaignIdFromLink.set(campaignCode);
      }
    }
  }

  /** Selects the campaign a link named, once the register can answer for it. */
  private bindCampaignFromCode(code: string): void {
    const wanted = code.toLowerCase();

    const match = this.campaignOptions().find(
      (option) =>
        option.reference.toLowerCase() === wanted || option.name.toLowerCase() === wanted,
    );

    if (match) {
      this.selectedCampaign.set(match);
      this.campaignPickerOpen.set(false);
    }
  }

  /**
   * The screen's presentation configuration - copy, currencies and geographies.
   *
   * NO DONOR DATA IS FETCHED HERE ANY MORE. This used to also pull /assets/data/donors.json into
   * the browser so the form could decide for itself whether the person typing was already a
   * donor. That put one organisation's donor list on a public page, made recognition depend on a
   * file rather than on records, and produced a different answer depending on whether the fetch
   * had finished. The API answers the question now, on the intent, against the right
   * organisation's records.
   */
  private loadConfig(): void {
    this.uiState.set('loading');

    this.dataService.getPublicDonationInitiationData().subscribe({
      next: (config: PublicDonationInitiationConfig) => {
        this.pageTitle.set(config.pageTitle);
        this.pageSubtitle.set(config.pageSubtitle);
        this.operatingTimeZone.set(config.operatingTimeZone);
        this.consentPolicyVersion.set(config.consentPolicyVersion);
        this.currencyCatalogue.set(config.currencies);
        this.geographyCatalogue.set(config.geographies);
        this.permissions.set(config.permissions);
        this.maxDonationAmount.set(config.maxDonationAmount);
        this.uiState.set('ready');

        const campaignCode = this.route.snapshot.queryParamMap.get('campaign');

        if (campaignCode && !this.selectedCampaign()) {
          this.bindCampaignFromCode(campaignCode.trim());
        }

        this.bindIntentFromQueryString();
      },
      error: () => {
        this.uiState.set('ready');
        this.toast.show('Error', 'Failed to load the donation form configuration.', 'error');
      },
    });
  }

  /**
   * Reopens a donation that already exists, named on the query string.
   *
   * WHERE THE REFERENCE COMES FROM. The payments queue offers Continue to payment on a pending
   * row and Retry on a failed one, and both arrive here as ?intent=... The record is READ FROM
   * THE API rather than carried across the navigation in a field - which is what used to happen,
   * and meant the form was populated from whatever the previous screen happened to be holding,
   * with no way to tell a stale hand-off from a fresh one.
   */
  private bindIntentFromQueryString(): void {
    const reference = (this.route.snapshot.queryParamMap.get('intent') ?? '').trim();

    if (!reference) {
      return;
    }

    this.payments.getPublicIntent(reference).subscribe({
      next: (intent) => {
        this.intentReference.set(intent.intentReference);
        this.fullName.set(intent.donorName);
        this.emailOrMobile.set(intent.email ?? '');
        this.mobileNumber.set(intent.mobile ?? '');
        this.donationAmount.set(String(intent.amount.amount));
        this.currency.set(intent.amount.currencyCode);
        this.isExistingDonor.set(intent.existingDonorMatched === true);
        this.lifecycleState.set('Submitted');

        const campaign = this.campaignOptions().find((c) => c.name === intent.campaignName);

        if (campaign) {
          this.selectedCampaign.set(campaign);
        }

        this.pushActivity('Reopened donation ' + intent.intentReference + '.');
        this.toast.show(
          'Continue payment',
          'Donation ' + intent.intentReference + ' is ready to be paid.',
          'info',
        );

        // NOT PAID AUTOMATICALLY. Opening a provider's payment form on page load, before anybody
        // has pressed anything, is startling on the first visit and a trap on a retry: close it
        // and this form has no other control that would reopen it. Submit does that instead -
        // see requestSubmit, which continues a reopened donation rather than starting a second
        // one.
      },

      // A REFERENCE THAT DOES NOT RESOLVE LEAVES AN EMPTY FORM, which is the right state: the
      // donor can still give. Reporting "not found" to somebody who followed a link from an
      // e-mail tells them nothing they can act on.
      error: () => this.pushActivity('The donation named on the link could not be reopened.'),
    });
  }
}
