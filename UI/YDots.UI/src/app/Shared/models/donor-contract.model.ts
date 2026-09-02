/**
 * The typed contract for the Donors and Leads service.
 *
 * IT MIRRORS THE SERVER'S DTOs FIELD FOR FIELD, and the field NAMES are copied rather than
 * improved. A name invented on this side is `undefined` at runtime, which is exactly the failure
 * this file exists to prevent.
 *
 * FOUR CONVENTIONS RUN THROUGH THE WHOLE SECTION AND ARE WORTH READING ONCE.
 *
 * ONE CALL PER SCREEN. Each screen's GET returns its rows, every dropdown it needs, its totals
 * and its permitted actions in a single payload - `LeadWorkQueueResponse`, `AssignmentBoardResponse`
 * and their siblings. That is deliberate: a screen that made six calls would render six times and
 * could show a filter list that disagreed with the rows it was filtering.
 *
 * `permittedActions` IS THE SERVER'S ANSWER to what this caller may do next, computed from the
 * record's state AND the caller's permissions. Render buttons from it. A screen that decides for
 * itself eventually draws one the API refuses.
 *
 * MASKING IS REPORTED, NOT GUESSED. Contact details, evidence references and preferred contact
 * times arrive masked unless the caller holds the matching permission, and an `is*Masked` flag
 * says which. The screen shows the flag rather than trying to detect asterisks.
 *
 * `version` IS THE OPTIMISTIC-CONCURRENCY STAMP. Every state-changing call sends the version it
 * read back as `expectedVersion`; a stale one answers 409 rather than overwriting a colleague.
 * Several DON requests make it OPTIONAL, which is the server's choice - omitting it means "I did
 * not read a version", and the handler decides whether that is acceptable for that action.
 */

import { PagedResponse } from './api-response.model';

// =============================================================================================
// Shared shapes
// =============================================================================================

/**
 * One option in a selector.
 *
 * NOTE `value`/`label`, NOT `id`/`name`. DON's LookupItem differs from IAM's on purpose - it
 * carries an enum's string value rather than a row's identifier - and copying IAM's names here
 * would produce a list of `undefined`s.
 */
export interface DonLookupItem {
  value: string;
  label: string;
  description?: string | null;
}

export type { PagedResponse };

/** The body of every action that needs only a named reason. 10 to 2000 characters. */
export interface ReasonRequest {
  reason: string;
  expectedVersion?: number | null;
}

// =============================================================================================
// Donors - SCR-DON-001 and the directory
// =============================================================================================

export type DonorType = 'Individual' | 'Organisation' | 'Trust' | 'Corporate';

export interface CreateDonorRequest {
  /** Optional: send it and an importer keeps its own reference, omit it and the server allocates. */
  donorNumber?: string | null;
  donorType: DonorType;
  firstName?: string | null;
  lastName?: string | null;
  organisationName?: string | null;
  primaryEmail?: string | null;
  primaryPhone?: string | null;
  preferredLanguage: string;
  doNotContact: boolean;
}

/** `donorNumber` is absent on purpose: the reference never changes. */
export interface UpdateDonorRequest {
  donorType: DonorType;
  firstName?: string | null;
  lastName?: string | null;
  organisationName?: string | null;
  primaryEmail?: string | null;
  primaryPhone?: string | null;
  preferredLanguage: string;
  doNotContact: boolean;
  expectedVersion: number;
}

/** Six members and nothing else, so a hundred-row page stays small and carries nothing sensitive. */
export interface DonorListItem {
  id: string;
  displayCode: string;
  displayName: string;
  status: string;
  /** Who is accountable for this donor. Carried on the grid row so the list can show and filter it. */
  relationshipOwnerUserId: string | null;
  relationshipOwnerName: string | null;
  updatedAtUtc: string;
  version: number;

  /**
   * Contact and giving, for the columns the Donor List shows.
   *
   * MASKED BY THE SERVER unless the caller holds don.donors.view-sensitive-contact, and
   * `isContactMasked` says which. Never unmask either in the browser.
   */
  mobileNumber: string | null;
  emailAddress: string | null;
  campaignName: string | null;
  lastDonationAmount: number | null;
  lastDonationAtUtc: string | null;
  /** RECEIVED ONLY. Pledged money has not arrived and is not counted here. */
  lifetimeGiving: number;
  currency: string;
  /** Overdue | Due Today | Tomorrow | None - recomputed on every read. */
  followUpStatus: string;
  /** Verified | Pending | Failed | Expired. */
  verificationStatus: string;
  /** Granted | Partial | Withdrawn | Not provided. */
  consentStatus: string;
  /** A consent has expired or been withdrawn, so the permitted channels have changed. */
  consentReviewRequired: boolean;
  isContactMasked: boolean;
}

