
import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { WorkflowStateService } from '../../../../../Service/workflow-state.service';
import { PaymentEventRecord } from '../../../../../Shared/models/payment-event-queue.model';
import { ToastService } from '../../../Finance/shared/toast.service';
import { CampaignStoreService } from '../../../../../Shared/services/campaign-store.service';
import { DataService } from '../../../../../Service/data.service';

declare global {
  interface Window {
    Razorpay: any;
  }
}

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

interface RazorpayPaymentResponse {
  razorpay_payment_id: string;
  razorpay_order_id: string;
  razorpay_signature: string;
}
@Component({
  selector: 'app-donorform',
 imports: [CommonModule, FormsModule],
  templateUrl:'./donorform.html',
  styleUrl: './donorform.css',
})
export class DonorformComponent {

  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly workflow = inject(WorkflowStateService);
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly router = inject(Router);

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


  protected readonly campaignOptions = computed<readonly ScopeOption[]>(() =>
    this.campaignStore
      .all()
      .filter((c) => c.status !== 'Cancelled' && c.status !== 'Closed')
      .map((c) => ({
        reference: c.code,
        name: c.name,
        context: c.status,
      })),
  );
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
    if (!this.selectedCampaign()) {
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
   * Submit — validates first (4.2.4 Validation); a matching in-scope identity
   * surfaces the Duplicate state without exposing another person's protected
   * details (4.2.4 Duplicate); otherwise opens the Decision/review
   * confirmation before the record is created (4.2.6 High-risk action).
   */
  protected requestSubmit(): void {
 
    if (this.validationErrors().length > 0) {
      this.uiState.set('validation');
      this.focusFirstInvalid();
      return;
    }
    if (this.emailOrMobile().trim().toLowerCase() === 'existing.donor@ydot.org') {
      this.uiState.set('duplicate');
      return;
    }
    // Already-donor flow: if the entered name AND email/mobile already exist
    // together in the shared donor list, this person has donated before. They
    // are still allowed to pay on this public form — once the payment
    // succeeds they are navigated to the login page to sign in (see
    // onPaymentSuccess) instead of creating a second donor identity.
    this.isExistingDonor.set(!!this.findExistingDonor());
    if (this.isExistingDonor()) {
      this.toast.show(
        'Already a registered donor',
        'We found an existing donor with this name and contact. After your payment succeeds you will be taken to the sign-in page.',
        'info',
      );
    }
   // QR-scan flow: no reason gate — the confirm dialog already shows the
    // bound basic details and the only next step is the Razorpay checkout.
    const ref = this.mintIntentReference();
    this.intentReference.set(ref);
    this.paymentLinkDestination.set(`https://pay.ydot.org/i/${ref}`);
    this.lifecycleState.set('Submitted');
    this.lastOutcome.set({
      action: 'Submit',
      reference: ref,
      state: 'Submitted',
      effectiveTime: this.nowLabel(),
      downstream: 'Payment-link generation queued',
      nextAction: 'Continue to payment',
      reason: this.submitReason().trim(),
    });
    this.pushActivity(`Submitted. Reference ${ref} created.`);

    // Push the submitted donation into the Payment event queue with Pending payment status.
    const queueRecord: PaymentEventRecord = {
      // The row's own identifier. The server assigns the real GUID when the gateway event
      // arrives; this locally minted id is only carried on the in-memory pending record
      // (no UI code reads it back) so the field contract stays satisfied.
      eventId: `PEV-${ref}`,
      // The donation intent this event belongs to — `ref` is exactly the intent reference
      // minted above, so the queue row and the checkout stay addressable to the same intent.
      donationIntentId: ref,
      // Concurrency stamp of a freshly created row; real versions come back from the server.
      version: 1,
      eventReference: `EVT-${ref}`,
      gatewayEventType: 'payment.intent.created',
      gatewayEventId: `evt_${ref.toLowerCase()}`,
      failureType: 'None',
      signatureResult: 'Not verified',
      receivedTime: this.nowLabel(),
      mappedIntentOrPayment: ref,
      duplicateStatus: 'Unique',
      sequenceStatus: 'In order',
      maskedEventSummary: `•••• donation intent ${ref} awaiting payment`,
      attempts: 0,
      eventState: 'New',
      resolutionAction: null,
      resolutionReason: null,
      donorName: this.donorType() === 'Organisation' ? this.organisationName().trim() : this.fullName().trim() || 'Donor',
      donorEmail: this.emailOrMobile().trim(),
      campaignName: this.selectedCampaign()?.name ?? 'Public donation initiation',
      donationAmount: this.donationAmount().trim(),
      currency: this.currencyLabel(this.currency()) || this.currency() || 'INR',
      paymentStatus: 'Pending',
    };
    this.dataService.addDonationToPaymentEventQueue(queueRecord);
    // Remember whether this payer was already a donor BEFORE this submission
    // (the registration below adds/updates them in the shared list, so the
    // lookup cannot be repeated reliably after this point). A later
    // queue-based continue-payment reads the same flag by intent reference.
    this.dataService.setIntentExistingDonorFlag(ref, this.isExistingDonor());

    // Change flow: register the person in the shared donor list immediately.
    // Registered donors and paid donors are the SAME list — Donor List reads
    // this exact state, so nothing can drift apart. Paying later updates this
    // same record instead of creating a second one.
    this.workflow.registerDonorFromPayment({
      name: this.donorType() === 'Organisation' ? this.organisationName().trim() : this.fullName().trim() || 'Donor',
      ...this.donorIdentity(),
      campaign: this.selectedCampaign()?.name,
      reference: ref,
      paid: false,
    });

    this.uiState.set('success');
    this.toast.show('Donation Submitted', `Donation intent ${ref} has been submitted.`, 'success');

    // Straight-through flow: the lifecycle is now 'Submitted', so open the
    // Razorpay checkout immediately — the donor should not need a second
    // click on "Continue to payment". The call re-checks its own guards and
    // shows a clear toast if the amount is below 1 or checkout.js cannot load.
    this.requestContinueToPayment();
  }

  // ----- Submit — Decision/review high-risk confirmation (4.2.1 Decision/review / 4.2.6) -----
  protected readonly submitReason = signal('');
  protected readonly submitReasonMin = 10;
  protected readonly submitReasonMax = 500;
  protected readonly submitReasonValid = computed(() => {
    const len = this.submitReason().trim().length;
    return len >= this.submitReasonMin && len <= this.submitReasonMax;
  });
  protected readonly submitReasonCount = computed(() => this.submitReason().trim().length);


  /**
   * Confirm Submit — executes idempotently; shows the stable reference,
   * accepted/committed result, pending dependency and safe next action
   * (4.2.3 Submit / 4.2.6 Success). Also pushes the submitted donation
   * into the Payment event queue with a Pending payment status.
   */

  /**
   * Continue to payment — opens the Razorpay checkout with the submitted
   * donation data bound to the payment (name, email, amount, currency).
   * On successful payment the lifecycle advances to "Awaiting payment".
   */
  protected requestContinueToPayment(): void {
    if (!this.continueToPaymentAllowed()) {
      return;
    }

    const amount = Number(this.donationAmount());
    const currency = this.currencyLabel(this.currency()) || this.currency() || 'INR';
    const name = this.donorType() === 'Organisation' ? this.organisationName().trim() || 'Donor' : this.fullName().trim() || 'Donor';
    const contact = this.emailOrMobile().trim();
    const isEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contact);
    const reference = this.intentReference();

    // Razorpay rejects amounts below one unit of the currency — guard here so
    // the donor gets a clear message instead of a cryptic checkout error.
    if (!Number.isFinite(amount) || amount < 1) {
      this.toast.show('Payment', 'Enter a donation amount of at least 1 to continue to payment.', 'warning');
      return;
    }

    // Build the Razorpay order payload from the submitted form data.
    const options: any = {
      key: 'rzp_test_TCwSZidEO9q88a', // Razorpay test key
      amount: Math.round(amount * 100), // Razorpay expects amount in the smallest currency unit (paise)
      currency: currency,
      name: 'YDot Donations',
      description: `Donation intent ${reference} · ${this.selectedCampaign()?.name ?? 'Campaign'}`,
      // NOTE: no order_id — this demo has no server-side Orders API. Passing an
      // EMPTY order_id makes checkout.js throw "Order ID is invalid" and the
      // payment window never opens; omitting it entirely is the supported
      // no-order checkout flow.
      prefill: {
        name: name,
        // Razorpay validates prefill.email — only send it when the donor really
        // entered an email; a mobile number goes to prefill.contact instead.
        email: isEmail ? contact : '',
        contact: isEmail ? '' : contact,
      },
      notes: {
        intent_reference: reference,
        campaign_reference: this.selectedCampaign()?.reference ?? '',
        campaign_name: this.selectedCampaign()?.name ?? '',
        donor_type: this.donorType(),
        organisation_name: this.organisationName().trim(),
        donor_name: name,
        donor_email: contact,
        pan_or_tax_id: this.panOrTaxId().trim(),
        address_text: this.addressText().trim(),
        geography: this.geographyLabel(this.geography()),
        anonymous_donation: this.anonymousDonation() ? 'Yes' : 'No',
        dedication: this.dedicationEnabled() ? `${this.dedicationType()} ${this.dedicateeName().trim()}` : '',
        recurring: this.recurringDonation() ? this.recurringFrequency() : 'One-time',
        comments: this.comments().trim(),
      },
      theme: {
        color: '#2e4034',
      },
      handler: (response: RazorpayPaymentResponse) => {
        // Payment succeeded — bind the payment response to the record.
        this.onPaymentSuccess(response);
      },
      modal: {
        ondismiss: () => {
          // Standard checkout has no separate failure callback — an abandoned
          // (or failed) attempt ends with the modal closing. The queue record
          // stays Pending so the donor can retry later from the Payment event
          // queue, which lands back on this page via continue-payment.
          this.toast.show('Payment Cancelled', 'You closed the payment window. Your intent remains Submitted.', 'warning');
        },
      },
    };

    this.openRazorpayCheckout(options);
  }

