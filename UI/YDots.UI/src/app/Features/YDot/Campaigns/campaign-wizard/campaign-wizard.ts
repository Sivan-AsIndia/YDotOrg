import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { UiState, CampaignStatus, CampaignWizardPermissions, EligibleRecord } from '../../../../Shared/models/campaign.model';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { OrganisationContextService } from '../../../../Shared/services/organisation-context.service';
import { CampaignApiService } from '../../../../Service/campaign-api.service';
import { GeoMasterService } from '../../../../Shared/services/geo-master.service';
import { MasterLookup } from '../../../../Shared/models/global-master.model';

/**
 * One row of a controlled catalogue: the API identifier the server wants, and the text a
 * person reads.
 *
 * THE TWO ARE NOT INTERCHANGEABLE, which is the whole reason this type exists. Every picker on
 * this page used to hold plain strings and send the visible one, so the create request carried
 * 'INR' where a currency id belonged and 'India' where a country id belonged - and the API,
 * which types all four as Guid, refused the body outright with a 400. Holding the pair means
 * the id goes to the server and the label goes on the screen, and neither can be mistaken for
 * the other.
 */
interface MasterOption {
  readonly ref: string;
  readonly label: string;
}

/**
 * Campaign wizard.
 *
 * Guides a Campaign Manager through creating a campaign in ordered steps and
 * saving it as a draft.
 *
 *  Route           : /fundraising/campaigns/campaign-wizard
 *  Purpose         : Capture the campaign activation configuration.
 *  Primary users   : Campaign Manager
 *  View permission : cam.campaign-wizard.view
 *  Primary action  : Save draft
 *  Data scope      : Records inside the actor's active organisation, campaign
 *                    and geography.
 *  History rule    : Delete is available only for an unused draft with no
 *                    downstream reference; otherwise use a lifecycle action.
 */

@Component({
  selector: 'app-campaign-wizard',
  imports: [CommonModule, FormsModule, ClickOutsideDirective],
  templateUrl: './campaign-wizard.html',
  styleUrl: './campaign-wizard.css',
})
export class CampaignWizardComponent {
  /** Single shared source of truth for campaign data — Save draft writes here. */
  private readonly people = inject(PeopleDirectoryService);

