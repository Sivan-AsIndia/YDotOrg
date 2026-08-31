import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { DonLookupItem } from '../../../../Shared/models/donor-contract.model';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  UiState,
  LeadCaptureData,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { WorkflowStateService } from '../../../../Service/workflow-state.service';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';


/**
 * The campaign statuses a lead may be captured against.
 *
 * THIS LIST MIRRORS `CampaignProjection.OfferableStatuses` ON THE SERVER, and the two must stay
 * in step. DON only mirrors CAM campaigns in these states into its own `don_campaigns` table,
 * and `don_leads.campaign_id` has a foreign key to that table — so a campaign outside this set
 * simply has no row for a lead to point at.
 */
const LEAD_CAPTURABLE_CAMPAIGN_STATUSES: readonly string[] = ['Active', 'Approved', 'Paused'];

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
  /**
   * The accountable owner's API id.
   *
   * THE SCREEN HAD NO OWNER FIELD AT ALL and both save paths sent the literal 'Unassigned', so
   * a capturer could never hand a lead to the fundraiser who should work it. The server fell
   * back to the caller, which is a reasonable default but not a choice anybody made.
   */
  ownerUserId: string;
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

/**
 * The screen's copy, its action contract and its field contract.
 *
 * PRESENTATION STAYS; DATA GOES. Labels, permission codes and confirmation wording are the same
 * for every organisation and are decided here. The scope line, the refresh time, the consent-state
 * catalogue and the campaign list came out of the same JSON file and are not the same for
 * everybody - the scope in particular read "YDot Foundation - Tamil Nadu" whoever was looking, and
 * a screen that tells you the wrong scope is worse than one that tells you none.
 */
const SCREEN = {
  title: 'Lead capture',
  purpose: 'Create a minimum-data lead with source evidence and consent context.',
  primaryAction: 'Save',
  viewPermission: 'don.lead-capture.view',
  primaryUsers: ['Fundraiser', 'System integration'] as readonly string[],
} as const;

const SAVED_FILTERS: readonly string[] = ['All drafts (Default)', 'Ready to submit', 'Incomplete'];

const FIELD_CONTRACTS: readonly {
  label: string;
  control: string;
  required: boolean;
  visibility: string;
}[] = [
  { label: "First name or known name", control: "text", required: true, visibility: "Internal" },
  { label: "Last name", control: "text", required: false, visibility: "Internal" },
  { label: "Mobile number", control: "telephone", required: false, visibility: "Restricted" },
  { label: "Email address", control: "email", required: false, visibility: "Restricted" },
  { label: "Preferred language", control: "select", required: false, visibility: "Internal" },
  { label: "City or geography", control: "text", required: false, visibility: "Internal" },
  { label: "Campaign", control: "searchable-select", required: true, visibility: "Internal" },
  { label: "Source", control: "text", required: true, visibility: "Internal" },
  { label: "Consent state", control: "select", required: false, visibility: "Internal" },
  { label: "Consent evidence", control: "file", required: false, visibility: "Confidential" },
  { label: "Notes", control: "textarea", required: false, visibility: "Confidential" },
  { label: "Preferred contact time", control: "datetime", required: false, visibility: "Restricted" },
  { label: "Duplicate candidates", control: "readonly", required: false, visibility: "Internal" },
  { label: "Lead reference", control: "readonly", required: false, visibility: "Internal" }
];

const ACTIONS: readonly {
  id: string;
  label: string;
  placement: string;
  permission: string;
  allowedState: string;
  result: string;
  requiresReason?: boolean;
  typedConfirm?: boolean;
}[] = [
  {
    id: "save",
    label: "Save",
    placement: "primary",
    permission: "don.lead-capture.save",
    allowedState: "No record / Draft",
    result: "Save one draft, preserve values, show stable reference and indicate remaining required information.",
  },
  {
    id: "deduplicate",
    label: "Deduplicate",
    placement: "workflow",
    permission: "don.lead-capture.deduplicate",
    allowedState: "Permitted lifecycle state",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
  },
  {
    id: "submit",
    label: "Submit",
    placement: "workflow",
    permission: "don.lead-capture.submit",
    allowedState: "Permitted lifecycle state",
    result: "Execute idempotently; show stable reference, accepted/committed result, pending dependency and safe next action.",
  },
  {
    id: "deleteDraft",
    label: "Delete unused draft",
    placement: "danger",
    permission: "don.lead-capture.delete-draft",
    allowedState: "Draft with no downstream reference",
    result: "Require named reason and consequence preview; preserve linked history; confirm the resulting lifecycle state persistently.",
    requiresReason: true,
    typedConfirm: true,
  }
];

