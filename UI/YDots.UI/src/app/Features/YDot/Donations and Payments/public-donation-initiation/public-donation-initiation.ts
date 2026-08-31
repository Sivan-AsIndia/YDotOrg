import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { CampaignApiService } from '../../../../Service/campaign-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { formatMoment } from '../../../../Shared/models/payment-adapters';

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

/** A scope-aware selector option with a stable reference and disambiguating context (4.2.2). */
interface ScopeOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

/** A controlled catalogue value shown as label + underlying stable reference (4.2.2). */
interface CatalogueOption {
  readonly reference: string;
  readonly label: string;
}

interface ActivityEntry {
  readonly time: string;
  readonly text: string;
}

/** Related-and-history sub-tabs (4.2.1 Related and history). */
type RelatedTab = 'Linked' | 'Documents' | 'Activity' | 'Integration' | 'Support' | 'Audit';

/**
 * Public donation initiation - SCR-PAY-004, sections 11 to 14.
 *
 * TWO SERVER CALLS AND NOTHING INVENTED IN BETWEEN.
 *
 *   Submit               - POST /api/public/donations/initiate. The intent reference comes back
 *                          from the server, unguessable and unique platform-wide, and it is what
 *                          every later step is addressed by. Consent is captured HERE, before the
 *                          intent exists, with the wording's version travelling on the request so
 *                          a consent given today is distinguishable from last year's.
 *
 *   Continue to payment  - POST /api/public/donations/{reference}/payment-link. The organisation's
 *                          OWN gateway issues the link and the API records the attempt at the same
 *                          moment. The donor pays on the provider's hosted page - Razorpay's, where
 *                          that is the configured provider - and the outcome comes back through the
 *                          signed webhook, not through this page.
 *
 * WHAT THIS REPLACES: a checkout opened in the browser with a Razorpay TEST KEY compiled into the
 * bundle and no order id. A donor could be charged and the platform would never learn of it,
 * because neither the request nor the result passed through the API - and the key, being in the
 * bundle, was readable by anybody who opened the page.
 *
 * THE ORGANISATION IS NEVER SENT. The server resolves it from the tracking reference the donor
 * followed, or from the campaign; anything else would let a caller create donations against any
 * charity on the platform.
 */
@Component({
  selector: 'app-public-donation-initiation',
  imports: [CommonModule, FormsModule],
  templateUrl: './public-donation-initiation.html',
  styleUrl: './public-donation-initiation.css',
})
export class PublicDonationInitiationComponent {
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly campaignApi = inject(CampaignApiService);
  private readonly tokens = inject(AuthTokenService);
  private readonly destroyRef = inject(DestroyRef);

  // ================= Application shell / task header (4.2.1) =================
  protected readonly pageTitle = signal('Public donation initiation');
  protected readonly pageSubtitle = signal(
    'Collect minimum identity, amount and consent before creating a unique intent.',
  );
  protected readonly operatingTimeZone = signal('Asia/Kolkata · IST (UTC+05:30)');

  protected readonly lifecycleState = signal<LifecycleState>('No record');
  protected readonly lastRefresh = signal('');

  /** Intent reference - server-derived and immutable in this view; blank until Submit. */
  protected readonly intentReference = signal<string>('');

  /** The intent's id and version, needed by the payment-link call. */
  private readonly intentId = signal('');
  private readonly intentVersion = signal(0);

  protected readonly owner = computed(() =>
    this.fullName().trim() ? `${this.fullName().trim()} · Donor` : 'Donor · not yet identified',
  );

  protected readonly scopeSummary = computed(() =>
    this.selectedCampaign()
      ? `Public intake · ${this.selectedCampaign()!.name} (${this.selectedCampaign()!.context})`
      : 'Public intake · awaiting an eligible campaign or appeal in your active scope',
  );

  /**
   * What a visitor to this page may do.
   *
   * THE FORM ITSELF IS OPEN. Creating an intent and paying on the resulting link are anonymous by
   * design - a stranger with a QR code has no account, and requiring one would mean asking
   * somebody to register before they may give money. The API accepts those two routes without a
   * token and resolves the organisation from the reference in the route, never from anything the
   * caller can choose.
   */
  protected readonly permissions = signal<EffectivePermissions>({
    view: true,
    submit: true,
    continueToPayment: true,
  });