  private readonly store = inject(CampaignStoreService);
  private readonly user = inject(CurrentUserService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  /** Countries, states, cities and currencies — the platform's global master data. */
  /**
   * The country, state, city and currency pickers.
   *
   * GeoMasterService, NOT MasterService. That one serves the five Masters ADMIN screens and every
   * call on it is gated on `GlobalMaster.Section` — so a Campaign Manager without that permission
   * got a 403 where the country and currency lists should have been, and this wizard showed
   * "The currency and country lists could not be loaded" to somebody who had done nothing wrong.
   * `MasterLookupsController` is the ungated read side and this is its client half; it needs
   * authentication and nothing more, while the scoped query filter still keeps one Organisation's
   * private additions out of another's.
   */
  private readonly masters = inject(GeoMasterService);
  /** Channels — the campaign module's own reference catalogue. */
  private readonly campaignApi = inject(CampaignApiService);
  /** The signed-in Organisation, which supplies the currency now that nobody can choose one. */
  private readonly organisation = inject(OrganisationContextService);

  /**
   * Why the controlled catalogues are empty, when they are.
   *
   * SURFACED RATHER THAN SWALLOWED. Every picker on step 2 and step 3 is now filled from an
   * API, so a failed reference load leaves the form unusable - and an empty dropdown with no
   * explanation is the single most confusing way for that to present. The banner says the
   * lists could not be loaded, which is a problem somebody can act on.
   */
  protected readonly referenceError = signal<string | null>(null);

  // ================= Task header =================

  /** Stable reference where applicable — a new configuration has none until saved. */
  protected readonly stableReference = signal<string | null>(null);
  /** Lifecycle state — a new configuration begins with no record, then Draft. */
  // 'Submitted' is a state this wizard can genuinely be in: confirmSubmit leaves the campaign
  // with its approver, and the header must say so rather than still calling it a draft.
  protected readonly lifecycleState = signal<'No record' | 'Draft' | 'Submitted'>('No record');
  /** Owner is captured as a field below; the header echoes the accountable owner. */
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  /** Freshness for the working configuration. */
  protected readonly lastSaved = signal<string | null>(null);
  /** Optimistic-lock token used for the conflict state. */
  protected readonly concurrencyVersion = signal('Version 1');

  /** Effective permissions decided server-side; the client mirrors the same decision. */
  protected readonly permissions = computed<CampaignWizardPermissions>(() => ({
    view: this.user.hasPermission('cam.campaigns.view'),
    saveDraft: this.user.hasPermission('cam.campaigns.create'),
    validate: this.user.hasPermission('cam.campaigns.view'),
    submit: this.user.hasPermission('cam.campaigns.submit'),
    deleteDraft: this.user.hasPermission('cam.campaigns.delete-draft'),
  }));

  // ================= Context and filters =================

  /**
   * The scope this wizard captures records inside.
   *
   * IT IS THE SIGNED-IN ORGANISATION. The comment above this line used to say "server-qualified"
   * and the value was the literal 'FY 2025-26 · GreenSol India Pvt Ltd' - a company belonging to
   * nobody on the platform, shown to every operator of every charity. A scope label that can be
   * wrong is worse than no label at all: it tells somebody they are working inside another
   * organisation's records, which is the one thing this line exists to reassure them about.
   */
  protected get activeScope(): string {
    return this.user.organisationName() || 'Your organisation';
  }

  // ================= Progressive-disclosure steps =================
  //
  // The fields are grouped into ordered steps so the user fills them a few at a
  // time; the final step is the review-and-confirm region.

  protected readonly steps: readonly { readonly title: string; readonly caption: string }[] = [
    { title: 'Basic Information', caption: 'Campaign identity' },
    // TARGET & BUDGET IS ON HOLD and is deliberately absent, along with its panel, its recap
    // and its fields. Turning it back on means restoring this entry, the template panel, the
    // three signals and their keys in `requiredValid`.
    { title: 'Channels & Sources', caption: 'Select channels, source and location' },
    { title: 'Publication & Notice', caption: 'Public description and terms' },
    { title: 'Review & Launch', caption: 'Review and confirm' },
  ];
  protected readonly stepIndex = signal(0);
  protected readonly totalSteps = this.steps.length;
  /** Steps on which the user has attempted to advance — drives red field highlighting. */
  protected readonly stepAttempted = signal<readonly boolean[]>([false, false, false, false]);
  protected wasAttempted(step: number): boolean {
    return this.stepAttempted()[step] ?? false;
  }
  private markStepAttempted(step: number): void {
    this.stepAttempted.update((arr) => arr.map((v, i) => (i === step ? true : v)));
  }
  protected readonly currentStep = computed(() => this.steps[this.stepIndex()]);
  protected readonly isFirstStep = computed(() => this.stepIndex() === 0);
  protected readonly isLastStep = computed(() => this.stepIndex() === this.totalSteps - 1);

  protected goToStep(i: number): void {
    if (i >= 0 && i < this.totalSteps) this.stepIndex.set(i);
  }
  protected nextStep(): void {
    if (this.isLastStep()) return;
    const step = this.stepIndex();
    // Validate the current step before advancing; block and highlight invalid fields.
    if (!this.isStepValid(step)) {
      this.markStepAttempted(step);
      this.uiState.set('validation');
      return;
    }
    if (this.uiState() === 'validation') this.uiState.set('ready');
    this.stepIndex.update((i) => i + 1);
  }
  protected previousStep(): void {
    if (!this.isFirstStep()) this.stepIndex.update((i) => i - 1);
  }

  /** Required-field validity for a single step, checked before advancing. */
  protected isStepValid(step: number): boolean {
    const r = this.requiredValid();
    switch (step) {
      case 0:
        return r.campaignName && r.campaignCode && r.purpose && r.fundProgramme && r.owner && r.startDate && r.endDate;
      case 1:
        return (
          r.reminderDaysBefore &&
          r.reminderTime &&
          r.channels &&
          r.country &&
          r.region &&
          r.city &&
          r.pincode
        );
      case 2:
        return r.publicDescription && r.termsNotice;
      default:
        return true;
    }
  }

  // ================= Fields and controls =================

  // --- Campaign name — free-text input; the user types the name directly. ---
  protected readonly campaignName = signal('');
  protected readonly campaignNameMax = 250;
  protected readonly campaignNameValid = computed(() => {
    const v = this.campaignName().trim();
    return v.length > 0 && v.length <= this.campaignNameMax;
  });

  // --- Campaign code — fully editable, user-typed code, auto-uppercased, 20
  // characters maximum (no fixed prefix). ---
  protected readonly campaignCodeMax = 20;
  protected readonly campaignCode = signal('');
  protected setCampaignCode(value: string): void {
    this.campaignCode.set(value.toUpperCase().slice(0, this.campaignCodeMax));
  }

  // --- Purpose — rich-text editor with character counter, 10–1,000 chars.
  // purpose holds the plain-text mirror used for length validation; purposeHtml
  // holds the formatted (bold/italic/list/font) markup, matching the Public
  // description / Terms and notice rich-text fields. ---
  protected readonly purpose = signal('');
  protected readonly purposeHtml = signal('');
  /** One-time seed for the contenteditable so re-entering step 1 restores content without a cursor jump. */
  protected readonly purposeSeed = signal('');
  protected readonly purposeMin = 10;
  protected readonly purposeMax = 1000;
  protected readonly purposeLen = computed(() => this.purpose().trim().length);
  protected readonly purposeValid = computed(
    () => this.purposeLen() >= this.purposeMin && this.purposeLen() <= this.purposeMax,
  );

  // --- Fund or programme — text input ---
  protected readonly fundProgramme = signal('');
  protected readonly fundProgrammeMax = 250;
  protected readonly fundProgrammeValid = computed(() => {
    const v = this.fundProgramme().trim();
    return v.length > 0 && v.length <= this.fundProgrammeMax;
  });

  // --- Owner — single scope-aware selector that captures one or more accountable
  // owners as chips. One box holds every chosen owner; picking an owner adds
  // a chip and removes that person from the dropdown so the same owner cannot be
  // chosen twice for this campaign. Starting a new campaign shows every owner again.
  /**
   * The people a campaign can be made accountable to.
   *
   * FROM IAM, WITHIN THE CALLER'S DATA SCOPE. The four names here were invented and were shared,
   * character for character, with five other screens - so every organisation on the platform was
   * offered the same four strangers, and a campaign assigned to one of them had no real owner at
   * all.
   */
  protected readonly ownerCatalogue = computed<readonly EligibleRecord[]>(() =>
    this.people.assignable().map((person) => ({
      ref: person.reference,
      label: person.name,
      context: person.context,
    })),
  );
  /** Owners chosen for this campaign, shown as chips inside the single owner box. */
  protected readonly selectedOwners = signal<readonly EligibleRecord[]>([]);
  protected readonly ownerQuery = signal('');
  protected readonly ownerOpen = signal(false);
  /** Available owners = catalogue minus already-selected, filtered by the live query. */
  protected readonly ownerResults = computed<readonly EligibleRecord[]>(() => {
    const chosen = new Set(this.selectedOwners().map((o) => o.ref));
    const available = this.ownerCatalogue().filter((o) => !chosen.has(o.ref));
    const q = this.ownerQuery().trim().toLowerCase();
    if (!q) return available;
    return available.filter((o) => `${o.label} ${o.ref} ${o.context}`.toLowerCase().includes(q));
  });
  protected chooseOwner(record: EligibleRecord): void {
    if (this.selectedOwners().some((o) => o.ref === record.ref)) return;
    this.selectedOwners.update((list) => [...list, record]);
    this.ownerQuery.set('');
    this.ownerOpen.set(false);
  }
  protected removeOwnerChip(ref: string): void {
    this.selectedOwners.update((list) => list.filter((o) => o.ref !== ref));
  }
  /** Every selected owner's name, comma-separated — used in the recap/summary panels. */
  protected readonly ownersLabel = computed(
    () => this.selectedOwners().map((o) => o.label).join(', ') || '—',
  );

  // --- Start date — date picker with time-zone label ---
  protected readonly startDate = signal('');
  // --- End date — date picker with time-zone label ---
  protected readonly endDate = signal('');

  /**
   * Today, as the `yyyy-MM-dd` an `<input type="date">` understands.
   *
   * BUILT FROM THE LOCAL PARTS, not from `toISOString()`. `toISOString` converts to UTC first, so
   * anywhere east of Greenwich it returns YESTERDAY for the first few hours of every day — and a
   * `min` one day early is a floor that does not hold on exactly the mornings somebody is most
   * likely to be scheduling a campaign that starts today.
   */
  protected readonly todayIso = (() => {
    const now = new Date();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');
    return `${now.getFullYear()}-${month}-${day}`;
  })();

  /**
   * The floor under End date: the start, once one is chosen, and today until then.
   *
   * This is what greys out the impossible half of the end-date calendar. Without it the picker
   * opened on the current month with every day selectable, so "the end cannot fall before the
   * start" could only be discovered by choosing a date and being told off for it.
   */
  protected readonly endDateMin = computed(() => this.startDate() || this.todayIso);

  /**
   * A campaign may not START IN THE PAST.
   *
   * NEITHER SIDE ENFORCED THIS. The input carried no `min`, this computed only asked whether the
   * field was non-empty, and the server's validator only refused the default value — so a
   * campaign could be created with a start date years gone, and would then be counted as elapsed
   * before it was ever activated. The rule is now stated here, on the input as `min`, and in the
   * API validator, because a client-side floor is a courtesy and the server's is the rule.
   *
   * TODAY IS ALLOWED. "Starts today" is the ordinary case for a campaign somebody is setting up
   * this morning; only yesterday is wrong.
   */
  protected readonly startDateValid = computed(() => {
    const value = this.startDate();
    return !!value && value >= this.todayIso;
  });

  /** True when a start date has been entered but falls before today. */
  protected readonly startDateInPast = computed(
    () => !!this.startDate() && this.startDate() < this.todayIso);

  /** Reject impossible ranges (end before start). */
  protected readonly endDateValid = computed(() => {
    if (!this.endDate()) return false;
    if (!this.startDate()) return true;
    return new Date(this.endDate()) >= new Date(this.startDate());
  });
  /** Interpreted date shown before submit. */
  protected interpretDate(value: string): string {
    if (!value) return '—';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '—';
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  // --- Currency — resolved, never chosen ---
  //
  // TARGET, BUDGET AND THE CURRENCY PICKER ARE GONE with the Target & Budget step. What is left
  // here is the smallest thing the API still demands: `CreateCampaignRequest.CurrencyId` is a
  // non-empty Guid, so a currency has to reach the server even though nobody can pick one. It is
  // resolved from the Organisation's default - see `applyDefaultCurrency`.
  //
  // TARGET AND BUDGET ARE NOT SENT AT ALL. `persistToStore` omits both keys, which lets the
  // store default them on create (0 and null) and, on an edit, keeps whatever the stored record
  // already held instead of overwriting a real target with a zero.
  //
  // THE CATALOGUE COMES FROM THE MASTERS API, and `ref` is the currency's Guid. It used to be
  // four hard-coded rows whose `ref` was the ISO code - 'INR', 'USD' - and that string went
  // into the create body's `currencyId`, which the API declares as a Guid. System.Text.Json
  // could not convert it, so the whole request was rejected with a 400 before any handler saw
  // it and no campaign was ever created.
  protected readonly currencyCatalogue = signal<readonly MasterOption[]>([]);
  protected readonly currency = signal('');
  protected readonly currencyLabel = computed(
    () => this.currencyCatalogue().find((c) => c.ref === this.currency())?.label ?? '—',
  );

  // --- State and City — the country's own administrative divisions, from the masters API ---
  //
  // THESE WERE A HARD-CODED MAP OF FIVE COUNTRIES' SUBDIVISION NAMES, plus a free-text
  // fallback for every other country, and the chosen NAME went into `stateId` and `cityId` -
  // both declared as Guids by the API. A free-typed city could never have matched a master row
  // at all.
  //
  // BOTH LISTS CASCADE and are loaded on demand: states for the chosen country, cities for the
  // chosen state. Neither is fetched until its parent is picked, because neither question has
  // an answer before then.
  //
  // WHEN A COUNTRY HAS NO STATES IN THE MASTER DATA the field is satisfied empty rather than
  // dead-ended. `StateId` and `CityId` are nullable on the API, and seven of the twelve seeded
  // countries have no states - requiring a selection that cannot be made would make those
  // countries unusable.
  /** The first-level administrative division is always labelled "State". */
  protected readonly regionLabel = computed(() => 'State');
  protected readonly regionOptions = signal<readonly MasterOption[]>([]);
  /** The SELECTED STATE'S ID. */
  protected readonly region = signal('');
  protected readonly regionValid = computed(
    () => this.regionOptions().length === 0 || !!this.region(),
  );
  protected readonly selectedRegionLabel = computed(
    () => this.regionOptions().find((r) => r.ref === this.region())?.label ?? '—',
  );
  protected readonly regionQuery = signal('');
  protected readonly regionOpen = signal(false);
  protected readonly regionResults = computed<readonly MasterOption[]>(() => {
    const q = this.regionQuery().trim().toLowerCase();
    const all = this.regionOptions();
    if (!q || q === this.selectedRegionLabel().toLowerCase()) return all;
    return all.filter((r) => r.label.toLowerCase().includes(q));
  });
  protected chooseRegion(r: MasterOption): void {
    this.region.set(r.ref);
    this.regionQuery.set(r.label);
    this.regionOpen.set(false);
    // The city list belongs to the state, so changing the state invalidates it.
    this.clearCity();
    this.loadCities(r.ref);
  }
  protected clearRegion(): void {
    this.region.set('');
    this.regionQuery.set('');
    this.cityOptions.set([]);
    this.clearCity();
  }

  protected readonly cityOptions = signal<readonly MasterOption[]>([]);
  /** The SELECTED CITY'S ID. */
  protected readonly city = signal('');
  protected readonly cityValid = computed(() => this.cityOptions().length === 0 || !!this.city());
  protected readonly selectedCityLabel = computed(
    () => this.cityOptions().find((c) => c.ref === this.city())?.label ?? '—',
  );
  protected readonly cityQuery = signal('');
  protected readonly cityOpen = signal(false);
  protected readonly cityResults = computed<readonly MasterOption[]>(() => {
    const q = this.cityQuery().trim().toLowerCase();
    const all = this.cityOptions();
    if (!q || q === this.selectedCityLabel().toLowerCase()) return all;
    return all.filter((c) => c.label.toLowerCase().includes(q));
  });
  protected chooseCity(c: MasterOption): void {
    this.city.set(c.ref);
    this.cityQuery.set(c.label);
    this.cityOpen.set(false);
  }
  protected clearCity(): void {
    this.city.set('');
    this.cityQuery.set('');
  }

  protected readonly pincode = signal('');
  protected readonly pincodeValid = computed(() => /^\d{3,10}$/.test(this.pincode().trim()));
  /** Zip code accepts digits only — strip any non-digit as it is typed or pasted. */
  protected onPincodeInput(el: HTMLInputElement): void {
    const digits = el.value.replace(/\D/g, '');
    if (el.value !== digits) el.value = digits;
    this.pincode.set(digits);
  }

  // --- Lifecycle activation — how the campaign becomes Active from Scheduled,
  // plus a mandatory reminder before the start date for either mode. ---
  protected readonly activationMode = signal<'auto' | 'manual'>('manual');
  protected readonly reminderDaysBefore = signal<number | null>(null);
  protected readonly reminderTime = signal('');
  protected readonly reminderDaysBeforeValid = computed(() => {
    const v = this.reminderDaysBefore();
    return v !== null && v >= 1 && v <= 30;
  });
  protected readonly reminderTimeValid = computed(() => !!this.reminderTime());

  /**
   * The reminder time as a person reads it: "02:30 PM", not "14:30".
   *
   * WHY THIS EXISTS. The control is `<input type="time">`, and a native time input renders in the
   * BROWSER's locale - there is no attribute, and no CSS, that makes it show AM/PM. On a machine
   * set to a 24-hour locale there is no meridiem anywhere on the field, and the Review recap
   * printed the raw stored value, so "reminder at 09:00" and "reminder at 21:00" were told apart
   * only by the reader's arithmetic. This is shown beside the field and in the recap so the
   * meridiem is on screen whatever the browser does with the input itself.
   *
   * THE STORED VALUE IS UNCHANGED. It stays `HH:mm`, which is what the API's `reminderTime`
   * takes; this is presentation only.
   */
  protected readonly reminderTimeDisplay = computed(() => this.formatTime12(this.reminderTime()));

  protected formatTime12(value: string): string {
    const match = /^(\d{1,2}):(\d{2})/.exec(value ?? '');

    if (!match) {
      return '\u2014';
    }

    const hours = Number(match[1]);
    const minutes = match[2];

    if (!Number.isFinite(hours) || hours < 0 || hours > 23) {
      return '\u2014';
    }

    const meridiem = hours < 12 ? 'AM' : 'PM';
    // 00:xx is 12 AM and 12:xx is 12 PM - the two the modulo alone gets wrong.
    const twelve = hours % 12 === 0 ? 12 : hours % 12;

    return `${String(twelve).padStart(2, '0')}:${minutes} ${meridiem}`;
  }

  // --- Channels — searchable controlled choice from the approved catalogue ---
  //
  // FROM THE CAM REFERENCE ENDPOINT, so `ref` is the channel's Guid. The five rows this list
  // used to hold were invented, and the wizard sent their LABELS - 'Website', 'Email' - as
  // `channelIds`, which the API declares as Guids. The seeded channels are different rows
  // entirely, so even the codes would not have matched.
  protected readonly channelCatalogue = signal<readonly MasterOption[]>([]);
  protected readonly channelQuery = signal('');
  protected readonly selectedChannels = signal<readonly string[]>([]);
  protected readonly channelsValid = computed(() => this.selectedChannels().length > 0);
  protected readonly channelResults = computed(() => {
    const q = this.channelQuery().trim().toLowerCase();
    const all = this.channelCatalogue();
    if (!q) return all;
    return all.filter((c) => c.label.toLowerCase().includes(q));
  });
  protected toggleChannel(ref: string): void {
    this.selectedChannels.update((list) =>
      list.includes(ref) ? list.filter((r) => r !== ref) : [...list, ref],
    );
  }
  protected channelLabel(ref: string): string {
    return this.channelCatalogue().find((c) => c.ref === ref)?.label ?? ref;
  }

  /** The chosen channels as people read them — the review step must never print Guids. */
  protected readonly selectedChannelLabels = computed(() =>
    this.selectedChannels().map((ref) => this.channelLabel(ref)),
  );

  // --- Country — searchable single-select from the platform's country master.
  //
  // FROM THE MASTERS API, and `ref` is the country's Guid. This was a hard-coded list of ~195
  // country NAMES, and the chosen name went straight into the create body's `countryId`, which
  // the API declares as a Guid - so the request was refused before it reached a handler. The
  // list is also now honest about reach: a campaign can only name a country the platform
  // actually holds, rather than offering 195 of which twelve resolve. ---
  protected readonly countryCatalogue = signal<readonly MasterOption[]>([]);
  protected readonly countryQuery = signal('');
  protected readonly countryOpen = signal(false);
  /** The SELECTED COUNTRY'S ID, which is what the API's `countryId` takes. */
  protected readonly selectedCountry = signal('');
  protected readonly countryValid = computed(() => !!this.selectedCountry());
  protected readonly selectedCountryLabel = computed(
    () => this.countryCatalogue().find((c) => c.ref === this.selectedCountry())?.label ?? '—',
  );
  /** Filter the country list live as the user types. */
  protected readonly countryResults = computed<readonly MasterOption[]>(() => {
    const q = this.countryQuery().trim().toLowerCase();
    const all = this.countryCatalogue();
    if (!q || q === this.selectedCountryLabel().toLowerCase()) return all;
    return all.filter((c) => c.label.toLowerCase().includes(q));
  });
  protected chooseCountry(c: MasterOption): void {
    this.selectedCountry.set(c.ref);
    this.countryQuery.set(c.label);
    this.countryOpen.set(false);
    // Reset the state when the country changes — its options differ per country — and load
    // the new country's states. The city list hangs off the state, so it goes too.
    this.clearRegion();
    this.loadStates(c.ref);
  }
  protected clearCountry(): void {
    this.selectedCountry.set('');
    this.countryQuery.set('');
    this.regionOptions.set([]);
    this.clearRegion();
  }

  // --- Public description — rich-text editor with counter, 10–2,000.
  // publicDescription holds the plain-text mirror used for length validation and the
  // summary; publicDescriptionHtml holds the formatted (bold/italic/list/font) markup. ---
  protected readonly publicDescription = signal('');
  protected readonly publicDescriptionHtml = signal('');
  /** One-time seed for the contenteditable so re-entering the step restores content without a cursor jump. */
  protected readonly descSeed = signal('');
  protected readonly publicDescriptionMax = 2000;
  protected readonly publicDescriptionLen = computed(() => this.publicDescription().trim().length);
  protected readonly publicDescriptionValid = computed(() => {
    const len = this.publicDescriptionLen();
    return len >= this.purposeMin && len <= this.publicDescriptionMax;
  });

  /** Rich-text font controls offered in the Publication & Notice toolbar. */
  protected readonly fontSizes: readonly { readonly value: string; readonly label: string }[] = [
    { value: '2', label: 'Small' },
    { value: '3', label: 'Normal' },
    { value: '4', label: 'Medium' },
    { value: '5', label: 'Large' },
    { value: '6', label: 'X-Large' },
  ];
  protected readonly fontFamilies: readonly { readonly value: string; readonly label: string }[] = [
    { value: 'Inter, system-ui, sans-serif', label: 'Sans serif' },
    { value: 'Georgia, "Times New Roman", serif', label: 'Serif' },
    { value: '"Courier New", ui-monospace, monospace', label: 'Monospace' },
  ];

  /** Line-spacing options offered in the rich-text toolbars. */
  protected readonly lineHeights: readonly { readonly value: string; readonly label: string }[] = [
    { value: '1.2', label: 'Tight' },
    { value: '1.6', label: 'Normal' },
    { value: '2', label: 'Relaxed' },
    { value: '2.4', label: 'Loose' },
  ];
  /** Per-field line height (applied to the whole editor for a predictable result). */
  protected readonly descLineHeight = signal('1.6');
  protected readonly termsLineHeight = signal('1.6');

  /**
   * Preserve the live selection/range across a toolbar click. Clicking a toolbar
   * button steals focus from the contenteditable *before* the click handler runs;
   * calling `editor.focus` afterwards does not restore the caret/selection the
   * user actually had, so a command like "bulleted list" ends up acting on the
   * browser's fallback selection (often the whole paragraph) instead of just the
   * caret position or the text the user highlighted. Toolbar buttons/selects call
   * `preventDefault` on `mousedown` (see the template) so focus + selection are
   * never lost in the first place, which is the actual fix; this stash/restore is
   * a defensive fallback for hosts where that isn't enough.
   */
  private lastRange: Range | null = null;
  protected stashSelection(): void {
    const sel = window.getSelection();
    if (sel && sel.rangeCount > 0) this.lastRange = sel.getRangeAt(0).cloneRange();
  }
  private restoreSelection(editor: HTMLElement): void {
    editor.focus();
    const sel = window.getSelection();
    if (sel && this.lastRange && editor.contains(this.lastRange.commonAncestorContainer)) {
      sel.removeAllRanges();
      sel.addRange(this.lastRange);
    }
  }

  /** Apply an inline formatting command to the selection inside the rich-text editor. */
  protected applyFormat(editor: HTMLElement, command: string, value?: string): void {
    this.restoreSelection(editor);
    try {
      document.execCommand('styleWithCSS', false, 'true');
      document.execCommand(command, false, value);
    } catch {
      /* execCommand is unavailable in some hosts — the plain text is still captured on input. */
    }
    this.syncEditor(editor);
  }
  /** Line spacing control — sets the editor's line-height (predictable across the whole field). */
  protected applyLineHeight(target: 'desc' | 'terms' | 'popup', value: string): void {
    if (target === 'desc') this.descLineHeight.set(value);
    else if (target === 'terms') this.termsLineHeight.set(value);
    else this.popupLineHeight.set(value);
  }
  /** Last in-range markup per editor, used to revert an over-limit edit. */
  private readonly lastValidHtml: Record<string, string> = {};
  /** The hard character cap for a given rich-text editor. */
  private editorMax(editor: HTMLElement): number {
    if (editor.id === 'f-terms') return this.termsNoticeMax;
    if (editor.id === 'f-preview-edit')
      return this.previewField() === 'terms' ? this.termsNoticeMax : this.publicDescriptionMax;
    if (editor.id === 'f-public') return this.publicDescriptionMax;
    return this.purposeMax; // f-purpose caps at purposeMax
  }
  /** Put the caret at the very end of the editor (used after a revert). */
  private placeCaretEnd(editor: HTMLElement): void {
    const sel = window.getSelection();
    if (!sel) return;
    const range = document.createRange();
    range.selectNodeContents(editor);
    range.collapse(false);
    sel.removeAllRanges();
    sel.addRange(range);
  }

  /**
   * Paste, without the page it was copied from.
   *
   * WHAT WENT WRONG WITHOUT THIS. A browser pastes the SOURCE's markup into a contenteditable,
   * not its words: a paragraph copied from a web page arrives wrapped in a span carrying that
   * page's font stack, colour and white-space rule, which is a couple of hundred characters of
   * styling per block. These editors count the characters a person can SEE and then send the
   * markup, so a description that read as 770 characters travelled as several thousand - past
   * the column that stores it, and the save was refused with a message that named no field. It
   * also meant a campaign description quietly inherited the typography of wherever the text had
   * been written.
   *
   * THE TEXT AND ITS LINE BREAKS SURVIVE; only the styling is dropped. Formatting this field is
   * what the toolbar above it is for, and formatting applied there is proportional to the text
   * rather than repeated around every block.
   */
  protected handlePaste(event: ClipboardEvent, editor: HTMLElement): void {
    const clipboard = event.clipboardData;

    if (!clipboard) {
      return;
    }

    const text = clipboard.getData('text/plain');

    // The browser's own paste is refused either way. An empty plain-text reading means the
    // clipboard holds something that is not text at all - an image, most often - which has no
    // place in any of these fields.
    event.preventDefault();

    if (text.length === 0) {
      return;
    }

    // insertText keeps the caret, the undo stack and the surrounding formatting intact, which
    // rewriting innerHTML by hand does not.
    try {
      document.execCommand('insertText', false, text);
    } catch {
      /* Unavailable in some hosts - the field simply takes no paste there. */
      return;
    }

    this.syncEditor(editor);
  }

  /** Mirror an editor's markup + plain text into the correct backing signals. */
  protected syncEditor(editor: HTMLElement): void {
    // Hard character cap: contenteditable ignores maxlength, so if this input pushed
    // the field past its limit, revert to the last in-range markup and keep the caret
    // at the end — the user simply cannot enter more than the limit.
    if ((editor.innerText ?? '').trim().length > this.editorMax(editor)) {
      const prev = this.lastValidHtml[editor.id];
      if (prev !== undefined) {
        editor.innerHTML = prev;
        this.placeCaretEnd(editor);
      }
      return;
    }
    this.lastValidHtml[editor.id] = editor.innerHTML;

    if (editor.id === 'f-purpose') {
      this.purposeHtml.set(editor.innerHTML);
      this.purpose.set(editor.innerText ?? '');
    } else if (editor.id === 'f-terms') {
      this.termsNoticeHtml.set(editor.innerHTML);
      this.termsNotice.set(editor.innerText ?? '');
    } else if (editor.id === 'f-preview-edit') {
      this.popupDraftHtml.set(editor.innerHTML);
      this.popupDraftText.set(editor.innerText ?? '');
    } else {
      this.publicDescriptionHtml.set(editor.innerHTML);
      this.publicDescription.set(editor.innerText ?? '');
    }
  }
  /** Back-compat alias kept for the public-description editor binding. */
  protected syncDescription(editor: HTMLElement): void {
    this.syncEditor(editor);
  }

  // ================= Review & Launch — read/edit popup for the two rich-text fields =================
  //
  // Public description and Terms & notice are shown on the Review & Launch step as
  // click-to-open popups (matching the campaign-detail "Read more" pattern) rather
  // than long inline blocks; the popup opens in read view first and reveals the
  // full formatting toolbar only once the user chooses Edit.

  protected readonly previewField = signal<'description' | 'terms' | null>(null);
  protected readonly previewEditing = signal(false);
  protected readonly popupSeed = signal('');
  protected readonly popupDraftHtml = signal('');
  protected readonly popupDraftText = signal('');
  protected readonly popupLineHeight = signal('1.6');

  protected readonly previewFieldLabel = computed(() =>
    this.previewField() === 'terms' ? 'Terms and notice' : 'Public description',
  );
  /** Read-view content for the popup — always the committed (saved-in-form) markup. */
  protected readonly previewFieldHtml = computed(() =>
    this.previewField() === 'terms' ? this.termsNoticeHtml() || `<p>${this.termsNotice() || 'Nothing entered yet.'}</p>` :
      this.publicDescriptionHtml() || `<p>${this.publicDescription() || 'Nothing entered yet.'}</p>`,
  );

  /** Whether the given rich-text field currently holds any entered value. */
  protected hasFieldContent(which: 'description' | 'terms'): boolean {
    return which === 'terms'
      ? this.termsNotice().trim().length > 0
      : this.publicDescription().trim().length > 0;
  }
  protected openFieldPreview(which: 'description' | 'terms'): void {
    // Do not open the preview popup when nothing has been entered yet — the
    // preview is only meaningful once the field holds a value.
    if (!this.hasFieldContent(which)) return;
    this.previewField.set(which);
    this.previewEditing.set(false);
  }
  protected closeFieldPreview(): void {
    this.previewField.set(null);
    this.previewEditing.set(false);
  }
  /** Reveal the toolbar + editable body, seeded from the current committed markup. */
  protected startEditPreview(): void {
    const which = this.previewField();
    if (!which) return;
    this.popupSeed.set(which === 'terms' ? this.termsNoticeHtml() : this.publicDescriptionHtml());
    this.popupDraftHtml.set(this.popupSeed());
    this.popupDraftText.set(which === 'terms' ? this.termsNotice() : this.publicDescription());
    this.popupLineHeight.set(which === 'terms' ? this.termsLineHeight() : this.descLineHeight());
    this.previewEditing.set(true);
  }
  protected cancelEditPreview(): void {
    // Discard the draft — the committed signals were never touched.
    this.previewEditing.set(false);
  }
  /** Write the popup draft back into the real field the user was editing. */
  protected savePreviewEdit(): void {
    const which = this.previewField();
    if (!which) return;
    if (which === 'terms') {
      this.termsNoticeHtml.set(this.popupDraftHtml());
      this.termsNotice.set(this.popupDraftText());
      this.termsLineHeight.set(this.popupLineHeight());
    } else {
      this.publicDescriptionHtml.set(this.popupDraftHtml());
      this.publicDescription.set(this.popupDraftText());
      this.descLineHeight.set(this.popupLineHeight());
    }
    this.previewEditing.set(false);
  }

  // --- Terms and notice — rich-text editor with counter, up to 20,000 chars ---
  protected readonly termsNotice = signal('');
  protected readonly termsNoticeHtml = signal('');
  /** One-time seed for the terms contenteditable so re-entering the step restores content. */
  protected readonly termsSeed = signal('');
  protected readonly termsNoticeMax = 20000;
  protected readonly termsNoticeLen = computed(() => this.termsNotice().trim().length);
  protected readonly termsNoticeValid = computed(
    () => this.termsNoticeLen() >= this.purposeMin && this.termsNoticeLen() <= this.termsNoticeMax,
  );

  // --- Draft version — read-only, server-derived, immutable in this view ---
  protected readonly draftVersion = signal('Draft v0 — not yet saved');

  // ================= Required-field validity + progress =================

  /** Every field in the wizard is required. */
  protected readonly requiredValid = computed(() => ({
    campaignName: this.campaignNameValid(),
    campaignCode: this.campaignCode().trim().length > 0,
    purpose: this.purposeValid(),
    fundProgramme: this.fundProgrammeValid(),
    owner: this.selectedOwners().length > 0,
    startDate: this.startDateValid(),
    endDate: this.endDateValid(),
    // TARGET AMOUNT, CURRENCY AND BUDGET ARE ABSENT while Target & Budget is on hold. A field
    // the user cannot reach must not be counted as required information, or the progress meter
    // could never reach 100% and Submit would stay disabled for ever.
    channels: this.channelsValid(),
    country: this.countryValid(),
    region: this.regionValid(),
    city: this.cityValid(),
    pincode: this.pincodeValid(),
    reminderDaysBefore: this.reminderDaysBeforeValid(),
    reminderTime: this.reminderTimeValid(),
    publicDescription: this.publicDescriptionValid(),
    termsNotice: this.termsNoticeValid(),
  }));

  protected readonly requiredComplete = computed(() =>
    Object.values(this.requiredValid()).every(Boolean),
  );

  /** Steps whose required fields are all satisfied (drives the "n of 4 completed" summary). */
  protected readonly step1Complete = computed(() => {
    const r = this.requiredValid();
    return r.campaignName && r.campaignCode && r.purpose && r.fundProgramme && r.owner && r.startDate && r.endDate;
  });
  protected readonly step2Complete = computed(() => {
    const r = this.requiredValid();
    return r.reminderDaysBefore && r.reminderTime && r.channels && r.country && r.region && r.city && r.pincode;
  });
  protected readonly step3Complete = computed(() => {
    const r = this.requiredValid();
    return r.publicDescription && r.termsNotice;
  });
  /** Review & Launch is complete once every required field is captured (ready to submit). */
  protected readonly step4Complete = computed(() => this.requiredComplete());

  protected readonly stepCompletion = computed(() => [
    this.step1Complete(),
    this.step2Complete(),
    this.step3Complete(),
    this.step4Complete(),
  ]);
  protected readonly completedCount = computed(() => this.stepCompletion().filter(Boolean).length);

  /** Total required fields and how many are satisfied. */
  protected readonly requiredTotal = computed(() => Object.keys(this.requiredValid()).length);
  protected readonly requiredDone = computed(() => Object.values(this.requiredValid()).filter(Boolean).length);
  /**
   * Progress reflects captured required information, so completing every required
   * field reaches 100% (the conditional Channels/Publication steps are optional and
   * do not hold it back).
   */
  protected readonly progressPct = computed(() =>
    Math.round((this.requiredDone() / this.requiredTotal()) * 100),
  );

  /** The remaining required information, surfaced after Save draft. */
  protected readonly remainingRequired = computed(() => {
    const r = this.requiredValid();
    const labels: Record<string, string> = {
      campaignName: 'Campaign name',
      campaignCode: 'Campaign code',
      purpose: 'Purpose',
      fundProgramme: 'Fund or programme',
      owner: 'Owner',
      startDate: 'Start date',
      endDate: 'End date',
      channels: 'Channels',
      country: 'Country',
      region: 'State',
      city: 'City',
      pincode: 'Zip code',
      reminderDaysBefore: 'Reminder days before start date',
      reminderTime: 'Reminder time',
      publicDescription: 'Public description',
      termsNotice: 'Terms and notice',
    };
    return Object.entries(r)
      .filter(([, ok]) => !ok)
      .map(([key]) => labels[key]);
  });

  // ================= Decision / review =================

  /** Before-and-after values, effective permission, evidence, reason and resulting state. */
  protected readonly decisionReview = computed(() => ({
    before: this.lifecycleState() === 'No record' ? 'No record' : 'Draft (saved)',
    after: 'Draft submitted for activation',
    effectivePermission: 'cam.campaigns.submit',
    evidence: `${this.stableReference() ?? 'Unsaved draft'} · ${this.concurrencyVersion()} · ${this.lastSaved() ?? 'not yet saved'}`,
    reason: 'Submit the controlled activation configuration',
    resultingState: 'Submitted · pending activation dependencies',
  }));

  // ================= Actions, eligibility and result =================

  protected readonly actionsMenuOpen = signal(false);
  protected toggleActionsMenu(): void {
    this.actionsMenuOpen.update((v) => !v);
  }

  /** Save draft — Primary; allowed in No record / Draft. */
  protected readonly saveDraftAllowed = computed(
    () =>
      this.permissions().saveDraft &&
      (this.lifecycleState() === 'No record' || this.lifecycleState() === 'Draft') &&
      this.requiredComplete() &&
      this.uiState() !== 'no-access',
  );
  /** Validate — Workflow action; permitted lifecycle state. */
  protected readonly validateAllowed = computed(
    () => this.permissions().validate && this.lifecycleState() === 'Draft' && this.uiState() !== 'no-access',
  );
  /** Submit — Workflow action; enabled once all required information is captured. */
  protected readonly submitAllowed = computed(
    () =>
      this.permissions().submit &&
      this.requiredComplete() &&
      this.uiState() !== 'no-access',
  );
  /** Delete unused draft — Danger menu; Draft with no downstream reference. */
  protected readonly deleteDraftAllowed = computed(
    () =>
      this.permissions().deleteDraft &&
      this.lifecycleState() === 'Draft' &&
      this.uiState() !== 'no-access',
  );

  /** Populate every wizard field from an existing store record, and mark it as the
   *  record Save Draft writes back to (instead of creating a new one). No-op when
   *  `ref` is absent/unknown — the wizard then starts blank as normal. */
  private loadExistingDraft(ref: string | null): void {
    const record = ref ? this.store.get(ref) : undefined;
    if (!record) return;

    this.campaignName.set(record.name);
    this.setCampaignCode(record.code);
    this.purpose.set(record.purpose ?? '');
    this.purposeHtml.set(record.purpose ?? '');
    this.fundProgramme.set(record.fundProgramme ?? '');
    const ownerRefs =
      record.ownerReferences && record.ownerReferences.length
        ? record.ownerReferences
        : record.ownerReference
          ? [record.ownerReference]
          : [];
    if (ownerRefs.length) {
      this.selectedOwners.set(
        ownerRefs
          .map((ref) => this.ownerCatalogue().find((o) => o.ref === ref))
          .filter((o): o is EligibleRecord => !!o),
      );
    }
    this.startDate.set(record.startDate ?? '');
    this.endDate.set(record.endDate ?? '');
    // THE RECORD ALREADY HOLDS IDS, so these are set directly rather than resolved from a
    // label. The queries are seeded with the stored names so the combos read correctly before
    // their catalogues arrive; the label computeds take over once the lists load.
    //
    // The currency is carried over silently: the draft's own currency outranks the
    // Organisation default, and the API needs one either way.
    if (record.currency) {
      this.currency.set(record.currency);
    }
    this.selectedChannels.set([...(record.channels ?? [])]);
    if (record.country) {
      this.selectedCountry.set(record.country);
      this.countryQuery.set(record.countryName ?? '');
      this.loadStates(record.country);
    }
    this.region.set(record.region ?? '');
    this.regionQuery.set(record.regionName ?? '');
    if (record.region) this.loadCities(record.region);
    this.city.set(record.city ?? '');
    this.cityQuery.set(record.cityName ?? '');
    this.pincode.set(record.pincode ?? '');
    this.activationMode.set(record.activationMode ?? 'manual');
    this.reminderDaysBefore.set(record.reminderDaysBefore ?? 3);
    this.reminderTime.set(record.reminderTime ?? '');
    this.publicDescription.set(record.publicDescription ?? '');
    this.publicDescriptionHtml.set(record.publicDescriptionHtml ?? '');
    this.termsNotice.set(record.termsNotice ?? '');
    this.termsNoticeHtml.set(record.termsNoticeHtml ?? '');

    this.stableReference.set(record.code);
    this.lifecycleState.set('Draft');
    this.draftVersion.set('Draft v1 — saved');
  }

  // ----- Save draft -----
  /** Persist every captured wizard field into the shared store record. */
  /**
   * Writes the captured fields to the store and reports the outcome.
   *
   * `onSaved` CARRIES THE REFERENCE rather than the caller reading it from this method's return
   * value. On the edit path the callback fires SYNCHRONOUSLY - an existing record already has
   * its API id, so there is nothing to wait for - and a caller writing
   * `const ref = this.persistToStore(..., () => use(ref))` would touch `ref` inside its own
   * temporal dead zone and throw. Handing the reference to the callback removes the trap
   * instead of documenting it.
   */
  private persistToStore(
    ref: string | null,
    status: CampaignStatus = 'Draft',
    onSaved?: (outcome: {
      readonly saved: boolean;
      readonly reference: string;
      readonly error?: string;
    }) => void,
  ): string {
    const name = this.campaignName().trim();
    const ownerRefs = this.selectedOwners().map((o) => o.ref);
    const ownerRef = ownerRefs[0];
    const role = this.user.role();
    const fields = {
      name: name || 'Untitled campaign',
      purpose: this.purpose(),
      status,
      ...(ownerRef ? { ownerReference: ownerRef } : {}),
      ownerReferences: ownerRefs.length ? ownerRefs : undefined,
      fundProgramme: this.fundProgramme().trim() || undefined,
      startDate: this.startDate(),
      endDate: this.endDate(),
      // TARGET AND BUDGET ARE OMITTED, not set to a default. The keys must be ABSENT rather
      // than undefined: the store merges `{ ...current, ...patch }`, so a present-but-undefined
      // key would blank a stored target on every edit. Absent, a create takes the store's
      // defaults (0 and null) and an edit keeps what the record already holds.
      //
      // IDS GO TO THE SERVER, NAMES GO ON THE SCREEN. These used to hold the label, which
      // is what the API rejected; the `*Name` twins carry what the detail page should print.
      currency: this.currency() || undefined,
      currencyName: this.currency() ? this.currencyLabel() : undefined,
      channels: [...this.selectedChannels()],
      channelNames: this.selectedChannelLabels(),
      country: this.selectedCountry() || undefined,
      countryName: this.selectedCountry() ? this.selectedCountryLabel() : undefined,
      regionLabel: this.selectedCountry() ? this.regionLabel() : undefined,
      region: this.region() || undefined,
      regionName: this.region() ? this.selectedRegionLabel() : undefined,
      city: this.city() || undefined,
      cityName: this.city() ? this.selectedCityLabel() : undefined,
      pincode: this.pincode() || undefined,
      publicDescription: this.publicDescription() || undefined,
      publicDescriptionHtml: this.publicDescriptionHtml() || undefined,
      termsNotice: this.termsNotice() || undefined,
      termsNoticeHtml: this.termsNoticeHtml() || undefined,
      activationMode: this.activationMode(),
      reminderDaysBefore: this.reminderDaysBefore() ?? undefined,
      reminderTime: this.reminderTime() || undefined,
      // An Approver creating a campaign is named as its own accountable manager; anyone else
      // leaves it unset for the notification fallback, which routes to the Approver role.
      //
      // THE NAME CHANGED WITH THE CATALOGUE. This read 'Campaign Manager', which no token
      // carries since the role set was cut to four - so the field was silently never set and
      // every campaign fell through to the fallback.
      ...(role === 'Approver' ? { managerReference: this.user.reference() } : {}),
    };
    if (ref) {
      // The stable reference (store key) is never rewritten from an in-progress edit
      // only the user-typed Campaign code at the moment of first save becomes the key.
      this.store.update(ref, fields);

      // An existing record already has its API id, so a caller may act on it at once.
      onSaved?.({ saved: true, reference: ref });
      return ref;
    }
    const code = this.campaignCode();

    return this.store.create(
      {
        ...fields,
        code,
        createdByRef: this.user.reference(),
        createdByRole: role,
      },
      onSaved && ((outcome) => onSaved({ ...outcome, reference: code })),
    );
  }

  /** Save one draft, preserve values, show stable reference, indicate remaining required info. */
  /**
   * Saves the draft.
   *
   * "SAVED" IS ANNOUNCED ONLY ONCE THE SERVER HAS SAVED IT. This reported success on the line
   * after the call, while the request was still in flight, so a rejected create still said
   * "Draft saved" and gave a reference for a campaign that did not exist.
   */
  protected saveDraft(): void {
    if (!this.saveDraftAllowed()) return;
    this.actionsMenuOpen.set(false);

    this.persistToStore(this.stableReference(), 'Draft', (outcome) => {
      if (!outcome.saved) {
        this.toast.show(
          'Draft not saved',
          outcome.error ?? 'The campaign could not be saved.',
          'error',
        );
        this.uiState.set('validation');
        return;
      }

      const ref = outcome.reference;

      this.stableReference.set(ref);
      this.lifecycleState.set('Draft');
      this.concurrencyVersion.update((v) => `Version ${Number(v.replace(/\D/g, '')) + 1}`);
      this.draftVersion.set(`Draft v${this.concurrencyVersion().replace(/\D/g, '')} — saved`);
      this.lastSaved.set('Today, just now · IST');
      this.successRef.set(ref);
      this.toast.show('Draft saved', `Reference ${ref} saved.`, 'success');
      this.uiState.set('ready');
    });
  }

  // ----- Validate -----
  /** Refresh or change only the authorised record in scope; show confirmed result. */
  protected validate(): void {
    if (!this.validateAllowed()) return;
    this.actionsMenuOpen.set(false);
    if (!this.requiredComplete()) {
      this.uiState.set('validation');
      // Focus the first step that still holds a missing required field.
      if (!this.step1Complete()) this.stepIndex.set(0);
      else if (!this.step2Complete()) this.stepIndex.set(1);
      return;
    }
    this.successRef.set(this.stableReference() ?? '—');
    this.toast.show('Validation passed', 'All required information is complete.', 'success');
    this.uiState.set('ready');
  }

  // ----- Review -----
  /** Move to the Decision / review step; refresh only the authorised record in scope. */
  protected review(): void {
    this.actionsMenuOpen.set(false);
    this.stepIndex.set(this.totalSteps - 1);
    this.uiState.set('ready');
  }

  // ----- Submit -----
  protected readonly submitDialogOpen = signal(false);
  /**
   * Opens the submit confirmation, or explains why it will not.
   *
   * IT USED TO REFUSE IN SILENCE. `submitAllowed()` is three conditions - the submit permission,
   * every required field captured, and not being in the no-access state - and this set the
   * 'validation' state for all three. The validation banner only renders
   * `@if (uiState() === 'validation' && !requiredComplete())`, so when the form WAS complete and
   * the refusal came from the permission, Submit did nothing at all: no dialog, no banner, no
   * toast, no console entry. The screen looked broken rather than refused, which is exactly how
   * "I filled everything in and Submit does nothing" happens.
   */
  protected requestSubmit(): void {
    if (this.uiState() === 'no-access') {
      return;
    }

    if (!this.requiredComplete()) {
      // Missing required information routes to the validation state, which lists the fields.
      this.uiState.set('validation');
      this.markEveryStepAttempted();
      this.toast.show(
        'Some required information is missing',
        `${this.remainingRequired().length} field(s) still needed: `
        + `${this.remainingRequired().slice(0, 3).join(', ')}`
        + `${this.remainingRequired().length > 3 ? '\u2026' : ''}`,
        'warning');
      return;
    }

    if (!this.permissions().submit) {
      this.toast.show(
        'Not permitted',
        'Submitting a campaign for approval needs the cam.campaigns.submit permission. '
        + 'Save it as a draft and ask somebody who holds that permission to submit it.',
        'error');
      return;
    }

    this.actionsMenuOpen.set(false);
    this.submitDialogOpen.set(true);
  }

  /** Turns on the red highlighting for every step, so the missing fields are visible on each. */
  private markEveryStepAttempted(): void {
    this.stepAttempted.set(this.steps.map(() => true));
  }
  protected cancelSubmit(): void {
    this.submitDialogOpen.set(false);
  }
  /**
   * Execute idempotently; show stable reference, committed result, pending dependency,
   * next safe action. Tiered role-based approval: a Super Admin's submission is
   * auto-approved and moves straight to Scheduled; a Campaign Manager or Campaign
   * Owner's submission goes to Submitted and waits for a Super Admin (or, for records
   * they didn't create themselves, a Campaign Manager) to approve it.
   */
  /**
   * Saves the campaign, then submits it for approval.
   *
   * THE SUBMIT WAITS FOR THE SAVE, and that ordering is the entire fix here. This used to call
   * `persistToStore` and `submitForApproval` on consecutive lines. `persistToStore` returns the
   * campaign's CODE synchronously while the create request is still in flight, and the store
   * cannot map a code to an API id until that request comes back - so the submit found no id,
   * returned silently, and the campaign sat in Draft for ever. It never appeared in a Campaign
   * Manager's approval queue, and nothing anywhere said why.
   *
   * IT ALSO NO LONGER ANNOUNCES SUCCESS BEFORE THE SERVER HAS AGREED. The old version toasted,
   * set the lifecycle state and navigated regardless of the outcome, which is how a create
   * rejected with a 400 still looked exactly like a successful one.
   */
  protected confirmSubmit(): void {
    this.submitDialogOpen.set(false);
    this.uiState.set('ready');

    // Persist first (still Draft) so every captured field is on the record, then hand the
    // lifecycle transition to the store's submitForApproval rule — the single source of truth
    // for the approval workflow and its notifications, not a status flip done locally here.
    this.persistToStore(this.stableReference(), 'Draft', (outcome) => {
      if (!outcome.saved) {
        this.toast.show(
          'Campaign not submitted',
          outcome.error ?? 'The campaign could not be saved, so it was not submitted.',
          'error',
        );
        this.uiState.set('validation');
        return;
      }

      const ref = outcome.reference;

      this.store.submitForApproval(ref, this.user.role(), this.user.reference());

      this.stableReference.set(ref);

      // SUBMITTED, NOT DRAFT. This set the header's lifecycle state back to 'Draft' immediately
      // after a successful submit, so the one screen that had just sent the campaign for approval
      // was also the one screen still calling it a draft.
      this.lifecycleState.set('Submitted');
      this.lastSaved.set('Today, just now · IST');
      this.successRef.set(ref);
      this.toast.show('Submitted for approval', `${ref} is with its approver.`, 'success');

      // Return to the Campaign Register, which surfaces a persistent success banner via the
      // ?created reference.
      this.router.navigate(['/app/fundraising/campaigns/campaign-register'], {
        queryParams: { created: ref },
      });
    });
  }

  // ----- Delete unused draft — Danger; named reason + consequence preview -----
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteReason = signal('');
  protected readonly deleteReasonMin = 10;
  protected readonly deleteReasonMax = 2000;
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.deleteReasonMin && len <= this.deleteReasonMax;
  });
  protected requestDeleteDraft(): void {
    if (!this.deleteDraftAllowed()) return;
    this.actionsMenuOpen.set(false);
    this.deleteReason.set('');
    this.deleteDialogOpen.set(true);
  }
  protected cancelDeleteDraft(): void {
    this.deleteDialogOpen.set(false);
  }
  /** Require named reason and consequence preview; a true delete from the shared store. */
  protected confirmDeleteDraft(): void {
    if (!this.deleteReasonValid()) return;
    const ref = this.stableReference();
    if (ref) {
      this.store.delete(ref);
    }
    this.deleteDialogOpen.set(false);
    this.lifecycleState.set('No record');
    this.stableReference.set(null);
    this.lastSaved.set(null);
    this.draftVersion.set('Draft v0 — not yet saved');
    this.successRef.set('Deleted');
    this.toast.show('Draft deleted', ref ? `${ref} was removed.` : 'The unused draft was removed.', 'success');
    this.uiState.set('ready');

    // The record it was editing no longer exists, so staying on its form is staying on a screen
    // about nothing. The register is where the remaining campaigns are.
    this.leaveToRegister();
  }