export interface DonorDetail {
  id: string;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  donorNumber: string;
  donorType: DonorType;
  firstName: string | null;
  lastName: string | null;
  organisationName: string | null;
  primaryEmail: string | null;
  primaryPhone: string | null;
  preferredLanguage: string;
  status: string;
  doNotContact: boolean;
  displayName: string;
  approvalState: string;
  relationshipOwnerUserId: string | null;
  relationshipOwnerName: string | null;
  notes: string | null;
  /** True when the address above is masked. The screen says so rather than guessing. */
  isEmailMasked: boolean;
  isPhoneMasked: boolean;
  permittedActions: string[];
}

export interface DonorLookup {
  id: string;
  displayName: string;
  status: string;
}

export interface DonorSearchFilter {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  status?: string | null;
  donorType?: string | null;
  approvalState?: string | null;
  relationshipOwnerUserId?: string | null;
}

// =============================================================================================
// Leads - SCR-DON-002 and the work queue
// =============================================================================================

/**
 * The consent block embedded in lead capture.
 *
 * IT IS PART OF THE LEAD FORM, not a separate screen, and that is the approved pattern: a lead
 * and its permission-to-contact evidence are captured in one act rather than two. Nothing below
 * `collectConsent` is read until that toggle is true, and no legal acknowledgement is
 * pre-selected.
 */
export interface LeadConsentRequest {
  collectConsent: boolean;
  emailConsent: boolean;
  smsConsent: boolean;
  whatsAppConsent: boolean;
  phoneCallConsent: boolean;
  consentSource?: string | null;
  consentDateUtc?: string | null;
  consentNotes?: string | null;
  consentEvidenceReference?: string | null;
  /** Why the permission is being asked for. 10 to 2000 characters when consent is collected. */
  purpose?: string | null;
}

export interface CreateLeadRequest {
  firstName: string;
  lastName?: string | null;
  /** E.164. A lead needs at least one way of being reached - this or the e-mail. */
  mobileNumber?: string | null;
  emailAddress?: string | null;
  preferredLanguage?: string | null;
  city?: string | null;
  geographyCode?: string | null;
  /** Required. Must be an active campaign inside the caller's scope. */
  campaignId: string;
  source: string;
  notes?: string | null;
  preferredContactTimeUtc?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  teamCode?: string | null;
  nextAction?: string | null;
  nextActionDueUtc?: string | null;
  consent?: LeadConsentRequest | null;
}

export interface UpdateLeadRequest {
  firstName: string;
  lastName?: string | null;
  mobileNumber?: string | null;
  emailAddress?: string | null;
  preferredLanguage?: string | null;
  city?: string | null;
  geographyCode?: string | null;
  campaignId: string;
  source: string;
  notes?: string | null;
  preferredContactTimeUtc?: string | null;
  nextAction?: string | null;
  nextActionDueUtc?: string | null;
  consent?: LeadConsentRequest | null;
  expectedVersion: number;
}

export interface LeadListItem {
  id: string;
  leadReference: string;
  /** Name plus a partial contact. Masked unless the caller holds the sensitive-contact permission. */
  nameAndContactPreview: string;
  /** Display name on its own, for the grid's Lead Name column. */
  name: string;
  /** Masked by the SERVER unless the caller holds don.donors.view-sensitive-contact. */
  mobileNumber: string | null;
  /** Masked on the same rule as mobileNumber. Never unmask either in the browser. */
  emailAddress: string | null;
  campaignName: string | null;
  ownerUserId: string | null;
  ownerName: string | null;
  status: string;
  source: string | null;
  /** Cold | Warm | Hot. With donationPotential this replaces formal qualification. */
  temperature: string;
  /** Low | Medium | High. A band, never a rupee amount. */
  donationPotential: string;
  /** 0-100, recomputed server-side on every read so its recency component is never stale. */
  healthScore: number;
  nextAction: string | null;
  nextActionDueUtc: string | null;
  slaState: string;
  lastContactOutcome: string;
  preferredLanguage: string;
  /** True once a donation converted this lead; the queue drops it and the Donor List gains it. */
  isConverted: boolean;
  /** The donor this lead became, so a row can link straight through to Donor 360. */
  convertedDonorId: string | null;
  updatedAtUtc: string;
  version: number;
  isContactMasked: boolean;
  permittedActions: string[];
}

/**
 * The six summary cards on the lead work queue.
 *
 * COUNTED SERVER-SIDE OVER THE WHOLE SCOPE. Do not recompute these from `leads.items` - that is
 * one page, so the cards would disagree with themselves every time somebody paged.
 */
export interface LeadQueueSummary {
  totalLeads: number;
  unassignedLeads: number;
  assignedLeads: number;
  hotLeads: number;
  convertedLeads: number;
  highDonationPotential: number;
}

export interface LeadConsentSummary {
  id: string;
  channel: string;
  consentState: string;
  status: string;
  noticeVersion: string;
  effectiveAtUtc: string;
  expiryAtUtc: string | null;
}

