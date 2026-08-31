// ================= Campaign-related shared models =================

import type { CampaignRole } from '../services/current-user.service';

/**
 * Campaign lifecycle status — the doc 03 §5.5 / §7.5 canonical 9-state list
 * (previously a 5-value mockup-derived catalogue; migrated to match the
 * source-of-truth lifecycle table shared by every CAM screen).
 */
export type CampaignStatus =
  | 'Draft'
  | 'Submitted'
  | 'Approved'
  | 'Scheduled'
  | 'Active'
  | 'Paused'
  | 'Closing'
  | 'Closed'
  | 'Cancelled';

/** UI states used across campaign components. */
export type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Effective permissions for campaign operations. */
export interface EffectivePermissions {
  readonly view: boolean;
  readonly operate: boolean;
}

/** Detail tab definition. */
export interface DetailTab {
  readonly key: string;
  readonly label: string;
}

/** Fact display item with optional tone. */
export interface Fact {
  readonly label: string;
  readonly value: string;
  readonly tone?: 'good' | 'blue' | 'gold' | 'muted';
}

/** Source row for campaign channels. */
export interface SourceRow {
  readonly name: string;
  readonly raised: number;
  readonly share: number;
  readonly donations: number;
  readonly tone: string;
}

/** History/generic row display item. */
export interface HistoryRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}

/** Activity item with tone. */
export interface ActivityItem {
  readonly title: string;
  readonly detail: string;
  readonly time: string;
  readonly tone: string;
}

/** A scope-aware selector option with a stable reference and disambiguating context. */
export interface OwnerOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
  readonly initials: string;
  readonly tone: string;
  /** Optional profile picture; when present the avatar shows the image instead of initials. */
  readonly avatarUrl?: string;
  /** Optional contact details, shown in the single-owner details popup. */
  readonly email?: string;
  readonly phone?: string;
}

/** How a campaign becomes Active from Scheduled. A reminder before the start
 *  date is mandatory for either mode (captured via reminderDaysBefore/reminderTime). */
export type ActivationMode = 'auto' | 'manual';

/** A single campaign record row — the shared, canonical shape (§4.1.2). */
export interface CampaignRecord {
  readonly code: string;
  readonly name: string;
  readonly purpose: string;
  readonly status: CampaignStatus;
  readonly ownerReference: string;
  /** Every accountable owner when a campaign has more than one; ownerReference always holds the first. */
  readonly ownerReferences?: readonly string[];
  readonly startDate: string;
  readonly endDate: string;
  readonly targetAmount: number;
  readonly reconciledAmount: number;
  readonly progress: number;
  /** Not a displayed field — supports the History rule (delete only with no downstream reference). */
  readonly hasDownstreamReference?: boolean;
  /** Not a displayed field — supports the "current user is that draft's creator" delete eligibility rule. */
  readonly createdByRef?: string;

  // ---- Extended campaign detail (channels & sources, publication, activation) ----
  //
  // THE REFERENCE FIELDS BELOW HOLD API IDENTIFIERS, NOT NAMES. `currency`, `country`,
  // `region`, `city` and `channels` are the Guids the CAM API's create and update bodies
  // require, and they used to hold display strings - 'INR', 'India', 'Tamil Nadu', 'Website' -
  // which the API refused with a 400 before the handler ever ran, so no campaign was created
  // at all. Each has a `*Name` twin below carrying what a person should read, because an id is
  // not something to show anybody.
  /** Fund or programme this campaign belongs to — captured on Wizard step 1 (required). */
  readonly fundProgramme?: string;
  /** Currency selected on Wizard step 2. The master-data id. */
  readonly currency?: string;
  /** The selected currency as a person reads it, e.g. "INR — Indian Rupee". */
  readonly currencyName?: string;
  readonly budgetAmount?: number;
  /** Channel master-data ids. */
  readonly channels?: readonly string[];
  /** The selected channels as people read them, e.g. ["Website", "Email"]. */
  readonly channelNames?: readonly string[];
  readonly sources?: readonly string[];
  /** Country master-data id. */
  readonly country?: string;
  /** The selected country's name, e.g. "India". */
  readonly countryName?: string;
  /** Country-correct label for the first-level administrative division (State / Province / Region …). */
  readonly regionLabel?: string;
  /** State / province master-data id. */
  readonly region?: string;
  /** The selected state's name, e.g. "Tamil Nadu". */
  readonly regionName?: string;
  /** City master-data id. */
  readonly city?: string;
  /** The selected city's name, e.g. "Chennai". */
  readonly cityName?: string;
  readonly pincode?: string;
  /** Public-facing description — plain-text mirror + formatted HTML. */
  readonly publicDescription?: string;
  readonly publicDescriptionHtml?: string;
  /** Terms and notice — plain-text mirror + formatted HTML (rich text, up to 20,000 chars). */
  readonly termsNotice?: string;
  readonly termsNoticeHtml?: string;
  readonly activationMode?: ActivationMode;
  readonly reminderDaysBefore?: number;
  readonly reminderTime?: string;

  // ---- Approval / audit metadata ----
  /** The campaign manager accountable for this campaign (notification recipient). */
  readonly managerReference?: string;
  /** The role that created the campaign — drives the tiered approval rule. */
  readonly createdByRole?: CampaignRole;
  readonly approvedByRef?: string;
  readonly createdAt?: string;
  readonly updatedAt?: string;
  /** False for a freshly created campaign; true once its content is edited (Created vs Updated). */
  readonly wasEdited?: boolean;
}

/** Sortable column configuration. */
export interface SortableColumn {
  readonly key: keyof CampaignRecord;
  readonly label: string;
  readonly sortable: boolean;
  readonly numeric?: boolean;
}

export type SortDirection = 'asc' | 'desc';

/** Campaign permissions for register. */
export interface CampaignRegisterPermissions {
  readonly view: boolean;
  readonly create: boolean;
  readonly export: boolean;
  readonly deleteDraft: boolean;
}

/** Wizard permissions. */
export interface CampaignWizardPermissions {
  readonly view: boolean;
  readonly saveDraft: boolean;
  readonly validate: boolean;
  readonly submit: boolean;
  readonly deleteDraft: boolean;
}

/** Eligible record for wizard selector. */
export interface EligibleRecord {
  readonly ref: string;
  readonly label: string;
  readonly context: string;
}

/** Currency catalogue item. */
export interface CurrencyItem {
  readonly ref: string;
  readonly label: string;
}

/** Channel catalogue item. */
export interface ChannelItem {
  readonly ref: string;
  readonly label: string;
}

/** Step configuration for wizard. */
export interface WizardStep {
  readonly title: string;
  readonly caption: string;
}

/** Related tab configuration. */
export interface RelatedTab {
  readonly key: string;
  readonly label: string;
  readonly rows?: readonly HistoryRow[];
}

/** Persistent outcome display. */
export interface PersistentOutcome {
  readonly reference: string;
  readonly state: string;
  readonly effectiveTime: string;
  readonly downstreamStatus: string;
  readonly owner: string;
  readonly nextAction: string;
}