  /**
   * Discard — leaves the wizard.
   *
   * IT USED TO STAY. With no draft saved yet it called `resetForm()`, which blanks every field
   * and sets `stepIndex` back to zero — so the only visible effect of pressing Discard was the
   * progress bar sliding back to step one, on a form the person had just decided to abandon.
   * They were still in the wizard, with no way out but the sidebar.
   *
   * Discard now returns to the Campaign Register, which is where somebody who has decided not to
   * create a campaign wants to be. The form is reset first so re-entering the wizard starts
   * clean rather than resuming abandoned input.
   *
   * A SAVED DRAFT STILL GOES THROUGH DELETE, because a committed record needs a named reason and
   * a consequence preview before it disappears; `confirmDeleteDraft` navigates once that is done.
   */
  protected discard(): void {
    if (this.lifecycleState() === 'Draft') {
      this.requestDeleteDraft();
      return;
    }

    this.resetForm();
    this.leaveToRegister();
  }

  /** Back to the Campaign Register. */
  private leaveToRegister(): void {
    this.router.navigate(['/app/fundraising/campaigns/campaign-register']);
  }

  private resetForm(): void {
    this.campaignName.set('');
    this.campaignCode.set('');
    this.selectedOwners.set([]);
    this.ownerQuery.set('');
    this.ownerOpen.set(false);
    this.purpose.set('');
    this.purposeHtml.set('');
    this.purposeSeed.set('');
    this.fundProgramme.set('');
    this.startDate.set('');
    this.endDate.set('');
    this.selectedChannels.set([]);
    this.clearCountry();
    this.city.set('');
    this.pincode.set('');
    this.activationMode.set('manual');
    this.reminderDaysBefore.set(3);
    this.reminderTime.set('');
    this.publicDescription.set('');
    this.publicDescriptionHtml.set('');
    this.descSeed.set('');
    this.descLineHeight.set('1.6');
    this.termsNotice.set('');
    this.termsNoticeHtml.set('');
    this.termsSeed.set('');
    this.termsLineHeight.set('1.6');
    this.previewField.set(null);
    this.previewEditing.set(false);
    this.stepIndex.set(0);
  }