export interface LeadDetail {
  id: string;
  leadReference: string;
  firstName: string;
  lastName: string | null;
  mobileNumber: string | null;
  emailAddress: string | null;
  preferredLanguage: string;
  city: string | null;
  geographyCode: string | null;
  campaignId: string;
  campaignName: string | null;
  source: string;
  consentState: string;
  consentEvidenceReference: string | null;
  notes: string | null;
  preferredContactTimeUtc: string | null;
  duplicateCandidateSummary: string | null;
  status: string;
  ownerUserId: string | null;
  ownerName: string | null;
  teamCode: string | null;
  nextAction: string | null;
  nextActionDueUtc: string | null;
  slaState: string;
  lastContactOutcome: string;
  lastContactedAtUtc: string | null;
  acceptedAtUtc: string | null;
  qualifiedAtUtc: string | null;
  convertedDonorId: string | null;
  convertedAtUtc: string | null;
  closureReason: string | null;
  isDraft: boolean;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  isContactMasked: boolean;
  isEvidenceMasked: boolean;
  consents: LeadConsentSummary[];
  permittedActions: string[];
}

/**
 * A possible duplicate.
 *
 * DELIBERATELY VAGUE ABOUT THE OTHER PERSON: a category and a route to compare, never their
 * name, e-mail or phone. Naming them would turn a duplicate check into a directory lookup for
 * anybody who can reach the screen.
 */
export interface DuplicateCandidate {
  candidateId: string;
  candidateType: string;
  matchCategory: string;
  confidence: string;
  safeSummary: string;
  comparisonRoute: string;
}

export interface DeduplicateResult {
  leadId: string;
  leadReference: string;
  candidateCount: number;
  state: string;
  message: string;
  candidates: DuplicateCandidate[];
}

export interface LeadLookup {
  id: string;
  leadReference: string;
  displayName: string;
  status: string;
}

// ---- The work queue's own payload ------------------------------------------------------------

export interface LeadWorkQueueResponse {
  screenId: string;
  route: string;
  leads: PagedResponse<LeadListItem>;
  campaignOptions: DonLookupItem[];
  ownerOptions: DonLookupItem[];
  statusOptions: DonLookupItem[];
  slaStateOptions: DonLookupItem[];
  languageOptions: DonLookupItem[];
  contactOutcomeOptions: DonLookupItem[];
  /** Counts per status, for the tabs across the top. Server-side, so they cover every page. */
  statusCounts: Record<string, number>;
  /** The summary cards, scope-wide. See LeadQueueSummary. */
  summary: LeadQueueSummary;
  /** Cold / Warm / Hot, for the temperature filter. */
  temperatureOptions: DonLookupItem[];
  /** Low / Medium / High, for the donation-potential filter. */
  donationPotentialOptions: DonLookupItem[];
  permittedActions: string[];
  activeFilterSummary: string;
  activeScope: string;
  lastRefreshedAtUtc: string;
  state: string;
}

export interface LeadWorkQueueFilter {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  status?: string | null;
  campaignId?: string | null;
  ownerUserId?: string | null;
  slaState?: string | null;
  temperature?: string | null;
  donationPotential?: string | null;
  /** Owner-scoped view: the server resolves this to the caller, which is what My Leads is. */
  onlyMine?: boolean | null;
  /**
   * Unassigned | Assigned - the Lead Queue's own tabs.
   *
   * NOT THE SAME QUESTION AS ownerUserId, which asks "whose?". A null ownerUserId means "do not
   * filter by owner", so it cannot express "has no owner" at all.
   */
  assignmentState?: 'Unassigned' | 'Assigned' | null;
  /** The Converted Leads tab. Converted leads are hidden from the queue by default. */
  isConverted?: boolean | null;
  /** The Recently Added tab, which is a different ordering rather than a filter. */
  newestFirst?: boolean | null;
  preferredLanguage?: string | null;
  lastContactOutcome?: string | null;
  dueFromUtc?: string | null;
  dueToUtc?: string | null;
}

export interface AcceptLeadRequest {
  comment?: string | null;
  expectedVersion?: number | null;
}

export interface AssignLeadRequest {
  newOwnerUserId: string;
  newOwnerName: string;
  teamCode?: string | null;
  /** Required. 10 to 2000 characters. */
  assignmentReason: string;
  effectiveAtUtc?: string | null;
  expectedVersion?: number | null;
}

export interface ContactLeadRequest {
  /** Which channel was actually used. Checked against the consent rows before it is accepted. */
  channel: string;
  outcome: string;
  notes?: string | null;
  occurredAtUtc?: string | null;
  nextAction?: string | null;
  nextActionDueUtc?: string | null;
  expectedVersion?: number | null;
}

export interface QualifyLeadRequest {
  /** Required. 10 to 2000 characters. */
  qualificationNotes: string;
  nextAction?: string | null;
  nextActionDueUtc?: string | null;
  /** True parks the lead in Nurture instead of moving it to Qualified. */
  moveToNurture: boolean;
  expectedVersion?: number | null;
}