  /**
   * Open the Razorpay checkout once `window.Razorpay` is available. index.html
   * ships checkout.js, but if that script was blocked or is still loading,
   * `window.Razorpay` is undefined and `new window.Razorpay(...)` would throw
   * silently — so load the script on demand and surface a clear error toast
   * when the gateway cannot be reached at all.
   */
  private openRazorpayCheckout(options: any): void {
    const open = () => {
      if (window.Razorpay) {
        new window.Razorpay(options).open();
        this.pushActivity('Razorpay checkout opened.');
      } else {
        this.pushActivity('Razorpay checkout could not be loaded.');
        this.toast.show('Payment Unavailable', 'Razorpay checkout could not be loaded. Check the internet connection and try again.', 'error');
      }
    };

    if (window.Razorpay) {
      open();
      return;
    }

    const scriptId = 'razorpay-checkout-js';
    const existing = document.getElementById(scriptId) as HTMLScriptElement | null;
    if (existing) {
      // The index.html script tag is present but has not finished loading yet.
      existing.addEventListener('load', open, { once: true });
      existing.addEventListener('error', open, { once: true });
      return;
    }

    const script = document.createElement('script');
    script.id = scriptId;
    script.src = 'https://checkout.razorpay.com/v1/checkout.js';
    script.async = true;
    script.addEventListener('load', open, { once: true });
    script.addEventListener('error', open, { once: true });
    document.body.appendChild(script);
  }

