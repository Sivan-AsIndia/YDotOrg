import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  UiState,
  LeadCaptureData,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { createXlsx, createZip, readXlsxRows } from '../../../../Shared/services/spreadsheet';
import { apiErrorMessage, apiFieldErrors } from '../../../../Shared/models/api-response.model';
import {
  BulkLeadImportRow,
  BulkLeadImportRowResult,
  CreateLeadRequest,
  DonLookupItem,
  LeadCaptureResponse,
} from '../../../../Shared/models/donor-contract.model';


export interface MobileNumberEntry {
  readonly id: string;
  value: string;
  isPrimary: boolean;
}

/** Upload → scan → classify → link pipeline shown for consent evidence. */
export type EvidenceStatus = 'idle' | 'uploading' | 'scanned' | 'classified' | 'linked';

export interface ConsentFields {
  emailConsent: boolean;
  smsConsent: boolean;
  whatsappConsent: boolean;
  phoneConsent: boolean;
  doNotContact: boolean;
  recognitionPreference: 'Anonymous' | 'Recognised' | '';
  consentNotes: string;
  /* ---------------------------------------------------------------- *
   * Consent & Preference Centre field set (mirrors Screen 15) — Purpose,
   * Channel, Consent State, Evidence Source, Effective/Expiry Time,
   * Contact Restrictions and Correction Reason.
   * ---------------------------------------------------------------- */
  purpose: string;
  channelRef: string;
  consentState: string;
  evidenceFileName: string;
  evidenceStatus: EvidenceStatus;
  effectiveDate: string;
  effectiveTime: string;
  expiryDate: string;
  expiryTime: string;
  contactRestrictionPhone: string;
  correctionReason: string;
}

export interface LeadCaptureFormFields {
  firstName: string;
  lastName: string;
  displayName: string;
  mobiles: MobileNumberEntry[];
  email: string;
  preferredLanguage: string;
  /* Structured, approved-geography location capture (replaces free-text
   * "Location"): country + state + city are constrained to the approved
   * catalogue, while the entered address text is preserved separately. */
  geoCountry: string;
  geoState: string;
  geoCity: string;
  addressDetails: string;
  leadSource: string;
  campaign: string;
  sourceDetails: string;
  collectConsent: boolean;
  consent: ConsentFields;
}

type BulkUploadStatus =
  | 'idle'
  | 'invalid'
  | 'parsing'
  | 'ready'
  | 'importing'
  | 'imported'
  | 'error';

interface BulkUploadState {
  status: BulkUploadStatus;
  fileName: string;
  fileSizeLabel: string;
  errorMessage: string;
  totalRecords: number;
  validRecords: number;
  invalidRecords: number;
}

interface ScreenData {
  readonly screen: {
    readonly viewId: string;
    readonly title: string;
    readonly route: string;
    readonly purpose: string;
    readonly primaryAction: string;
    readonly viewPermission: string;
    readonly primaryUsers: readonly string[];
    readonly scope: string;
    readonly lastRefresh: string;
  };
  readonly permissions: Record<string, boolean>;
  readonly draftReference: string;
  readonly status: string;
  readonly fields: LeadCaptureData['fields'];
  readonly duplicateCandidates: readonly string[];
  readonly campaignOptions: readonly { reference: string; label: string; context: string }[];
  readonly languages: readonly string[];
  readonly consentStates: readonly string[];
  readonly savedFilters: readonly string[];
  readonly fieldContracts: readonly {
    label: string;
    control: string;
    required: boolean;
    visibility: string;
  }[];
  readonly actions: readonly {
    id: string;
    label: string;
    placement: string;
    permission: string;
    allowedState: string;
    result: string;
    requiresReason?: boolean;
    typedConfirm?: boolean;
  }[];
}

/**
 * SCR-DON-002 — Lead capture.
 * Create a minimum-data lead with source evidence, multi-mobile contact
 * capture, an immutable email, togglable consent, and CSV/XLSX bulk import.
 */