export interface ConvertLeadRequest {
  /** Link to this existing donor instead of creating a new one. */
  existingDonorId?: string | null;
  donorType?: string | null;
  /** Required. 10 to 2000 characters. */
  conversionReason: string;
  expectedVersion?: number | null;
}

// ---- Lead capture - SCR-DON-002 --------------------------------------------------------------

export interface LeadCaptureResponse {
  screenId: string;
  route: string;
  lead: LeadDetail | null;
  campaignOptions: DonLookupItem[];
  languageOptions: DonLookupItem[];
  consentChannelOptions: DonLookupItem[];
  consentStateOptions: DonLookupItem[];
  ownerOptions: DonLookupItem[];
  currentNoticeVersion: string;
  duplicateCandidates: DuplicateCandidate[];
  permittedActions: string[];
  activeScope: string;
  state: string;
}

// =============================================================================================
// Donor 360 - SCR-DON-003
// =============================================================================================

export interface DonorContact {
  id: string;
  name: string;
  description: string | null;
  channel: string;
  value: string;
  isPrimary: boolean;
  isVerified: boolean;
  status: string;
  isMasked: boolean;
}

export interface DonorTag {
  id: string;
  code: string;
  name: string;
  description: string | null;
  status: string;
}

export interface IdentityAndContactSummary {
  displayName: string;
  donorType: string;
  primaryEmail: string | null;
  primaryPhone: string | null;
  preferredLanguage: string;
  doNotContact: boolean;
  additionalContacts: DonorContact[];
  tags: DonorTag[];
  isMasked: boolean;
}

export interface RelationshipOwner {
  userId: string;
  name: string | null;
}

export interface ConsentStatus {
  overallState: string;
  grantedChannelCount: number;
  withdrawnChannelCount: number;
  lastRecordedAtUtc: string | null;
  noticeVersion: string | null;
}

export interface CommunicationPreference {
  channel: string;
  consentState: string;
  status: string;
  effectiveAtUtc: string | null;
  expiryAtUtc: string | null;
  publicRecognitionPreference: boolean;
}

/**
 * One row of "Donation totals by stage".
 *
 * IT CARRIES ITS OWN CUT-OFF AND FRESHNESS because the numbers are owned by the PAYMENTS module,
 * not this one. A figure shown without saying how current it is invites somebody to reconcile
 * against it.
 */
export interface DonationTotal {
  stage: string;
  currency: string;
  totalAmount: number;
  transactionCount: number;
  asAtUtc: string;
  refreshedAtUtc: string;
  sourceFreshness: string;
}

export interface CampaignHistoryEntry {
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  leadReference: string;
  convertedAtUtc: string | null;
}

export interface Conversation {
  id: string;
  name: string;
  description: string | null;
  interactionType: string;
  channel: string | null;
  occurredAtUtc: string;
  outcome: string;
  performedByName: string | null;
  status: string;
}

export interface Donor360FollowUp {
  id: string;
  followUpReference: string;
  nextAction: string | null;
  dueAtUtc: string | null;
  priority: string;
  status: string;
  relationshipOwnerName: string | null;
}

export interface DonorPromise {
  id: string;
  reference: string;
  amount: number;
  currency: string;
  promisedAtUtc: string;
  dueAtUtc: string | null;
  status: string;
  campaignName: string | null;
}

export interface DonorDocument {
  id: string;
  reference: string;
  name: string;
  description: string | null;
  classification: string;
  scanStatus: string | null;
  createdAtUtc: string;
  expiresAtUtc: string | null;
}

export interface DuplicateLink {
  mergeCaseId: string;
  reviewReference: string;
  status: string;
  identityConfidence: string;
  decision: string | null;
  comparisonRoute: string;
}

export interface ActivityHistoryEntry {
  id: string;
  actionCode: string;
  targetType: string;
  result: string;
  reason: string | null;
  occurredAtUtc: string;
  correlationId: string;
}

/** One payload per panel, so the screen makes ONE call and every tab already has its content. */
export interface Donor360Response {
  screenId: string;
  route: string;
  donorReference: string;
  donor: DonorDetail;
  identityAndContactSummary: IdentityAndContactSummary;
  relationshipOwner: RelationshipOwner | null;
  consentStatus: ConsentStatus;
  communicationPreferences: CommunicationPreference[];
  donationTotalsByStage: DonationTotal[];
  campaignHistory: CampaignHistoryEntry[];
  conversations: Conversation[];
  followUps: Donor360FollowUp[];
  promises: DonorPromise[];
  documents: DonorDocument[];
  duplicateLinks: DuplicateLink[];
  activityHistory: ActivityHistoryEntry[];
  permittedActions: string[];
  /** Which fields came back masked, named rather than inferred. */
  maskedFields: string[];
  activeScope: string;
  state: string;
}