  /** Handle a successful Razorpay payment — advance lifecycle and bind payment data. */
  private onPaymentSuccess(response: RazorpayPaymentResponse): void {
    this.lifecycleState.set('Awaiting payment');
    this.lastOutcome.set({
      action: 'Continue to payment',
      reference: this.intentReference(),
      state: 'Awaiting payment',
      effectiveTime: this.nowLabel(),
      downstream: `Payment captured · Razorpay ${response.razorpay_payment_id}`,
      nextAction: 'Complete payment at the payment-link destination',
      reason: '',
    });
    this.pushActivity(`Payment initiated via Razorpay. Payment ID ${response.razorpay_payment_id}.`);
    // Update the payment event queue status to Success.
    this.dataService.updatePaymentEventQueueStatus(`EVT-${this.intentReference()}`, 'Success');
    // Change flow: the successful payment UPDATES the same donor record that
    // was created on Submit (matched by email/mobile) — amounts, last donation
    // date and lifetime giving now reflect the real payment in Donor List.
    this.workflow.registerDonorFromPayment({
      name: this.donorType() === 'Organisation' ? this.organisationName().trim() || 'Donor' : this.fullName().trim() || 'Donor',
      ...this.donorIdentity(),
      campaign: this.selectedCampaign()?.name,
      reference: this.intentReference(),
      amount: Number(this.donationAmount()) || 0,
      paid: true,
    });
    this.uiState.set('success');
    this.toast.show('Payment Successful', `Payment ${response.razorpay_payment_id} was successful. A confirmation email has been sent to ${this.emailOrMobile().trim()}.`, 'success');
    // Already-donor flow: the payer matched an existing donor record on
    // Submit (name + payment email/mobile). Now that the payment succeeded,
    // hand them over to the login page to sign in rather than keeping them
    // on the public form. The short delay lets the success toast be seen
    // before the route changes (the root toast overlay survives navigation).
    if (this.isExistingDonor()) {
      this.isExistingDonor.set(false);
      setTimeout(() => {
        this.router.navigate(['/auth/sign-in']);
      }, 1200);
    }
  }