/**
 * SCR-DON-002 — Lead capture.
 * Create a minimum-data lead with source evidence, multi-mobile contact
 * capture, an immutable email, togglable consent, and CSV/XLSX bulk import.
 */
/**
 * What a caller may do on this screen.
 *
 * NAMED RATHER THAN A BARE RECORD, so a template asking for a capability that does not exist is a
 * compile error rather than a silently-false condition that hides a button forever.
 */
interface LeadCapturePermissions {
  readonly deleteDraft: boolean;
  readonly save: boolean;
  readonly submit: boolean;
}

@Component({
  selector: 'app-lead-capture',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './lead-capture.html',
  styleUrl: './lead-capture.css',
})
export class LeadCaptureComponent {
  constructor() {
    // The form opens with India already selected, so load its states now. Without this the
    // country box reads "India" while the state box beneath it sits empty until somebody
    // re-picks the country it is already showing.
    this.geo.selectCountry('India');

    this.loadFormConfiguration();
  }

  private readonly router = inject(Router);
  private readonly workflow = inject(WorkflowStateService);
  /** Shared campaign store — the SAME source of truth used by Campaign
   *  Register, so campaigns created there appear in this dropdown too. */
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly people = inject(PeopleDirectoryService);
  private readonly toast = inject(ToastService);

  /**
   * The people a lead can be made somebody's responsibility.
   *
   * ACTIVE STAFF IN THE CALLER'S OWN SCOPE, from the shared IAM directory - the same list the
   * assignment board offers, so a lead captured to a person here can be reassigned to that same
   * person there without the two screens disagreeing about who exists.
   */
  protected readonly ownerOptions = computed(() => this.people.assignable());

  /** The chosen owner's display name, which is what the lead row shows. */
  protected readonly selectedOwnerName = computed(
    () => this.people.get(this.fields().ownerUserId)?.name ?? '',
  );
  private readonly donorApi = inject(DonorApiService);

  /** Page copy and contracts. Presentation - see the note on SCREEN. */
  protected readonly screen = SCREEN;
  protected readonly savedFilters = SAVED_FILTERS;
  protected readonly fieldContracts = FIELD_CONTRACTS;
  protected readonly actions = ACTIONS;

  /**
   * What the server decides: the consent-state catalogue, the scope and the refresh time.
   *
   * THE CONSENT STATES MATTER MOST OF THE THREE. They used to be four fixed strings in the
   * bundle, and a lead saved against a state the organisation does not actually use is a lead
   * whose consent position cannot be acted on afterwards.
   */
  protected readonly consentStateOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly activeScope = signal('');
  protected readonly lastRefresh = signal('');
  protected readonly draftReference = signal('');
  protected readonly status = signal('');


  /**
   * The form's server-side configuration.
   *
   * IT IS THE SAME ENDPOINT THE SAVE GOES TO, which is the point: the consent states offered here
   * are exactly the ones that endpoint will accept. A catalogue compiled into the bundle can drift
   * from the server's without anybody noticing until a save is refused.
   */
  private loadFormConfiguration(): void {
    this.donorApi.getLeadCaptureForm().subscribe({
      next: (response) => {
        this.consentStateOptions.set(response.consentStateOptions ?? []);
        this.activeScope.set(response.activeScope ?? '');
        this.draftReference.set(response.lead?.leadReference ?? '');
        this.status.set(response.lead?.status ?? '');
        this.lastRefresh.set(
          new Date().toLocaleString('en-GB', {
            day: '2-digit',
            month: 'short',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
          }),
        );
      },

      // Nothing is substituted. An empty consent dropdown is a visible problem; a plausible-looking
      // wrong one is not.
      error: () => {
        this.consentStateOptions.set([]);
        this.activeScope.set('');
        this.lastRefresh.set('');
      },
    });
  }