export interface CreateIntentRequest {
  amount: number;
  currency: string;
  dueAtUtc?: string | null;
  campaignId?: string | null;
  /** Required. 10 to 2000 characters. */
  notes: string;
}

export interface CorrectDonorRequest {
  donorType?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  organisationName?: string | null;
  primaryEmail?: string | null;
  primaryPhone?: string | null;
  /** Send both or neither. A name with no id labels the record with somebody unroutable. */
  relationshipOwnerUserId?: string | null;
  relationshipOwnerName?: string | null;
  preferredLanguage?: string | null;
  doNotContact?: boolean | null;
  /** Required. 10 to 2000 characters. */
  correctionReason: string;
  expectedVersion?: number | null;
}

// =============================================================================================
// Assignment board
// =============================================================================================

export interface AssignmentBoardRow {
  leadId: string;
  leadReference: string;
  leadPreview: string;
  campaignName: string | null;
  currentOwnerUserId: string | null;
  currentOwnerName: string | null;
  suggestedOwnerUserId: string | null;
  suggestedOwnerName: string | null;
  /** Why the board suggests that owner - language, team, load. Shown so the suggestion is auditable. */
  suggestionRationale: string | null;
  currentOwnerOpenWorkCount: number;
  nextAction: string | null;
  nextActionDueUtc: string | null;
  slaState: string;
  preferredLanguage: string;
  teamCode: string | null;
  status: string;
  version: number;
}

export interface OwnerWorkload {
  userId: string;
  name: string;
  teamCode: string | null;
  openWorkCount: number;
  workloadBand: string;
}

export interface AssignmentBoardResponse {
  screenId: string;
  route: string;
  rows: PagedResponse<AssignmentBoardRow>;
  owners: OwnerWorkload[];
  campaignOptions: DonLookupItem[];
  teamOptions: DonLookupItem[];
  languageOptions: DonLookupItem[];
  workloadBandOptions: DonLookupItem[];
  slaStateOptions: DonLookupItem[];
  permittedActions: string[];
  activeFilterSummary: string;
  activeScope: string;
  /** The cap on one bulk route. The server enforces it; the screen shows it before the attempt. */
  bulkRouteMaximumItems: number;
  state: string;
}

export interface AssignmentRequest {
  leadId: string;
  newOwnerUserId: string;
  newOwnerName: string;
  teamCode?: string | null;
  /** Required. 10 to 2000 characters. */
  assignmentReason: string;
  effectiveAtUtc?: string | null;
  expectedVersion?: number | null;
}

export interface BulkRouteRequest {
  leadIds: string[];
  newOwnerUserId: string;
  newOwnerName: string;
  teamCode?: string | null;
  assignmentReason: string;
  effectiveAtUtc?: string | null;
}

export interface BulkRouteItem {
  leadId: string;
  leadReference: string | null;
  routed: boolean;
  outcome: string;
}

/** Every lead is reported separately: partial processing is explicit, never a silent skip. */
export interface BulkRouteResult {
  requestedCount: number;
  routedCount: number;
  skippedCount: number;
  items: BulkRouteItem[];
  message: string;
  state: string;
}

export interface AssignmentHistoryItem {
  id: string;
  previousOwnerUserId: string | null;
  previousOwnerName: string | null;
  newOwnerUserId: string;
  newOwnerName: string;
  assignmentReason: string;
  effectiveAtUtc: string;
  assignedByUserId: string;
  isBulkRoute: boolean;
}

export interface AssignmentHistory {
  leadId: string;
  leadReference: string;
  items: AssignmentHistoryItem[];
}

export interface AssignmentBoardLead {
  lead: LeadDetail;
  history: AssignmentHistory;
}

// =============================================================================================
// Consent and preference centre
// =============================================================================================

export interface ConsentListItem {
  id: string;
  donorId: string | null;
  donorReference: string | null;
  donorDisplayName: string | null;
  leadId: string | null;
  name: string;
  description: string | null;
  purpose: string;
  channel: string;
  consentState: string;
  status: string;
  noticeVersion: string;
  evidenceSource: string;
  evidenceReference: string | null;
  effectiveAtUtc: string;
  expiryAtUtc: string | null;
  publicRecognitionPreference: boolean;
  contactRestrictions: string | null;
  correctionReason: string | null;
  /** A correction supersedes rather than overwrites; this points at the replacement. */
  supersededByConsentId: string | null;
  withdrawnAtUtc: string | null;
  withdrawalReason: string | null;
  capturedByName: string | null;
  createdAtUtc: string;
  version: number;
  isEvidenceMasked: boolean;
  permittedActions: string[];
}

export interface ConsentCentreResponse {
  screenId: string;
  route: string;
  consents: PagedResponse<ConsentListItem>;
  /** Superseded and withdrawn rows. Kept, because a consent trail that hides its past is not one. */
  consentHistory: ConsentListItem[];
  channelOptions: DonLookupItem[];
  consentStateOptions: DonLookupItem[];
  statusOptions: DonLookupItem[];
  currentNoticeVersion: string;
  permittedActions: string[];
  activeFilterSummary: string;
  activeScope: string;
  state: string;
}