  /** Handle a failed Razorpay payment — keep the intent Submitted and notify the donor. */
  private onPaymentFailure(error: any): void {
    this.lifecycleState.set('Submitted');
    this.pushActivity(`Payment failed via Razorpay. ${error?.error?.description ?? 'Payment was not completed.'}`);
    // Update the payment event queue status to Fail.
    this.dataService.updatePaymentEventQueueStatus(`EVT-${this.intentReference()}`, 'Fail');
    this.toast.show('Payment Failed', 'Your payment could not be completed. A notification email has been sent to your email address. Please try again.', 'error');
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
  /**
   * Splits the single "Email or mobile" field into identity parts used to
   * match the donor record in the shared list, so Submit and a later
   * successful payment always resolve to the SAME donor.
   */
  private donorIdentity(): { email?: string; mobile?: string } {
    const contact = this.emailOrMobile().trim();
    if (!contact) {
      return {};
    }
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contact)
      ? { email: contact }
      : { mobile: contact };
  }

  /**
   * Already-donor lookup — matches ONLY when BOTH the entered full name AND
   * the entered contact (email case-insensitively, or mobile by digits) are
   * found on the same record in the shared donor list. Reuses the same
   * identity semantics as WorkflowStateService.registerDonorFromPayment so
   * the gate and the registration can never disagree about who is a donor.
   */
  private findExistingDonor(): { email: string; name: string } | undefined {
    const contact = this.emailOrMobile().trim().toLowerCase();
    const name = this.fullName().trim().toLowerCase();
    if (!contact || !name) {
      return undefined;
    }
    const isEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contact);
    const mobileDigits = isEmail ? '' : contact.replace(/\D+/g, '');
    return this.workflow.donors().find((donor) => {
      if (donor.name.trim().toLowerCase() !== name) {
        return false;
      }
      if (isEmail) {
        return donor.email.trim().toLowerCase() === contact;
      }
      return mobileDigits.length >= 10 && donor.mobile.replace(/\D+/g, '') === mobileDigits;
    });
  }

  private mintIntentReference(): string {
    const seq = Math.floor(100 + Math.random() * 900);
    return `INT-2025-${seq}`;
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
    this.loadConfig();
    this.seedSharedDonors();
  }

  /**
   * The already-donor gate matches against the shared donor list, but that
   * list is normally seeded by the Donors & Leads screens. This public form
   * is usually the FIRST page a donor opens, so seed it here too — the same
   * runtime fetch as Donor List, with the same merge semantics in
   * WorkflowStateService.seedDonors (donors registered later in this session
   * stay authoritative over the JSON records).
   */
  private seedSharedDonors(): void {
    fetch('/assets/data/donors.json')
      .then((response) => (response.ok ? response.json() : null))
      .then((data) => {
        if (data) {
          this.workflow.seedDonors(data);
        }
      })
      .catch(() => {
        // Best-effort seeding: with no donor data available the gate simply
        // lets every submission through, exactly as before.
      });
  }

  private loadConfig(): void {
    this.uiState.set('loading');
    this.dataService.getPublicDonationInitiationData().subscribe({
      next: (config: PublicDonationInitiationConfig) => {
        this.pageTitle.set(config.pageTitle);
        this.pageSubtitle.set(config.pageSubtitle);
        this.operatingTimeZone.set(config.operatingTimeZone);
        this.consentPolicyVersion.set(config.consentPolicyVersion);
        // NOTE: campaignOptions is intentionally NOT set from config.campaigns
        // anymore — it now lives as a `computed()` sourced live from
        // CampaignStoreService (the same store behind Campaign Overview /
        // Campaign Register), so it always reflects the real campaign list.
        this.currencyCatalogue.set(config.currencies);
        this.geographyCatalogue.set(config.geographies);
        this.permissions.set(config.permissions);
        this.maxDonationAmount.set(config.maxDonationAmount);
        this.uiState.set('ready');

        // Check if we arrived from the Payment event queue with a Pending donation
        // that needs to continue payment. Bind the data and open Razorpay.
        this.bindPendingDonationFromQueue();
      },
      error: () => {
        this.uiState.set('ready');
        this.toast.show('Error', 'Failed to load public donation initiation configuration.', 'error');
      },
    });
  }

  /** Bind a Pending donation record from the Payment event queue and open Razorpay. */
  private bindPendingDonationFromQueue(): void {
    const pending = this.dataService.getPendingDonationForPayment();
    if (!pending) return;

    // Clear the stored record so it is not re-applied on refresh.
    this.dataService.clearPendingDonationForPayment();

    // Bind the donation data from the queue record.
    this.fullName.set(pending.donorName);
    this.emailOrMobile.set(pending.donorEmail);
    this.donationAmount.set(pending.donationAmount);
    this.currency.set(pending.currency);

    // Find and select the matching campaign.
    const campaign = this.campaignOptions().find((c) => c.name === pending.campaignName);
    if (campaign) {
      this.selectedCampaign.set(campaign);
    }

    // Set the intent reference and lifecycle state so Continue to payment is enabled.
    this.intentReference.set(pending.mappedIntentOrPayment);
    this.lifecycleState.set('Submitted');

    // Already-donor flag for the queue continuation path: the Submit that
    // created this intent captured it (before registering the payer in the
    // shared list); fall back to the live donor lookup when no flag exists.
    const existingDonorFlag = this.dataService.getIntentExistingDonorFlag(pending.mappedIntentOrPayment);
    this.isExistingDonor.set(existingDonorFlag ?? !!this.findExistingDonor());

    // Show a toast so the donor knows the data is bound and ready.
    this.toast.show('Continue Payment', `Donation data loaded for ${pending.mappedIntentOrPayment}. Click "Continue to payment" to open Razorpay.`, 'info');
  }
}