import { Injectable, inject, signal } from '@angular/core';
import { MonoTypeOperatorFunction, forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { DonorApiService } from './donor-api.service';
import { PeopleDirectoryService } from '../Shared/services/people-directory.service';
import { OrganisationScopeService } from '../Shared/services/organisation-scope.service';
import { apiErrorMessage } from '../Shared/models/api-response.model';
import {
  DonorListItem,
  FollowUp as ApiFollowUp,
  LeadListItem,
} from '../Shared/models/donor-contract.model';

export interface WorkflowLead {
  id: string;
  name: string;
  mobile: string;
  email: string;
  source: string;
  campaign: string;
  stage: string;
  temperature: 'Cold' | 'Warm' | 'Hot';
  donationPotential: 'Low' | 'Medium' | 'High';
  /** The owner's display name, for the screens. */
  owner: string;
  /**
   * The owner's API id.
   *
   * CARRIED BESIDE THE NAME because every write that touches ownership takes the id, and the
   * screens hold the name. Without it each of them had to translate a display name back into a
   * person through the directory, which only matches on id or staff code - so the translation
   * always failed and the name went to the server as though it were a Guid.
   */
  ownerUserId?: string | null;
  lastActivity: string;
  nextFollowUp: string;
  healthScore: number;
  healthReasons: string[];
  lastContactOutcome: string;
  language: string;
  createdAt: string;
  masked: boolean;
  converted: boolean;
  donorId?: string;
  followUpStatus?: string;
  qualificationReadiness?: string;
  recommendedNextAction?: string;
  contactRestricted?: boolean;
}


export interface WorkflowDonor {
  donorId: string;
  name: string;
  mobile: string;
  email: string;
  location: string;
  region: string;
  campaign: string;
  owner: string;
  /** The owner's API id, so a correction can name a person the platform can route work to. */
  ownerUserId?: string | null;
  ownerInitials: string;
  ownerColor: string;
  reference: string;
  lastDonationAmount: number;
  lastDonationDate: string;
  lifetimeGiving: number;
  followUpStatus: string;
  consentStatus: string;
  verificationStatus: string;
  engagementTag: string;
  consentReviewRequired: boolean;
  createdDate: string;
}

export interface WorkflowFollowUp {
  id: string;
  recordId: string;
  recordName: string;
  recordType: 'Lead' | 'Donor';
  followUpType: string;
  scheduledDate: string;
  scheduledTime: string;
  priority: string;
  status: string;
  dependencyStatus: string;
  dependencyBlockedReason?: string;
  slaStatus: string;
  assignedTo: string;
  assignedToInitials: string;
  campaign: string;
  phone: string;
  email: string;
  purpose: string;
  expectedOutcome: string;
  successCriteria: string;
  lastCommunicationType?: string;
  lastCommunicationOutcome?: string;
  lastCommunicationDate?: string;
  reminderSettings: string;
  attachments: string[];
  history: { date: string; label: string }[];
}

export interface WorkflowCommunication {
  id: string;
  recordId: string;
  type: string;
  date: string;
  time: string;
  createdBy: string;
  direction: string;
  outcome: string;
  summary: string;
  engagement?: string;
  quality?: string;
  important?: boolean;
  attachment?: string;
  notes?: string;
  followUpDate?: string;
  followUpTime?: string;
  followUpPriority?: string;
  followUpPurpose?: string;
  followUpStatus?: string;
}

/** Shape of one lead entry inside assets/data/my-leads.json (leads JSON). */
interface LeadJsonItem {
  reference: string;
  name: string;
  campaign: string;
  owner: string;
  stage: string;
  temperature: string;
  healthScore: number;
  nextFollowUp: string;
  followUpStatus: string;
  qualificationReadiness: string;
  language: string;
  source: string;
  lastContactOutcome: string;
  recommendedNextAction: string;
  contactRestricted: boolean;
  email?: string;
  mobile?: string;
}

interface LeadJsonGroup {
  key: string;
  label: string;
  count: number;
  items: LeadJsonItem[];
}

interface WorkflowSnapshot {
  donors: WorkflowDonor[];
  leads: WorkflowLead[];
  followUps: WorkflowFollowUp[];
  communications: WorkflowCommunication[];
  /** Lead references moved out of the leads list into donors after payment —
   *  kept here so static JSON seeding never resurrects a converted lead. */
  removedLeadIds?: string[];
}

/**
 * The shared client state behind the twelve Donors and Leads screens.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. Every screen used to import a JSON file at BUILD TIME and
 * push it into this store through a `seed*` method; the store then kept the result in
 * localStorage so it survived a refresh. Four consequences followed and all four were real:
 *
 *   - NOTHING REACHED THE SERVER. A lead accepted, contacted or qualified existed in one
 *     browser's local storage and nowhere else. Two people working the same queue never saw one
 *     another's work.
 *   - THE DATA WAS IDENTICAL FOR EVERY ORGANISATION, because a file compiled into the bundle has
 *     no idea who is asking. Tenant isolation stopped at the API boundary.
 *   - THE MASKING RULES COULD NOT WORK. Whether a donor's phone number is shown depends on a
 *     permission the server checks; a static file has one answer for everybody, and that answer
 *     was "show it".
 *   - `registerDonorFromPayment` INVENTED DONOR RECORDS in the browser, complete with generated
 *     DON-2026-###### references. Those numbers duplicated real ones the moment anybody else
 *     created a donor.
 *
 * IT NOW LOADS FROM `DON /api/v1/donors`, while keeping its SYNCHRONOUS SIGNAL SURFACE. That
 * shape is deliberate: twelve screens read `leads()`, `donors()`, `getLead(id)` and their
 * siblings from templates and computed properties, and turning those into observables would mean
 * rewriting all twelve. Reads stay synchronous against the loaded signals; writes go to the API
 * and refresh them.
 *
 * THE SIGNALS ARE A CACHE OF THE SERVER'S ANSWER, not the source of truth. `refresh()` reloads
 * them and every write calls it on completion, so a screen never shows a state the server did
 * not agree to.
 *
 * THE `seed*` METHODS ARE NO-OPS NOW and are kept only so the twelve call sites still compile.
 * Each one says so where it is defined.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowStateService {
  private readonly api = inject(DonorApiService);
  private readonly organisationScope = inject(OrganisationScopeService);
  private readonly people = inject(PeopleDirectoryService);
  readonly donors = signal<WorkflowDonor[]>([]);
  readonly leads = signal<WorkflowLead[]>([]);
  readonly followUps = signal<WorkflowFollowUp[]>([]);
  readonly communications = signal<WorkflowCommunication[]>([]);
  /** Lead references MOVED from the leads list into donors after payment.
   *  Kept INSIDE the single shared snapshot (same storage as donors/leads),
   *  so re-seeding the static leads JSON cannot resurrect a lead that has
   *  already become a donor. */
  private readonly removedLeadIds = signal<ReadonlySet<string>>(new Set());

  /** True while the first load is in flight, so a screen can tell "loading" from "none". */
  readonly isLoading = signal(false);

  /** The last load failure. Screens surface it rather than rendering a silent blank. */
  readonly loadError = signal<string | null>(null);

  /** The API id behind a lead reference, so a screen working in references can still write. */
  private readonly leadIdsByReference = new Map<string, string>();

  /** The concurrency stamp per lead reference, sent back on the next write. */
  private readonly leadVersionsByReference = new Map<string, number>();

  /**
   * Campaign name to id, for the writes that need an id from a screen holding a name.
   *
   * BUILT FROM THE REFERENCE DATA rather than from the loaded leads: a campaign with no leads yet
   * would otherwise be unresolvable, which is exactly when somebody is most likely to be moving a
   * lead onto it.
   */
  private readonly campaignIdsByName = new Map<string, string>();

  /** The API id and concurrency stamp per follow-up reference, so a screen in codes can write. */
  private readonly followUpIdsByReference = new Map<string, string>();
  private readonly followUpVersionsByReference = new Map<string, number>();

  /** The same for donors. */
  private readonly donorIdsByReference = new Map<string, string>();
  private readonly donorVersionsByReference = new Map<string, number>();

  constructor() {
    this.refresh();
    this.organisationScope.onOrganisationChange(() => this.reloadForOrganisation());
  }

  /**
   * Everything here belongs to ONE Organisation, so a switch discards it and reloads.
   *
   * Discarded FIRST: reloading alone would leave the previous Organisation's donors and leads
   * readable on screen for the length of three round trips. See `OrganisationScopeService`.
   */
  private reloadForOrganisation(): void {
    this.donors.set([]);
    this.leads.set([]);
    this.followUps.set([]);
    this.communications.set([]);
    this.removedLeadIds.set(new Set());
    this.loadError.set(null);
    this.leadIdsByReference.clear();
    this.leadVersionsByReference.clear();
    this.campaignIdsByName.clear();
    this.followUpIdsByReference.clear();
    this.followUpVersionsByReference.clear();
    this.donorIdsByReference.clear();
    this.donorVersionsByReference.clear();
    this.refresh();
  }

  /**
   * Reloads leads, donors and follow-ups from the API.
   *
   * THREE CALLS IN PARALLEL, and a failure in any ONE of them does not blank the other two:
   * a Volunteer may read the lead queue and not the donor directory, and half a workspace is far
   * more useful than an error page.
   *
   * A LARGE PAGE, deliberately. The screens page and filter in memory over whatever they are
   * given, so this is the working set rather than a page size.
   */
  refresh(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    // A FAILED READ IS RECORDED, NOT TURNED INTO AN EMPTY LIST.
    //
    // All three of these used to be `catchError(() => of([]))`, which made a 401, a 403 or a
    // 500 indistinguishable from "this organisation has no leads" - the work queue rendered its
    // ordinary empty state and `loadError` was never set, because the error never reached the
    // subscriber. That is what made a lead that failed to save look merely absent.
    //
    // The streams still recover, because one broken endpoint should not blank the other two:
    // the failure is REMEMBERED in `failures` and reported once the forkJoin settles.
    const failures: string[] = [];

    // A 403 IS NOT A FAILURE HERE, IT IS AN ANSWER.
    //
    // This workspace reads three slices - leads, donors and follow-ups - but most roles are
    // entitled to only some of them. A Data Steward, Finance Officer, Payment Operations or
    // Campaign Owner holds don.donors.view and NOT don.lead-work-queue.view or
    // don.follow-up-planner.view, all by design.
    //
    // Recording those refusals as load failures broke the Donor List for exactly those four
    // roles: the donor call returned 200 with the rows present, and the screen still showed
    // "Unable to load donors" because two slices they were never meant to see came back 403.
    // Retry could not help, because nothing was wrong.
    //
    // So a 403 recovers silently to an empty slice, and everything else - 500, a timeout, a
    // dropped connection - is still remembered and reported. That keeps the original point of
    // this block, which is that a genuine failure must never masquerade as "no records".
    const isForbidden = (error: unknown): boolean =>
      typeof error === 'object' && error !== null && (error as { status?: number }).status === 403;

    const recover = <T>(what: string): MonoTypeOperatorFunction<T[]> =>
      catchError((error: unknown) => {
        if (!isForbidden(error)) {
          failures.push(`${what}: ${apiErrorMessage(error, 'the request failed')}`);
        }

        return of([] as T[]);
      });

    const leads$ = this.api.getLeadWorkQueue({ pageSize: 200 }).pipe(
      map((response) => response.leads.items),
      recover<LeadListItem>('Leads'),
    );

    const donors$ = this.api.searchDonors({ pageSize: 200 }).pipe(
      map((page) => page.items),
      recover<DonorListItem>('Donors'),
    );

    const followUps$ = this.api.getFollowUpPlanner({ pageSize: 200 }).pipe(
      map((response) => response.followUps.items),
      recover<ApiFollowUp>('Follow-ups'),
    );

    // The campaign lookup, so a write can send an id where the screen holds a name.
    this.api.searchCampaigns(undefined, 200).subscribe({
      next: (campaigns) => {
        this.campaignIdsByName.clear();

        for (const campaign of campaigns) {
          this.campaignIdsByName.set(campaign.name, campaign.id);
        }
      },
      error: () => this.campaignIdsByName.clear(),
    });

    forkJoin([leads$, donors$, followUps$]).subscribe({
      next: ([leads, donors, followUps]) => {
        this.leadIdsByReference.clear();
        this.leadVersionsByReference.clear();

        for (const lead of leads) {
          this.leadIdsByReference.set(lead.leadReference, lead.id);
          this.leadVersionsByReference.set(lead.leadReference, lead.version);
        }

        this.followUpIdsByReference.clear();
        this.followUpVersionsByReference.clear();

        for (const followUp of followUps) {
          const reference = followUp.followUpReference ?? followUp.id;
          this.followUpIdsByReference.set(reference, followUp.id);
          this.followUpVersionsByReference.set(reference, followUp.version ?? 0);
        }

        this.donorIdsByReference.clear();
        this.donorVersionsByReference.clear();

        for (const donor of donors) {
          const reference = donor.displayCode ?? donor.id;
          this.donorIdsByReference.set(reference, donor.id);
          this.donorVersionsByReference.set(reference, donor.version ?? 0);
        }

        this.leads.set(leads.map((lead) => this.toWorkflowLead(lead)));
        this.donors.set(donors.map((donor) => this.toWorkflowDonor(donor)));
        this.followUps.set(followUps.map((followUp) => this.toWorkflowFollowUp(followUp)));
        this.isLoading.set(false);

        // Whatever the individual streams could not fetch. Empty when all three answered.
        this.loadError.set(failures.length ? failures.join(' · ') : null);
      },
      error: (error: unknown) => {
        this.isLoading.set(false);
        this.loadError.set(
          apiErrorMessage(error, 'The donor and lead workspace could not be loaded.'),
        );
      },
    });
  }

  /**
   * One API lead row as the screens read it.
   *
   * TEMPERATURE, POTENTIAL AND HEALTH NOW COME FROM THE SERVER, and that is a correctness fix
   * rather than a tidy-up. They used to be guessed here from status and SLA state - a lead was
   * "Hot" because it was Qualified - so the Temperature column showed a derivation of the Stage
   * column sitting next to it, and the fundraiser's own reading of the conversation was nowhere
   * on screen. The module brief has temperature and potential REPLACING formal qualification, so
   * deriving one from the other inverted the whole point. They are stored on the lead now.
   *
   * NAME, MOBILE AND EMAIL ARE SEPARATE COLUMNS on the projection, masked server-side by the
   * same permission that masks the combined preview. `masked` still says whether that happened,
   * so a screen can show why a value is starred out rather than looking broken.
   */
  private toWorkflowLead(item: LeadListItem): WorkflowLead {
    return {
      id: item.leadReference,
      name: item.name || item.nameAndContactPreview,
      mobile: item.mobileNumber ?? '',
      email: item.emailAddress ?? '',
      source: item.source ?? '',
      campaign: item.campaignName ?? '',
      stage: item.status,
      temperature: item.temperature as WorkflowLead['temperature'],
      donationPotential: item.donationPotential as WorkflowLead['donationPotential'],
      owner: item.ownerName ?? 'Unassigned',
      ownerUserId: item.ownerUserId,
      lastActivity: item.lastContactOutcome,
      nextFollowUp: item.nextActionDueUtc
        ? new Date(item.nextActionDueUtc).toLocaleDateString('en-IN')
        : 'Not scheduled',
      healthScore: item.healthScore,
      healthReasons: [item.slaState, item.lastContactOutcome].filter(Boolean),
      lastContactOutcome: item.lastContactOutcome,
      language: item.preferredLanguage,
      createdAt: item.updatedAtUtc,
      masked: item.isContactMasked,
      donorId: item.convertedDonorId ?? undefined,
      converted: item.isConverted,
      followUpStatus: item.nextActionDueUtc ? 'Upcoming' : 'None',
      qualificationReadiness: item.status === 'Qualified' ? 'Ready' : 'Not Ready',
      recommendedNextAction: item.nextAction ?? '',
      contactRestricted: item.isContactMasked,
    };
  }


  /**
   * One API donor row as the screens read it.
   *
   * MOST FIELDS ARE EMPTY, and that is the API being right rather than this being incomplete.
   * The donor LIST projection is six columns by design - "no sensitive value travels to a list
   * view" - so contact details, campaign and giving history simply are not sent. They arrive
   * when a donor is opened and the 360 view is fetched, which is also where the masking rules
   * are applied per caller.
   */
  private toWorkflowDonor(item: DonorListItem): WorkflowDonor {
    // THE OWNER IS THE SERVER'S NOW. These three lines were the literals 'Unassigned', 'UA' and
    // one fixed colour, so every donor in the workspace was drawn as belonging to nobody -
    // including one a lead conversion had just handed to the fundraiser who nurtured it. The
    // list projection carries the relationship owner, so the column and the donor list's Owner
    // filter both read a real person.
    const owner = item.relationshipOwnerName?.trim() || '';
    const ownerPerson = this.people.get(item.relationshipOwnerUserId);

    return {
      donorId: item.id,
      name: item.displayName,
      mobile: '',
      email: '',
      location: '',
      region: '',
      campaign: '',
      owner: owner || (ownerPerson?.name ?? 'Unassigned'),
      ownerUserId: item.relationshipOwnerUserId,
      ownerInitials: ownerPerson?.initials ?? this.initials(owner || 'Unassigned'),
      ownerColor: '#1F3B57',
      reference: item.displayCode,
      lastDonationAmount: 0,
      lastDonationDate: '',
      lifetimeGiving: 0,
      followUpStatus: 'None',
      consentStatus: '',
      verificationStatus: '',
      engagementTag: item.status,
      consentReviewRequired: false,
      createdDate: item.updatedAtUtc?.slice(0, 10) ?? '',
    };
  }

  private toWorkflowFollowUp(item: ApiFollowUp): WorkflowFollowUp {
    const due = item.dueAtUtc ? new Date(item.dueAtUtc) : null;

    return {
      id: item.followUpReference,
      recordId: item.donorReference ?? item.leadReference ?? '',
      recordName: item.donorDisplayName ?? item.leadReference ?? '',
      recordType: item.donorId ? 'Donor' : 'Lead',
      followUpType: item.permittedChannel,
      scheduledDate: due ? due.toISOString().slice(0, 10) : '',
      scheduledTime: due
        ? due.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
        : '',
      priority: item.priority,
      status: item.status,

      // THE CONSENT WARNING IS THE DEPENDENCY. A follow-up whose channel the donor did not
      // permit is not "ready" however complete the rest of it looks.
      dependencyStatus: item.consentWarning.hasWarning ? 'Blocked' : 'Ready',
      dependencyBlockedReason: item.consentWarning.hasWarning
        ? item.consentWarning.message
        : undefined,

      slaStatus: 'On Time',
      assignedTo: item.relationshipOwnerName ?? 'Unassigned',
      assignedToInitials: this.initials(item.relationshipOwnerName ?? 'Unassigned'),
      campaign: '',
      phone: '',
      email: '',
      purpose: item.purpose ?? '',
      expectedOutcome: item.nextAction ?? '',
      successCriteria: '',
      reminderSettings: '',
      attachments: [],
      history: [],
    };
  }

  /**
   * NO LONGER SEEDS ANYTHING.
   *
   * Donors come from `DON /api/v1/donors`, which is the only place that knows which organisation
   * is asking and which contact details that caller may see. The method is kept because several
   * screens call it on open; it does nothing, and `refresh()` has already loaded the real list.
   */
  seedDonors(_source: unknown): void {
    // Intentionally empty. See the comment above.
  }

  /**
   * One donor, addressed by EITHER its API id or its display reference.
   *
   * BOTH, because the screens genuinely hold both and used to disagree about which. The 360 view
   * navigates on `donorId`, the verification and consent screens carry the DON-2026-###### the
   * person reads off the record, and a lookup that accepted only one of them silently returned
   * undefined for half its callers.
   */
  getDonor(id: string | null | undefined): WorkflowDonor | undefined {
    if (!id) return undefined;
    return this.donors().find((donor) => donor.donorId === id || donor.reference === id);
  }

  /**
   * Applies a local change to a donor row.
   *
   * IT NO LONGER PERSISTS. The donor record belongs to DON and is changed through its own
   * endpoints - correcting a donor, granting a consent, scheduling a follow-up - each of which
   * has its own permission and its own audit row. This keeps the signal in step between a write
   * and the refresh that follows it, and nothing more.
   */
  /**
   * Applies a change to a donor, and sends it.
   *
   * EVERY CHANGE HERE IS A CORRECTION, which is why it goes to the corrections endpoint rather
   * than to a general update. A donor's name, address and contact details are what a receipt is
   * issued against and what a tax authority may later ask about, so the server records what
   * changed, who changed it and why - and the reason is required for exactly that reason.
   *
   * A CHANGE WITH NO SUBSTANCE IS NOT SENT. `lastActivity` alone is a screen breadcrumb; posting a
   * correction for it would put an empty entry in a donor's correction history.
   */
  patchDonor(id: string, patch: Partial<WorkflowDonor>): void {
    const current = this.getDonor(id);

    this.donors.update((list) =>
      list.map((donor) =>
        donor.donorId === id || donor.reference === id ? { ...donor, ...patch } : donor,
      ),
    );

    // THE TWO LOOKUPS USED TO DISAGREE, and between them this method could never persist
    // anything. `getDonor` matched on the API id while `donorIdsByReference` is keyed by the
    // DISPLAY REFERENCE, so a caller passing the id resolved `current` and no `donorId`, and one
    // passing the reference resolved `donorId` and no `current`. Either way the guard below
    // returned early, every correction stayed in the browser, and the screens that called this
    // reported success. Resolving from the record itself removes the mismatch.
    const donorId = current?.donorId ?? this.donorIdsByReference.get(id);

    if (!current || !donorId) {
      return;
    }

    const changesSomething =
      (patch.name !== undefined && patch.name !== current.name)
      || (patch.email !== undefined && patch.email !== current.email)
      || (patch.mobile !== undefined && patch.mobile !== current.mobile)
      || (patch.owner !== undefined && patch.owner !== current.owner);

    if (!changesSomething) {
      return;
    }

    const merged = { ...current, ...patch };
    const [firstName, ...rest] = (merged.name ?? '').trim().split(/\s+/);

    const reason = 'Corrected from the donor workspace.';

    // THE OWNER IS SENT AS AN ID AND A NAME. `changesSomething` above has always counted an owner
    // change as a reason to post the correction, and the body then carried no owner at all - so
    // reassigning a donor posted a correction that corrected nothing and reported success.
    const ownerPerson = this.people.get(patch.ownerUserId ?? merged.owner);

    this.api
      .correctDonor(donorId, {
        firstName: firstName || merged.name || null,
        lastName: rest.join(' ') || null,
        primaryEmail: merged.email || null,
        primaryPhone: merged.mobile || null,
        relationshipOwnerUserId: ownerPerson?.reference ?? null,
        relationshipOwnerName: ownerPerson?.name ?? null,
        // The server requires ten characters, and a screen breadcrumb is often shorter.
        correctionReason:
          reason.length >= 10 ? reason : `${reason} - recorded from the donor workspace.`,

        expectedVersion:
          this.donorVersionsByReference.get(current.reference)
          ?? this.donorVersionsByReference.get(id)
          ?? null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => {
          this.loadError.set('The donor correction could not be saved. Reloading.');
          this.refresh();
        },
      });
  }

  /**
   * REMOVED, and its absence is the point.
   *
   * This used to create a donor record IN THE BROWSER when a payment completed: it invented a
   * DON-2026-###### reference from a local counter, guessed the campaign and owner from a JSON
   * lead file, and deleted the matching lead from local storage. Every part of that is now the
   * server's:
   *
   *   - The DONOR RECORD is created by the payments service, inside the transaction that records
   *     the donation, so a donor cannot exist for a gift that rolled back.
   *   - The REFERENCE is allocated from the existing per-organisation sequence, so it cannot
   *     duplicate one somebody else is using.
   *   - The LEAD CONVERSION is `POST /lead-work-queue/{id}/convert`, which preserves the lead's
   *     history and attribution rather than deleting it - the campaign that produced the lead is
   *     what makes the donation attributable afterwards.
   *
   * The method is kept as a no-op returning the existing record because the public donation
   * screen calls it after a payment. Refreshing is what makes the new donor appear.
   */
  registerDonorFromPayment(input: { name: string; email?: string; reference: string }): WorkflowDonor | undefined {
    this.refresh();

    const email = input.email?.trim().toLowerCase() ?? '';

    return this.donors().find(
      (donor) =>
        donor.reference === input.reference ||
        (!!email && donor.email.trim().toLowerCase() === email),
    );
  }

  /**
   * NO LONGER SEEDS ANYTHING. Leads come from `DON /api/v1/donors/lead-work-queue`.
   *
   * The elaborate tombstoning this used to do - remembering which leads had become donors so a
   * re-seed could not resurrect them - existed only because static JSON kept pushing deleted
   * records back in. With a server there is nothing to resurrect: a converted lead has status
   * Converted and the queue simply does not return it.
   */
  seedLeads(_leads: readonly WorkflowLead[]): void {
    // Intentionally empty. See the comment above.
  }

  /**
   * NO LONGER SEEDS ANYTHING. Follow-ups come from `DON /api/v1/donors/follow-up-planner`.
   */
  seedFollowUps(_followUps: readonly Omit<WorkflowFollowUp, 'recordId'>[]): void {
    // Intentionally empty.
  }

  /**
   * NO LONGER SEEDS ANYTHING.
   *
   * The interaction log lives on the donor's 360 view and is written by recording a contact
   * against a lead, which is an endpoint with its own consent check.
   */
  seedCommunications(_recordId: string, _records: readonly Omit<WorkflowCommunication, 'recordId'>[]): void {
    // Intentionally empty.
  }

  /**
   * Creates a lead.
   *
   * THE REFERENCE COMES FROM THE SERVER now. This used to build `LEAD-2026-####` from a local
   * counter seeded at 142 - which duplicated a real reference the moment two people captured a
   * lead at the same time, and reset to 142 whenever local storage was cleared.
   *
   * IT STILL RETURNS SYNCHRONOUSLY because the capture screen navigates on the returned record.
   * The row is added optimistically so that navigation lands on something, and `refresh()`
   * replaces it with the server's version - reference, id, version and all.
   *
   * A CAMPAIGN IS REQUIRED and there is no default. A lead with no campaign has no attribution,
   * so a donation it eventually produces could never be credited to anything; the server refuses
   * it, and a placeholder here would only move the failure later.
   */
  addLead(
    input: Partial<WorkflowLead> & Pick<WorkflowLead, 'name'>,
    options?: {
      /**
       * Promote the lead into the work queue once it is saved.
       *
       * A SAVED LEAD IS A DRAFT, AND A DRAFT IS INVISIBLE. The work queue's every query filters
       * on `!IsDraft`, so a lead that is only saved appears for nobody - not its capturer, not a
       * Campaign Manager, not an assignee. `POST /lead-capture/{id}/submit` is the one call that
       * clears the flag, and nothing in the application called it: the capture screen's Submit
       * button saved a draft and navigated to a queue the lead could never be in.
       */
      readonly submit?: boolean;

      /**
       * The outcome, once the server has answered.
       *
       * `reference` IS THE SERVER'S LEAD REFERENCE, not the provisional id this method returns
       * synchronously. The work queue keys its rows on that reference, so it is the only value
       * a caller can navigate to and actually land on the new row.
       */
      readonly onDone?: (outcome: {
        readonly saved: boolean;
        readonly reference?: string;
        readonly error?: string;
      }) => void;
    },
  ): WorkflowLead | null {
    // THE CAMPAIGN'S ID, RESOLVED FROM ITS NAME BEFORE ANYTHING ELSE HAPPENS.
    //
    // `input.campaign` is the campaign NAME, because that is what the capture screen's dropdown
    // binds. `CreateLeadRequest.CampaignId` is a Guid. This method used to send the name
    // straight through as `campaignId`, so System.Text.Json could not convert it and the API
    // answered 400 before any handler ran - every lead captured through this screen was lost,
    // while the screen showed the optimistic row and navigated to the work queue as though it
    // had worked. `updateLead` below has always resolved the id through this same map; only
    // this method skipped it.
    //
    // AN UNRESOLVED CAMPAIGN STOPS HERE rather than posting something the server must reject.
    // The map is filled by `refresh()` from the campaign lookup, so an empty result means
    // either that no campaign matches the name or that the lookup has not answered yet -
    // neither of which is improved by sending the request anyway.
    const campaignId = this.campaignIdsByName.get(input.campaign ?? '');

    if (!campaignId) {
      const message = input.campaign
        ? `The campaign "${input.campaign}" could not be matched, so the lead was not saved.`
        : 'Choose a campaign before saving the lead.';

      this.loadError.set(message);
      options?.onDone?.({ saved: false, error: message });

      return null;
    }

    const provisionalId = input.id ?? `LEAD-PENDING-${Date.now()}`;

    const optimistic: WorkflowLead = {
      id: provisionalId,
      name: input.name,
      mobile: input.mobile ?? '',
      email: input.email ?? '',
      source: input.source ?? 'Manual',
      campaign: input.campaign ?? '',
      stage: input.stage ?? 'New',
      temperature: input.temperature ?? 'Cold',
      donationPotential: input.donationPotential ?? 'Low',
      owner: input.owner ?? 'Unassigned',
      ownerUserId: input.ownerUserId ?? null,
      lastActivity: input.lastActivity ?? 'Created just now',
      nextFollowUp: input.nextFollowUp ?? 'Not scheduled',
      healthScore: input.healthScore ?? 20,
      healthReasons: input.healthReasons ?? ['New lead'],
      lastContactOutcome: input.lastContactOutcome ?? 'No contact yet',
      language: input.language ?? 'English',
      createdAt: input.createdAt ?? new Date().toISOString(),
      masked: input.masked ?? false,
      converted: false,
      followUpStatus: input.followUpStatus ?? 'Upcoming',
      qualificationReadiness: 'Not Ready',
      recommendedNextAction: input.recommendedNextAction ?? 'Initial contact',
      contactRestricted: false,
    };

    this.leads.update((list) => [optimistic, ...list.filter((item) => item.id !== provisionalId)]);

    const [firstName, ...rest] = input.name.trim().split(/\s+/);

    this.api
      .saveLead({
        firstName: firstName ?? input.name,
        lastName: rest.length ? rest.join(' ') : null,
        mobileNumber: input.mobile || null,
        emailAddress: input.email || null,
        preferredLanguage: input.language || null,
        campaignId,
        source: input.source ?? 'Manual',
        notes: null,

        // THE OWNER'S ID TRAVELS WITH THE NAME, and sending only the name was a quiet
        // corruption rather than a missing field. `CreateLeadRequest.OwnerUserId` defaults to
        // the CALLER when it is absent, while `OwnerName` is taken at face value - so a lead
        // captured on somebody else's behalf was stored owned by the capturer and labelled with
        // the other person's name. Every id-based query (my leads, the caller's own-records
        // scope, the assignment board's workload count) then disagreed with what the screen said.
        ownerUserId: input.ownerUserId || null,
        ownerName: input.owner && input.owner !== 'Unassigned' ? input.owner : null,
        nextAction: input.recommendedNextAction ?? null,
      })
      .subscribe({
        // THE SUBMIT USES THE ID FROM THIS RESPONSE, not a lookup in `leadIdsByReference`.
        // That map is only filled by `refresh()`, so reading it here would be the same
        // race the campaign wizard's Submit lost - the id is simply not there yet. The save
        // response carries it, which makes the chain exact.
        next: (created) => {
          const rollback = (error: unknown, fallback: string) => {
            this.leads.update((list) => list.filter((item) => item.id !== provisionalId));
            const message = apiErrorMessage(error, fallback);
            this.loadError.set(message);
            options?.onDone?.({ saved: false, error: message });
          };

          if (!options?.submit) {
            this.refresh();
            options?.onDone?.({ saved: true, reference: created.leadReference });
            return;
          }

          this.api
            .submitLead(created.id, { reason: 'Lead captured and submitted to the work queue.' })
            .subscribe({
              next: (submitted) => {
                this.refresh();
                options.onDone?.({ saved: true, reference: submitted.leadReference });
              },

              // SAVED BUT NOT SUBMITTED is a real state, not a failure to hide: the draft
              // exists and can be submitted again. Say so rather than implying nothing
              // happened.
              error: (error: unknown) =>
                rollback(
                  error,
                  'The lead was saved as a draft but could not be submitted to the work queue.',
                ),
            });
        },
        error: (error: unknown) => {
          this.leads.update((list) => list.filter((item) => item.id !== provisionalId));

          // THE SERVER'S OWN MESSAGE. A lead is refused for reasons the person can act on -
          // an unreachable contact, a language outside the catalogue, a campaign that is not
          // theirs - and a fixed sentence discarded every one of them.
          const message = apiErrorMessage(error, 'The lead could not be created.');
          this.loadError.set(message);
          options?.onDone?.({ saved: false, error: message });
        },
      });

    return optimistic;
  }

  /**
   * Replaces a lead in the loaded set.
   *
   * LOCAL ONLY, deliberately. There is no "save the whole lead" endpoint on the work queue -
   * every change to a lead goes through a named action (accept, assign, contact, qualify, close)
   * with its own permission and its own audit row. This keeps the signal in step between one of
   * those calls and the refresh that follows it.
   */
  upsertLead(lead: WorkflowLead): void {
    this.leads.update((list) => {
      const index = list.findIndex((item) => item.id === lead.id);

      if (index < 0) {
        return [lead, ...list];
      }

      const next = [...list];
      next[index] = { ...next[index], ...lead };
      return next;
    });
  }

  getLead(id: string | null | undefined): WorkflowLead | undefined {
    if (!id) return undefined;
    return this.leads().find((lead) => lead.id === id || lead.donorId === id);
  }

  /**
   * Assigns a lead to somebody.
   *
   * `ownerRef` IS THE OWNER'S API ID, not their display name, and the distinction is what this
   * method got wrong. The assignment board holds both - `OwnerOption.reference` and
   * `OwnerOption.label` - and passed the LABEL. `PeopleDirectoryService.idOf` matches on id or
   * staff code and never on name, so it returned undefined for every caller, the `?? owner`
   * fallback put a person's name in `newOwnerUserId`, and the API rejected it as an unparseable
   * Guid. Every assignment and every bulk route failed, while the board showed its own success
   * panel and the optimistic row kept the new owner on screen until the reload undid it.
   *
   * A REASON IS REQUIRED BY THE SERVER - ten characters at least - because an ownership change
   * somebody has to justify later is one worth recording the reason for. Callers that collect a
   * richer reason pass it; the rest get a plain statement of what happened.
   */
  assignLead(id: string, ownerRef: string, reason?: string): void {
    const leadId = this.leadIdsByReference.get(id);

    if (!leadId) {
      return;
    }

    const person = this.people.get(ownerRef);
    const ownerUserId = person?.reference ?? ownerRef;
    const ownerName = person?.name ?? ownerRef;

    // AN UNRESOLVED OWNER IS REFUSED HERE rather than sent. Without a directory match there is
    // no id to assign to, and posting the reference anyway produced a 400 whose message named a
    // JSON conversion rather than the real problem.
    if (!person) {
      this.loadError.set(
        `That owner could not be matched in the people directory, so ${id} was not reassigned.`,
      );
      return;
    }

    this.patchLead(id, {
      owner: ownerName,
      ownerUserId,
      stage: 'Assigned',
      lastActivity: `Assigned to ${ownerName}`,
    });

    const assignmentReason = reason?.trim()
      ? reason.trim()
      : `Reassigned to ${ownerName} from the lead workspace.`;

    this.api
      .assignLead(leadId, {
        newOwnerUserId: ownerUserId,
        newOwnerName: ownerName,
        assignmentReason:
          assignmentReason.length >= 10
            ? assignmentReason
            : `${assignmentReason} - recorded from the lead workspace.`,
        expectedVersion: this.leadVersionsByReference.get(id) ?? null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: (error: unknown) => {
          this.loadError.set(apiErrorMessage(error, 'The lead could not be assigned. Reloading.'));
          this.refresh();
        },
      });
  }

  /**
   * Converts a qualified lead into a donor.
   *
   * NOTHING IN THE APPLICATION CALLED THIS BEFORE. `DonorApiService.convertLead` existed, the
   * endpoint existed, the handler existed - and no screen in the module reached any of them, so
   * step 5 of the guided flow simply was not implemented. A lead could be captured, assigned,
   * contacted and qualified, and then stopped: the only way a donor appeared was a payment
   * creating one, which left the lead sitting in the queue as Qualified for ever and the
   * campaign attribution the conversion exists to carry was never established.
   *
   * `onDone` RECEIVES THE DONOR ID because the caller navigates to Donor 360 on success, and
   * that id only exists once the server has answered.
   */
  convertLead(
    id: string,
    input: {
      readonly conversionReason: string;
      /** Link to this donor instead of creating one. Used when deduplication found a match. */
      readonly existingDonorId?: string | null;
      readonly donorType?: string | null;
    },
    onDone?: (outcome: {
      readonly converted: boolean;
      readonly donorId?: string | null;
      readonly error?: string;
    }) => void,
  ): void {
    const leadId = this.leadIdsByReference.get(id);

    if (!leadId) {
      const message = `${id} is not loaded, so it was not converted. Refresh and try again.`;
      this.loadError.set(message);
      onDone?.({ converted: false, error: message });
      return;
    }

    // The server requires ten characters and refuses the whole conversion without them, so a
    // short reason is padded here rather than being rejected after the round trip.
    const reason = input.conversionReason.trim();

    this.api
      .convertLead(leadId, {
        existingDonorId: input.existingDonorId ?? null,
        donorType: input.donorType ?? null,
        conversionReason:
          reason.length >= 10 ? reason : `${reason} - converted from the lead workspace.`,
        expectedVersion: this.leadVersionsByReference.get(id) ?? null,
      })
      .subscribe({
        next: (lead) => {
          this.refresh();
          onDone?.({ converted: true, donorId: lead.convertedDonorId });
        },
        error: (error: unknown) => {
          // THE SERVER'S OWN MESSAGE. A conversion is refused for reasons the person can act on -
          // the lead is not Qualified yet, a donor with the same contact detail already exists -
          // and each one names the next step.
          const message = apiErrorMessage(error, 'The lead could not be converted to a donor.');
          this.loadError.set(message);
          onDone?.({ converted: false, error: message });
        },
      });
  }

  /**
   * Applies a change to a lead, and sends it.
   *
   * WHAT THIS USED TO BE. A local signal update and nothing else, with a comment saying a lead's
   * real state "moves through the named actions" - except twelve call sites across five screens
   * used this and only this. Marking a lead Qualified, Lost or Dormant, changing its temperature
   * or its donation potential, all of it lived in the browser and was gone on reload. A fundraiser
   * qualifying twenty leads in an afternoon had qualified nothing.
   *
   * IT NOW ROUTES BY WHAT CHANGED, because a lead's transitions are separate endpoints with
   * separate permissions rather than one status field:
   *
   *   - A STAGE CHANGE goes to the matching lifecycle endpoint. Qualified and Nurture are the same
   *     endpoint with a flag; Lost and Dormant are a close with a reason.
   *   - TEMPERATURE AND POTENTIAL are scoring fields on the lead itself, so they are a PUT.
   *   - AN ACTIVITY NOTE ALONE stays local. `lastActivity` is a breadcrumb the screen writes for
   *     itself - the server keeps its own history - and sending a PUT for it would overwrite real
   *     fields with whatever the row happened to be holding.
   *
   * The optimistic update is applied first so the screen stays responsive, and `refresh()` replaces
   * it with what the server actually stored.
   */
  patchLead(id: string, patch: Partial<WorkflowLead>): void {
    const current = this.getLead(id);

    this.leads.update((list) =>
      list.map((lead) => (lead.id === id ? { ...lead, ...patch } : lead)),
    );

    const leadId = this.leadIdsByReference.get(id);

    if (!current || !leadId) {
      return;
    }

    const expectedVersion = this.leadVersionsByReference.get(id) ?? null;
    const reason = patch.lastActivity?.trim() || 'Updated from the lead workspace.';

    // A reason has to be at least ten characters; the screens' breadcrumbs are often shorter.
    const padded = reason.length >= 10 ? reason : `${reason} - recorded from the lead workspace.`;

    if (patch.stage && patch.stage !== current.stage) {
      this.applyStageChange(id, leadId, patch.stage, padded, expectedVersion);
      return;
    }

    if (
      (patch.temperature && patch.temperature !== current.temperature)
      || (patch.donationPotential && patch.donationPotential !== current.donationPotential)
    ) {
      this.applyScoreChange(id, leadId, { ...current, ...patch }, expectedVersion);
    }
  }

  /**
   * A stage change, routed to the endpoint that owns it.
   *
   * ACCEPTED, QUALIFIED, NURTURE, LOST AND DORMANT ARE FIVE DIFFERENT DECISIONS, and the server
   * models them as such. A single "set the stage" call would have collapsed them into one
   * permission, which is exactly what the separate endpoints exist to prevent.
   */
  private applyStageChange(
    reference: string,
    leadId: string,
    stage: string,
    reason: string,
    expectedVersion: number | null,
  ): void {
    const done = {
      next: () => this.refresh(),
      error: () => {
        this.loadError.set('That change could not be saved. Reloading.');
        this.refresh();
      },
    };

    switch (stage) {
      case 'Accepted':
        this.api.acceptLead(leadId, { comment: reason, expectedVersion }).subscribe(done);
        return;

      case 'Qualified':
      case 'Nurture':
        this.api
          .qualifyLead(leadId, {
            qualificationNotes: reason,
            moveToNurture: stage === 'Nurture',
            expectedVersion,
          })
          .subscribe(done);
        return;

      case 'Lost':
      case 'Dormant':
        // BOTH ARE A CLOSE, and the reason is what distinguishes them in the record. Dormant is
        // not a softer Lost on the server - it is a closed lead whose reason says it may come back.
        this.api
          .closeLead(leadId, { reason: `${stage}: ${reason}`, expectedVersion })
          .subscribe(done);
        return;

      case 'Contacted':
        // Contact is logged through addCommunication, which carries the channel the consent check
        // needs and now posts it. A bare stage move to Contacted has no channel and would be
        // refused, so there is deliberately nothing to send from here.
        return;

      default:
        // An unmapped stage is left local rather than guessed at. Sending the wrong transition is
        // worse than sending none: it would be a real state change nobody asked for.
        return;
    }
  }

  /** Temperature and donation potential are fields on the lead, so they are a PUT. */
  private applyScoreChange(
    reference: string,
    leadId: string,
    lead: WorkflowLead,
    expectedVersion: number | null,
  ): void {
    // THE PUT REQUIRES A VERSION and there is no safe substitute. Sending 0 would either be
    // refused or - worse, if the row happened to be at 0 - would overwrite whatever a colleague
    // had just saved. An unknown version means this row is not properly loaded, so the honest
    // response is to reload rather than to guess.
    if (expectedVersion === null) {
      this.refresh();
      return;
    }

    const [firstName, ...rest] = lead.name.trim().split(/\s+/);

    this.api
      .updateLead(leadId, {
        firstName: firstName || lead.name,
        lastName: rest.join(' ') || null,
        mobileNumber: lead.mobile || null,
        emailAddress: lead.email || null,
        preferredLanguage: lead.language || null,
        campaignId: this.campaignIdsByName.get(lead.campaign) ?? '',
        source: lead.source || 'Manual',
        notes: `Temperature ${lead.temperature}, potential ${lead.donationPotential}.`,
        expectedVersion,
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => {
          this.loadError.set('The lead scoring could not be saved. Reloading.');
          this.refresh();
        },
      });
  }

  getRecord(id: string | null | undefined): WorkflowLead | WorkflowDonor | undefined {
    return this.getLead(id) ?? this.getDonor(id);
  }

  /**
   * Schedules a follow-up.
   *
   * THE SERVER CHECKS THE CONSENT before it accepts one. Scheduling a call to somebody who
   * withdrew phone consent is a breach committed by whoever scheduled it, and the planner's
   * consent warning is what gives them the chance not to - which is why
   * `consentWarningAcknowledged` is sent explicitly rather than assumed.
   *
   * It still returns synchronously so the calling screen can navigate; `refresh()` replaces the
   * optimistic row with the server's.
   */
  addFollowUp(
    input: Partial<WorkflowFollowUp> & Pick<WorkflowFollowUp, 'recordId'>,
  ): WorkflowFollowUp {
    const lead = this.getLead(input.recordId);
    const donor = this.getDonor(input.recordId);

    const optimistic: WorkflowFollowUp = {
      id: input.id ?? `FUP-PENDING-${Date.now()}`,
      recordId: input.recordId,
      recordName: input.recordName ?? lead?.name ?? donor?.name ?? input.recordId,
      recordType: input.recordType ?? (donor ? 'Donor' : 'Lead'),
      followUpType: input.followUpType ?? 'Call',
      scheduledDate: input.scheduledDate ?? new Date().toISOString().slice(0, 10),
      scheduledTime: input.scheduledTime ?? '10:00 AM',
      priority: input.priority ?? 'Medium',
      status: input.status ?? 'Pending',
      dependencyStatus: input.dependencyStatus ?? 'Ready',
      slaStatus: input.slaStatus ?? 'On Time',
      assignedTo: input.assignedTo ?? lead?.owner ?? donor?.owner ?? 'Unassigned',
      assignedToInitials: this.initials(
        input.assignedTo ?? lead?.owner ?? donor?.owner ?? 'Unassigned',
      ),
      campaign: input.campaign ?? lead?.campaign ?? donor?.campaign ?? '',
      phone: input.phone ?? '',
      email: input.email ?? '',
      purpose: input.purpose ?? 'Relationship follow-up',
      expectedOutcome: input.expectedOutcome ?? 'Progress relationship',
      successCriteria: input.successCriteria ?? 'Outcome recorded',
      reminderSettings: input.reminderSettings ?? '30 minutes before',
      attachments: input.attachments ?? [],
      history: input.history ?? [
        { date: new Date().toLocaleDateString('en-GB'), label: 'Created' },
      ],
    };

    this.followUps.update((list) => [
      optimistic,
      ...list.filter((item) => item.id !== optimistic.id),
    ]);

    const dueAtUtc = new Date(
      `${optimistic.scheduledDate}T${this.toIsoTime(optimistic.scheduledTime)}`,
    ).toISOString();

    this.api
      .scheduleFollowUp({
        donorId: donor ? donor.donorId : null,
        leadId: lead ? (this.leadIdsByReference.get(lead.id) ?? null) : null,

        // ID AND NAME TOGETHER. Sending only the name let the server keep its own fallback id -
        // the lead's owner, or failing that the caller - so a follow-up assigned to a colleague
        // was labelled with their name and owned by whoever scheduled it, and never appeared in
        // that colleague's queue.
        relationshipOwnerUserId:
          this.people.idOf(optimistic.assignedTo)
          ?? lead?.ownerUserId
          ?? donor?.ownerUserId
          ?? null,
        relationshipOwnerName: optimistic.assignedTo,
        purpose: optimistic.purpose,
        permittedChannel: optimistic.followUpType,
        nextAction: optimistic.expectedOutcome,
        dueAtUtc,
        priority: optimistic.priority,
        notes: null,

        // Explicit rather than defaulted: acknowledging a consent warning is a decision the
        // person makes on the planner, and sending true by default would record one they never
        // made.
        consentWarningAcknowledged: optimistic.dependencyStatus !== 'Blocked',
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => {
          this.followUps.update((list) => list.filter((item) => item.id !== optimistic.id));
          this.loadError.set('The follow-up could not be scheduled.');
        },
      });

    return optimistic;
  }

  /** "10:00 AM" to "10:00:00", so a scheduled date and time make a valid instant. */
  private toIsoTime(display: string): string {
    const match = /^(\d{1,2}):(\d{2})\s*(AM|PM)?$/i.exec(display.trim());

    if (!match) {
      return '09:00:00';
    }

    let hour = Number(match[1]);
    const minute = match[2];
    const meridiem = match[3]?.toUpperCase();

    if (meridiem === 'PM' && hour < 12) hour += 12;
    if (meridiem === 'AM' && hour === 12) hour = 0;

    return `${String(hour).padStart(2, '0')}:${minute}:00`;
  }

  /**
   * Applies a change to a follow-up, and sends it.
   *
   * IT WAS LOCAL ONLY, and nine call sites used it - completing a follow-up, rescheduling one,
   * cancelling one, reassigning one. A relationship manager who worked through their queue had
   * changed nothing: the tasks were all back the next morning, and the colleague covering for them
   * saw a queue that had never moved.
   *
   * ROUTED BY WHAT CHANGED, because each of those is a separate endpoint with its own permission
   * and its own required reason. A status field alone could not carry the reason a reschedule
   * needs, and a single setter would have collapsed four decisions into one.
   */
  patchFollowUp(id: string, patch: Partial<WorkflowFollowUp>): void {
    const current = this.getFollowUp(id);

    this.followUps.update((list) => list.map((item) => item.id === id ? { ...item, ...patch } : item));

    const followUpId = this.followUpIdsByReference.get(id);

    if (!current || !followUpId) {
      return;
    }

    const expectedVersion = this.followUpVersionsByReference.get(id) ?? null;

    const done = {
      next: () => this.refresh(),
      error: () => {
        this.loadError.set('The follow-up could not be saved. Reloading.');
        this.refresh();
      },
    };

    const status = patch.status;

    if (status && status !== current.status) {
      if (status === 'Completed') {
        this.api
          .completeFollowUp(followUpId, {
            completionOutcome: patch.expectedOutcome || current.expectedOutcome || 'Completed',
            completedAtUtc: new Date().toISOString(),
            expectedVersion,
          })
          .subscribe(done);

        return;
      }

      if (status === 'Cancelled') {
        this.api
          .cancelFollowUp(followUpId, {
            reason: patch.purpose || 'Cancelled from the follow-up workspace.',
            expectedVersion,
          })
          .subscribe(done);

        return;
      }
    }

    // A NEW DATE OR TIME IS A RESCHEDULE, whether or not the status moved with it. The server
    // requires a reason, because a task that keeps moving without one is how an overdue follow-up
    // stays permanently "upcoming".
    const dateChanged =
      (patch.scheduledDate && patch.scheduledDate !== current.scheduledDate)
      || (patch.scheduledTime && patch.scheduledTime !== current.scheduledTime);

    if (dateChanged) {
      const merged = { ...current, ...patch };

      this.api
        .rescheduleFollowUp(followUpId, {
          dueAtUtc: `${merged.scheduledDate}T${this.toIsoTime(merged.scheduledTime)}`,
          rescheduleReason: patch.purpose || 'Rescheduled from the follow-up workspace.',
          priority: merged.priority || null,
          expectedVersion,
        })
        .subscribe(done);

      return;
    }

    if (patch.assignedTo && patch.assignedTo !== current.assignedTo) {
      this.api
        .assignFollowUp(followUpId, {
          relationshipOwnerUserId: this.people.idOf(patch.assignedTo) ?? patch.assignedTo,
          relationshipOwnerName: this.people.name(patch.assignedTo),
          reason: 'Reassigned from the follow-up workspace.',
          expectedVersion,
        })
        .subscribe(done);
    }
    const updated = this.followUps().find((item) => item.id === id);
    if (updated) {
      const leadPatch: Partial<WorkflowLead> = { lastActivity: `Follow-up ${updated.status.toLowerCase()}` };
      if (updated.status === 'Completed') leadPatch.followUpStatus = 'Completed';
      if (updated.status === 'Cancelled') leadPatch.followUpStatus = 'Completed';
      if (updated.status === 'Pending' || updated.status === 'Rescheduled') {
        leadPatch.nextFollowUp = `${updated.scheduledDate} ${updated.scheduledTime}`;
        leadPatch.followUpStatus = 'Upcoming';
      }
      if (this.getLead(updated.recordId)) {
        this.patchLead(updated.recordId, leadPatch);
      } else if (this.getDonor(updated.recordId)) {
        const donorStatus = updated.status === 'Completed' || updated.status === 'Cancelled'
          ? 'None'
          : updated.status === 'Overdue'
            ? 'Overdue'
            : updated.status === 'Due Today'
              ? 'Due Today'
              : 'Upcoming';
        this.patchDonor(updated.recordId, { followUpStatus: donorStatus });
      }
    }
  }

  getFollowUp(id: string | null | undefined): WorkflowFollowUp | undefined {
    if (!id) return undefined;
    return this.followUps().find((f) => f.id === id);
  }

  followUpsFor(recordId: string): WorkflowFollowUp[] {
    return this.followUps().filter((f) => f.recordId === recordId);
  }

  addCommunication(input: Partial<WorkflowCommunication> & Pick<WorkflowCommunication, 'recordId' | 'type' | 'outcome' | 'summary'>): WorkflowCommunication {
    const communication: WorkflowCommunication = {
      id: input.id ?? `COM-${Date.now()}`,
      recordId: input.recordId,
      type: input.type,
      date: input.date ?? new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }),
      time: input.time ?? new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' }),
      createdBy: input.createdBy ?? this.getLead(input.recordId)?.owner ?? this.getDonor(input.recordId)?.owner ?? 'Current User',
      direction: input.direction ?? 'Outgoing',
      outcome: input.outcome,
      summary: input.summary,
      engagement: input.engagement,
      quality: input.quality,
      important: input.important ?? false,
      attachment: input.attachment,
      notes: input.notes,
      followUpDate: input.followUpDate,
      followUpTime: input.followUpTime,
      followUpPriority: input.followUpPriority,
      followUpPurpose: input.followUpPurpose,
      followUpStatus: input.followUpStatus,
    };
    this.communications.update((records) => [communication, ...records]);
    const lead = this.getLead(input.recordId);
    if (lead) {
      // THE CONTACT IS RECORDED ON THE SERVER. It was not, and the two halves of the code each
      // believed the other did it: `applyStageChange` returns without calling anything for the
      // Contacted stage, with a comment saying contact is logged through this method "which
      // carries the channel the consent check needs" - and this method only ever wrote to a
      // local signal. So no interaction row was ever created, the lead never legitimately
      // reached Contacted, and the consent gate that refuses a channel somebody withdrew never
      // ran at all.
      this.recordLeadContact(lead, communication);

      this.patchLead(input.recordId, {
        lastActivity: `${communication.type}: ${communication.outcome}`,
        lastContactOutcome: communication.outcome,
      });
    } else if (this.getDonor(input.recordId)) {
      this.patchDonor(input.recordId, { engagementTag: 'Recently Active' });
    }
    return communication;
  }

  /**
   * Sends one logged conversation to the lead's contact endpoint.
   *
   * THE CHANNEL IS THE POINT. The server checks it against the lead's consent rows and refuses a
   * channel the person withdrew, so it has to be the channel actually used - which is why an
   * interaction type with no consent-channel equivalent (a face-to-face meeting, an internal
   * note) is NOT quietly posted as a phone call. Those stay on the timeline and say so.
   */
  private recordLeadContact(lead: WorkflowLead, communication: WorkflowCommunication): void {
    const leadId = this.leadIdsByReference.get(lead.id);
    const channel = this.toConsentChannel(communication.type);

    if (!leadId) {
      return;
    }

    if (!channel) {
      this.loadError.set(
        `"${communication.type}" is not a channel the lead record can carry, so it was added to `
        + 'the timeline only. Log a call, e-mail, SMS or WhatsApp to update the lead itself.',
      );
      return;
    }

    this.api
      .contactLead(leadId, {
        channel,
        outcome: this.toContactOutcome(communication.outcome),
        notes: communication.summary || communication.notes || null,
        occurredAtUtc: new Date().toISOString(),
        nextAction: communication.followUpPurpose ?? null,
        expectedVersion: this.leadVersionsByReference.get(lead.id) ?? null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: (error: unknown) => {
          // A CONSENT REFUSAL IS THE IMPORTANT CASE and it arrives here. The server names the
          // channel the person did not permit, and that sentence is the whole value of the gate.
          this.loadError.set(
            apiErrorMessage(error, 'That contact could not be recorded against the lead.'),
          );
          this.refresh();
        },
      });
  }

  /** A screen's interaction type to the consent channel the API checks. Null when it has none. */
  private toConsentChannel(type: string): string | null {
    switch (type.trim().toLowerCase()) {
      case 'call':
      case 'phone':
      case 'phone call':
      case 'voice':
        return 'PhoneCall';
      case 'email':
      case 'e-mail':
        return 'Email';
      case 'sms':
      case 'text':
        return 'Sms';
      case 'whatsapp':
        return 'WhatsApp';
      case 'post':
      case 'letter':
        return 'Post';
      default:
        return null;
    }
  }

  /** A screen's outcome wording to the ContactOutcome the API accepts. */
  private toContactOutcome(outcome: string): string {
    switch (outcome.trim().toLowerCase()) {
      case 'no answer':
      case 'not reachable':
      case 'missed':
        return 'NoAnswer';
      case 'callback requested':
      case 'call back':
      case 'requested information':
        return 'CallbackRequested';
      case 'not interested':
      case 'declined':
        return 'NotInterested';
      case 'wrong number':
        return 'WrongNumber';
      case 'do not contact':
      case 'opt out':
        return 'DoNotContact';
      default:
        // Reached is the honest default: something was logged, so somebody was spoken to.
        return 'Reached';
    }
  }

  replaceCommunication(recordId: string, communication: WorkflowCommunication): void {
    this.communications.update((records) => records.map((record) => record.id === communication.id ? { ...communication, recordId } : record));
  }

  communicationsFor(recordId: string): WorkflowCommunication[] {
    return this.communications().filter((record) => record.recordId === recordId);
  }

  findRecordIdByName(name: string): string | undefined {
    return this.leads().find((lead) => lead.name === name)?.id ?? this.donors().find((donor) => donor.name === name)?.donorId;
  }

  /** Clears the loaded set and reloads from the server. */
  reset(): void {
    this.donors.set([]);
    this.leads.set([]);
    this.followUps.set([]);
    this.communications.set([]);
    this.refresh();
  }

  /**
   * THE localStorage SNAPSHOT IS GONE, and its removal is the point of this rewrite.
   *
   * Donors, leads, follow-ups and the interaction log used to be written into
   * `ydot-donor-lead-workflow-v2` on every change and restored on start. Three things were wrong
   * with that, and none was theoretical:
   *
   *   - IT WAS THE ONLY PLACE THE WORK EXISTED. A lead accepted and contacted lived in one
   *     browser; a colleague working the same queue saw none of it, and clearing site data threw
   *     it away.
   *   - IT SURVIVED SIGNING OUT. Donor names, e-mail addresses and interaction notes stayed on
   *     the machine after the session ended and were readable by the next person to use it - on
   *     a shared workstation that is a data-protection incident with no audit trail.
   *   - IT OUTLASTED THE ORGANISATION. Somebody switching between two organisations saw the
   *     first one's donors until the snapshot happened to be overwritten.
   *
   * The signals are now a cache of the server's answer for the life of the tab. Nothing donor
   * data touches disk on the client.
   */

  private initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).map((part) => part[0]).join('').slice(0, 2).toUpperCase();
  }
}