export interface GrantConsentRequest {
  donorId: string;
  /** Required. 10 to 2000 characters. */
  purpose: string;
  channel: string;
  evidenceSource: string;
  evidenceReference?: string | null;
  effectiveAtUtc: string;
  expiryAtUtc?: string | null;
  publicRecognitionPreference: boolean;
  contactRestrictions?: string | null;
  description?: string | null;
}

export interface WithdrawConsentRequest {
  reason: string;
  effectiveAtUtc?: string | null;
  expectedVersion?: number | null;
}

/** Supersedes the row with a corrected copy. Nothing is overwritten. */
export interface CorrectConsentRequest {
  correctionReason: string;
  purpose?: string | null;
  evidenceSource?: string | null;
  evidenceReference?: string | null;
  effectiveAtUtc?: string | null;
  expiryAtUtc?: string | null;
  publicRecognitionPreference?: boolean | null;
  contactRestrictions?: string | null;
  expectedVersion?: number | null;
}

// =============================================================================================
// Duplicate review
// =============================================================================================

export interface DuplicateReviewListItem {
  id: string;
  reviewReference: string;
  name: string;
  candidateAName: string;
  candidateBName: string;
  identityConfidence: string;
  status: string;
  decision: string | null;
  createdAtUtc: string;
  decidedAtUtc: string | null;
  version: number;
}

export interface CandidateSummary {
  donorId: string;
  donorNumber: string;
  displayName: string;
  donorType: string;
  status: string;
  preferredLanguage: string;
  createdAtUtc: string;
  maskedEmail: string | null;
  maskedPhone: string | null;
}

export interface DuplicateReviewDetail {
  id: string;
  reviewReference: string;
  name: string;
  description: string | null;
  status: string;
  candidateA: CandidateSummary;
  candidateB: CandidateSummary;
  contactComparison: string | null;
  identityConfidence: string;
  matchingEvidence: string | null;
  conflictingFields: string | null;
  /** What merging would do to the giving history. Shown BEFORE the decision, not after. */
  donationHistoryImpact: string | null;
  consentImpact: string | null;
  decision: string | null;
  decisionReason: string | null;
  survivingDonorId: string | null;
  mergePreview: string | null;
  decidedByUserId: string | null;
  decidedByName: string | null;
  decidedAtUtc: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  version: number;
  isContactComparisonMasked: boolean;
  isEvidenceMasked: boolean;
  permittedActions: string[];
}

export interface DuplicateReviewListResponse {
  screenId: string;
  route: string;
  reviews: PagedResponse<DuplicateReviewListItem>;
  statusOptions: DonLookupItem[];
  confidenceOptions: DonLookupItem[];
  decisionOptions: DonLookupItem[];
  permittedActions: string[];
  activeFilterSummary: string;
  activeScope: string;
  state: string;
}

export interface CreateDuplicateReviewRequest {
  candidateADonorId: string;
  candidateBDonorId: string;
  name: string;
  description?: string | null;
  identityConfidence?: string | null;
  matchingEvidence?: string | null;
}

export interface MergeDecisionRequest {
  decision: string;
  decisionReason: string;
  survivingDonorId?: string | null;
  expectedVersion?: number | null;
}

// =============================================================================================
// Follow-up planner
// =============================================================================================

/**
 * Whether the planned channel is one the donor actually permitted.
 *
 * SHOWN BEFORE THE FOLLOW-UP IS SAVED, not after it is worked. Scheduling a call to somebody who
 * withdrew phone consent is a compliance breach committed by the person who scheduled it, and
 * the warning is what gives them the chance not to.
 */
export interface ConsentWarning {
  hasWarning: boolean;
  level: string;
  message: string;
  permittedChannels: string[];
  prohibitedChannels: string[];
}

export interface FollowUp {
  id: string;
  followUpReference: string;
  donorId: string | null;
  donorReference: string | null;
  donorDisplayName: string | null;
  leadId: string | null;
  leadReference: string | null;
  relationshipOwnerUserId: string;
  relationshipOwnerName: string | null;
  purpose: string | null;
  permittedChannel: string;
  preferredLanguage: string;
  preferredContactTimeUtc: string | null;
  nextAction: string | null;
  dueAtUtc: string | null;
  priority: string;
  notes: string | null;
  consentWarningAcknowledged: boolean;
  consentNoticeVersion: string | null;
  consentAcknowledgedAtUtc: string | null;
  status: string;
  completedAtUtc: string | null;
  completionOutcome: string | null;
  rescheduleReason: string | null;
  cancellationReason: string | null;
  createdAtUtc: string;
  version: number;
  isNotesMasked: boolean;
  isPreferredTimeMasked: boolean;
  consentWarning: ConsentWarning;
  permittedActions: string[];
}

