// ================= Donors and Leads shared models =================
// SCR-DON-001 … SCR-DON-006 + DON-UI-07 + DON-UI-08 (Section 04)

/** Lead/donor lifecycle status — current catalogue values only. */
export type DonorLeadStatus =
  | 'New'
  | 'Assigned'
  | 'Contacted'
  | 'Qualified'
  | 'Converted'
  | 'Nurture'
  | 'Closed'
  | 'Suppressed';

/** UI states used across donors and leads components. */
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

/** Effective permissions for donors and leads operations. */
export interface DonorLeadPermissions {
  readonly view: boolean;
  readonly accept?: boolean;
  readonly assign?: boolean;
  readonly contact?: boolean;
  readonly qualify?: boolean;
  readonly close?: boolean;
  readonly save?: boolean;
  readonly submit?: boolean;
  readonly deleteDraft?: boolean;
  readonly deduplicate?: boolean;
  readonly correct?: boolean;
  readonly followUp?: boolean;
  readonly createIntent?: boolean;
  readonly merge?: boolean;
  readonly rejectCandidate?: boolean;
  readonly grant?: boolean;
  readonly withdraw?: boolean;
  readonly reviewEvidence?: boolean;
  readonly bulkRoute?: boolean;
  readonly sendChallenge?: boolean;
  readonly verifyCode?: boolean;
  readonly escalateReview?: boolean;
  readonly cancelVerification?: boolean;
  readonly scheduleFollowUp?: boolean;
  readonly markComplete?: boolean;
  readonly reschedule?: boolean;
  readonly cancelTask?: boolean;
  readonly reassign?: boolean;
  readonly inspectHistory?: boolean;
}

/** Field/control contract entry from the practical implementation views. */
export interface FieldContract {
  readonly label: string;
  readonly control:
    | 'text'
    | 'textarea'
    | 'telephone'
    | 'email'
    | 'select'
    | 'searchable-select'
    | 'date'
    | 'datetime'
    | 'file'
    | 'badge'
    | 'readonly'
    | 'checkbox'
    | 'numeric';
  readonly required: boolean;
  readonly visibility:
    | 'Internal'
    | 'Restricted'
    | 'Confidential'
    | 'Internal; public only';
  readonly validation?: string;
  readonly maxLength?: number;
  readonly minLength?: number;
}

/** Action contract from the practical implementation views. */
export interface ActionContract {
  readonly id: string;
  readonly label: string;
  readonly placement: 'primary' | 'secondary' | 'danger' | 'workflow';
  readonly permission: string;
  readonly allowedState: string;
  readonly result: string;
  readonly requiresReason?: boolean;
  readonly typedConfirm?: boolean;
}

/** A scope-aware selector option with a stable reference and context. */
export interface SelectOption {
  readonly reference: string;
  readonly label: string;
  readonly context: string;
  readonly initials?: string;
}

/** Read-only row for lists/history. */
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
  readonly tone: 'good' | 'blue' | 'gold' | 'plum' | 'meadow' | 'muted';
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

/** Lead work queue record (SCR-DON-001). */
export interface LeadWorkQueueItem {
  readonly reference: string;
  readonly nameOrContact: string;
  readonly campaign: string;
  readonly owner: string;
  readonly status: DonorLeadStatus;
  readonly nextActionDue: string;
  readonly language: string;
  readonly source: string;
  readonly nextAction: string;
  readonly slaState: string;
  readonly lastContactOutcome: string;
  readonly masked: boolean;
}

/** Lead capture data (SCR-DON-002). */
export interface LeadCaptureData {
  readonly draftReference: string;
  readonly status: string;
  readonly scope: string;
  readonly fields: LeadCaptureFields;
  readonly duplicateCandidates: readonly string[];
  readonly lastRefresh: string;
}

export interface LeadCaptureFields {
  readonly firstName: string;
  readonly lastName: string;
  readonly mobile: string;
  readonly email: string;
  readonly preferredLanguage: string;
  readonly city: string;
  readonly campaign: string;
  readonly source: string;
  readonly consentState: string;
  readonly consentEvidence: string;
  readonly notes: string;
  readonly preferredContactTime: string;
}

