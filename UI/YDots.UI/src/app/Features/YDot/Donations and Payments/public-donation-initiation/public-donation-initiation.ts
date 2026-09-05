import { CommonModule } from '@angular/common';
import { Component, Injector, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { CurrentUserService } from '../../../../Service/current-user.service';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { GatewayCheckoutService } from '../../../../Shared/services/gateway-checkout.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  CheckoutSession,
  ConfirmCheckoutRequest,
  PublicCampaignSummary,
  CreateDonationIntentRequest,
  DonationIntentResponse,
  ExistingDonorCheckResponse,
} from '../../../../Shared/models/payment.model';


/**
 * THE BROWSER NEVER HOLDS A RAZORPAY KEY, and that is the change that matters most on this
 * screen. It used to open Razorpay Checkout itself with `key: 'rzp_test_TCwSZidEO9q88a'`
 * compiled into the bundle, which meant three things at once:
 *
 *   - THE KEY WAS PUBLIC. Anyone who opened dev-tools on the donation page could read it and
 *     raise charges against this charity's account from anywhere.
 *   - NOTHING WAS VERIFIED. `handler` believed whatever the browser handed back, so a donation
 *     was "successful" because a script said so - no signature check, no gateway confirmation.
 *   - THE AMOUNT WAS DECIDED CLIENT-SIDE. `amount: Math.round(amount * 100)` came from a signal
 *     a person could edit; a donor could have paid one rupee against a ten-thousand intent.
 *
 * The flow the YDot Donation Flow document describes is the server's: Submit creates the intent
 * (section 2), the server checks Lead/Donor (section 3), and the server issues a Razorpay
 * payment link using credentials only it holds. The donor is sent to that link. The outcome
 * comes back from the gateway - webhook, or the verify poll - never from this page.
 */

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
  selector: 'app-public-donation-initiation',
  imports: [CommonModule, FormsModule],
  templateUrl: './public-donation-initiation.html',
  styleUrl: './public-donation-initiation.css',
})
export class PublicDonationInitiationComponent {
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly payments = inject(PaymentApiService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  /**
   * The campaign store, resolved only for a signed-in caller.
   *
   * IT IS DELIBERATELY NOT A FIELD INJECTION. `CampaignStoreService` calls the authenticated CAM
   * API in its own constructor and again every sixty seconds; injecting it on the donor-facing
   * form meant a stranger with a QR code triggered a 401 on page load and another every minute
   * they spent reading the form. Resolving it lazily means the anonymous path never constructs
   * it at all.
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
   * The QR code's or link's own reference, carried on the query string.
   *
   * IT IS HOW AN ANONYMOUS DONOR GETS A CAMPAIGN. Nobody signed in means no campaign list to
   * pick from - the authenticated CAM API would refuse it - so the link the donor followed has
   * to say which campaign it belongs to. The API resolves it to a campaign, a channel and a
   * source, which is also what makes the donation attributable afterwards.
   */
  protected readonly trackingReference = signal<string>('');

  /**
   * Whether this is the admin panel's view of the form rather than the donor's.
   *
   * FIG 2 OF THE DOCUMENT: the same form and the same fields appear under Donations & Payments
   * for an internal user, "for reference and support". The difference is not the fields - it is
   * that a signed-in user can be offered the campaign picker, because CAM will answer them.
   */
  protected readonly isInternalView = computed(() => this.currentUser.reference() !== '');

  protected readonly pageTitle = signal('Public donation initiation');
  protected readonly pageSubtitle = signal('Collect minimum identity, amount and consent before creating a unique intent.');
  protected readonly operatingTimeZone = signal('Asia/Kolkata · IST (UTC+05:30)');

  protected readonly lifecycleState = signal<LifecycleState>('No record');

  protected readonly lastRefresh = signal('Today, 09:30 AM · IST');

  protected readonly intentReference = signal<string>('');

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
   * EMPTY FOR AN ANONYMOUS DONOR, and that is correct rather than a gap: the campaign then comes
   * from the tracking reference in the link they followed, which the API resolves server-side.
   * Cancelled and Closed campaigns are filtered out because a gift cannot be attributed to one.
   */
  protected readonly campaignOptions = computed<readonly ScopeOption[]>(() => {
    const store = this.campaignStoreOrNull();

    // ANONYMOUS: the public endpoint. See loadPublicCampaigns.
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
    // LOCKED BY THE LINK IS ALSO LOCKED. A link that names the campaign has already decided
    // which appeal this gift belongs to.
    if (this.campaignLockedByLink()) {
      return;
    }

    if (this.formLocked()) {
      return;
    }
    this.campaignPickerOpen.update((v) => !v);
  }

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

  /** The PAN the income-tax department issues: five letters, four digits, a check letter. */
  private static readonly PanPattern = /^[A-Za-z]{5}[0-9]{4}[A-Za-z]$/;

  /**
   * A tax reference that is not a PAN - an overseas donor's TIN, a foreign registration number.
   * Letters, digits and the separators those references carry, within the thirty characters the
   * API stores a tax identifier in.
   */
  private static readonly TaxIdentifierPattern = /^[A-Za-z0-9][A-Za-z0-9/-]{3,29}$/;

  protected readonly panOrTaxId = signal<string>('');

  /**
   * Whether what has been typed can be a PAN or a tax identifier at all.
   *
   * IT IS NOT A NUMBER, AND TREATING IT AS ONE WAS THE WHOLE BUG. This read
   * `Number(raw); return Number.isNaN(n) || n < 0`, and `Number('ANUPT1651K')` is NaN - so every
   * genuine PAN ever typed into this box was rejected as out of format. The one value the field
   * exists to collect was the one value it would not accept, and the only inputs it did accept
   * were bare digits, which no PAN is.
   *
   * A PAN IS CHECKED TO ITS ACTUAL SHAPE. Anything else is treated as a foreign tax reference
   * and checked only for its characters and its length, because this form takes gifts in four
   * currencies and there is no single format an overseas identifier follows. The API keeps the
   * final say - it accepts thirty characters and no particular shape - so this stops a typo
   * reaching an 80G receipt without inventing a rule the server does not have.
   */
  protected readonly panInvalid = computed(() => {
    const raw = this.panOrTaxId().trim();
    if (!raw) {
      return false;
    }
    if (PublicDonationInitiationComponent.PanPattern.test(raw)) {
      return false;
    }
    return !PublicDonationInitiationComponent.TaxIdentifierPattern.test(raw);
  });
  protected readonly panMasked = computed(() => {
    const v = this.panOrTaxId().trim();
    if (!v) {
      return '';
    }
    return v.length <= 2 ? '••' : `${'•'.repeat(v.length - 2)}${v.slice(-2)}`;
  });


  protected readonly geographyCatalogue = signal<readonly CatalogueOption[]>([]);
  protected readonly geography = signal<string>('');
  protected readonly addressText = signal('');
  protected geographyLabel(reference: string): string {
    return this.geographyCatalogue().find((g) => g.reference === reference)?.label ?? '';
  }

 
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

  protected readonly publicRecognitionPreference = computed(() =>
    this.intentReference() ? 'Anonymous (default) · not yet approved for public display' : '',
  );


  protected readonly paymentLinkDestination = signal<string>('');

  protected readonly privacyNoticeVersion = computed(() => (this.intentReference() ? this.consentPolicyVersion().split(' · ')[0] : ''));


  protected readonly formLocked = computed(() => this.lifecycleState() !== 'No record');

  protected readonly submitAllowed = computed(
    () => this.permissions().submit && this.lifecycleState() === 'No record' && this.uiState() !== 'no-access',
  );

  protected readonly reviewAllowed = computed(() => this.permissions().view && this.uiState() !== 'no-access');

  /**
   * Whether "Continue to payment" can be pressed.
   *
   * 'AWAITING PAYMENT' COUNTS, AND LEAVING IT OUT WAS THE BUG. This tested
   * `lifecycleState() === 'Submitted'` alone - but an intent that has a payment link already, or
   * whose checkout the donor closed, sits in 'Awaiting payment', which is the state where
   * continuing is the ONLY thing left to do. Two flows dead-ended on it:
   *
   *   - Payment Queue -> Continue payment. The row exists precisely because a link was issued and
   *     not paid, so `bindIntentFromQueryString` reads a `paymentLinkUrl` and sets 'Awaiting
   *     payment'. The form arrived fully locked (`formLocked` is anything but 'No record') with
   *     the one button that could act on it disabled, above a toast reading "Select Continue to
   *     payment to finish it."
   *   - Closing the checkout window. `onCheckoutDismissed` sets 'Awaiting payment' and says
   *     "Select Continue to payment when you are ready" - pointing at the button it had just
   *     disabled.
   *
   * The donor could not finish paying from either, and the intent stayed Pending in the queue.
   */
  protected readonly continueToPaymentAllowed = computed(
    () =>
      this.permissions().continueToPayment &&
      (this.lifecycleState() === 'Submitted' || this.lifecycleState() === 'Awaiting payment') &&
      this.uiState() !== 'no-access',
  );

  protected requestReview(): void {
    if (!this.reviewAllowed()) {
      return;
    }
    this.lastRefresh.set(this.nowLabel());
    this.reviewedNote.set(`Reviewed as of ${this.lastRefresh()}. No change to the record outside your effective scope.`);
    this.pushActivity('Record reviewed; no unauthorised change applied.');
  }
  protected readonly reviewedNote = signal<string>('');

  protected readonly validationErrors = computed(() => {
    const errors: { field: string; label: string; message: string }[] = [];
    // REQUIRED ONLY WHEN THERE IS SOMETHING TO PICK. A donor who scanned a QR code has no
    // campaign list - the link itself carries the attribution - so demanding a selection here
    // would make the public form impossible to submit, which is precisely what it used to do.
    if (!this.selectedCampaign() && !this.trackingReference() && !this.campaignIdFromLink()) {
      errors.push({ field: 'campaign', label: 'Campaign or appeal', message: 'Enter Campaign or appeal.' });
    }
    if (this.mobileInvalid()) {
      errors.push({
        field: 'mobileNumber',
        label: 'Mobile No',
        message: 'Review Mobile No. Enter 10 to 15 digits.',
      });
    }
    if (!this.emailOrMobile().trim()) {
      errors.push({ field: 'emailOrMobile', label: 'Email', message: 'Enter Email.' });
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
    if (this.panInvalid()) {
      errors.push({
        field: 'panOrTaxId',
        label: 'PAN or tax identifier',
        message: 'Review PAN or tax identifier. The value does not meet the stated format or range.',
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
   * Submit - section 2 of the document, and the only button the donor presses.
   *
   * IT NO LONGER MINTS ITS OWN REFERENCE. `mintIntentReference()` returned
   * `INT-2025-<random 3 digits>`, so two donors a few milliseconds apart could be handed the
   * same one, and nothing on the server had ever heard of either. The reference now comes back
   * from the API, which is also what makes it quotable to support afterwards.
   */
  protected requestSubmit(): void {
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
        this.toast.show('Donation not started', apiErrorMessage(error), 'error');
      },
    });
  }

  /**
   * The request body, built from the form exactly once.
   *
   * THE AMOUNT GOES AS A NUMBER, NOT AS PAISE. Converting to the gateway's minor units is the
   * server's job; doing it here was how a client-side edit could decide what a donor paid.
   */
  private buildIntentRequest(): CreateDonationIntentRequest {
    const campaignRef = this.selectedCampaign()?.reference ?? '';

    return {
      donorName: this.fullName().trim() || 'Donor',

      // TWO FIELDS, TWO VALUES. This used to sniff one field with a regular expression and send
      // the result as an e-mail or a mobile depending on what it looked like - so a mistyped
      // address travelled as a phone number and the donor got no receipt and no invitation.
      email: this.emailOrMobile().trim(),
      mobile: this.mobileNumber().trim() || null,
      amount: Number(this.donationAmount()),
      currencyCode: this.currency(),

      // THE ID, NOT THE CODE. The API's campaignId is a Guid; sending 'CMP-2026-004' returns a
      // 400 before the handler runs. `apiId` is the store's map from the code a person reads to
      // the identifier the API requires.
      // THE LINK'S OWN ID WINS. It is already the identifier the API wants, and it is the only
      // one available to a donor with no session - see `campaignIdFromLink`.
      campaignId:
        this.campaignIdFromLink()
        ?? (campaignRef ? this.campaignStoreOrNull()?.apiId(campaignRef) ?? null : null),
      trackingReference: this.trackingReference() || null,
      taxIdentifier: this.panOrTaxId().trim() || null,
      addressLine1: this.addressText().trim() || null,

      // THE CHOSEN GEOGRAPHY, RATHER THAN NOTHING. The picker's value was read by the template
      // and by nothing else, so an administrative geography selected on this form never left
      // the browser and no receipt address was ever the poorer for it being wrong. The API's
      // countryId / stateId / cityId take the master catalogue's Guids, which this form cannot
      // resolve - a public donor may not read the catalogue - so the approved label travels on
      // the second address line, which is where a printed receipt wants it in any case.
      addressLine2: this.geographyLabel(this.geography()) || null,

      // Section 11: consent is captured BEFORE the intent exists, so it travels with the
      // creation rather than being written over it afterwards.
      consentGiven: this.consentChecked(),
      consentVersion: this.consentPolicyVersion(),
    };
  }

  /**
   * Section 3 - the Lead/Donor check, then payment.
   *
   * THE SERVER DECIDES, NOT THIS PAGE. The old code compared the typed address against the
   * literal string 'existing.donor@ydot.org' and showed the duplicate panel when it matched,
   * which recognised exactly one person in the world. `existingDonorMatched` is the API's
   * answer, and a matching Lead has already been upgraded to Donor by the time it comes back.
   */
  private onIntentCreated(intent: DonationIntentResponse): void {
    this.intentReference.set(intent.intentReference);
    this.lifecycleState.set('Submitted');
    this.lastOutcome.set({
      action: 'Submit',
      reference: intent.intentReference,
      state: intent.statusDescription || 'Submitted',
      effectiveTime: this.nowLabel(),
      downstream: 'Payment link requested from the gateway',
      nextAction: 'Complete the payment at the gateway',
    });
    this.pushActivity('Submitted. Reference ' + intent.intentReference + ' created.');

    // NO MATCH IS THE COMMON CASE AND HAS NO BRANCH. A stranger giving for the first time goes
    // straight to the gateway; they are converted to a Donor, given a login and sent an
    // activation invitation AFTER the money arrives, by the server, on the success path.
    if (intent.existingDonorMatched !== true) {
      this.startPayment(intent.intentReference, intent.version);
      return;
    }

    // A MATCH STOPS THE FLOW AND ASKS. Section 3 of the document: "If the person is already a
    // Donor they can pay right away, or log in and before making. After logging in, the
    // proceeds to payment." Both branches are the donor's to choose, so the page must offer
    // them rather than pick one - which is what it used to do, showing a "welcome back" toast
    // and redirecting to the gateway before anybody could read it.
    this.donorCheckLoading.set(true);
    this.payments.checkExistingDonor(intent.intentReference).subscribe({
      next: (check) => {
        this.donorCheckLoading.set(false);
        this.donorCheck.set(check);
        this.identityChoiceOpen.set(true);
        this.pushActivity(
          'Recognised as an existing donor' +
            (check.maskedEmail ? ' (' + check.maskedEmail + ')' : '') + '.',
        );
      },

      // THE CHECK FAILING MUST NOT BLOCK THE DONATION. It is a courtesy that offers a sign-in;
      // if it cannot answer, the donor carries on to payment exactly as an unrecognised one
      // would. Losing a gift because a lookup timed out is the worse outcome by a distance.
      error: () => {
        this.donorCheckLoading.set(false);
        this.pushActivity('Donor recognition unavailable; continuing to payment.');
        this.startPayment(intent.intentReference, intent.version);
      },
    });
  }

  // ===========================================================================================
  // Section 3 - the Lead/Donor branch
  // ===========================================================================================

  /** The server's answer: masked e-mail, whether an account is active, and what it advises. */
  protected readonly donorCheck = signal<ExistingDonorCheckResponse | null>(null);
  protected readonly donorCheckLoading = signal(false);
  protected readonly identityChoiceOpen = signal(false);

  /**
   * Whether the recognised donor can actually sign in.
   *
   * A DONOR RECORD IS NOT A LOGIN. Somebody converted from a Lead by an earlier donation has a
   * Donor record from the moment their payment cleared, but their account stays in an invited
   * state until they click the activation link in their e-mail. Offering "sign in" to them sends
   * them to a form no password of theirs will open, so the panel offers only the payment branch
   * and says why.
   */
  protected readonly canSignIn = computed(() => this.donorCheck()?.hasActiveAccount === true);

  protected readonly recognisedContact = computed(
    () => this.donorCheck()?.maskedEmail ?? 'your saved contact details',
  );

  /**
   * Sign in first, then come back and pay.
   *
   * THE INTENT REFERENCE TRAVELS ON THE RETURN URL, which is what "with the intent preserved"
   * has to mean in practice - the donation is already created and already carries the campaign,
   * amount and consent, so the donor must land back on the SAME record rather than on an empty
   * form they would have to fill in twice. `bindIntentFromQueryString` reads it back on arrival.
   */
  protected signInAndContinue(): void {
    const reference = this.intentReference();
    if (!reference) {
      return;
    }

    this.identityChoiceOpen.set(false);
    this.pushActivity('Sent to sign in; donation ' + reference + ' preserved.');
    this.router.navigate(['/auth/sign-in'], {
      queryParams: {
        returnUrl: '/app/donations/public-donation-initiation?intent=' + reference,
      },
    });
  }

  /** Pay now, without signing in. The donation is already recorded either way. */
  protected continueWithoutSigningIn(): void {
    const reference = this.intentReference();
    if (!reference) {
      return;
    }

    this.identityChoiceOpen.set(false);

    // RE-READ FOR THE CURRENT VERSION. The panel may have been open for some time, and the link
    // call is refused as stale against a version that has moved - which is the protection that
    // stops a second attempt being opened by accident.
    this.payments.getPublicIntent(reference).subscribe({
      next: (detail) => this.startPayment(detail.intentReference, detail.version),
      error: (error: unknown) =>
        this.toast.show('Payment unavailable', apiErrorMessage(error), 'error'),
    });
  }

  // ===========================================================================================
  // Payment - the checkout the donor actually sees
  // ===========================================================================================

  /**
   * Opens whichever provider the ORGANISATION is configured for.
   *
   * THIS SCREEN NO LONGER NAMES A PROVIDER, and that is the change. It used to hold Razorpay's
   * script URL, Razorpay's constructor shape and Razorpay's option names, so an organisation
   * configured for anything else had its order opened against that provider on the server and a
   * Razorpay card form drawn over it in the browser. Provider selection was tenant-wise on one
   * side of the wire only.
   *
   * `CheckoutSession.gatewayName` DECIDES, and it is the provider the server actually opened the
   * order against - which comes from that organisation's payment gateway configuration.
   */
  private readonly checkout = inject(GatewayCheckoutService);

  /**
   * Submit's real destination: open the provider's checkout over this page.
   *
   * WHY THIS REPLACED "SEND THEM A LINK". Pressing Submit used to ask the server for a PAYMENT
   * LINK and then navigate the whole browser to it. Two things followed and both were wrong for
   * somebody sitting in front of the form. The provider e-mails a payment link to the donor,
   * which is a second message nobody asked for; and coming back afterwards depends on a callback
   * URL reaching a host the provider can see - fine in production, and on a development machine
   * a donor who has paid is left on the provider's own page.
   *
   * WHAT HAPPENS INSTEAD. The server creates an ORDER against the organisation's configured
   * gateway, and this opens that provider's own checkout over this page.
   *
   * IT FALLS BACK RATHER THAN FAILING. An organisation whose provider cannot draw an in-page
   * checkout still gets the link flow - the server says so explicitly, and the version is re-read
   * first because opening and releasing the session moved it.
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

  /**
   * Draws the configured provider's payment form over this page.
   *
   * IT FALLS BACK RATHER THAN FAILING, and both fallbacks end in the same place. An organisation
   * whose provider has no in-page checkout at all is refused by the server, before this; one
   * whose provider has a server adapter but no browser SDK here is refused by the checkout
   * service, which reports it as unsupported rather than as an error. Either way the donor gets
   * a payment link, which every provider supports.
   */
  private openCheckout(session: CheckoutSession): void {
    void this.checkout
      .open(session, this.selectedCampaign()?.name || 'Donation', {
        onSucceeded: (confirmation) => this.confirmCheckout(session, confirmation),
        onFailed: () => this.onCheckoutFailed(session.intentReference),
        onDismissed: () => this.onCheckoutDismissed(),
      })
      .then((outcome) => {
        if (outcome === 'opened') {
          this.lifecycleState.set('Awaiting payment');
          this.uiState.set('success');
          this.pushActivity(
            'Checkout opened via ' + session.gatewayName + ' (attempt ' + session.attemptNumber + ').',
          );
          return;
        }

        if (outcome === 'unsupported') {
          this.pushActivity(
            session.gatewayName + ' has no in-page checkout; requesting a payment link instead.',
          );
        } else {
          this.pushActivity('The payment form could not be opened; requesting a payment link instead.');
        }

        // THE DONATION SURVIVES EITHER WAY. It is recorded and awaiting payment, so a link that
        // cannot be issued still leaves a row on the payments queue for recovery rather than a
        // gift that vanished with an error toast.
        this.payments.getPublicIntent(session.intentReference).subscribe({
          next: (detail) => this.requestPaymentLink(detail.intentReference, detail.version),
          error: () => {
            this.uiState.set('success');
            this.lifecycleState.set('Awaiting payment');
            this.pushActivity('The payment form could not be opened; the donation is awaiting payment.');
            this.toast.show(
              'Payment form unavailable',
              'We could not open the payment form. Select Continue to payment to try again.',
              'error',
            );
          },
        });
      });
  }

  /**
   * Hands the signed result back to the server, then shows the donor where they stand.
   *
   * THE PAGE DOES NOT DECIDE THE OUTCOME AND MUST NOT. What the provider hands the browser is a
   * payment id and a signature over it; only the server holds the secret that proves the
   * signature, and only the server can ask the provider whether the money actually moved. So
   * this posts the three values and goes to the result page either way - the result page reads
   * the answer from our own API, which is the only account of this worth showing anybody.
   */
  private confirmCheckout(session: CheckoutSession, confirmation: ConfirmCheckoutRequest): void {
    this.uiState.set('loading');
    this.pushActivity('Payment completed at the gateway; confirming.');

    this.payments
      .confirmCheckout(session.intentReference, confirmation)
      .subscribe({
        next: () => this.goToResult(session.intentReference),

        // A FAILED CONFIRMATION IS NOT A FAILED PAYMENT, and the donor must never be told it is.
        // The money may well have moved; what failed was our chance to hear about it on this
        // request. The result page asks again, and keeps asking.
        error: () => this.goToResult(session.intentReference),
      });
  }

  /** The donor closed the form without paying. Nothing failed; nothing was charged. */
  private onCheckoutDismissed(): void {
    this.uiState.set('success');
    this.lifecycleState.set('Awaiting payment');
    this.pushActivity('The donor closed the payment form without paying.');
    this.toast.show(
      'Payment not completed',
      'The payment form was closed. Select Continue to payment when you are ready.',
      'info',
    );
  }

  /** The provider declined the payment. A real outcome, so the donor is taken to it. */
  private onCheckoutFailed(intentReference: string): void {
    this.pushActivity('The payment was declined at the gateway.');
    this.goToResult(intentReference);
  }

  /**
   * Back to our own application, whatever happened.
   *
   * THE RESULT PAGE IS ALREADY THE RETURN HALF OF THIS FLOW - it reads the intent reference,
   * asks our API to verify with the gateway, and polls while the answer is still Pending. Sending
   * both success and failure through it means one page tells the donor where they stand, and it
   * tells them on OUR evidence rather than on anything the browser was handed.
   */
  private goToResult(intentReference: string): void {
    this.router.navigate(['/give/result'], { queryParams: { intent: intentReference } });
  }

  /**
   * Asks the server for the Razorpay payment link and sends the donor to it.
   *
   * THE FALLBACK, NOT THE MAIN ROUTE, since Submit opens a checkout instead. This runs for an
   * organisation whose provider cannot draw an in-page form, and when the checkout session could
   * not be opened at all - a link is worse for somebody sitting at the screen, and far better
   * than a donation nobody can pay.
   *
   * THE EXPECTED VERSION IS SENT because two tabs, or a double-clicked Submit, would otherwise
   * open two attempts against one intent - two links, and a donor who pays both has given twice.
   */
  private requestPaymentLink(intentReference: string, expectedVersion: number): void {
    this.payments.createPaymentLink(intentReference, { expectedVersion }).subscribe({
      next: (link) => {
        this.lifecycleState.set('Awaiting payment');
        this.paymentLinkDestination.set(link.paymentLinkUrl);
        this.uiState.set('success');
        this.pushActivity('Payment link issued via ' + link.gatewayName + ' (attempt ' + link.attemptNumber + ').');
        this.toast.show('Redirecting to payment', 'Opening the secure payment page.', 'success');

        // A FULL NAVIGATION, NOT A NEW TAB. A popup blocker silently swallowing the window is
        // indistinguishable, to the donor, from the button doing nothing at all.
        window.location.assign(link.paymentLinkUrl);
      },
      error: (error: unknown) => {
        // THE INTENT SURVIVES A LINK FAILURE. It is recorded and Pending, so it appears in the
        // Payment Queue for an admin to recover rather than vanishing with the error toast.
        this.uiState.set('success');
        this.lifecycleState.set('Submitted');
        this.pushActivity('Payment link could not be issued; the intent remains submitted.');
        this.toast.show('Payment link unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  protected readonly submitDialogOpen = signal(false);
  protected readonly submitReason = signal('');
  protected readonly submitReasonMin = 10;
  protected readonly submitReasonMax = 500;
  protected readonly submitReasonValid = computed(() => {
    const len = this.submitReason().trim().length;
    return len >= this.submitReasonMin && len <= this.submitReasonMax;
  });
  protected readonly submitReasonCount = computed(() => this.submitReason().trim().length);

  protected cancelSubmit(): void {
    this.submitDialogOpen.set(false);
  }

  /** The confirm button of the submit dialog, which the template currently keeps commented out. */
  protected confirmSubmit(): void {
    this.submitDialogOpen.set(false);
    this.requestSubmit();
  }

  /**
   * Continue to payment, for an intent that exists but has no usable link yet.
   *
   * IT RE-ASKS THE SERVER rather than reopening a checkout in this browser. Reading the intent
   * first is what supplies the current version - without it the link call would be refused as
   * stale, which is exactly the protection that stops a second attempt being opened by accident.
   */
  protected requestContinueToPayment(): void {
    const reference = this.intentReference();
    if (!this.continueToPaymentAllowed() || !reference) {
      return;
    }

    const existing = this.paymentLinkDestination();
    if (existing) {
      window.location.assign(existing);
      return;
    }

    this.payments.getPublicIntent(reference).subscribe({
      next: (detail) => this.startPayment(detail.intentReference, detail.version),
      error: (error: unknown) => this.toast.show('Payment unavailable', apiErrorMessage(error), 'error'),
    });
  }

  protected readonly lastOutcome = signal<{
    action: string;
    reference: string;
    state: string;
    effectiveTime: string;
    downstream: string;
    nextAction: string;
  } | null>(null);

  protected readonly relatedTabs: readonly RelatedTab[] = ['Linked', 'Documents', 'Activity', 'Integration', 'Support', 'Audit'];
  protected readonly activeRelatedTab = signal<RelatedTab>('Linked');
  protected selectRelatedTab(tab: RelatedTab): void {
    this.activeRelatedTab.set(tab);
  }
  protected readonly activityLog = signal<readonly ActivityEntry[]>([]);
  private pushActivity(text: string): void {
    this.activityLog.update((cur) => [{ time: this.nowLabel(), text }, ...cur]);
  }

  protected readonly uiState = signal<UiState>('ready');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected backToForm(): void {
    this.uiState.set('ready');
  }

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
    // THE LINK'S OWN REFERENCE, BEFORE ANYTHING ELSE. A donor who scanned a QR code arrives with
    // `?ref=` (or `?tracking=`) and no session; that value is the only thing on the page that
    // says which campaign the gift belongs to, and the API needs it on the create call.
    const params = this.route.snapshot.queryParamMap;
    this.trackingReference.set(params.get('ref') ?? params.get('tracking') ?? '');

    // A CAMPAIGN NAMED ON THE LINK BINDS THE PICKER AND LOCKS IT. See `campaignLockedByLink`.
    const campaignParam = (params.get('campaign') ?? '').trim();

    if (campaignParam) {
      this.campaignLockedByLink.set(true);

      if (PublicDonationInitiationComponent.isGuid(campaignParam)) {
        this.campaignIdFromLink.set(campaignParam);
      }
    }

    // NOTHING FROM A PREVIOUS GIFT, whatever the router did with this component. See below.
    if (!params.get('intent') && !params.get('intentReference')) {
      this.resetDonationFields();
    }

    this.prefillFromAccount();
    this.loadPublicCampaigns();
    this.loadConfig();

    // AND AGAIN ON EVERY LATER ARRIVAL. The constructor runs once; a donor sent back here after
    // paying may reach this route without the component being rebuilt, and that navigation is
    // the one that has a finished donation still on screen.
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((current) => {
      if (this.intentReference() && !current.get('intent') && !current.get('intentReference')) {
        this.resetDonationFields();
        this.prefillFromAccount();
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
    this.lifecycleState.set('No record');
    this.lastOutcome.set(null);
    this.uiState.set('ready');
  }


  /**
   * True when a link named the campaign, so the picker is bound and cannot be changed.
   *
   * THE FLOW DOCUMENT ASKS FOR EXACTLY THIS: "If the shared link carries a campaign name, that
   * campaign is auto-bound as the default on the donation form and cannot be edited or changed
   * by the donor." A donor who could re-point a link from a poster would be giving to a cause
   * the poster did not advertise.
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


  /**
   * Whether the donor's own details are fixed by their account rather than typed.
   *
   * IT IS THE SIGNED-IN CASE, AND ONLY THAT. Somebody with a session has a name and an e-mail on
   * their account, and a donation recorded against something they typed over the top would not
   * be theirs - the receipt, the donor record and the giving history would all attach to a
   * different person. An anonymous donor types everything, because there is nothing to take.
   *
   * IT DOES NOT LOCK THE GIFT. Campaign, amount, currency and consent stay editable: those are
   * the decisions the donor is here to make. Only who they are is settled.
   */
  protected readonly identityLocked = computed(
    () => this.isInternalView() && this.currentUser.email() !== '',
  );

  /**
   * Fills name, e-mail and mobile from the signed-in user.
   *
   * FROM THE TOKEN, NOT FROM A LOOKUP. The three values are claims the session already carries,
   * so this costs nothing and cannot fail halfway - and an anonymous visitor simply has none of
   * them, which leaves the form empty and typeable, exactly as it should be.
   */

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

        // RE-MATCHED NOW THE LIST EXISTS - a link naming a campaign arrives before this returns.
        this.bindCampaignFromLink();
      },
      error: () => this.publicCampaigns.set([]),
    });
  }

  private prefillFromAccount(): void {
    if (!this.isInternalView()) {
      return;
    }

    const email = this.currentUser.email();
    const name = this.currentUser.displayName();

    if (name) {
      this.fullName.set(name);
    }

    if (email) {
      this.emailOrMobile.set(email);
    }

    // NO MOBILE TO TAKE. The authenticated-user response carries a name and an e-mail and not a
    // telephone number, so this one field stays typeable for a signed-in donor rather than being
    // locked empty - which would be a required-looking control nobody could fill in.
  }

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

        // THE REGISTER HAS ANSWERED BY NOW, so a campaign named on the link can be matched.
        this.bindCampaignFromLink();

        this.bindIntentFromQueryString();
      },
      error: () => {
        this.uiState.set('ready');
        this.toast.show('Error', 'Failed to load public donation initiation configuration.', 'error');
      },
    });
  }

  /**
   * Selects the campaign a link named.
   *
   * BY CODE OR BY NAME, because a link may reasonably carry either - the code is what the
   * register shows and what a person would copy, and the name is what somebody hand-building a
   * link is likeliest to type.
   *
   * A CAMPAIGN THAT DOES NOT MATCH LEAVES THE PICKER OPEN rather than locking it empty. That is
   * the state an anonymous donor is in on every visit - the register is authenticated, so they
   * are offered no list - and their campaign comes from the tracking reference instead.
   */
  private bindCampaignFromLink(): void {
    const code = (this.route.snapshot.queryParamMap.get('campaign') ?? '').trim().toLowerCase();

    if (!code) {
      return;
    }

    const match = this.campaignOptions().find(
      (option) => option.reference.toLowerCase() === code || option.name.toLowerCase() === code,
    );

    if (match) {
      this.selectedCampaign.set(match);
      this.campaignPickerOpen.set(false);
    } else if (!this.campaignIdFromLink()) {
      this.campaignLockedByLink.set(false);
      this.pushActivity('The campaign named on the link is not open for donations.');
    }
  }

  /**
   * Reopens an intent this browser was sent to finish paying.
   *
   * IT READS THE INTENT FROM THE API, not from a DataService field the previous screen wrote.
   * The old version carried donor name, e-mail, amount and currency across in memory, which
   * meant the figures on this form were whatever the last screen chose to put there rather than
   * what the intent actually says - and a refresh lost the lot.
   */
  private bindIntentFromQueryString(): void {
    const params = this.route.snapshot.queryParamMap;
    const reference = params.get('intent') ?? params.get('intentReference') ?? '';
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
        this.paymentLinkDestination.set(intent.paymentLinkUrl ?? '');
        this.lifecycleState.set(intent.paymentLinkUrl ? 'Awaiting payment' : 'Submitted');

        const campaign = this.campaignOptions().find((c) => c.name === intent.campaignName);
        if (campaign) {
          this.selectedCampaign.set(campaign);
        }

        this.pushActivity('Loaded donation intent ' + intent.intentReference + '.');
        this.toast.show(
          'Continue payment',
          'Donation ' + intent.intentReference + ' is ready. Select Continue to payment to finish it.',
          'info',
        );
      },
      error: (error: unknown) =>
        this.toast.show('Donation not found', apiErrorMessage(error), 'error'),
    });
  }
}