export interface FollowUpPlannerResponse {
  screenId: string;
  route: string;
  followUps: PagedResponse<FollowUp>;
  channelOptions: DonLookupItem[];
  priorityOptions: DonLookupItem[];
  statusOptions: DonLookupItem[];
  languageOptions: DonLookupItem[];
  ownerOptions: DonLookupItem[];
  currentNoticeVersion: string;
  permittedActions: string[];
  activeFilterSummary: string;
  activeScope: string;
  state: string;
}

export interface ScheduleFollowUpRequest {
  donorId?: string | null;
  leadId?: string | null;
  relationshipOwnerUserId?: string | null;
  relationshipOwnerName?: string | null;
  purpose: string;
  permittedChannel: string;
  preferredLanguage?: string | null;
  preferredContactTimeUtc?: string | null;
  nextAction: string;
  dueAtUtc: string;
  priority?: string | null;
  notes?: string | null;
  /** Must be true when the consent warning has one. The server refuses it otherwise. */
  consentWarningAcknowledged: boolean;
}

export interface AssignFollowUpRequest {
  relationshipOwnerUserId: string;
  relationshipOwnerName: string;
  reason: string;
  expectedVersion?: number | null;
}

export interface CompleteFollowUpRequest {
  completionOutcome: string;
  completedAtUtc?: string | null;
  expectedVersion?: number | null;
}

export interface RescheduleFollowUpRequest {
  dueAtUtc: string;
  rescheduleReason: string;
  priority?: string | null;
  expectedVersion?: number | null;
}

// =============================================================================================
// Donor identity verification
// =============================================================================================

export interface IdentityVerification {
  id: string;
  verificationReference: string;
  donorId: string;
  donorReference: string | null;
  donorDisplayName: string | null;
  verificationPurpose: string | null;
  verificationChannel: string;
  /** Where the code went, masked. The full address is never returned to this screen. */
  maskedDestination: string | null;
  status: string;
  attemptCount: number;
  remainingAttempts: number;
  expiryAtUtc: string | null;
  identityConfidence: string;
  evidenceReference: string | null;
  reviewerUserId: string | null;
  reviewerName: string | null;
  sentAtUtc: string | null;
  verifiedAtUtc: string | null;
  escalationReason: string | null;
  cancellationReason: string | null;
  createdAtUtc: string;
  version: number;
  isEvidenceMasked: boolean;
  permittedActions: string[];
}

export interface IdentityVerificationListResponse {
  screenId: string;
  route: string;
  verifications: PagedResponse<IdentityVerification>;
  channelOptions: DonLookupItem[];
  statusOptions: DonLookupItem[];
  confidenceOptions: DonLookupItem[];
  permittedActions: string[];
  activeFilterSummary: string;
  activeScope: string;
  /** How long a code lasts and how many tries it gets. Shown so the donor can be told. */
  codeValidMinutes: number;
  maximumAttempts: number;
  state: string;
}

export interface SendChallengeRequest {
  donorId: string;
  verificationPurpose: string;
  verificationChannel: string;
}

export interface VerifyCodeRequest {
  code: string;
  expectedVersion?: number | null;
}

export interface EscalateVerificationRequest {
  reviewerUserId: string;
  reviewerName: string;
  escalationReason: string;
  evidenceReference?: string | null;
  expectedVersion?: number | null;
}

export interface ChallengeSentResponse {
  verification: IdentityVerification;
  deliveryStatus: string;
  message: string;
  /** Named when the message is queued rather than delivered, so the operator does not wait blind. */
  pendingDependency: string | null;
}

// =============================================================================================
// Navigation and reference data
// =============================================================================================

export interface DonorMenuItem {
  screenId: string;
  label: string;
  route: string;
  viewPermission: string;
}

/**
 * What the sidebar renders for this section.
 *
 * THE THREE FLAGS AT THE END decide whether a sensitive field or a controlled export is offered
 * at all. Hiding a menu entry is a convenience and never the authorisation - every route is
 * rechecked by the server when it is actually called.
 */
export interface DonorMenuResponse {
  menuGroup: string;
  items: DonorMenuItem[];
  roles: string[];
  permissions: string[];
  visibleSensitiveFields: string[];
  canSeeSensitiveContact: boolean;
  canSeeConfidentialEvidence: boolean;
  canExport: boolean;
}

export interface CampaignLookup {
  id: string;
  code: string;
  name: string;
  status: string;
  startsAtUtc: string | null;
  endsAtUtc: string | null;
}

/**
 * Every enum catalogue the section's selectors draw from, in one call.
 *
 * SERVER-SUPPLIED RATHER THAN HARD-CODED, so a value added to an enum appears in the UI without
 * anybody remembering to update a list on this side - which is precisely how a dropdown ends up
 * offering a value the API rejects.
 */