/** Donor 360 data (SCR-DON-003). */
export interface Donor360Data {
  readonly donorReference: string;
  readonly identitySummary: string;
  readonly relationshipOwner: string;
  readonly consentStatus: string;
  readonly communicationPreferences: string;
  readonly donationTotals: string;
  readonly donationCount: number;
  readonly campaignHistory: readonly string[];
  readonly conversations: readonly HistoryRow[];
  readonly followUps: readonly HistoryRow[];
  readonly promises: readonly HistoryRow[];
  readonly documents: readonly HistoryRow[];
  readonly duplicateLinks: readonly string[];
  readonly activityHistory: readonly ActivityItem[];
  readonly lastRefresh: string;
}

/** Duplicate review data (SCR-DON-004). */
export interface DuplicateReviewData {
  readonly reviewReference: string;
  readonly candidateA: string;
  readonly candidateB: string;
  readonly contactComparison: string;
  readonly identityConfidence: string;
  readonly matchingEvidence: string;
  readonly conflictingFields: readonly string[];
  readonly donationHistoryImpact: string;
  readonly consentImpact: string;
  readonly decisionOptions: readonly string[];
  readonly mergePreview: readonly HistoryRow[];
  readonly lastRefresh: string;
}

/** Consent and preference centre data (SCR-DON-005). */
export interface ConsentPreferenceData {
  readonly consentReference: string;
  readonly donorReference: string;
  readonly purpose: string;
  readonly channel: string;
  readonly consentState: string;
  readonly noticeVersion: string;
  readonly evidenceSource: string;
  readonly effectiveTime: string;
  readonly expiryTime: string;
  readonly publicRecognitionPreference: string;
  readonly contactRestrictions: string;
  readonly history: readonly HistoryRow[];
  readonly lastRefresh: string;
}

/** Assignment board data (SCR-DON-006). */
export interface AssignmentBoardData {
  readonly scope: string;
  readonly filters: AssignmentFilters;
  readonly rows: readonly AssignmentRow[];
  readonly lastRefresh: string;
}

export interface AssignmentFilters {
  readonly campaigns: readonly string[];
  readonly teams: readonly string[];
  readonly languages: readonly string[];
  readonly workloadBands: readonly string[];
  readonly slaStates: readonly string[];
}

export interface AssignmentRow {
  readonly leadReference: string;
  readonly leadPreview: string;
  readonly campaign: string;
  readonly team: string;
  readonly language: string;
  readonly workloadBand: string;
  readonly slaState: string;
  readonly currentOwner: string;
  readonly suggestedOwner: string;
  readonly openWorkCount: number;
  readonly nextActionDue: string;
}

/** Donor identity verification data (DON-UI-07). */
export interface IdentityVerificationData {
  readonly verificationReference: string;
  readonly donorReference: string;
  readonly purpose: string;
  readonly channel: string;
  readonly maskedDestination: string;
  readonly status: string;
  readonly attemptCount: number;
  readonly maxAttempts: number;
  readonly expiryTime: string;
  readonly identityConfidence: string;
  readonly evidenceReference: string;
  readonly reviewer: string;
  readonly history: readonly HistoryRow[];
  readonly savedFilters: readonly string[];
  readonly lastRefresh: string;
}

/** Follow-up planner data (DON-UI-08). */
export interface FollowUpPlannerData {
  readonly followUpReference: string;
  readonly donorOrLeadReference: string;
  readonly relationshipOwner: string;
  readonly purpose: string;
  readonly permittedChannel: string;
  readonly preferredLanguage: string;
  readonly preferredContactTime: string;
  readonly nextAction: string;
  readonly dueDate: string;
  readonly priority: string;
  readonly notes: string;
  readonly consentWarning: string;
  readonly assignedScope: string;
  readonly lastRefresh: string;
}

/** Confirmation dialog configuration. */
export interface ConfirmDialogConfig {
  readonly title: string;
  readonly message: string;
  readonly confirmLabel: string;
  readonly cancelLabel: string;
  readonly tone: 'primary' | 'danger';
  readonly requireReason: boolean;
  readonly reasonLabel?: string;
  readonly reasonMin?: number;
  readonly reasonMax?: number;
  readonly typedConfirm?: boolean;
  readonly beforeAfter?: readonly { label: string; before: string; after: string }[];
  readonly affectedRecord?: string;
  readonly effectiveTime?: string;
}