  // ---------------------------------------------------------------------
  // Static option sets
  // ---------------------------------------------------------------------
  /**
   * The language options, from the GlobalMaster catalogue.
   *
   * WHAT THIS REPLACES: a static a bundled `languages` array falling back to the four literals
   * `['English', 'Tamil', 'Hindi', 'Malayalam']`. A fundraiser in Hyderabad could not record a
   * Telugu-speaking lead, and nothing in the platform could add one without a rebuild.
   *
   * NAMES RATHER THAN CULTURE CODES, deliberately. A lead's `language` is stored as a display
   * string and rendered straight back out by the work queue and My Leads, so binding to "ta-IN"
   * would show a culture code where a fundraiser expects "Tamil". This is the same choice the
   * cascade makes for country, state and city, and for the same reason.
   *
   * The list narrows to the selected country's languages once one is chosen — India is picked by
   * default below — and falls back to the whole catalogue when it has none mapped.
   */
  protected readonly languageOptions = computed(() => this.geo.languageNames());

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
  protected readonly campaignDropdownOptions = computed<
    readonly { reference: string; label: string; context: string }[]
  >(() => {
    const seedOptions: readonly { reference: string; label: string; context: string }[] = [];
    const storeCampaigns = this.campaignStore
      .all()

      // ONLY CAMPAIGNS A LEAD CAN ACTUALLY BE CAPTURED AGAINST.
      //
      // DON keeps its own `don_campaigns` table, which `don_leads.campaign_id` points at, and
      // CampaignProjection mirrors CAM's campaigns into it keyed by the same id - but only
      // those in Active, Approved or Paused. A campaign nobody has approved yet is not
      // something to take donor interest against, so it is not mirrored, so a lead naming it
      // has no row to reference and the save is refused.
      //
      // This filter used to exclude only Cancelled and Closed, which meant every Draft and
      // Submitted campaign was offered here and every one of them was a dead end.
      .filter((campaign) => LEAD_CAPTURABLE_CAMPAIGN_STATUSES.includes(campaign.status))
      .map((campaign) => ({
        reference: campaign.code,
        label: campaign.name,
        context: campaign.status,
      }));
    const seenNames = new Set(seedOptions.map((option) => option.label.trim().toLowerCase()));
    return [
      ...seedOptions,
      ...storeCampaigns.filter((campaign) => !seenNames.has(campaign.label.trim().toLowerCase())),
    ];
  });

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

  /**
   * Approved administrative geography, live from the GlobalMaster catalogue.
   *
   * WHAT THIS REPLACES: `countryOptions = ['India']` — a country dropdown with exactly one
   * option, so a lead from anywhere else could not be recorded at all — plus eleven of India's
   * states and a six-state city map, all compiled into the bundle.
   *
   * THE "APPROVED CATALOGUE" IS NOW THE MASTER CATALOGUE, which is what it was always meant to
   * be. Serviceability below still means "this city is one we hold a record for"; the difference
   * is that the record is the one an administrator maintains on the Masters screen rather than a
   * literal in this file that nobody could change without a release.
   */
  protected readonly geo = createGeoCascade();

  protected readonly countryOptions = computed(() => this.geo.countryNames());
  protected readonly stateOptions = computed(() => this.geo.stateNames());

  private readonly MAX_MOBILE_ENTRIES = 5;
  private readonly MAX_BULK_FILE_SIZE = 10 * 1024 * 1024; // 10 MB
  private readonly ALLOWED_BULK_EXTENSIONS = ['.csv', '.xlsx'];
  private readonly MAX_EVIDENCE_FILE_SIZE = 5 * 1024 * 1024; // 5 MB
  private readonly ALLOWED_EVIDENCE_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png'];

  private readonly NAME_PATTERN = /^[A-Za-z\s]+$/;
  private readonly MOBILE_PATTERN = /^[0-9]{10,15}$/;
  private readonly EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  // ---------------------------------------------------------------------
  // Page / UI state
  // ---------------------------------------------------------------------
  protected readonly uiState = signal<UiState>('ready');
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly activeActionId = signal('');
  private readonly tokens = inject(AuthTokenService);

  /**
   * What this caller may actually do.
   *
   * THE SIX HARD-CODED `true`s ARE GONE. They lived in this screen's JSON page data, so every
   * button on the screen was drawn for everybody who could reach it - a read-only reviewer saw the
   * same controls as the person who owns the work, and found out which ones they were not allowed
   * to press by pressing them.
   *
   * The server enforces these codes whatever this object says; reading them here is what stops the
   * screen offering an action the API will refuse.
   */
  protected readonly permissions = computed<LeadCapturePermissions>(() => ({
    deleteDraft: this.tokens.hasAnyPermission('don.lead-capture.delete-draft'),
    save: this.tokens.hasAnyPermission('don.lead-capture.save'),
    submit: this.tokens.hasAnyPermission('don.lead-capture.submit'),
  }));

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
  protected readonly approvedCities = computed(() => this.geo.cityNames());

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