export interface DonReferenceData {
  donorTypes: DonLookupItem[];
  donorStatuses: DonLookupItem[];
  approvalStates: DonLookupItem[];
  leadStatuses: DonLookupItem[];
  slaStates: DonLookupItem[];
  contactOutcomes: DonLookupItem[];
  consentChannels: DonLookupItem[];
  consentStates: DonLookupItem[];
  consentStatuses: DonLookupItem[];
  contactChannels: DonLookupItem[];
  interactionTypes: DonLookupItem[];
  mergeDecisions: DonLookupItem[];
  mergeCaseStatuses: DonLookupItem[];
  identityConfidences: DonLookupItem[];
  verificationChannels: DonLookupItem[];
  verificationStatuses: DonLookupItem[];
  followUpStatuses: DonLookupItem[];
  followUpPriorities: DonLookupItem[];
  workloadBands: DonLookupItem[];
  campaignStatuses: DonLookupItem[];
  donationStages: DonLookupItem[];
  promiseStatuses: DonLookupItem[];
  documentClassifications: DonLookupItem[];
  languages: DonLookupItem[];
}

// =============================================================================================
// Helper
// =============================================================================================

/**
 * Whether the server said this caller may take an action.
 *
 * ALWAYS ASK THIS rather than re-deriving the rule. The server's answer already folds in the
 * record's state and the caller's permissions, and a second copy of the rule on this side is one
 * that will eventually disagree.
 */
export function canPerformDonorAction(
  record: { permittedActions?: readonly string[] } | null | undefined,
  action: string,
): boolean {
  return !!record?.permittedActions?.some(
    (candidate) => candidate.toLowerCase() === action.toLowerCase(),
  );
}

/**
 * One row of an uploaded lead file.
 *
 * EVERY FIELD IS A STRING, INCLUDING THE CAMPAIGN. A spreadsheet holds what somebody typed, not
 * what the database holds - the campaign arrives as "Clean Water 2026" rather than as a Guid.
 * Resolving it, and rejecting a row that names a campaign nobody has, is the server's job.
 */
export interface BulkLeadImportRow {
  /** 1-based, as the person sees it in their spreadsheet, so an error names a row they can find. */
  rowNumber: number;
  firstName?: string | null;
  lastName?: string | null;
  mobileNumber?: string | null;
  emailAddress?: string | null;
  preferredLanguage?: string | null;
  city?: string | null;
  campaignNameOrCode?: string | null;
  source?: string | null;
  notes?: string | null;
}

export interface BulkLeadImportRequest {
  rows: BulkLeadImportRow[];
  /** Used when a row leaves the campaign blank. */
  defaultCampaignId?: string | null;
  defaultSource?: string | null;
}

export interface BulkLeadImportRowResult {
  rowNumber: number;
  imported: boolean;
  leadReference: string | null;
  reason: string | null;
}

/**
 * The outcome of the whole file.
 *
 * PARTIAL SUCCESS IS THE NORMAL CASE. A file of two hundred leads with three bad rows creates a
 * hundred and ninety-seven and names the three.
 */
export interface BulkLeadImportResponse {
  submittedCount: number;
  importedCount: number;
  rejectedCount: number;
  results: BulkLeadImportRowResult[];
  message: string;
}

// =========================================================================================
// Communication Timeline
// =========================================================================================

/**
 * One line of the timeline.
 *
 * `notes` IS NULL WHEN MASKED, not blank-string-masked. A call note records what a donor said
 * about their circumstances, which is more revealing than the phone number beside it, so the
 * server withholds it entirely rather than sending a redacted version.
 */
export interface CommunicationTimelineEntry {
  id: string;
  interactionType: string;
  channel: string | null;
  /** Incoming | Outgoing | Internal - derived server-side from the outcome. */
  direction: string;
  occurredAtUtc: string;
  outcome: string;
  summary: string;
  notes: string | null;
  performedByName: string | null;
  isNotesMasked: boolean;
}

/**
 * The Communication Timeline for a lead, or for the donor it became.
 *
 * IT SPANS THE CONVERSION. Interactions recorded before conversion carry the lead id and those
 * after carry the donor id; the server merges both, which is what makes the document's promise
 * that "the converted donor retains the existing owner and Communication Timeline history" true
 * on screen.
 */
export interface CommunicationTimelineResponse {
  screenId: string;
  route: string;
  leadId: string | null;
  leadReference: string | null;
  donorId: string | null;
  donorReference: string | null;
  displayName: string;
  mobileNumber: string | null;
  emailAddress: string | null;
  campaignName: string | null;
  source: string | null;
  preferredLanguage: string;
  ownerName: string | null;
  status: string;
  temperature: string;
  donationPotential: string;
  healthScore: number;
  entries: CommunicationTimelineEntry[];
  temperatureOptions: DonLookupItem[];
  donationPotentialOptions: DonLookupItem[];
  interactionTypeOptions: DonLookupItem[];
  outcomeOptions: DonLookupItem[];
  permittedActions: string[];
  isContactMasked: boolean;
  activeScope: string;
  state: string;
}