  // ================= Main work - field and control contract (4.2.2) =================

  /**
   * Campaign or appeal.
   *
   * READ FROM THE CAMPAIGN SERVICE'S LOOKUP, which returns the campaigns of the organisation this
   * session is operating in - not a bundled list, and not every campaign on the platform. The
   * option's `reference` is the campaign CODE, because that is what an operator reads; the
   * identifier the API needs is held beside it and never rendered.
   */
  protected readonly campaignOptions = signal<readonly ScopeOption[]>([]);
  private readonly campaignIdsByCode = new Map<string, string>();

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
    if (this.formLocked()) {
      return;
    }
    this.campaignPickerOpen.update((v) => !v);
  }

  protected readonly fullName = signal('');

  protected readonly emailOrMobile = signal('');
  protected readonly emailValid = computed(() => {
    const v = this.emailOrMobile().trim();
    if (!v) {
      return true; // required-ness is reported separately; this only flags format
    }
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v) || /^\+?[0-9]{7,15}$/.test(v);
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
  protected readonly maxDonationAmount = signal(10_000_000);
  protected readonly amountInvalid = computed(() => {
    const raw = this.donationAmount().trim();
    if (!raw) {
      return false;
    }
    const n = Number(raw);
    if (Number.isNaN(n) || n <= 0) {
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
    return (
      this.currencyCatalogue()
        .find((c) => c.reference === reference)
        ?.label.split(' - ')[0] ?? reference
    );
  }
  protected currencyFullLabel(reference: string): string {
    return this.currencyCatalogue().find((c) => c.reference === reference)?.label ?? '';
  }

  /**
   * PAN or tax identifier.
   *
   * IT IS AN IDENTIFIER, NOT A NUMBER. The previous version validated it with `Number()` and
   * rejected anything non-numeric, which refused every real Indian PAN - they are five letters,
   * four digits and a letter. The check now accepts a PAN or a plain tax reference and leaves
   * anything else to the server.
   */
  protected readonly panOrTaxId = signal<string>('');
  protected readonly panInvalid = computed(() => {
    const raw = this.panOrTaxId().trim().toUpperCase();
    if (!raw) {
      return false;
    }
    if (/^[A-Z]{5}[0-9]{4}[A-Z]$/.test(raw)) {
      return false;
    }
    return !/^[A-Z0-9-]{6,20}$/.test(raw);
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

  protected readonly consentPolicyVersion = signal('v1');
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

  /** The payment page the donor is sent to. Server-derived and immutable in this view. */
  protected readonly paymentLinkDestination = signal<string>('');

  protected readonly privacyNoticeVersion = computed(() =>
    this.intentReference() ? this.consentPolicyVersion() : '',
  );

  // ================= Actions, eligibility and result (4.2.3) =================

  protected readonly formLocked = computed(() => this.lifecycleState() !== 'No record');

  protected readonly submitAllowed = computed(
    () =>
      this.permissions().submit &&
      this.lifecycleState() === 'No record' &&
      this.uiState() !== 'no-access',
  );

  protected readonly reviewAllowed = computed(
    () => this.permissions().view && this.uiState() !== 'no-access',
  );

  protected readonly continueToPaymentAllowed = computed(
    () =>
      this.permissions().continueToPayment &&
      this.lifecycleState() === 'Submitted' &&
      this.uiState() !== 'no-access',
  );

  /**
   * Review - re-reads the intent from the server.
   *
   * IT ACTUALLY REFRESHES. The previous version only rewrote a timestamp, so "Reviewed as of…"
   * asserted freshness the screen had not obtained.
   */
  protected requestReview(): void {
    if (!this.reviewAllowed()) {
      return;
    }

    const reference = this.intentReference();

    if (!reference) {
      this.lastRefresh.set(this.nowLabel());
      this.reviewedNote.set('Nothing has been submitted yet, so there is no record to re-read.');
      return;
    }

    this.paymentApi.getPublicIntent(reference).subscribe({
      next: (intent) => {
        this.lastRefresh.set(this.nowLabel());
        this.intentVersion.set(intent.version);
        this.paymentLinkDestination.set(intent.paymentLinkUrl ?? '');
        this.reviewedNote.set(
          `Re-read as of ${this.lastRefresh()}. The record is ${intent.statusDescription}.`,
        );
        this.pushActivity(`Record re-read; the server reports ${intent.statusDescription}.`);

        if (intent.status === 'paid') {
          this.lifecycleState.set('Awaiting payment');
          this.uiState.set('success');
        }
      },
      error: (error) => {
        this.lastRefresh.set(this.nowLabel());
        this.reviewedNote.set(
          apiErrorMessage(error, 'The record could not be re-read just now. Try again shortly.'),
        );
      },
    });
  }
  protected readonly reviewedNote = signal<string>('');

  // ----- Required-field validation (4.2.2 Req. + 4.2.6) -----
  protected readonly validationErrors = computed(() => {
    const errors: { field: string; label: string; message: string }[] = [];
    if (!this.selectedCampaign()) {
      errors.push({
        field: 'campaign',
        label: 'Campaign or appeal',
        message: 'Enter Campaign or appeal.',
      });
    }
    if (!this.emailOrMobile().trim()) {
      errors.push({
        field: 'emailOrMobile',
        label: 'Email or mobile',
        message: 'Enter Email or mobile.',
      });
    } else if (!this.emailValid()) {
      errors.push({
        field: 'emailOrMobile',
        label: 'Email or mobile',
        message: 'Review Email or mobile. The value does not meet the stated format or range.',
      });
    }
    if (!this.donationAmount().trim()) {
      errors.push({
        field: 'donationAmount',
        label: 'Donation amount',
        message: 'Enter Donation amount.',
      });
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
        message: 'Review PAN or tax identifier. A PAN is five letters, four digits and a letter.',
      });
    }
    if (!this.consentChecked()) {
      errors.push({
        field: 'consent',
        label: 'Consent acknowledgement',
        message: 'Enter Consent acknowledgement.',
      });
    }
    return errors;
  });

  protected readonly remainingRequired = computed(() =>
    this.validationErrors()
      .filter((e) => e.message.startsWith('Enter '))
      .map((e) => e.label),
  );

  protected requestSubmit(): void {
    if (!this.submitAllowed()) {
      return;
    }
    if (this.validationErrors().length > 0) {
      this.uiState.set('validation');
      this.focusFirstInvalid();
      return;
    }
    this.submitReason.set('');
    this.submitDialogOpen.set(true);
  }

  // ----- Submit - Decision/review high-risk confirmation (4.2.1 / 4.2.6) -----
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

  /**
   * Creates the donation intent.
   *
   * THE REFERENCE COMES BACK FROM THE SERVER, unguessable and unique platform-wide, and it is what
   * every later step - the payment link, the result page, the verification - is addressed by.
   *
   * SECTION 12 IS ANSWERED BY THE SERVER TOO. Whether this donor is already known is an
   * organisation-scoped question, so the same person can be known to one charity here and a
   * stranger to another; asking it in the browser would have been an oracle.
   */
  protected confirmSubmit(): void {
    if (!this.submitReasonValid()) {
      this.toast.show('Validation Error', 'Please provide a valid reason.', 'warning');
      return;
    }

    const identity = this.donorIdentity();
    const campaignCode = this.selectedCampaign()?.reference ?? '';

    this.uiState.set('loading');

    this.paymentApi
      .initiateDonation({
        donorName: this.fullName().trim() || 'Donor',
        email: identity.email ?? '',
        amount: Number(this.donationAmount()) || 0,
        currencyCode: this.currency() || 'INR',
        mobile: identity.mobile ?? null,

        // THE IDENTIFIER, resolved from the code the operator picked. Sending the code where the
        // API wants the id leaves the gift attributed to no campaign at all.
        campaignId: this.campaignIdsByCode.get(campaignCode) ?? null,

        // The reference from the QR code or link the donor followed. It is what resolves the
        // campaign, the channel and - crucially - the organisation the gift belongs to.
        trackingReference: this.trackingReference() || null,

        sourceType: this.trackingReference() ? 'qrCode' : 'directLink',
        taxIdentifier: this.panOrTaxId().trim() || null,
        addressLine1: this.addressText().trim() || null,
        consentGiven: this.consentChecked(),
        consentVersion: this.consentPolicyVersion(),

        // The form defaults to anonymous and only a separately approved publication field changes
        // that, so nothing here opts a donor into being named publicly.
        allowPublicRecognition: false,
      })
      .subscribe({
        next: (intent) => {
          this.intentReference.set(intent.intentReference);
          this.intentId.set(intent.id);
          this.intentVersion.set(intent.version);
          this.paymentLinkDestination.set(intent.paymentLinkUrl ?? '');
          this.lifecycleState.set('Submitted');
          this.submitDialogOpen.set(false);
          this.lastRefresh.set(this.nowLabel());

          this.lastOutcome.set({
            action: 'Submit',
            reference: intent.intentReference,
            state: intent.statusDescription,
            effectiveTime: this.nowLabel(),
            downstream: 'Ready for a payment link',
            nextAction: 'Continue to payment',
            reason: this.submitReason().trim(),
          });

          this.pushActivity(`Submitted. Reference ${intent.intentReference} created.`);

          if (intent.existingDonorMatched === true) {
            this.toast.show(
              'Welcome back',
              'We recognise this address. Sign in to see your giving history, or continue as you are.',
              'info',
            );
          }

          this.uiState.set('success');
          this.toast.show(
            'Donation Submitted',
            `Donation intent ${intent.intentReference} has been created.`,
            'success',
          );
        },
        error: (error) => {
          this.submitDialogOpen.set(false);
          this.uiState.set('ready');
          this.toast.show(
            'Could not submit',
            apiErrorMessage(
              error,
              'The donation could not be started. Please check the details and try again.',
            ),
            'error',
          );
        },
      });
  }

  /**
   * The tracking reference the donor arrived with.
   *
   * READ FROM THE URL, because that is where a QR code puts it. It is what the server resolves the
   * organisation from when nobody is signed in.
   */
  protected readonly trackingReference = signal(
    new URLSearchParams(window.location.search).get('t') ?? '',
  );

  // ================= Continue to payment =================

  /** True while the donor is away at the provider's page and the outcome is being watched. */
  protected readonly awaitingGateway = signal(false);
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  /**
   * Sends the donor to the payment provider.
   *
   * THE LINK IS ISSUED BY THE SERVER, from the organisation's own gateway account, and the payment
   * attempt is recorded at the same moment. Where that account is a Razorpay one the donor lands
   * on Razorpay's hosted page and pays there; the platform never handles the card, never holds a
   * publishable key, and learns the outcome from the SIGNED webhook rather than from anything this
   * page could be persuaded to report.
   *
   * THE OUTCOME IS THEN VERIFIED, NOT ASSUMED. A donor who closes the tab, or a webhook that is
   * slow, must not leave the screen claiming a payment that did not happen - so the page asks the
   * server what the gateway says, on a bounded poll, and reports exactly that.
   */
  protected requestContinueToPayment(): void {
    if (!this.continueToPaymentAllowed()) {
      return;
    }

    const reference = this.intentReference();

    if (!reference) {
      this.toast.show('Not submitted', 'Submit the donation before continuing to payment.', 'warning');
      return;
    }

    const existing = this.paymentLinkDestination();

    if (existing) {
      this.openGateway(existing);
      return;
    }

    this.uiState.set('loading');

    this.paymentApi
      .createPaymentLink(reference, {
        expectedVersion: this.intentVersion(),
        preferredMethod: null,
      })
      .subscribe({
        next: (link) => {
          this.paymentLinkDestination.set(link.paymentLinkUrl);
          this.uiState.set('success');
          this.pushActivity(
            `Payment link issued by ${link.gatewayName}, attempt ${link.attemptNumber}.`,
          );
          this.openGateway(link.paymentLinkUrl);
        },
        error: (error) => {
          this.uiState.set('ready');
          this.toast.show(
            'Payment could not be started',
            apiErrorMessage(
              error,
              'The payment provider could not be reached. Nothing has been charged; please try again.',
            ),
            'error',
          );
        },
      });
  }

  /** Opens the provider's page and starts watching for the outcome. */
  private openGateway(url: string): void {
    window.open(url, '_blank', 'noopener');
    this.lifecycleState.set('Awaiting payment');
    this.awaitingGateway.set(true);

    this.lastOutcome.set({
      action: 'Continue to payment',
      reference: this.intentReference(),
      state: 'Awaiting payment',
      effectiveTime: this.nowLabel(),
      downstream: 'Waiting for the payment provider to confirm',
      nextAction: 'Complete the payment on the page that opened',
      reason: '',
    });

    this.toast.show(
      'Payment page opened',
      'Complete the payment on the provider page. This screen updates as soon as they confirm.',
      'info',
    );

    this.startWatchingOutcome();
  }

  /**
   * Watches the intent for a terminal outcome.
   *
   * BOUNDED, AND IT STOPS ITSELF. Two minutes at five-second intervals covers a normal checkout;
   * beyond that the donor has almost certainly closed the tab, and a poll that never ends is a tab
   * that never sleeps. Whatever happens, the truth is on the server - Review re-reads it.
   */
  private startWatchingOutcome(): void {
    this.stopWatchingOutcome();

    const reference = this.intentReference();
    let attempts = 0;

    this.pollHandle = setInterval(() => {
      attempts += 1;

      if (attempts > 24) {
        this.stopWatchingOutcome();
        this.pushActivity('Stopped watching for the payment outcome. Use Review to check again.');
        return;
      }

      this.paymentApi.verifyPublicPayment(reference).subscribe({
        next: (verification) => {
          const state = verification.backendPaymentState.trim().toLowerCase();

          if (state === 'confirmed') {
            this.stopWatchingOutcome();
            this.uiState.set('success');
            this.lastOutcome.set({
              action: 'Continue to payment',
              reference,
              state: 'Paid',
              effectiveTime: this.nowLabel(),
              downstream: 'The provider confirms the payment succeeded',
              nextAction:
                verification.receiptEligibility === 'Eligible'
                  ? 'Your receipt will be sent to the address on this donation'
                  : 'No further action required',
              reason: '',
            });
            this.pushActivity('The provider confirms the payment succeeded.');
            this.toast.show(
              'Payment received',
              'Thank you. The provider confirms your payment succeeded.',
              'success',
            );
            return;
          }

          if (state === 'failed') {
            this.stopWatchingOutcome();
            this.lifecycleState.set('Submitted');
            this.pushActivity('The provider reports the payment failed. The intent is unchanged.');
            this.toast.show(
              'Payment not completed',
              'The provider reports the payment did not go through. You can try again.',
              'warning',
            );
          }
        },
        error: () => {
          // A verification that could not be made leaves the outcome exactly as unknown as it was.
          // The poll simply tries again.
        },
      });
    }, 5000);
  }

  private stopWatchingOutcome(): void {
    if (this.pollHandle) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
    this.awaitingGateway.set(false);
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
  protected readonly relatedTabs: readonly RelatedTab[] = [
    'Linked',
    'Documents',
    'Activity',
    'Integration',
    'Support',
    'Audit',
  ];
  protected readonly activeRelatedTab = signal<RelatedTab>('Linked');
  protected selectRelatedTab(tab: RelatedTab): void {
    this.activeRelatedTab.set(tab);
  }
  protected readonly activityLog = signal<readonly ActivityEntry[]>([]);
  private pushActivity(text: string): void {
    this.activityLog.update((cur) => [{ time: this.nowLabel(), text }, ...cur]);
  }

  // ================= UI states (4.2.4 / 4.2.7) =================
  protected readonly uiState = signal<UiState>('loading');
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

  // ================= Helpers =================
  /** Splits the single "Email or mobile" field into the two the API takes. */
  private donorIdentity(): { email?: string; mobile?: string } {
    const contact = this.emailOrMobile().trim();
    if (!contact) {
      return {};
    }
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contact) ? { email: contact } : { mobile: contact };
  }

  private nowLabel(): string {
    return formatMoment(new Date().toISOString());
  }

  constructor() {
    this.destroyRef.onDestroy(() => this.stopWatchingOutcome());
    this.loadConfig();
    this.loadCampaigns();
  }

  private loadConfig(): void {
    this.uiState.set('loading');
    this.dataService.getPublicDonationInitiationData().subscribe({
      next: (config) => {
        this.pageTitle.set(config.pageTitle);
        this.pageSubtitle.set(config.pageSubtitle);
        this.operatingTimeZone.set(config.operatingTimeZone);
        this.consentPolicyVersion.set(config.consentPolicyVersion);
        this.currencyCatalogue.set(config.currencies);
        this.geographyCatalogue.set(config.geographies);
        this.permissions.set(config.permissions);
        this.maxDonationAmount.set(config.maxDonationAmount);

        // The organisation's own settlement currency is the sensible default, and the API rejects
        // an intent whose currency the gateway account does not settle in.
        if (!this.currency() && config.currencies.length > 0) {
          this.currency.set(config.currencies[0].reference);
        }

        this.lastRefresh.set(this.nowLabel());
        this.uiState.set('ready');

        this.bindPendingDonationFromQueue();
      },
      error: () => {
        this.uiState.set('ready');
        this.toast.show('Error', 'The donation form could not be prepared.', 'error');
      },
    });
  }

  /**
   * Loads the campaigns a gift can be attributed to.
   *
   * ONLY WHEN SOMEBODY IS SIGNED IN. A truly public visitor has no organisation to draw a campaign
   * list from - theirs is resolved from the tracking reference in the link they followed - and
   * asking for one anonymously would answer 401. The picker is therefore empty for a stranger and
   * populated for an operator taking a donation on somebody's behalf, which is exactly who each
   * case is.
   */
  private loadCampaigns(): void {
    if (!this.tokens.user()) {
      return;
    }

    this.campaignApi.lookupCampaigns().subscribe({
      next: (campaigns) => {
        this.campaignIdsByCode.clear();

        const options = campaigns
          .filter((c) => c.status !== 'cancelled' && c.status !== 'closed')
          .map((c) => {
            this.campaignIdsByCode.set(c.code, c.id);
            return { reference: c.code, name: c.name, context: c.status };
          });

        this.campaignOptions.set(options);
      },
      error: () => this.campaignOptions.set([]),
    });
  }

  /**
   * Binds a donation handed over from the payment event queue.
   *
   * IT RE-READS THE INTENT rather than trusting the row it was handed. The queue's record is a
   * gateway EVENT: it carries no version and no payment link, and both are needed the moment
   * somebody presses Continue to payment.
   */
  private bindPendingDonationFromQueue(): void {
    const pending = this.dataService.getPendingDonationForPayment();
    if (!pending) return;

    this.dataService.clearPendingDonationForPayment();

    const reference = pending.mappedIntentOrPayment;

    if (!reference) {
      this.toast.show(
        'Nothing to continue',
        'That gateway event did not correlate to a donation intent.',
        'warning',
      );
      return;
    }

    this.paymentApi.getPublicIntent(reference).subscribe({
      next: (intent) => {
        this.intentReference.set(intent.intentReference);
        this.intentId.set(intent.id);
        this.intentVersion.set(intent.version);
        this.fullName.set(intent.donorName);
        this.emailOrMobile.set(intent.email || (intent.mobile ?? ''));
        this.donationAmount.set(String(intent.amount.amount));
        this.currency.set(intent.amount.currencyCode);
        this.paymentLinkDestination.set(intent.paymentLinkUrl ?? '');
        this.consentChecked.set(intent.consentGiven);
        this.lifecycleState.set(intent.status === 'paid' ? 'Awaiting payment' : 'Submitted');
        this.lastRefresh.set(this.nowLabel());

        const campaign = this.campaignOptions().find((c) => c.name === intent.campaignName);
        if (campaign) {
          this.selectedCampaign.set(campaign);
        }

        this.pushActivity(`Loaded ${intent.intentReference} from the payment event queue.`);

        this.toast.show(
          'Continue payment',
          `${intent.intentReference} is ready. Select "Continue to payment" to open the provider's page.`,
          'info',
        );
      },
      error: (error) =>
        this.toast.show(
          'Could not load that donation',
          apiErrorMessage(error, 'That donation intent could not be read.'),
          'error',
        ),
    });
  }
}