  // ================= UI state demonstrability =================

  protected readonly uiState = signal<UiState>('ready');
  protected readonly successRef = signal('—');
  protected dismissState(): void {
    this.uiState.set('ready');
  }

  // ================= Controlled catalogues =================

  /**
   * Loads the currency, country and channel catalogues.
   *
   * TWO CALLS, BOTH CACHED for the life of the application by the services behind them, so
   * re-entering the wizard costs nothing. States and cities are deliberately NOT loaded here:
   * they depend on a choice the user has not made yet.
   */
  /**
   * Chooses the currency the user no longer can.
   *
   * THE API STILL REQUIRES ONE. `CreateCampaignRequest.CurrencyId` is a non-empty Guid and its
   * validator says so, so withdrawing the Target & Budget step without this would send an empty
   * string where a Guid belongs and every create would come back 400 - which is exactly the
   * failure this wizard was fixed for. The Organisation's own default currency is the honest
   * answer; the first row in the catalogue is the fallback when it names one that is not in it.
   *
   * IT NEVER OVERWRITES A CHOSEN VALUE, so a draft saved while the step was still on screen
   * keeps the currency it was saved with.
   */
  private applyDefaultCurrency(currencies: readonly MasterLookup[]): void {
    if (this.currency() || currencies.length === 0) {
      return;
    }

    const preferred = (this.organisation.current()?.defaultCurrency ?? '').trim().toUpperCase();
    const match = preferred
      ? currencies.find((currency) => currency.code?.toUpperCase() === preferred)
      : undefined;

    this.currency.set((match ?? currencies[0]).id);
  }