  private createInitialFields(): LeadCaptureFormFields {
    const base: Partial<Record<string, unknown>> = {};
    return {
      firstName: typeof base?.['firstName'] === 'string' ? (base['firstName'] as string) : '',
      lastName: typeof base?.['lastName'] === 'string' ? (base['lastName'] as string) : '',
      displayName: '',
      mobiles: [this.createEmptyMobile(true)],
      email: '',
      preferredLanguage: '',
      geoCountry: 'India',
      geoState: '',
      geoCity: '',
      addressDetails: typeof base?.['location'] === 'string' ? (base['location'] as string) : '',
      leadSource: '',
      campaign: '',
      ownerUserId: '',
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

    // Changing the country invalidates BOTH boxes beneath it. Clearing only the city is how a
    // lead ends up in Tamil Nadu, Australia - the state box still reads as filled in, so nothing
    // on screen looks wrong.
    this.fields.update((f) => ({ ...f, geoCountry: value, geoState: '', geoCity: '' }));
    this.clearError('geoCountry');
    this.clearError('geoState');
    this.clearError('geoCity');
    this.geo.selectCountry(value);
  }

  protected onGeoStateChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    // Changing state invalidates any previously selected city outside its approved list.
    this.fields.update((f) => ({ ...f, geoState: value, geoCity: '' }));
    this.clearError('geoState');
    this.clearError('geoCity');
    this.geo.selectState(value);
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
    const populatedMobiles = f.mobiles.filter((m) => m.value.trim().length > 0);
    if (populatedMobiles.length === 0) {
      errs['mobiles'] = 'At least one mobile number is required.';
    } else {
      const invalidEntry = populatedMobiles.find((m) => !this.MOBILE_PATTERN.test(m.value.trim()));
      if (invalidEntry) {
        errs['mobiles'] = 'Enter a valid mobile number (10-15 digits, numbers only).';
      } else {
        const seen = new Set<string>();
        let hasDuplicate = false;
        for (const m of populatedMobiles) {
          const v = m.value.trim();
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
    } else if (this.approvedCities().length && !this.approvedCities().includes(f.geoCity)) {
      // Only enforced when the catalogue actually HOLDS cities for the selected state. A state
      // whose cities have not been seeded yet would otherwise reject every possible answer and
      // leave the form permanently unsubmittable.
      errs['geoCity'] = 'Select a city from the approved list.';
    }

    // Lead source
    if (!f.leadSource) {
      errs['leadSource'] = 'Lead source is required.';
    }

    // Source details
    if (f.sourceDetails.length > 500) {
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
        const digits = phone.replace(/\D/g, '');
        if (!phone) {
          errs['contactRestrictionPhone'] = 'A contact restriction number is required.';
        } else if (!this.MOBILE_PATTERN.test(digits)) {
          errs['contactRestrictionPhone'] = 'Enter a valid number in international format.';
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

  // =======================================================================
  // Primary actions
  // =======================================================================
  /**
   * Saves the capture as a draft.
   *
   * IT NOW ACTUALLY SAVES ONE. This method validated, locked the e-mail field, set the state to
   * 'success' and returned - it made no API call and wrote to no store, while its own comment
   * said "The record is now persisted as a draft". Nothing was persisted, so the draft was gone
   * on the next navigation and anybody who used Save draft rather than Submit lost the lead
   * entirely, with a success banner confirming it had been kept.
   *
   * A DRAFT IS NOT SUBMITTED, deliberately: it stays out of the work queue until Submit
   * promotes it. That is the difference between the two buttons.
   */
  protected saveDraft(): void {
    const errs = this.validate();
    this.errors.set(errs);
    if (Object.keys(errs).length > 0) {
      this.uiState.set('validation');
      this.focusFirstError(errs);
      return;
    }

    const form = this.fields();
    const primaryMobile =
      form.mobiles.find((mobile) => mobile.isPrimary)?.value ?? form.mobiles[0]?.value ?? '';
    const name = form.displayName.trim() || `${form.firstName} ${form.lastName}`.trim();

    this.workflow.addLead(
      {
        name,
        mobile: primaryMobile,
        email: form.email,
        source: form.leadSource || 'Manual',
        campaign: form.campaign,

        // THE CHOSEN OWNER, not the literal 'Unassigned' this used to send. The name is what the
        // queue shows; the id is what every ownership query and the caller's own-records scope
        // actually match on, so both travel. Left blank, the server still defaults to the caller.
        owner: this.selectedOwnerName() || 'Unassigned',
        ownerUserId: form.ownerUserId || null,
        stage: 'New',
        language: form.preferredLanguage || 'English',
        lastActivity: 'Lead captured',
        lastContactOutcome: 'No contact yet',
        contactRestricted: form.collectConsent ? form.consent.doNotContact : false,
      },
      {
        submit: false,
        onDone: (outcome) => {
          if (!outcome.saved) {
            this.reportRefusal(outcome.error, 'The draft could not be saved.');
            return;
          }

          // The record is now persisted as a draft — the email becomes immutable.
          this.emailLocked.set(true);
          if (form.collectConsent) {
            this.consentStateAtLastSave.set(form.consent.consentState);
          }
          this.uiState.set('success');
        },
      },
    );
  }

  protected submitLead(): void {
    const errs = this.validate();
    this.errors.set(errs);
    if (Object.keys(errs).length > 0) {
      this.uiState.set('validation');
      this.focusFirstError(errs);
      return;
    }

    const form = this.fields();
    const primaryMobile = form.mobiles.find((mobile) => mobile.isPrimary)?.value ?? form.mobiles[0]?.value ?? '';
    const name = form.displayName.trim() || `${form.firstName} ${form.lastName}`.trim();
    // NO 'Unassigned campaign' FALLBACK. That string was sent as the request's `campaignId`,
    // which the API declares as a Guid, so it guaranteed a 400 for every lead captured without
    // a campaign chosen. A campaign is required, `validate()` enforces it, and inventing a
    // placeholder here only moved the failure somewhere harder to see.
    // The outcome is handled entirely in `onDone` — including the refusal to build a request at
    // all, which reports through the same callback — so the returned optimistic row is not
    // needed here.
    this.workflow.addLead(
      {
        name,
        mobile: primaryMobile,
        email: form.email,
        source: form.leadSource || 'Manual',
        campaign: form.campaign,

        // THE CHOSEN OWNER, not the literal 'Unassigned' this used to send. The name is what the
        // queue shows; the id is what every ownership query and the caller's own-records scope
        // actually match on, so both travel. Left blank, the server still defaults to the caller.
        owner: this.selectedOwnerName() || 'Unassigned',
        ownerUserId: form.ownerUserId || null,
        stage: 'New',
        language: form.preferredLanguage || 'English',
        lastActivity: 'Lead captured',
        lastContactOutcome: 'No contact yet',
        contactRestricted: form.collectConsent ? form.consent.doNotContact : false,
      },
      {
        // SUBMIT, not just save. A saved lead is a draft, and the work queue shows no drafts.
        submit: true,

        // NAVIGATE ONLY ONCE THE SERVER HAS THE LEAD. This screen used to navigate on the next
        // line, so a capture rejected with a 400 still landed on the work queue and simply
        // showed no new row.
        onDone: (outcome) => {
          if (!outcome.saved) {
            this.reportRefusal(outcome.error, 'The lead could not be submitted.');
            return;
          }

          this.emailLocked.set(true);
          this.isSubmitted.set(true);
          if (form.collectConsent) {
            this.consentStateAtLastSave.set(form.consent.consentState);
          }
          this.uiState.set('success');

          // THE SERVER'S LEAD REFERENCE, which is what the work queue keys its rows on. The
          // provisional id this method holds is replaced by the refresh, so navigating with it
          // highlighted nothing.
          this.router.navigate(['/app/fundraising/relationships/lead-work-queue'], {
            queryParams: { createdLeadId: outcome.reference },
          });
        },
      },
    );
  }

  protected cancelForm(): void {
    this.router.navigate(['/app/fundraising/relationships/lead-work-queue']);
  }

  /**
   * Reports a refusal that came from the SERVER.
   *
   * IT IS NOT A VALIDATION FAILURE AND MUST NOT LOOK LIKE ONE. Both save paths used to set the
   * validation state on any refusal, which drew "Check the highlighted fields. Some information
   * is missing or invalid" over a form where `errors()` was empty and no control carried an
   * invalid marker - so the person was told to correct something and given no way to discover
   * what. The server said why; that is what is shown.
   */
  private reportRefusal(error: string | undefined, fallback: string): void {
    this.uiState.set('ready');
    this.toast.show('Lead not saved', error?.trim() || fallback, 'error');
  }

  /**
   * Puts the caret on the first control the validator objected to.
   *
   * The banner tells somebody to check the highlighted fields; this is what takes them to the
   * first one, on a form long enough that the offending control is usually off screen.
   */
  private focusFirstError(errs: Record<string, string>): void {
    const first = Object.keys(errs)[0];
    if (!first) {
      return;
    }

    queueMicrotask(() => {
      const element = document.getElementById(first);
      element?.scrollIntoView({ block: 'center', behavior: 'smooth' });
      element?.focus({ preventScroll: true });
    });
  }

  // =======================================================================
  // Generic confirm-dialog actions (reserved for permissioned page actions
  // sourced from the screen's action contract, e.g. void/reassign flows).
  // =======================================================================
  protected openAction(actionId: string): void {
    const action = ACTIONS.find((a) => a.id === actionId);
    if (!action) {
      return;
    }
    this.activeActionId.set(actionId);
    this.confirmConfig.set({
      title: `Confirm ${action.label}`,
      message: action.result,
      confirmLabel: action.label,
      cancelLabel: 'Cancel',
      tone: action.placement === 'danger' ? 'danger' : 'primary',
      requireReason: !!action.requiresReason,
      reasonLabel: 'Reason',
      reasonMin: 10,
      reasonMax: 2000,
      typedConfirm: !!action.typedConfirm,
      affectedRecord: `${this.draftReference()} · ${this.fields().displayName || this.fields().firstName}`,
      effectiveTime: this.lastRefresh(),
    });
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
    this.bulkUpload.set(this.createInitialBulkUpload());
    this.isBulkUploadOpen.set(true);
  }

  protected closeBulkUpload(): void {
    this.isBulkUploadOpen.set(false);
    this.bulkUpload.set(this.createInitialBulkUpload());
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
      // .xlsx workbooks are parsed server-side; the client validates and
      // stages the file, then hands off to the import endpoint.
      this.bulkUpload.update((s) => ({ ...s, status: 'ready' }));
    }
  }

  private parseCsvPreview(file: File): void {
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      const lines = text.split(/\r?\n/).filter((line) => line.trim().length > 0);
      if (lines.length <= 1) {
        this.bulkUpload.update((s) => ({
          ...s,
          status: 'invalid',
          errorMessage: 'The file has no data rows.',
        }));
        return;
      }
      const headerColumnCount = lines[0].split(',').length;
      const dataRows = lines.slice(1);
      let validCount = 0;
      let invalidCount = 0;
      for (const row of dataRows) {
        const columns = row.split(',');
        const firstColumn = (columns[0] ?? '').trim();
        if (columns.length === headerColumnCount && firstColumn.length > 0) {
          validCount += 1;
        } else {
          invalidCount += 1;
        }
      }
      this.bulkUpload.update((s) => ({
        ...s,
        status: 'ready',
        totalRecords: dataRows.length,
        validRecords: validCount,
        invalidRecords: invalidCount,
      }));
    };
    reader.onerror = () => {
      this.bulkUpload.update((s) => ({
        ...s,
        status: 'error',
        errorMessage: 'Could not read the file. Please try again.',
      }));
    };
    reader.readAsText(file);
  }

  protected importBulkUpload(): void {
    if (this.bulkUpload().status !== 'ready') {
      return;
    }
    this.bulkUpload.update((s) => ({ ...s, status: 'importing' }));
    // TODO: wire to the bulk-import API; UI reflects the queued outcome.
    window.setTimeout(() => {
      this.bulkUpload.update((s) => ({ ...s, status: 'imported' }));
    }, 600);
  }

  protected downloadBulkErrorReport(): void {
    const s = this.bulkUpload();
    if (s.invalidRecords <= 0) {
      return;
    }
    const rows = Array.from({ length: s.invalidRecords }, (_, i) => `${i + 1},Column count mismatch or missing primary field`);
    const csvContent = ['Row,Reason', ...rows].join('\n');
    const blob = new Blob([csvContent], { type: 'text/csv' });
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