@Component({
  selector: 'app-lead-capture',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './lead-capture.html',
  styleUrl: './lead-capture.css',
})
export class LeadCaptureComponent {
  private readonly router = inject(Router);
  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);

  /**
   * The screen's own chrome.
   *
   * A SIGNAL, NOT A CONSTANT FROM A JSON FILE. `lead-capture.json` supplied the title, the
   * campaign list, the languages and - most consequentially - a `permissions` map that said what
   * this caller could do. A file compiled into the bundle has one answer for everybody, so the
   * Submit button was drawn for an APPROVER who holds no `don.lead-capture.submit` and would be
   * refused by the endpoint.
   */
  protected readonly screen = signal({
    viewId: 'SCR-DON-002',
    title: 'Lead capture',
    route: '/app/fundraising/relationships/lead-capture',
    purpose: 'Capture a new lead and its consent, then send it to the Lead Queue.',
    scope: '',
    lastRefresh: '',
  });

  protected readonly permissions = signal<Record<string, boolean>>({
    view: false,
    save: false,
    submit: false,
    deduplicate: false,
  });

  /** The current privacy-notice version, recorded against any consent captured here. */
  protected readonly currentNoticeVersion = signal('');

  /** Granted / Withdrawn / Not provided, as the API's catalogue lists them. */
  protected readonly consentStateOptions = signal<readonly string[]>([]);

  /** The saved lead, once there is one. Its id and version drive Update and Submit. */
  protected readonly savedLeadId = signal<string | null>(null);
  protected readonly savedLeadVersion = signal<number>(0);
  protected readonly savedLeadReference = signal<string>('');

  // ---------------------------------------------------------------------
  // Static option sets
  // ---------------------------------------------------------------------
  /**
   * Filled from the API's `languageOptions`. Empty until it answers.
   *
   * VALUE AND LABEL, NOT LABEL ALONE. This held only the labels, and the select therefore used
   * the label as its option value - so the form posted `preferredLanguage: "English (India)"`
   * while `SupportedLanguages.IsSupported` on the API compares against the codes ("en-IN").
   * Every save failed the moment a language was chosen, and a language HAD to be chosen because
   * this screen makes the field required.
   */
  protected readonly languageOptions = signal<readonly DonLookupItem[]>([]);

  protected readonly leadSourceOptions: readonly string[] = [
    'Website',
    'Campaign',
    'Event',
    'Referral',
    'Bulk Upload',
    'Walk-In',
    'Partner NGO',
  ];

  /**
   * Campaign dropdown — the seed campaign names from lead-capture.json PLUS
   * every live campaign in the shared CampaignStoreService (including ones
   * just created in the Campaign wizard/register). Cancelled and Closed store
   * campaigns are hidden; entries are de-duplicated by name so a seed option
   * and a store campaign sharing a name appear only once.
   */
  /**
   * The campaigns a lead may be captured against.
   *
   * THE API'S LIST, WHICH IS ALREADY SCOPED. The old version concatenated a seed array from the
   * JSON file with the campaign store's records and de-duplicated by lower-cased name, so a
   * campaign belonging to another organisation could appear if the two happened to share a name.
   */
  protected readonly campaignDropdownOptions = signal<
    readonly { reference: string; label: string; context: string }[]
  >([]);

  protected readonly recognitionOptions: readonly ('Anonymous' | 'Recognised')[] = [
    'Anonymous',
    'Recognised',
  ];

  /** Consent & Preference Centre — effective approved channel catalogue. */
  protected readonly channelOptions: readonly { reference: string; label: string }[] = [
    { reference: 'CHN-EMAIL', label: 'Email' },
    { reference: 'CHN-SMS', label: 'SMS' },
    { reference: 'CHN-WHATSAPP', label: 'WhatsApp' },
    { reference: 'CHN-PHONE', label: 'Phone call' },
  ];

  /** Approved administrative geography — country and state catalogues. */
  protected readonly countryOptions: readonly string[] = ['India'];
  protected readonly stateOptions: readonly string[] = [
    'Tamil Nadu',
    'Karnataka',
    'Kerala',
    'Andhra Pradesh',
    'Telangana',
    'Puducherry',
    'Maharashtra',
    'Delhi',
    'Gujarat',
    'West Bengal',
  ];
  /** Approved cities per state; states without an entry have none approved yet. */
  private readonly APPROVED_CITY_CATALOG: Readonly<Record<string, readonly string[]>> = {
    'Tamil Nadu': ['Chennai', 'Coimbatore', 'Madurai', 'Tiruchirappalli', 'Salem'],
    'Karnataka': ['Bengaluru', 'Mysuru', 'Mangaluru'],
    'Kerala': ['Kochi', 'Thiruvananthapuram', 'Kozhikode'],
    'Andhra Pradesh': ['Visakhapatnam', 'Vijayawada'],
    'Telangana': ['Hyderabad', 'Warangal'],
    'Puducherry': ['Puducherry'],
  };

  private readonly MAX_MOBILE_ENTRIES = 5;
  private readonly MAX_BULK_FILE_SIZE = 10 * 1024 * 1024; // 10 MB
  private readonly ALLOWED_BULK_EXTENSIONS = ['.csv', '.xlsx'];

  /**
   * The bulk-upload template: its columns, and one filled-in row.
   *
   * THE HEADERS ARE THE ONES THE PARSER LOOKS FOR. `columnOf` matches on the name with spaces
   * and punctuation stripped, so "First name" finds `firstname` — which means these labels and
   * that matcher have to be changed together, and are next to each other for that reason.
   *
   * ONE SAMPLE ROW, NOT THREE. It exists to show the shape of each column — a mobile number with
   * no spaces, a campaign named the way the campaign list names it. More rows only mean more to
   * delete before the person types their own, and a row left behind by accident is a lead
   * nobody meant to create.
   */
  private readonly BULK_TEMPLATE_HEADERS: readonly string[] = [
    'First name',
    'Last name',
    'Mobile',
    'Email',
    'Preferred language',
    'City',
    'Campaign',
    'Source',
    'Notes',
  ];

  private readonly BULK_TEMPLATE_SAMPLE: readonly string[] = [
    'Anita',
    'Raman',
    '9876543210',
    'anita.raman@example.com',
    'Tamil',
    'Chennai',
    'CMP-2026-001',
    'Event',
    'Met at the Chennai donor meet. Asked to be called after 6pm.',
  ];
  private readonly MAX_EVIDENCE_FILE_SIZE = 5 * 1024 * 1024; // 5 MB
  private readonly ALLOWED_EVIDENCE_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png'];

  private readonly NAME_PATTERN = /^[A-Za-z\s]+$/;

  /**
   * E.164, and it has to be E.164 BECAUSE THAT IS WHAT THE SERVER ACCEPTS.
   *
   * This used to be /^[0-9]{10,15}$/ - bare digits, no plus - while `PrimaryPhoneValue` on the
   * API side matches ^\+[1-9]\d{7,14}$. The two rules had no overlap at all, so the form was
   * unsubmittable in both directions: typing the placeholder's own "+91 98765 43210" failed the
   * browser check, and typing "9876543210" passed it and then came back as a 400 from the
   * validation filter. That 400 is the "Review the highlighted fields before continuing."
   * message, which is why the screen could complain without marking anything.
   */
  private readonly MOBILE_PATTERN = /^\+[1-9]\d{7,14}$/;

  /** What a person is allowed to TYPE, before normalisation - digits, spaces, dashes, brackets. */
  private readonly MOBILE_INPUT_PATTERN = /^\+?[0-9][0-9\s()-]{6,20}$/;

  /**
   * Bare local numbers get the default prefix rather than a rejection.
   *
   * The rest of this screen already assumes +91 (see `maskedContactRestrictionPhone`), as does
   * the country-code default on Create user, so a ten-digit number typed without a prefix is
   * completed rather than refused. Anything typed WITH a prefix is left exactly as typed.
   */
  private readonly DEFAULT_DIALLING_CODE = '+91';

  /** Matches `EmailValue` on the API, which requires a top-level domain of two or more. */
  private readonly EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

  /**
   * The single definition of "the number as the API will store it".
   *
   * Mirrors `PrimaryPhoneValue.Normalise` on the server: separators go, the prefix stays. Both
   * the validator and `buildLeadRequest` run through here so the value that was checked is the
   * value that is sent.
   */
  private normaliseMobile(raw: string): string {
    const compact = raw.replace(/[\s()-]/g, '').trim();

    if (!compact) {
      return '';
    }

    return compact.startsWith('+') ? compact : `${this.DEFAULT_DIALLING_CODE}${compact}`;
  }

  // ---------------------------------------------------------------------
  // Page / UI state
  // ---------------------------------------------------------------------
  protected readonly uiState = signal<UiState>('ready');
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly activeActionId = signal('');

  // ---------------------------------------------------------------------
  // Form state
  // ---------------------------------------------------------------------
  protected readonly fields = signal<LeadCaptureFormFields>(this.createInitialFields());
  protected readonly errors = signal<Record<string, string>>({});

  /** Once true, the Email field becomes permanently read-only. */
  protected readonly emailLocked = signal(false);
  protected readonly isSubmitted = signal(false);

  protected readonly isFormValid = computed(() => Object.keys(this.validate()).length === 0);

  /** Consent state recorded at the last save/submit — used to detect an in-flight correction. */
  protected readonly consentStateAtLastSave = signal('');

  // ---------------------------------------------------------------------
  // Geography — approved-catalogue lookups
  // ---------------------------------------------------------------------
  protected readonly approvedCities = computed(
    () => this.APPROVED_CITY_CATALOG[this.fields().geoState] ?? [],
  );

  /** Serviceability is verified against the approved city list, separately from the free-text address. */
  protected readonly serviceability = computed<'serviceable' | 'unconfirmed' | null>(() => {
    const city = this.fields().geoCity;
    if (!city) {
      return null;
    }
    return this.approvedCities().includes(city) ? 'serviceable' : 'unconfirmed';
  });

  // ---------------------------------------------------------------------
  // Consent & Preference Centre — conditional-field visibility
  // ---------------------------------------------------------------------
  protected readonly showExpiry = computed(() => this.fields().consent.consentState === 'Granted');

  protected readonly showContactRestriction = computed(() => {
    const c = this.fields().consent;
    return c.doNotContact || c.consentState === 'Withdrawn';
  });

  protected readonly showCorrectionReason = computed(() => {
    const last = this.consentStateAtLastSave();
    return !!last && this.fields().consent.consentState !== last;
  });

  /** Masked review value shown before submission; full number stays restricted elsewhere. */
  protected readonly maskedContactRestrictionPhone = computed(() => {
    const digits = this.fields().consent.contactRestrictionPhone.replace(/\D/g, '');
    const local = digits.length > 10 ? digits.slice(-10) : digits;
    if (local.length < 10) {
      return '—';
    }
    return `+91 ${local.slice(0, 2)}${'•'.repeat(5)}${local.slice(-3)}`;
  });

  // ---------------------------------------------------------------------
  // Bulk upload state
  // ---------------------------------------------------------------------
  protected readonly isBulkUploadOpen = signal(false);
  protected readonly bulkUpload = signal<BulkUploadState>(this.createInitialBulkUpload());

  /** Set when the panel is opened on top of a part-typed form, so the person picks one. */
  protected readonly bulkBlockedByForm = signal(false);

  // ---------------------------------------------------------------------
  // ONE SCREEN, TWO WAYS IN — AND THEY MUST NOT RUN AT ONCE
  //
  // "Submit lead" at the bottom of the page submits the ONE lead typed into the form. It has
  // never had anything to do with the uploaded file: the file is sent by its own button inside
  // the bulk panel. Nothing said so, though, and the two sat on the same page with a single
  // obvious-looking submit button at the end of it — so an Initiator who typed a lead AND
  // attached a file could reasonably press Submit expecting both, and get one lead and a
  // silently discarded file. Pressing both buttons instead created the typed lead a second time,
  // as a duplicate of a row already in the sheet.
  //
  // The fix is to make the screen hold one mode at a time. Staging a file locks the form;
  // starting the form makes the panel ask which one is meant. Each path keeps its own button,
  // and each button now says what it submits.
  // ---------------------------------------------------------------------

  /** A file is attached and not yet imported: the form is not the thing being submitted. */
  protected readonly isBulkStaged = computed(() =>
    ['parsing', 'ready', 'importing'].includes(this.bulkUpload().status),
  );

  /**
   * Whether anything has been typed into the individual form.
   *
   * Country is left out on purpose — it is pre-filled with India, so counting it would make an
   * untouched form look started and put the bulk panel behind a prompt every time.
   */
  protected readonly hasFormInput = computed(() => {
    const f = this.fields();

    return (
      !!this.savedLeadId()
      || [
        f.firstName,
        f.lastName,
        f.displayName,
        f.email,
        f.preferredLanguage,
        f.geoState,
        f.geoCity,
        f.addressDetails,
        f.leadSource,
        f.campaign,
        f.sourceDetails,
      ].some((value) => value.trim().length > 0)
      || f.mobiles.some((mobile) => mobile.value.trim().length > 0)
      || f.collectConsent
    );
  });

  // =======================================================================
  // Initialisation helpers
  // =======================================================================
  private createEmptyConsent(): ConsentFields {
    return {
      emailConsent: false,
      smsConsent: false,
      whatsappConsent: false,
      phoneConsent: false,
      doNotContact: false,
      recognitionPreference: '',
      consentNotes: '',
      purpose: '',
      channelRef: '',
      consentState: '',
      evidenceFileName: '',
      evidenceStatus: 'idle',
      effectiveDate: '',
      effectiveTime: '',
      expiryDate: '',
      expiryTime: '',
      contactRestrictionPhone: '',
      correctionReason: '',
    };
  }

  private generateId(): string {
    return `m_${Math.random().toString(36).slice(2, 10)}`;
  }

  private createEmptyMobile(isPrimary: boolean): MobileNumberEntry {
    return { id: this.generateId(), value: '', isPrimary };
  }

  /**
   * A blank form.
   *
   * IT USED TO ARRIVE PRE-FILLED. `lead-capture.json` carried a `fields` block with a first name,
   * a last name and a location, so every person opening Create Lead was greeted by somebody
   * else's half-typed details and had to clear them before typing their own.
   */
  private createInitialFields(): LeadCaptureFormFields {
    return {
      firstName: '',
      lastName: '',
      displayName: '',
      mobiles: [this.createEmptyMobile(true)],
      email: '',
      preferredLanguage: '',
      geoCountry: 'India',
      geoState: '',
      geoCity: '',
      addressDetails: '',
      leadSource: '',
      campaign: '',
      sourceDetails: '',
      collectConsent: false,
      consent: this.createEmptyConsent(),
    };
  }

  private createInitialBulkUpload(): BulkUploadState {
    return {
      status: 'idle',
      fileName: '',
      fileSizeLabel: '',
      errorMessage: '',
      totalRecords: 0,
      validRecords: 0,
      invalidRecords: 0,
    };
  }

  // =======================================================================
  // Banner / page state helpers
  // =======================================================================
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  // =======================================================================
  // Generic field helpers
  // =======================================================================
  protected updateField<K extends keyof LeadCaptureFormFields>(
    key: K,
    value: LeadCaptureFormFields[K],
  ): void {
    this.fields.update((f) => ({ ...f, [key]: value }));
    this.clearError(key as string);
  }

  protected onTextInput(key: keyof LeadCaptureFormFields, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
    this.updateField(key, target.value as LeadCaptureFormFields[typeof key]);
  }

  protected clearError(key: string): void {
    if (!(key in this.errors())) {
      return;
    }
    this.errors.update((e) => {
      const next = { ...e };
      delete next[key];
      return next;
    });
  }

  // =======================================================================
  // Mobile numbers (multiple)
  // =======================================================================
  protected trackByMobileId(_: number, item: MobileNumberEntry): string {
    return item.id;
  }

  protected addMobile(): void {
    if (this.fields().mobiles.length >= this.MAX_MOBILE_ENTRIES) {
      return;
    }
    this.fields.update((f) => ({ ...f, mobiles: [...f.mobiles, this.createEmptyMobile(false)] }));
  }

  protected removeMobile(id: string): void {
    this.fields.update((f) => {
      if (f.mobiles.length <= 1) {
        return f;
      }
      const filtered = f.mobiles.filter((m) => m.id !== id);
      if (filtered.length && !filtered.some((m) => m.isPrimary)) {
        filtered[0] = { ...filtered[0], isPrimary: true };
      }
      return { ...f, mobiles: filtered };
    });
    this.clearError('mobiles');
  }

  protected updateMobileValue(id: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.fields.update((f) => ({
      ...f,
      mobiles: f.mobiles.map((m) => (m.id === id ? { ...m, value } : m)),
    }));
    this.clearError('mobiles');
  }

  protected setPrimaryMobile(id: string): void {
    this.fields.update((f) => ({
      ...f,
      mobiles: f.mobiles.map((m) => ({ ...m, isPrimary: m.id === id })),
    }));
  }

  // =======================================================================
  // Geography (country / state / city — approved administrative catalogue)
  // =======================================================================
  protected onGeoCountryChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.updateField('geoCountry', value);
  }

  protected onGeoStateChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    // Changing state invalidates any previously selected city outside its approved list.
    this.fields.update((f) => ({ ...f, geoState: value, geoCity: '' }));
    this.clearError('geoState');
    this.clearError('geoCity');
  }

  protected onGeoCityChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.updateField('geoCity', value);
  }

  // =======================================================================
  // Consent
  // =======================================================================
  protected toggleConsentCollection(): void {
    const wasOn = this.fields().collectConsent;
    this.fields.update((f) => ({
      ...f,
      collectConsent: !f.collectConsent,
      // Turning consent OFF clears any previously captured consent values,
      // since consent is not collected while the toggle is off.
      consent: f.collectConsent ? this.createEmptyConsent() : f.consent,
    }));
    if (wasOn) {
      this.consentStateAtLastSave.set('');
    }
    [
      'recognitionPreference',
      'consentNotes',
      'purpose',
      'channelRef',
      'consentState',
      'evidence',
      'effective',
      'expiry',
      'contactRestrictionPhone',
      'correctionReason',
    ].forEach((key) => this.clearError(key));
  }

  protected updateConsent<K extends keyof ConsentFields>(key: K, value: ConsentFields[K]): void {
    this.fields.update((f) => ({ ...f, consent: { ...f.consent, [key]: value } }));
  }

  protected onConsentCheckbox(key: keyof ConsentFields, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.updateConsent(key, checked as ConsentFields[typeof key]);

    // Ticking any channel answers the "choose at least one channel" rule, so the message goes
    // as soon as it is satisfied rather than surviving until the next submit.
    if (key === 'emailConsent' || key === 'smsConsent' || key === 'whatsappConsent' || key === 'phoneConsent') {
      this.clearError('consentChannels');
    }
  }

  protected onRecognitionChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as ConsentFields['recognitionPreference'];
    this.updateConsent('recognitionPreference', value);
    this.clearError('recognitionPreference');
  }

  protected onConsentNotesInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.updateConsent('consentNotes', value);
    this.clearError('consentNotes');
  }

  protected onConsentPurposeInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.updateConsent('purpose', value);
    this.clearError('purpose');
  }

  protected onConsentChannelChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.updateConsent('channelRef', value);
    this.clearError('channelRef');
  }

  protected onConsentStateChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.updateConsent('consentState', value);
    this.clearError('consentState');
    this.clearError('expiry');
    this.clearError('contactRestrictionPhone');
  }

  protected onConsentDateTimeInput(
    key: 'effectiveDate' | 'effectiveTime' | 'expiryDate' | 'expiryTime',
    event: Event,
  ): void {
    const value = (event.target as HTMLInputElement).value;
    this.updateConsent(key, value);
    this.clearError(key === 'expiryDate' || key === 'expiryTime' ? 'expiry' : 'effective');
  }

  protected onConsentContactRestrictionInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.updateConsent('contactRestrictionPhone', value);
    this.clearError('contactRestrictionPhone');
  }

  protected onConsentCorrectionReasonInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.updateConsent('correctionReason', value);
    this.clearError('correctionReason');
  }

  /** Secure evidence uploader — validates type/size, then reflects the scan → classify → link pipeline. */
  protected onEvidenceFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files && input.files[0];
    if (!file) {
      return;
    }

    const name = file.name.toLowerCase();
    const hasValidExtension = this.ALLOWED_EVIDENCE_EXTENSIONS.some((ext) => name.endsWith(ext));
    if (!hasValidExtension) {
      this.errors.update((e) => ({ ...e, evidence: 'Only PDF, JPG or PNG files are supported.' }));
      input.value = '';
      return;
    }
    if (file.size > this.MAX_EVIDENCE_FILE_SIZE) {
      this.errors.update((e) => ({ ...e, evidence: 'File exceeds the 5 MB size limit.' }));
      input.value = '';
      return;
    }

    this.clearError('evidence');
    this.updateConsent('evidenceFileName', file.name);
    this.updateConsent('evidenceStatus', 'uploading');

    // Scan → classify → link happen server-side; the UI reflects each stage
    // as it completes so status is visible before submission.
    window.setTimeout(() => this.updateConsent('evidenceStatus', 'scanned'), 500);
    window.setTimeout(() => this.updateConsent('evidenceStatus', 'classified'), 1000);
    window.setTimeout(() => this.updateConsent('evidenceStatus', 'linked'), 1500);
  }

  // =======================================================================
  // Validation
  // =======================================================================
  private validate(): Record<string, string> {
    const f = this.fields();
    const errs: Record<string, string> = {};

    // First name
    const firstName = f.firstName.trim();
    if (!firstName) {
      errs['firstName'] = 'First name is required.';
    } else if (firstName.length < 2 || firstName.length > 100) {
      errs['firstName'] = 'First name must be between 2 and 100 characters.';
    } else if (!this.NAME_PATTERN.test(firstName)) {
      errs['firstName'] = 'First name can only contain letters and spaces.';
    }

    // Last name
    const lastName = f.lastName.trim();
    if (!lastName) {
      errs['lastName'] = 'Last name is required.';
    } else if (lastName.length > 100) {
      errs['lastName'] = 'Last name cannot exceed 100 characters.';
    } else if (!this.NAME_PATTERN.test(lastName)) {
      errs['lastName'] = 'Last name can only contain letters and spaces.';
    }

    // Display name
    const displayName = f.displayName.trim();
    if (!displayName) {
      errs['displayName'] = 'Display name is required.';
    } else if (displayName.length > 150) {
      errs['displayName'] = 'Display name cannot exceed 150 characters.';
    }

    // Mobile numbers (at least one, all populated ones must be valid + unique)
    //
    // CHECKED IN THE NORMALISED FORM, which is the form that gets sent. Duplicate detection runs
    // on it too, so "+91 98765 43210" and "9876543210" are correctly seen as the same number
    // instead of being saved twice.
    const populatedMobiles = f.mobiles.filter((m) => m.value.trim().length > 0);
    if (populatedMobiles.length === 0) {
      errs['mobiles'] = 'At least one mobile number is required.';
    } else {
      const invalidEntry = populatedMobiles.find(
        (m) =>
          !this.MOBILE_INPUT_PATTERN.test(m.value.trim())
          || !this.MOBILE_PATTERN.test(this.normaliseMobile(m.value)),
      );
      if (invalidEntry) {
        errs['mobiles'] =
          'Enter the number in international format, for example +91 98765 43210. '
          + 'A 10-digit number without a prefix is treated as +91.';
      } else {
        const seen = new Set<string>();
        let hasDuplicate = false;
        for (const m of populatedMobiles) {
          const v = this.normaliseMobile(m.value);
          if (seen.has(v)) {
            hasDuplicate = true;
            break;
          }
          seen.add(v);
        }
        if (hasDuplicate) {
          errs['mobiles'] = 'Duplicate mobile numbers are not allowed.';
        }
      }
    }

    // Email — mandatory and format-checked; immutability is enforced via emailLocked()
    const email = f.email.trim();
    if (!email) {
      errs['email'] = 'Email is required.';
    } else if (!this.EMAIL_PATTERN.test(email)) {
      errs['email'] = 'Enter a valid email address.';
    }

    // Preferred language
    if (!f.preferredLanguage) {
      errs['preferredLanguage'] = 'Preferred language is required.';
    }

    // Geography — required, constrained to the approved administrative catalogue.
    // The free-text address is preserved separately and never validated against the catalogue.
    if (!f.geoCountry) {
      errs['geoCountry'] = 'Country is required.';
    }
    if (!f.geoState) {
      errs['geoState'] = 'State is required.';
    }
    if (!f.geoCity) {
      errs['geoCity'] = 'City is required.';
    } else if (!this.approvedCities().includes(f.geoCity)) {
      errs['geoCity'] = 'Select a city from the approved list.';
    }

    // Lead source
    if (!f.leadSource) {
      errs['leadSource'] = 'Lead source is required.';
    }

    // Source details
    //
    // OPTIONAL, BUT NOT OPTIONALLY SHORT. It is sent as `notes`, and the API's rule is
    // .Length(10, 2000) applied only when the value is non-empty - so leaving it blank is fine
    // and typing "Booth" is a 400. The rule is stated here rather than discovered on submit.
    const sourceDetails = f.sourceDetails.trim();
    if (sourceDetails.length > 0 && sourceDetails.length < 10) {
      errs['sourceDetails'] = 'Use at least 10 characters, or leave this blank.';
    } else if (f.sourceDetails.length > 500) {
      errs['sourceDetails'] = 'Source details cannot exceed 500 characters.';
    }

    // Consent (only validated while the toggle is ON)
    if (f.collectConsent) {
      if (!f.consent.recognitionPreference) {
        errs['recognitionPreference'] = 'Select a recognition preference.';
      }
      if (f.consent.consentNotes.length > 2000) {
        errs['consentNotes'] = 'Consent notes cannot exceed 2000 characters.';
      }

      // Purpose — must contain meaningful text, 10–2000 characters.
      const purpose = f.consent.purpose.trim();
      if (!purpose) {
        errs['purpose'] = 'Purpose is required.';
      } else if (purpose.length < 10 || purpose.length > 2000) {
        errs['purpose'] = 'Purpose must be between 10 and 2000 characters.';
      }

      // Channel — effective approved catalogue only.
      if (!f.consent.channelRef) {
        errs['channelRef'] = 'Select a channel.';
      }

      // THE CHECKBOXES, NOT THE DROPDOWN, ARE WHAT THE API READS. `channelRef` is a single
      // select that is never sent; the request carries the four booleans, and
      // LeadConsentRequestValidator refuses a consent block with none of them set. This screen
      // validated the dropdown and ignored the checkboxes, so ticking nothing produced a 400
      // whose message ("Choose at least one channel...") arrived keyed to `consent.emailConsent`
      // and was then discarded. Checked here, in the same words the server uses.
      if (
        !f.consent.emailConsent
        && !f.consent.smsConsent
        && !f.consent.whatsappConsent
        && !f.consent.phoneConsent
      ) {
        errs['consentChannels'] = 'Choose at least one channel, or turn Collect consent off.';
      }

      // Consent state — current catalogue values; Granted depends on attached evidence.
      if (!f.consent.consentState) {
        errs['consentState'] = 'Select a consent state.';
      } else if (f.consent.consentState === 'Granted' && !f.consent.evidenceFileName) {
        errs['consentState'] = 'Granted consent requires attached evidence.';
      }

      // Effective time — required; interpreted value is shown before submission.
      if (!f.consent.effectiveDate) {
        errs['effective'] = 'Effective date is required.';
      }

      // Expiry time — required only when the selected consent state requires it.
      if (this.showExpiry()) {
        if (!f.consent.expiryDate) {
          errs['expiry'] = 'Expiry date is required for granted consent.';
        } else if (f.consent.effectiveDate) {
          const effective = new Date(`${f.consent.effectiveDate}T${f.consent.effectiveTime || '00:00'}`);
          const expiry = new Date(`${f.consent.expiryDate}T${f.consent.expiryTime || '00:00'}`);
          if (expiry.getTime() <= effective.getTime()) {
            errs['expiry'] = 'Expiry must be after the effective time.';
          }
        }
      }

      // Contact restrictions — required only when the channel/consent/policy requires them.
      if (this.showContactRestriction()) {
        const phone = f.consent.contactRestrictionPhone.trim();
        if (!phone) {
          errs['contactRestrictionPhone'] = 'A contact restriction number is required.';
        } else if (
          !this.MOBILE_INPUT_PATTERN.test(phone)
          || !this.MOBILE_PATTERN.test(this.normaliseMobile(phone))
        ) {
          errs['contactRestrictionPhone'] = 'Enter a valid number in international format, for example +91 98765 43210.';
        }
      }

      // Correction reason — required only when amending a previously saved consent state.
      if (this.showCorrectionReason()) {
        const reason = f.consent.correctionReason.trim();
        if (!reason) {
          errs['correctionReason'] = 'Explain why this consent record is being corrected.';
        } else if (reason.length < 10 || reason.length > 2000) {
          errs['correctionReason'] = 'Correction reason must be between 10 and 2000 characters.';
        }
      }
    }

    return errs;
  }

  /**
   * Server field paths translated into this form's error keys.
   *
   * The API answers a failed save with one row per bad field - `mobileNumber`,
   * `consent.purpose` - and this screen threw every one of them away: both error handlers
   * called `apiErrorMessage` and nothing called `apiFieldErrors`. That is why the banner could
   * say "Check the highlighted fields" with nothing highlighted anywhere on the page: the
   * sentence naming the problem existed, and the screen dropped it one function before display.
   *
   * The names differ on purpose - the form groups five mobile inputs under `mobiles` and calls
   * the API's `notes` "Source details" - so the mapping is explicit rather than assumed.
   */
  private static readonly SERVER_FIELD_MAP: Readonly<Record<string, string>> = {
    firstName: 'firstName',
    lastName: 'lastName',
    mobileNumber: 'mobiles',
    emailAddress: 'email',
    preferredLanguage: 'preferredLanguage',
    city: 'geoCity',
    geographyCode: 'geoState',
    campaignId: 'campaign',
    source: 'leadSource',
    notes: 'sourceDetails',
    'consent.purpose': 'purpose',
    'consent.consentSource': 'leadSource',
    'consent.consentNotes': 'consentNotes',
    'consent.consentEvidenceReference': 'evidence',
    'consent.emailConsent': 'consentChannels',
  };

  /**
   * Anything the map does not cover, so a new server rule cannot go silent.
   *
   * A rule added on the API that this form has no control for would otherwise reproduce the
   * exact bug above. It is shown in the banner instead of being lost.
   */
  protected readonly unmappedServerErrors = signal<readonly string[]>([]);

  /** Puts a rejected save's field errors onto the controls that produced them. */
  private applyServerErrors(error: unknown): void {
    const fieldErrors = apiFieldErrors(error);
    const mapped: Record<string, string> = {};
    const unmapped: string[] = [];

    for (const [path, message] of Object.entries(fieldErrors)) {
      const key = LeadCaptureComponent.SERVER_FIELD_MAP[path];

      if (key) {
        mapped[key] = message;
      } else {
        unmapped.push(message);
      }
    }

    this.unmappedServerErrors.set(unmapped);

    if (Object.keys(mapped).length > 0) {
      this.errors.update((existing) => ({ ...existing, ...mapped }));
    }
  }

  // =======================================================================
  // Primary actions
  // =======================================================================
  /**
   * Builds the API's create body from the form.
   *
   * THE CAMPAIGN GOES AS AN ID. The form's `campaign` field holds the reference a person picked
   * from the dropdown; the API's `campaignId` is a Guid, and sending the label returns a 400
   * before the handler runs.
   */
  private buildLeadRequest(): CreateLeadRequest | null {
    const form = this.fields();
    const campaign = this.campaignDropdownOptions().find(
      (option) => option.reference === form.campaign || option.label === form.campaign,
    );

    if (!campaign) {
      return null;
    }

    const primaryMobile =
      form.mobiles.find((mobile) => mobile.isPrimary)?.value ?? form.mobiles[0]?.value ?? '';

    return {
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim() || null,
      // E.164 OR NOTHING. `PrimaryPhoneValue` is the server's rule and it does not guess.
      mobileNumber: this.normaliseMobile(primaryMobile) || null,
      emailAddress: form.email.trim() || null,
      preferredLanguage: form.preferredLanguage || null,
      city: form.geoCity || null,
      geographyCode: form.geoState || null,
      campaignId: this.campaignIdByReference.get(campaign.reference) ?? campaign.reference,
      source: form.leadSource || 'Manual',
      notes: form.sourceDetails.trim() || null,

      // CONSENT TRAVELS WITH THE CREATE, not after it. The server writes one Consent row per
      // permitted channel, which is what makes the Consent Centre and the follow-up planner's
      // channel check read from a single source rather than from a flag on the lead.
      consent: form.collectConsent
        ? {
            collectConsent: true,
            emailConsent: form.consent.emailConsent,
            smsConsent: form.consent.smsConsent,
            whatsAppConsent: form.consent.whatsappConsent,
            phoneCallConsent: form.consent.phoneConsent,
            consentSource: form.consent.evidenceFileName || form.leadSource || null,
            consentNotes: form.consent.consentNotes.trim() || null,
            purpose: form.consent.purpose || null,
          }
        : null,
    };
  }

  /**
   * Save - creates the lead, or updates the one already created.
   *
   * IT REALLY SAVES NOW. The old version set `emailLocked` and a 'success' banner and called
   * nothing, so a person could fill the form, see "saved", close the tab and lose everything.
   */
  protected saveDraft(): void {
    if (this.rejectWhileBulkStaged()) {
      return;
    }

    const errs = this.validate();
    this.errors.set(errs);
    this.unmappedServerErrors.set([]);
    if (Object.keys(errs).length > 0) {
      this.uiState.set('validation');
      return;
    }

    const request = this.buildLeadRequest();
    if (!request) {
      this.errors.set({ ...errs, campaign: 'Choose a campaign from the list.' });
      this.uiState.set('validation');
      return;
    }

    this.saving.set(true);
    const existingId = this.savedLeadId();

    const call = existingId
      ? this.api.updateLead(existingId, { ...request, expectedVersion: this.savedLeadVersion() })
      : this.api.saveLead(request);

    call.subscribe({
      next: (lead) => {
        this.saving.set(false);
        this.savedLeadId.set(lead.id);
        this.savedLeadVersion.set(lead.version);
        this.savedLeadReference.set(lead.leadReference);

        // THE E-MAIL BECOMES IMMUTABLE ONCE THE RECORD EXISTS, because it is the key the
        // donation flow matches on when deciding whether a payment converts this lead.
        this.emailLocked.set(true);
        if (this.fields().collectConsent) {
          this.consentStateAtLastSave.set(this.fields().consent.consentState);
        }
        this.uiState.set('success');
        this.toast.show('Lead saved', `${lead.leadReference} was saved.`, 'success');
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.applyServerErrors(error);
        this.uiState.set('validation');
        this.toast.show('Lead not saved', apiErrorMessage(error), 'error');
      },
    });
  }

  /**
   * Submit - saves, then promotes the draft into the Lead Queue.
   *
   * TWO CALLS, IN ORDER. `save` creates the record and `submit` moves it out of draft; the
   * document's flow is "enter the details, save the record; the saved lead appears in the Lead
   * Work Queue list", and a draft never appears there.
   */
  protected submitLead(): void {
    if (this.rejectWhileBulkStaged()) {
      return;
    }

    const errs = this.validate();
    this.errors.set(errs);
    this.unmappedServerErrors.set([]);
    if (Object.keys(errs).length > 0) {
      this.uiState.set('validation');
      return;
    }

    const request = this.buildLeadRequest();
    if (!request) {
      this.errors.set({ ...errs, campaign: 'Choose a campaign from the list.' });
      this.uiState.set('validation');
      return;
    }

    this.saving.set(true);
    const existingId = this.savedLeadId();

    const save = existingId
      ? this.api.updateLead(existingId, { ...request, expectedVersion: this.savedLeadVersion() })
      : this.api.saveLead(request);

    save
      .pipe(
        switchMap((lead) => {
          this.savedLeadId.set(lead.id);
          this.savedLeadVersion.set(lead.version);
          this.savedLeadReference.set(lead.leadReference);
          // THE REASON IS THE AUDIT ENTRY. Submitting is what moves a lead out of draft and into
          // somebody's work queue, and the trail should say why rather than just when.
          return this.api
            .submitLead(lead.id, {
              reason: 'Captured and submitted to the Lead Queue from Lead Capture.',
              expectedVersion: lead.version,
            })
            .pipe(
            map(() => lead),
          );
        }),
      )
      .subscribe({
        next: (lead) => {
          this.saving.set(false);
          this.emailLocked.set(true);
          this.isSubmitted.set(true);
          if (this.fields().collectConsent) {
            this.consentStateAtLastSave.set(this.fields().consent.consentState);
          }
          this.uiState.set('success');
          this.toast.show('Lead created', `${lead.leadReference} is now in the Lead Queue.`, 'success');

          // THE API'S ID, NOT A MINTED ONE, so the queue opens the row that was actually saved.
          this.router.navigate(['/app/fundraising/relationships/lead-work-queue'], {
            queryParams: { createdLeadId: lead.id },
          });
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.applyServerErrors(error);
          this.uiState.set('validation');
          this.toast.show('Lead not submitted', apiErrorMessage(error), 'error');
        },
      });
  }

  /**
   * Stops "Submit lead" and "Save draft" from running while a file is staged.
   *
   * THE BUTTONS ARE ALREADY DISABLED in that state — this is the same rule stated where the
   * write happens, because a disabled button is a hint and not a guarantee: the form can also be
   * submitted with the Enter key, and `(ngSubmit)` reaches this method either way.
   */
  private rejectWhileBulkStaged(): boolean {
    if (!this.isBulkStaged()) {
      return false;
    }

    this.toast.show(
      'Finish the upload first',
      'A file is attached. Import it with the button in the bulk upload panel, or remove the file '
      + 'to go back to entering one lead by hand.',
      'warning',
    );

    return true;
  }

  protected readonly saving = signal(false);

  /**
   * The dropdown shows a code; the API wants the id.
   *
   * Both come back together from `getLeadCaptureForm`, so this map is filled once rather than
   * being reconstructed by matching one string against another at save time.
   */
  private readonly campaignIdByReference = new Map<string, string>();

  constructor() {
    this.loadForm();
  }

  /**
   * One call fills the whole form's context.
   *
   * IT ALSO DECIDES WHICH BUTTONS EXIST. `permittedActions` is where the three-role model reaches
   * this screen: an APPROVER holds no `don.lead-capture.save` or `.submit`, so neither button is
   * drawn for them. Nothing here names a role, and the endpoints re-check every code.
   */
  private loadForm(): void {
    this.uiState.set('loading');

    this.api.getLeadCaptureForm().subscribe({
      next: (response: LeadCaptureResponse) => {
        this.campaignIdByReference.clear();
        for (const option of response.campaignOptions) {
          // `description` carries the campaign code; `value` is the Guid.
          this.campaignIdByReference.set(option.description ?? option.value, option.value);
        }

        this.campaignDropdownOptions.set(
          response.campaignOptions.map((option: DonLookupItem) => ({
            reference: option.description ?? option.value,
            label: option.label,
            context: option.description ?? '',
          })),
        );

        this.languageOptions.set(response.languageOptions);
        this.consentStateOptions.set(response.consentStateOptions.map((option) => option.label));
        this.currentNoticeVersion.set(response.currentNoticeVersion);

        // VERBS, AS THE API ANSWERS THEM, AND NOTHING INFERRED FROM THEM. `submit` used to fall
        // back to `Save`, because the endpoint withheld 'Submit' until a draft existed and a
        // blank form would otherwise have drawn no Submit button at all. The endpoint now answers
        // 'Submit' on the caller's permission, so the fallback is gone - and it had to go: an
        // APPROVER holds `don.lead-capture.save` and not `.submit`, so reading Save as Submit
        // drew them a button that 403s.
        const permitted = response.permittedActions ?? [];
        this.permissions.set({
          view: permitted.length > 0,
          save: permitted.includes('Save'),
          submit: permitted.includes('Submit'),
          deduplicate: permitted.includes('Deduplicate'),
        });

        this.screen.update((current) => ({
          ...current,
          scope: response.activeScope,
          lastRefresh: new Date().toLocaleString('en-GB', {
            day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
          }),
        }));

        this.uiState.set('ready');
      },
      error: (error: unknown) => {
        this.uiState.set('dependency-failure');
        this.toast.show('Lead capture unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  protected cancelForm(): void {
    this.router.navigate(['/app/fundraising/relationships/lead-work-queue']);
  }

  // =======================================================================
  // Generic confirm-dialog actions (reserved for permissioned page actions
  // sourced from the screen's action contract, e.g. void/reassign flows).
  // =======================================================================
  /**
   * REMOVED - it was driven by an `actions` array in the JSON file.
   *
   * The dialog rendered whatever label and result text the file listed, and `onConfirm` set the
   * page to 'success' without calling anything. Save and Submit are this screen's only writes and
   * both now go to the API directly.
   */
  protected openAction(_actionId: string): void {
    return;
  }

  protected onConfirm(_reason: string): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.uiState.set('success');
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }

  // =======================================================================
  // Bulk upload (CSV / XLSX only)
  // =======================================================================
  protected openBulkUpload(): void {
    this.isBulkUploadOpen.set(true);

    // ASKED, NOT DECIDED. Either answer is reasonable — the person may have started typing and
    // then remembered they have the sheet, or may have opened the panel by mistake — and the one
    // thing that must not happen is the typed lead being thrown away without being mentioned.
    if (this.hasFormInput()) {
      this.bulkBlockedByForm.set(true);
      return;
    }

    this.bulkBlockedByForm.set(false);
    this.bulkUpload.set(this.createInitialBulkUpload());
  }

  protected closeBulkUpload(): void {
    this.isBulkUploadOpen.set(false);
    this.bulkBlockedByForm.set(false);
    this.bulkUpload.set(this.createInitialBulkUpload());
  }

  /** "Use the file" — the typed form is cleared and the upload takes over. */
  protected discardFormForBulk(): void {
    this.resetForm();
    this.bulkBlockedByForm.set(false);
    this.bulkUpload.set(this.createInitialBulkUpload());
  }

  /** "Keep what I typed" — the panel closes again and the form is untouched. */
  protected keepFormAndCloseBulk(): void {
    this.bulkBlockedByForm.set(false);
    this.isBulkUploadOpen.set(false);
  }

  /** Detaches a staged file, which unlocks the individual form again. */
  protected clearBulkFile(): void {
    this.parsedBulkRows.set([]);
    this.bulkResults.set([]);
    this.bulkUpload.set(this.createInitialBulkUpload());
  }

  /**
   * Empties the form back to a blank capture.
   *
   * A DRAFT ALREADY SAVED IS NOT DELETED — it stays in the Lead Work Queue, which is why the
   * prompt says so. Deleting it silently here would lose work that the person has explicitly
   * saved, and this is a "which of the two am I doing" question, not a delete.
   */
  private resetForm(): void {
    this.fields.set(this.createInitialFields());
    this.errors.set({});
    this.savedLeadId.set(null);
    this.savedLeadVersion.set(0);
    this.savedLeadReference.set('');
    this.consentStateAtLastSave.set('');
    this.emailLocked.set(false);
    this.isSubmitted.set(false);
    this.uiState.set('ready');
  }

  /**
   * Downloads the template as a ZIP holding both formats, each with the same single sample row.
   *
   * IT USED TO BE A LINK TO A FILE THAT WAS NEVER THERE. The button was
   * `<a href="assets/templates/lead-bulk-upload-template.csv" download>`, and no such asset has
   * ever existed in the build — so the dev server answered with the SPA's `index.html`, the
   * browser saved that under the requested name, and the download shelf showed
   * "lead-bulk-upload-template.htm — File wasn't available on site". Anyone who opened it got the
   * application's HTML shell where the columns should have been.
   *
   * BOTH FORMATS IN ONE ARCHIVE, because the uploader accepts either and a person who works in
   * Excel should not have to convert a CSV first — nor should the one who works in a text editor
   * be handed a workbook. They carry identical columns, so whichever is filled in parses the
   * same way.
   *
   * BUILT HERE RATHER THAN SHIPPED AS A STATIC ASSET. The column names have to agree with
   * `columnOf` below or the upload is rejected as "needs a First name column", and a checked-in
   * file drifts from the parser the first time a column is renamed. This one cannot.
   */
  protected downloadBulkTemplate(): void {
    const grid = [[...this.BULK_TEMPLATE_HEADERS], [...this.BULK_TEMPLATE_SAMPLE]];
    const encoder = new TextEncoder();

    const csv = grid
      .map((row) => row.map((value) => '"' + value.replace(/"/g, '""') + '"').join(','))
      .join('\r\n');

    const archive = createZip([
      // A BOM, so Excel opens the CSV as UTF-8 rather than as the local codepage — without it a
      // name with an accent in it arrives mangled and the lead is created under the wrong name.
      { name: 'lead-bulk-upload-template.csv', data: encoder.encode('﻿' + csv) },
      { name: 'lead-bulk-upload-template.xlsx', data: createXlsx(grid, 'Leads') },
    ]);

    const url = URL.createObjectURL(archive);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'lead-bulk-upload-template.zip';
    link.click();
    URL.revokeObjectURL(url);
  }

  protected onBulkFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files && input.files[0];
    if (!file) {
      return;
    }

    const name = file.name.toLowerCase();
    const hasValidExtension = this.ALLOWED_BULK_EXTENSIONS.some((ext) => name.endsWith(ext));
    const sizeLabel = this.formatFileSize(file.size);

    if (!hasValidExtension) {
      this.bulkUpload.set({
        ...this.createInitialBulkUpload(),
        status: 'invalid',
        fileName: file.name,
        fileSizeLabel: sizeLabel,
        errorMessage: 'Only .csv and .xlsx files are supported.',
      });
      input.value = '';
      return;
    }

    if (file.size > this.MAX_BULK_FILE_SIZE) {
      this.bulkUpload.set({
        ...this.createInitialBulkUpload(),
        status: 'invalid',
        fileName: file.name,
        fileSizeLabel: sizeLabel,
        errorMessage: 'File exceeds the 10 MB size limit.',
      });
      input.value = '';
      return;
    }

    this.bulkUpload.set({
      ...this.createInitialBulkUpload(),
      status: 'parsing',
      fileName: file.name,
      fileSizeLabel: sizeLabel,
    });

    if (name.endsWith('.csv')) {
      this.parseCsvPreview(file);
    } else {
      this.parseXlsxPreview(file);
    }
  }

  /**
   * Reads an uploaded workbook.
   *
   * IT USED TO READ NOTHING. The branch was a comment saying ".xlsx workbooks are parsed
   * server-side" followed by `status = 'ready'` — but nothing is parsed server-side, the file
   * itself is never sent, and no rows had been collected. The panel therefore showed
   * "0 Records / 0 Valid / 0 Errors" above a button reading "Import 0 leads", and pressing it
   * returned immediately because `rows.length === 0`. Half the formats the picker advertises did
   * nothing at all, silently.
   *
   * The grid goes through the same column matching as the CSV, so a workbook and a CSV with the
   * same columns import identically.
   */
  private parseXlsxPreview(file: File): void {
    readXlsxRows(file)
      .then((grid) => this.ingestGrid(grid))
      .catch((error: unknown) => {
        this.bulkUpload.update((state) => ({
          ...state,
          status: 'error',
          errorMessage:
            error instanceof Error
              ? error.message
              : 'Could not read that workbook. Try saving it as CSV and uploading that.',
        }));
      });
  }

  /**
   * Parses the file and KEEPS the rows.
   *
   * THE OLD VERSION COUNTED AND THREW THEM AWAY. It reported "197 valid, 3 invalid" from a column
   * count and then discarded everything it had read, so the import step had nothing to send -
   * which is why it was a `setTimeout` rather than a request.
   *
   * A HEADER ROW IS REQUIRED, and the columns are found by name rather than by position. A file
   * whose columns are in a different order is the ordinary case when somebody exports from
   * another system, and matching by position would silently put e-mail addresses in the city
   * column.
   */
  private parseCsvPreview(file: File): void {
    const reader = new FileReader();

    reader.onload = () => {
      const text = String(reader.result ?? '')
        // Excel writes a UTF-8 BOM, and it lands on the first header — "﻿First name" does
        // not match "firstname", so the file was refused for having no First name column.
        .replace(/^﻿/, '');

      // NOT FILTERED HERE. Blank lines are skipped by `ingestGrid`, which still counts them, so
      // an error naming row 58 means row 58 of the spreadsheet the person is looking at.
      this.ingestGrid(text.split(/\r?\n/).map((line) => this.splitCsvLine(line)));
    };

    reader.onerror = () => {
      this.bulkUpload.update((state) => ({
        ...state,
        status: 'error',
        errorMessage: 'Could not read the file. Please try again.',
      }));
    };

    reader.readAsText(file);
  }

  /**
   * Turns a parsed grid — from either format — into rows, and reports the count.
   *
   * A HEADER ROW IS REQUIRED, and the columns are found by name rather than by position. A file
   * whose columns are in a different order is the ordinary case when somebody exports from
   * another system, and matching by position would silently put e-mail addresses in the city
   * column.
   */
  private ingestGrid(grid: readonly (readonly string[])[]): void {
    const isBlank = (row: readonly string[]) =>
      row.every((value) => String(value ?? '').trim().length === 0);

    // The header is the first row with anything in it; a spreadsheet exported with a title or a
    // spacer above the columns is common enough not to reject.
    const headerRow = grid.findIndex((row) => !isBlank(row));
    const body = headerRow < 0 ? [] : grid.slice(headerRow + 1);

    if (headerRow < 0 || body.every(isBlank)) {
      this.bulkUpload.update((state) => ({
        ...state,
        status: 'invalid',
        errorMessage: 'The file has a header row but no data rows.',
      }));
      return;
    }

    const headers = grid[headerRow].map((header) =>
      String(header ?? '').trim().toLowerCase().replace(/[^a-z]/g, ''),
    );

    const columnOf = (...names: string[]): number => {
      for (const name of names) {
        const index = headers.indexOf(name);
        if (index >= 0) {
          return index;
        }
      }
      return -1;
    };

    const firstNameIndex = columnOf('firstname', 'first', 'name', 'leadname');
    if (firstNameIndex < 0) {
      this.bulkUpload.update((state) => ({
        ...state,
        status: 'invalid',
        errorMessage: 'The file needs a "First name" column. Download the template to see the expected columns.',
      }));
      return;
    }

    const lastNameIndex = columnOf('lastname', 'last', 'surname');
    const mobileIndex = columnOf('mobile', 'mobilenumber', 'phone', 'phonenumber');
    const emailIndex = columnOf('email', 'emailaddress', 'mail');
    const languageIndex = columnOf('language', 'preferredlanguage');
    const cityIndex = columnOf('city', 'location');
    const campaignIndex = columnOf('campaign', 'campaignname', 'campaigncode');
    const sourceIndex = columnOf('source', 'leadsource');
    const notesIndex = columnOf('notes', 'note', 'comments');

    const at = (columns: readonly string[], index: number): string | null =>
      index >= 0 && index < columns.length ? String(columns[index] ?? '').trim() || null : null;

    const rows: BulkLeadImportRow[] = [];
    let invalid = 0;

    body.forEach((columns, offset) => {
      if (isBlank(columns)) {
        return;
      }

      const firstName = at(columns, firstNameIndex);
      const mobile = at(columns, mobileIndex);
      const email = at(columns, emailIndex);

      // COUNTED AS INVALID HERE, BUT STILL SENT. The server is the authority on what it will
      // accept, and it names each rejection - showing a count now is only so the person is not
      // surprised by the outcome.
      if (!firstName || (!mobile && !email)) {
        invalid += 1;
      }

      rows.push({
        // 1-based and counted from the top of the FILE, blanks included, so the number in an
        // error report is the row the person can scroll to.
        rowNumber: headerRow + offset + 2,
        firstName,
        lastName: at(columns, lastNameIndex),
        mobileNumber: mobile,
        emailAddress: email,
        preferredLanguage: at(columns, languageIndex),
        city: at(columns, cityIndex),
        campaignNameOrCode: at(columns, campaignIndex),
        source: at(columns, sourceIndex),
        notes: at(columns, notesIndex),
      });
    });

    this.parsedBulkRows.set(rows);
    this.bulkUpload.update((state) => ({
      ...state,
      status: 'ready',
      totalRecords: rows.length,
      validRecords: rows.length - invalid,
      invalidRecords: invalid,
    }));
  }

  /**
   * Splits one CSV line, honouring quoted fields.
   *
   * `line.split(',')` WAS WRONG FOR REAL DATA. A notes column reading "Called, no answer" became
   * two columns, which shifted every field after it - so the e-mail address landed in the city.
   */
  private splitCsvLine(line: string): string[] {
    const values: string[] = [];
    let current = '';
    let inQuotes = false;

    for (let i = 0; i < line.length; i++) {
      const char = line[i];

      if (char === '"') {
        if (inQuotes && line[i + 1] === '"') {
          current += '"';
          i++;
        } else {
          inQuotes = !inQuotes;
        }
      } else if (char === ',' && !inQuotes) {
        values.push(current);
        current = '';
      } else {
        current += char;
      }
    }

    values.push(current);
    return values;
  }

  /** The rows the parser read, held so the import can send them. */
  private readonly parsedBulkRows = signal<readonly BulkLeadImportRow[]>([]);

  /** What the server said about each row, for the result list and the error report. */
  protected readonly bulkResults = signal<readonly BulkLeadImportRowResult[]>([]);

  /**
   * Sends the parsed rows.
   *
   * IT USED TO BE A TIMER. The body was `window.setTimeout(() => status = 'imported', 600)` under
   * the comment "TODO: wire to the bulk-import API", so somebody could upload two hundred leads,
   * read "Imported", and have created none of them.
   */
  protected importBulkUpload(): void {
    const rows = this.parsedBulkRows();
    if (this.bulkUpload().status !== 'ready' || rows.length === 0) {
      return;
    }

    // The form is locked while a file is staged, so this should be unreachable — it is here
    // because "should be unreachable" is not a reason to create a lead twice if it is not.
    if (this.hasFormInput()) {
      this.bulkBlockedByForm.set(true);
      return;
    }

    this.bulkUpload.update((state) => ({ ...state, status: 'importing' }));

    this.api
      .bulkImportLeads({
        rows: [...rows],

        // A row that names no campaign falls back to whichever one is selected on the form, if
        // any. The server rejects a row with neither rather than guessing.
        defaultCampaignId: this.campaignIdByReference.get(this.fields().campaign) ?? null,
        defaultSource: 'Bulk Upload',
      })
      .subscribe({
        next: (result) => {
          this.bulkResults.set(result.results);
          this.bulkUpload.update((state) => ({
            ...state,
            status: 'imported',
            totalRecords: result.submittedCount,
            validRecords: result.importedCount,
            invalidRecords: result.rejectedCount,
            errorMessage: result.rejectedCount > 0 ? result.message : '',
          }));
          this.toast.show(
            'Upload processed',
            result.message,
            result.rejectedCount > 0 ? 'warning' : 'success',
          );
        },
        error: (error: unknown) => {
          this.bulkUpload.update((state) => ({
            ...state,
            status: 'error',
            errorMessage: apiErrorMessage(error),
          }));
          this.toast.show('Upload failed', apiErrorMessage(error), 'error');
        },
      });
  }

  /**
   * The rejected rows, with the reason the server gave for each.
   *
   * THE OLD REPORT WAS FABRICATED. It generated one line per invalid row reading "Column count
   * mismatch or missing primary field" regardless of what was actually wrong, and numbered them
   * 1..n rather than by their row in the file - so it could not be used to fix the spreadsheet.
   */
  protected downloadBulkErrorReport(): void {
    const rejected = this.bulkResults().filter((result) => !result.imported);
    if (rejected.length === 0) {
      return;
    }

    const escape = (value: string) => '"' + value.replace(/"/g, '""') + '"';
    const lines = rejected.map(
      (result) => result.rowNumber + ',' + escape(result.reason ?? 'Rejected.'),
    );

    const blob = new Blob([['Row,Reason', ...lines].join('\n')], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'bulk-upload-error-report.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  private formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  }
}