  private loadReferenceCatalogues(): void {
    // TWO CALLS RATHER THAN ONE, because the ungated lookup routes are per-list. Both degrade to
    // an empty array instead of throwing, so the error branches that used to catch a 403 are
    // gone; an empty country list is what now stands for "the catalogue could not be read", and
    // it is reported rather than left as a picker with nothing in it and no explanation.
    this.masters.getCurrencies().subscribe((currencies) => {
      this.currencyCatalogue.set(
        currencies.map((currency) => ({
          ref: currency.id,
          label: `${currency.code} — ${currency.name}`,
        })),
      );

      this.applyDefaultCurrency(currencies);
    });

    this.masters.getCountries().subscribe((countries) => {
      this.countryCatalogue.set(this.toOptions(countries));

      if (countries.length === 0) {
        this.referenceError.set(
          'The currency and country lists could not be loaded. Reload the page to try again.',
        );
      }
    });

    this.campaignApi.getReferenceData().subscribe({
      next: (reference) => {
        // ACTIVE CHANNELS ONLY. A retired channel is one the API will refuse on the way back
        // in, so offering it produces a selection the create call rejects.
        this.channelCatalogue.set(
          reference.channels
            .filter((channel) => channel.isActive)
            .map((channel) => ({ ref: channel.id, label: channel.name })),
        );
      },
      error: () =>
        this.referenceError.set(
          'The channel list could not be loaded. Reload the page to try again.',
        ),
    });
  }

  private loadStates(countryId: string): void {
    // An empty list on failure is honest here: `regionValid` then treats the field as satisfied,
    // exactly as it does for a country that genuinely has no states, and the optional `stateId`
    // goes up as null rather than as something invented. The service already degrades to an
    // empty array rather than throwing, so that path needs no error branch of its own.
    this.masters.getStates(countryId).subscribe((states) => {
      this.regionOptions.set(this.toOptions(states));
    });
  }

  private loadCities(stateProvinceId: string): void {
    this.masters.getCities(stateProvinceId).subscribe((cities) => {
      this.cityOptions.set(this.toOptions(cities));
    });
  }

  /** A master lookup row as a picker option. Active rows only — see the channel note above. */
  private toOptions(rows: readonly MasterLookup[]): readonly MasterOption[] {
    return rows
      .filter((row) => row.status === 'active')
      .map((row) => ({ ref: row.id, label: row.name }));
  }

  constructor() {
    this.loadReferenceCatalogues();

    // Editing an existing Draft — opened from the Campaign Register's "Edit" row action
    // with ?ref={code}. Pre-fills every field from the shared store record so Save
    // Draft below writes back to the SAME record instead of creating a new one.
    this.loadExistingDraft(this.route.snapshot.queryParamMap.get('ref'));

    // Re-seed the rich-text editor from the stored markup whenever the Publication step
    // (index 2 since Target & Budget was withdrawn) becomes active, so leaving and returning to
    // the step preserves formatting without resetting the caret mid-typing (descSeed only
    // changes on step entry).
    effect(() => {
      if (this.stepIndex() === 0) {
        untracked(() => {
          this.purposeSeed.set(this.purposeHtml());
        });
      } else if (this.stepIndex() === 2) {
        untracked(() => {
          this.descSeed.set(this.publicDescriptionHtml());
          this.termsSeed.set(this.termsNoticeHtml());
        });
      }
    });
    // No access must hide the record, fields, counts, actions and search — never a
    // disabled-only affordance, matching every other CAM page.
    effect(() => {
      const canView = this.permissions().view;
      const current = untracked(this.uiState);
      if (!canView && current !== 'no-access') {
        this.uiState.set('no-access');
      } else if (canView && current === 'no-access') {
        this.uiState.set('ready');
      }
    });
  }

  // ================= Persistent outcome =================

  /** A toast may support but cannot replace this persistent confirmation. */
  protected readonly persistentOutcome = computed(() => ({
    reference: this.stableReference() ?? 'Unsaved draft',
    state: this.lifecycleState(),
    effectiveTime: this.lastSaved() ?? 'not yet saved',
    downstreamStatus: this.requiredComplete()
      ? 'All required information captured'
      : `${this.remainingRequired().length} required field(s) remaining`,
    owner: this.selectedOwners().map((o) => o.label).join(', ') || '—',
    nextAction: this.requiredComplete() ? 'Submit for activation' : 'Complete required information',
  }));
}
