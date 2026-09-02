import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, catchError, map, of } from 'rxjs';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  DonLookupItem,
  FollowUp as ApiFollowUp,
  FollowUpPlannerResponse,
} from '../../../../Shared/models/donor-contract.model';

export type RecordType = 'Lead' | 'Donor';
export type FollowUpType = 'Call' | 'Meeting' | 'Email' | 'SMS' | 'WhatsApp' | 'Task' | 'Site Visit';
export type Priority = 'Low' | 'Medium' | 'High' | 'Urgent';
export type FollowUpStatus = 'Pending' | 'Completed' | 'Cancelled' | 'Escalated' | 'Rescheduled';
export type DependencyStatus = 'Ready' | 'Blocked';
export type SlaStatus = 'On Time' | 'Approaching' | 'Breached';
export type QueueView = 'grid' | 'kanban' | 'calendar';

export interface HistoryEvent {
  date: string;
  label: string;
}

export interface FollowUp {
  id: string;
  recordId?: string;
  recordName: string;
  recordType: RecordType;
  followUpType: FollowUpType;
  scheduledDate: string;
  scheduledTime: string;
  priority: Priority;
  status: FollowUpStatus;
  dependencyStatus: DependencyStatus;
  dependencyBlockedReason?: string;
  slaStatus: SlaStatus;
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
  history: HistoryEvent[];
  /** The server's row version. Every write on this screen sends it back for the concurrency check. */
  version: number;
  /** What the caller may do to THIS follow-up, as the server decided it. */
  permittedActions: readonly string[];
}

export interface SavedView {
  id: string;
  label: string;
}

export interface CalendarDay {
  date: string;
  label: string;
  dayNumber: string;
  count: number;
}

export interface AgendaItem {
  time: string;
  title: string;
  followUpId: string;
}

/**
 * OWNERS - REMOVED as a constant.
 *
 * It listed four names compiled into the bundle, so every organisation's Reassign and Escalate
 * dropdowns offered the same four strangers - and reassigning to one of them wrote a name that
 * matched no user account. The owners now come from the API's `ownerOptions`, which are real
 * users inside the caller's scope.
 */

export const SAVED_VIEWS: SavedView[] = [
  { id: 'mine', label: 'My Follow-Ups' },
  { id: 'today', label: "Today's Follow-Ups" },
  { id: 'overdue', label: 'Overdue' },
  { id: 'upcoming', label: 'Upcoming' },
  { id: 'high', label: 'High Priority' },
  { id: 'meetings', label: 'Meetings' },
  { id: 'calls', label: 'Calls' },
  { id: 'escalated', label: 'Escalated' },
  { id: 'completedToday', label: 'Completed Today' },
];

function initials(name: string): string {
  return name.split(' ').map((p) => p[0]).join('').slice(0, 2).toUpperCase();
}

/**
 * MOCK_FOLLOW_UPS - REMOVED.
 *
 * It was an array of fabricated follow-ups compiled into the bundle and pushed into
 * `WorkflowStateService` on construction, so every organisation saw the same queue, every
 * reschedule was forgotten on refresh, and the counts across the top counted the file.
 */

function buildCalendarStrip(centerIso: string, source: FollowUp[]): CalendarDay[] {
  const days: CalendarDay[] = [];
  const center = new Date(centerIso + 'T00:00:00');
  for (let i = -2; i <= 2; i++) {
    const d = new Date(center);
    d.setDate(center.getDate() + i);
    const iso = d.toISOString().slice(0, 10);
    const count = source.filter((f) => f.scheduledDate === iso && f.status !== 'Cancelled').length;
    days.push({
      date: iso,
      label: d.toLocaleDateString('en-US', { weekday: 'short' }),
      dayNumber: String(d.getDate()),
      count,
    });
  }
  return days;
}

function to24h(time: string): number {
  const match = /(\d{1,2}):(\d{2})\s*(AM|PM)/i.exec(time);
  if (!match) return 0;
  let hours = parseInt(match[1], 10);
  const minutes = parseInt(match[2], 10);
  const period = match[3].toUpperCase();
  if (period === 'PM' && hours !== 12) hours += 12;
  if (period === 'AM' && hours === 12) hours = 0;
  return hours * 60 + minutes;
}

type QuickFilterKey = 'dueToday' | 'overdue' | 'upcoming' | 'highPriority' | 'attention' | 'meetings' | 'mine' | 'today' | 'calls' | 'escalated' | 'completedToday' | null;

interface GeneralFilters {
  status: Set<FollowUpStatus>;
  type: Set<FollowUpType>;
  priority: Set<Priority>;
  owner: string | null;
  campaign: string | null;
  dateFrom: string | null;
  dateTo: string | null;
}

type ModalKind = 'reschedule' | 'reassign' | 'cancel' | 'escalate' | 'history' | 'complete';

interface ActiveModal {
  kind: ModalKind;
  ids: string[];
}

const TODAY_ISO = new Date().toISOString().slice(0, 10);
const CURRENT_USER = 'Arun Kumar';

function emptyFilters(): GeneralFilters {
  return {
    status: new Set(),
    type: new Set(),
    priority: new Set(),
    owner: null,
    campaign: null,
    dateFrom: null,
    dateTo: null,
  };
}

@Component({
  selector: 'app-follow-up-queue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './follow-up-queue.html',
  styleUrls: ['./follow-up-queue.css'],
})
export class FollowUpQueueComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DonorApiService);
  readonly today = TODAY_ISO;
  readonly savedViews: SavedView[] = SAVED_VIEWS;
  /** Real users, from the API. `value` is the user id the write needs; `label` is the name. */
  readonly ownerOptions = signal<readonly DonLookupItem[]>([]);
  readonly owners = computed(() => this.ownerOptions().map((option) => option.label));
  readonly statusOptions: FollowUpStatus[] = ['Pending', 'Completed', 'Cancelled', 'Rescheduled', 'Escalated'];
  readonly typeOptions: FollowUpType[] = ['Call', 'Meeting', 'Email', 'SMS', 'WhatsApp', 'Task', 'Site Visit'];
  readonly priorityOptions: Priority[] = ['Low', 'Medium', 'High', 'Urgent'];
  /** Filled from whatever campaigns the loaded follow-ups actually belong to. */
  readonly campaigns = computed(() =>
    Array.from(new Set(this.followUps().map((f) => f.campaign).filter(Boolean))).sort(),
  );

  private readonly followUps = signal<FollowUp[]>([]);

  readonly viewMode = signal<QueueView>('grid');
  readonly searchTerm = signal('');
  readonly filtersOpen = signal(false);
  readonly activeFilters = signal<GeneralFilters>(emptyFilters());
  readonly draftFilters = signal<GeneralFilters>(emptyFilters());
  readonly activeSavedViewId = signal<string | null>(null);
  readonly activeQuickFilter = signal<QuickFilterKey>(null);
  readonly calendarCenterDate = signal<string>(TODAY_ISO);
  readonly selectedStripDate = signal<string | null>(null);
  readonly calendarMonthCursor = signal<string>('2026-08-01');
  readonly calendarSelectedDate = signal<string | null>(null);

  readonly selectedIds = signal<Set<string>>(new Set());
  readonly previewId = signal<string | null>(null);
  readonly activeModal = signal<ActiveModal | null>(null);
  readonly toastMessage = signal<string | null>(null);
  readonly openActionMenuId = signal<string | null>(null);
  private toastTimer: any = null;

  readonly rescheduleDate = signal('');
  readonly rescheduleTime = signal('');
  readonly rescheduleReason = signal('');
  readonly reassignOwner = signal('');
  readonly reassignReason = signal('');
  readonly cancelReason = signal('');
  readonly escalateTo = signal('');
  readonly escalateReason = signal('');
  readonly escalateNotes = signal('');
  readonly completionNote = signal('');
  readonly recordFilterId = signal(this.route.snapshot.queryParamMap.get('donorId') ?? this.route.snapshot.queryParamMap.get('leadId'));

  constructor() {
    this.load();
  }

  readonly loading = signal(false);
  readonly loadError = signal('');

  /**
   * The follow-ups scheduled for this owner's leads.
   *
   * THE DOCUMENT DEFINES THE SCOPE: "The Follow-Up Queue lists all follow-ups scheduled for leads
   * assigned to the particular owner." `onlyMine` is how the server is told that; resolving it
   * server-side from the token is what makes it true, because a browser cannot be trusted to say
   * whose queue it is looking at.
   */
  private load(): void {
    this.loading.set(true);
    this.loadError.set('');

    this.api.getFollowUpPlanner({ page: 1, pageSize: 200, onlyMine: true }).subscribe({
      next: (response: FollowUpPlannerResponse) => {
        this.followUps.set(response.followUps.items.map((item) => this.toQueueRow(item)));
        this.ownerOptions.set(response.ownerOptions);
        this.loading.set(false);

        const requestedId = this.route.snapshot.queryParamMap.get('followUpId');
        if (requestedId && this.followUps().some((item) => item.id === requestedId)) {
          this.previewId.set(requestedId);
          if (this.route.snapshot.queryParamMap.get('action') === 'reschedule') {
            queueMicrotask(() => this.openReschedule(requestedId));
          }
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(apiErrorMessage(error));
        this.showToast(this.loadError());
      },
    });
  }

  /**
   * Maps the API's follow-up onto the row this screen draws.
   *
   * THE DUE DATE ARRIVES AS ONE UTC INSTANT and the grid shows a date and a time separately, so
   * it is split here rather than stored twice. Splitting in local time is deliberate: a follow-up
   * due at 09:00 for a fundraiser in Chennai should read 09:00 to them.
   */
  private toQueueRow(item: ApiFollowUp): FollowUp {
    const due = item.dueAtUtc ? new Date(item.dueAtUtc) : null;
    const isLead = !!item.leadId;
    const owner = item.relationshipOwnerName ?? 'Unassigned';

    return {
      id: item.id,
      recordId: item.leadId ?? item.donorId ?? undefined,
      recordName: item.donorDisplayName ?? item.leadReference ?? item.followUpReference,
      recordType: isLead ? 'Lead' : 'Donor',

      // THE CHANNEL IS THE PERMITTED ONE, not a preference. The server refuses a follow-up on a
      // channel the donor has withdrawn consent for, so this is already the allowed answer.
      followUpType: this.toFollowUpType(item.permittedChannel),
      scheduledDate: due ? this.toDateInput(due) : '',
      scheduledTime: due ? this.toTimeInput(due) : '',
      priority: (item.priority as Priority) ?? 'Medium',
      status: (item.status as FollowUpStatus) ?? 'Pending',

      // A CONSENT WARNING IS A BLOCKER. The document's queue shows a dependency state; the real
      // dependency on a follow-up is whether the donor may be contacted on that channel at all.
      dependencyStatus: item.consentWarning?.hasWarning && !item.consentWarningAcknowledged
        ? 'Blocked'
        : 'Ready',
      dependencyBlockedReason: item.consentWarning?.hasWarning
        ? item.consentWarning.message
        : undefined,
      slaStatus: this.toSlaStatus(due),
      assignedTo: owner,
      assignedToInitials: initials(owner),
      campaign: '',

      // MASKED BY THE SERVER unless the caller holds the sensitive-contact permission.
      phone: '',
      email: '',
      purpose: item.purpose ?? '',
      expectedOutcome: item.nextAction ?? '',
      successCriteria: '',
      lastCommunicationOutcome: item.completionOutcome ?? undefined,
      reminderSettings: '',
      attachments: [],
      history: [],
      version: item.version,
      permittedActions: item.permittedActions ?? [],
    };
  }

  private toFollowUpType(channel: string): FollowUpType {
    switch (channel) {
      case 'Email': return 'Email';
      case 'Sms':
      case 'SMS': return 'SMS';
      case 'WhatsApp': return 'WhatsApp';
      case 'PhoneCall':
      case 'Call': return 'Call';
      case 'Meeting': return 'Meeting';
      case 'SiteVisit': return 'Site Visit';
      default: return 'Task';
    }
  }

  /**
   * On time / approaching / breached, computed from the due date.
   *
   * RECOMPUTED ON READ rather than stored, because overdue happens as time passes and not
   * because somebody saved the record - a stored value would be wrong for most of the day.
   */
  private toSlaStatus(due: Date | null): SlaStatus {
    if (!due) {
      return 'On Time';
    }
    const hoursAway = (due.getTime() - Date.now()) / 3_600_000;
    if (hoursAway < 0) return 'Breached';
    if (hoursAway < 24) return 'Approaching';
    return 'On Time';
  }

  private toDateInput(value: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
  }

  private toTimeInput(value: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(value.getHours())}:${pad(value.getMinutes())}`;
  }

  /** A date and a time from the form, back into the UTC instant the API stores. */
  private toDueUtc(date: string, time: string): string {
    return new Date(`${date}T${time || '09:00'}`).toISOString();
  }

  private followUpById(id: string): FollowUp | undefined {
    return this.followUps().find((item) => item.id === id);
  }

  readonly filteredFollowUps = computed<FollowUp[]>(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const gf = this.activeFilters();
    const quick = this.activeQuickFilter();
    const strip = this.selectedStripDate();

    return this.followUps().filter((f) => {
      if (this.recordFilterId() && f.recordId !== this.recordFilterId()) return false;
      if (term) {
        const hay = `${f.id} ${f.recordName} ${f.phone}`.toLowerCase();
        if (!hay.includes(term)) return false;
      }
      if (gf.status.size && !gf.status.has(f.status)) return false;
      if (gf.type.size && !gf.type.has(f.followUpType)) return false;
      if (gf.priority.size && !gf.priority.has(f.priority)) return false;
      if (gf.owner && f.assignedTo !== gf.owner) return false;
      if (gf.campaign && f.campaign !== gf.campaign) return false;
      if (gf.dateFrom && f.scheduledDate < gf.dateFrom) return false;
      if (gf.dateTo && f.scheduledDate > gf.dateTo) return false;
      if (strip && f.scheduledDate !== strip) return false;

      switch (quick) {
        case 'dueToday':
        case 'today':
          if (f.scheduledDate !== TODAY_ISO) return false;
          break;
        case 'overdue':
          if (!(f.scheduledDate < TODAY_ISO && f.status === 'Pending')) return false;
          break;
        case 'upcoming':
          if (!(f.scheduledDate > TODAY_ISO && f.status === 'Pending')) return false;
          break;
        case 'highPriority':
          if (!(f.priority === 'High' || f.priority === 'Urgent')) return false;
          break;
        case 'attention':
          if (!(f.status === 'Escalated' || f.slaStatus === 'Breached' || f.dependencyStatus === 'Blocked')) return false;
          break;
        case 'mine':
          if (f.assignedTo !== CURRENT_USER) return false;
          break;
        case 'escalated':
          if (f.status !== 'Escalated') return false;
          break;
        case 'meetings':
          if (f.followUpType !== 'Meeting') return false;
          break;
        case 'calls':
          if (f.followUpType !== 'Call') return false;
          break;
        case 'completedToday':
          if (!(f.status === 'Completed' && f.scheduledDate === TODAY_ISO)) return false;
          break;
      }
      return true;
    });
  });

  readonly kpiOverdue = computed(() => this.followUps().filter((f) => f.scheduledDate < TODAY_ISO && f.status === 'Pending').length);
  readonly overduePercent = computed(() => {
    const total = this.followUps().length;
    return total ? Math.round((this.kpiOverdue() / total) * 100) : 0;
  });
  readonly queueHealth = computed<'Healthy' | 'Warning' | 'Critical'>(() => {
    const p = this.overduePercent();
    if (p < 5) return 'Healthy';
    if (p <= 15) return 'Warning';
    return 'Critical';
  });

  readonly kanbanColumns = computed(() => {
    const list = this.filteredFollowUps();
    return [
      { key: 'dueToday', label: 'Due Today', items: list.filter((f) => f.scheduledDate === TODAY_ISO && f.status === 'Pending') },
      { key: 'upcoming', label: 'Upcoming', items: list.filter((f) => f.scheduledDate > TODAY_ISO && f.status === 'Pending') },
      { key: 'overdue', label: 'Overdue', items: list.filter((f) => f.scheduledDate < TODAY_ISO && f.status === 'Pending') },
      { key: 'completed', label: 'Completed', items: list.filter((f) => f.status === 'Completed') },
    ];
  });

  readonly calendarWeeks = computed(() => {
    const cursor = new Date(this.calendarMonthCursor() + 'T00:00:00');
    const year = cursor.getFullYear();
    const month = cursor.getMonth();
    const firstOfMonth = new Date(year, month, 1);
    const startOffset = firstOfMonth.getDay();
    const gridStart = new Date(firstOfMonth);
    gridStart.setDate(gridStart.getDate() - startOffset);

    const list = this.filteredFollowUps();
    const weeks = [];
    let cell = new Date(gridStart);
    for (let w = 0; w < 6; w++) {
      const week = [];
      for (let d = 0; d < 7; d++) {
        const iso = cell.toISOString().slice(0, 10);
        week.push({
          iso,
          dayNumber: cell.getDate(),
          inMonth: cell.getMonth() === month,
          items: list.filter((f) => f.scheduledDate === iso),
        });
        cell.setDate(cell.getDate() + 1);
      }
      weeks.push(week);
    }
    return weeks;
  });

  readonly calendarMonthLabel = computed(() =>
    new Date(this.calendarMonthCursor() + 'T00:00:00').toLocaleDateString('en-US', { month: 'long', year: 'numeric' }),
  );

  readonly previewFollowUp = computed<FollowUp | null>(() => {
    const id = this.previewId();
    return id ? this.followUps().find((f) => f.id === id) ?? null : null;
  });

  readonly allVisibleSelected = computed(() => {
    const visible = this.filteredFollowUps();
    if (!visible.length) return false;
    const sel = this.selectedIds();
    return visible.every((f) => sel.has(f.id));
  });

  readonly calendarStrip = computed<CalendarDay[]>(() => buildCalendarStrip(this.calendarCenterDate(), this.followUps()));

  readonly agendaItems = computed<AgendaItem[]>(() =>
    this.followUps()
      .filter((f) => f.scheduledDate === TODAY_ISO && f.status !== 'Cancelled')
      .sort((a, b) => to24h(a.scheduledTime) - to24h(b.scheduledTime))
      .map((f) => ({ time: f.scheduledTime, title: `${f.followUpType} \u00b7 ${f.recordName}`, followUpId: f.id })),
  );

  readonly kpiDueToday = computed(() => this.followUps().filter((f) => f.scheduledDate === TODAY_ISO && f.status === 'Pending').length);
  readonly kpiUpcoming = computed(() => this.followUps().filter((f) => f.scheduledDate > TODAY_ISO && f.status === 'Pending').length);
  readonly kpiCompletedToday = computed(() => this.followUps().filter((f) => f.status === 'Completed' && f.history.some((h) => h.label.toLowerCase().includes('completed') && h.date === new Date().toLocaleDateString('en-GB'))).length || this.followUps().filter((f) => f.status === 'Completed' && f.scheduledDate === TODAY_ISO).length);
  readonly kpiEscalated = computed(() => this.followUps().filter((f) => f.status === 'Escalated').length);
  readonly kpiCompletionRate = computed(() => {
    const total = this.followUps().filter((f) => f.status !== 'Cancelled').length;
    const done = this.followUps().filter((f) => f.status === 'Completed').length;
    return total ? Math.round((done / total) * 100) : 0;
  });

  readonly slaBreakdown = computed(() => {
    const list = this.followUps().filter((f) => f.status === 'Pending' || f.status === 'Escalated');
    return {
      onTime: list.filter((f) => f.slaStatus === 'On Time').length,
      approaching: list.filter((f) => f.slaStatus === 'Approaching').length,
      breached: list.filter((f) => f.slaStatus === 'Breached').length,
    };
  });

  readonly agingBuckets = computed(() => {
    const buckets = { b0: 0, b1: 0, b2: 0, b3: 0 };
    const now = new Date(TODAY_ISO + 'T00:00:00').getTime();
    this.followUps()
      .filter((f) => f.status === 'Pending')
      .forEach((f) => {
        const scheduled = new Date(f.scheduledDate + 'T00:00:00').getTime();
        const days = Math.max(0, Math.round((now - scheduled) / 86400000));
        if (days <= 3) buckets.b0++;
        else if (days <= 7) buckets.b1++;
        else if (days <= 15) buckets.b2++;
        else buckets.b3++;
      });
    return buckets;
  });

  readonly activeFilterCount = computed(() => {
    const f = this.activeFilters();
    return f.status.size + f.type.size + f.priority.size + (f.owner ? 1 : 0) + (f.campaign ? 1 : 0) + (f.dateFrom ? 1 : 0) + (f.dateTo ? 1 : 0);
  });

  readonly historyFollowUp = computed<FollowUp | null>(() => {
    const modal = this.activeModal();
    if (!modal || modal.kind !== 'history') return null;
    return this.followUps().find((f) => f.id === modal.ids[0]) ?? null;
  });

  readonly bulkFollowUps = computed<FollowUp[]>(() => {
    const ids = this.selectedIds();
    return this.followUps().filter((f) => ids.has(f.id));
  });

  setView(view: QueueView) {
    this.viewMode.set(view);
  }

  onSearchChange(value: string) {
    this.searchTerm.set(value);
  }

  toggleFiltersPanel() {
    if (!this.filtersOpen()) {
      this.draftFilters.set(this.cloneFilters(this.activeFilters()));
    }
    this.filtersOpen.set(!this.filtersOpen());
  }

  private cloneFilters(f: GeneralFilters): GeneralFilters {
    return {
      status: new Set(f.status),
      type: new Set(f.type),
      priority: new Set(f.priority),
      owner: f.owner,
      campaign: f.campaign,
      dateFrom: f.dateFrom,
      dateTo: f.dateTo,
    };
  }

  toggleDraftSet<T extends string>(key: 'status' | 'type' | 'priority', value: T) {
    const draft = this.cloneFilters(this.draftFilters());
    const set = draft[key] as unknown as Set<T>;
    if (set.has(value)) set.delete(value);
    else set.add(value);
    this.draftFilters.set(draft);
  }

  setDraftOwner(v: string) {
    this.draftFilters.update((f) => ({ ...this.cloneFilters(f), owner: v || null }));
  }
  setDraftCampaign(v: string) {
    this.draftFilters.update((f) => ({ ...this.cloneFilters(f), campaign: v || null }));
  }
  setDraftDateFrom(v: string) {
    this.draftFilters.update((f) => ({ ...this.cloneFilters(f), dateFrom: v || null }));
  }
  setDraftDateTo(v: string) {
    this.draftFilters.update((f) => ({ ...this.cloneFilters(f), dateTo: v || null }));
  }

  applyFilters() {
    this.activeFilters.set(this.cloneFilters(this.draftFilters()));
    this.activeSavedViewId.set(null);
    this.filtersOpen.set(false);
    this.showToast('Filters applied');
  }

  resetFilters() {
    this.draftFilters.set(emptyFilters());
    this.activeFilters.set(emptyFilters());
    this.activeQuickFilter.set(null);
    this.activeSavedViewId.set(null);
    this.searchTerm.set('');
    this.showToast('Filters reset');
  }

  applySavedView(view: SavedView) {
    this.activeFilters.set(emptyFilters());
    this.selectedStripDate.set(null);
    this.activeSavedViewId.set(view.id);
    const map: Record<string, QuickFilterKey> = {
      mine: 'mine', today: 'today', overdue: 'overdue', upcoming: 'upcoming', high: 'highPriority',
      meetings: 'meetings', calls: 'calls', escalated: 'escalated', completedToday: 'completedToday',
    };
    this.activeQuickFilter.set(map[view.id] ?? null);
  }

  shiftCalendarMonth(months: number) {
    const d = new Date(this.calendarMonthCursor() + 'T00:00:00');
    d.setMonth(d.getMonth() + months);
    this.calendarMonthCursor.set(d.toISOString().slice(0, 10));
  }

  onCalendarDayClick(day: { iso: string; items: FollowUp[] }) {
    if (day.items.length === 1) {
      this.openPreview(day.items[0].id);
    } else {
      this.calendarSelectedDate.set(day.iso);
    }
  }

  isSelected(id: string) { return this.selectedIds().has(id); }
  toggleSelect(id: string) {
    const set = new Set(this.selectedIds());
    if (set.has(id)) set.delete(id); else set.add(id);
    this.selectedIds.set(set);
  }
  toggleSelectAllVisible() {
    const visible = this.filteredFollowUps();
    if (this.allVisibleSelected()) {
      const set = new Set(this.selectedIds());
      visible.forEach((f) => set.delete(f.id));
      this.selectedIds.set(set);
    } else {
      const set = new Set(this.selectedIds());
      visible.forEach((f) => set.add(f.id));
      this.selectedIds.set(set);
    }
  }
  clearSelection() {
    this.selectedIds.set(new Set());
  }

  openPreview(id: string) { this.previewId.set(id); }
  closePreview() { this.previewId.set(null); }

  openReschedule(id: string) {
    const f = this.followUps().find((x) => x.id === id);
    this.rescheduleDate.set(f?.scheduledDate ?? '');
    this.rescheduleTime.set(f?.scheduledTime ?? '');
    this.rescheduleReason.set('');
    this.activeModal.set({ kind: 'reschedule', ids: [id] });
  }
  openBulkReschedule() {
    this.rescheduleDate.set('');
    this.rescheduleTime.set('');
    this.rescheduleReason.set('');
    this.activeModal.set({ kind: 'reschedule', ids: [...this.selectedIds()] });
  }
  /**
   * Reschedule - the document's own menu action.
   *
   * ONE REQUEST PER FOLLOW-UP, because each carries its own version. Bundling them would mean
   * dropping the concurrency check, and a follow-up somebody else moved while this dialog was
   * open would be silently overwritten rather than reported.
   */
  confirmReschedule() {
    const modal = this.activeModal();
    if (!modal || !this.rescheduleDate()) {
      return;
    }

    const dueAtUtc = this.toDueUtc(this.rescheduleDate(), this.rescheduleTime());
    const reason = this.rescheduleReason().trim() || 'Rescheduled from the follow-up queue.';

    this.runBatch(
      modal.ids,
      (id) => {
        const row = this.followUpById(id);
        return this.api.rescheduleFollowUp(id, {
          dueAtUtc,
          rescheduleReason: reason,
          expectedVersion: row?.version ?? null,
        });
      },
      (count) => (count > 1 ? `${count} follow-ups rescheduled` : 'Follow-up rescheduled'),
    );
  }

  openReassign(id: string) {
    this.reassignOwner.set('');
    this.activeModal.set({ kind: 'reassign', ids: [id] });
  }
  openBulkReassign() {
    this.reassignOwner.set('');
    this.activeModal.set({ kind: 'reassign', ids: [...this.selectedIds()] });
  }
  confirmReassign() {
    const modal = this.activeModal();
    const ownerName = this.reassignOwner();
    if (!modal || !ownerName) {
      return;
    }

    // THE USER ID, NOT THE NAME. The dropdown shows names; the API assigns to an account.
    const owner = this.ownerOptions().find((option) => option.label === ownerName);
    if (!owner) {
      this.showToast('Choose an owner from the list.');
      return;
    }

    this.runBatch(
      modal.ids,
      (id) => {
        const row = this.followUpById(id);
        return this.api.assignFollowUp(id, {
          relationshipOwnerUserId: owner.value,
          relationshipOwnerName: owner.label,
          reason: 'Reassigned from the follow-up queue.',
          expectedVersion: row?.version ?? null,
        });
      },
      (count) => (count > 1 ? `${count} reassigned to ${ownerName}` : `Reassigned to ${ownerName}`),
    );
  }

  openCancel(id: string) {
    this.cancelReason.set('');
    this.activeModal.set({ kind: 'cancel', ids: [id] });
  }
  openBulkCancel() {
    this.cancelReason.set('');
    this.activeModal.set({ kind: 'cancel', ids: [...this.selectedIds()] });
  }
  confirmCancel() {
    const modal = this.activeModal();
    const reason = this.cancelReason().trim();
    if (!modal || !reason) {
      return;
    }

    this.runBatch(
      modal.ids,
      (id) => {
        const row = this.followUpById(id);
        return this.api.cancelFollowUp(id, { reason, expectedVersion: row?.version ?? null });
      },
      (count) => (count > 1 ? `${count} cancelled` : 'Follow-up cancelled'),
    );
  }

  openEscalate(id: string) {
    this.escalateTo.set('');
    this.escalateReason.set('');
    this.activeModal.set({ kind: 'escalate', ids: [id] });
  }
  /**
   * Escalate - the document's menu action, which "opens the escalation pop-up".
   *
   * IT IS A REASSIGNMENT WITH A REASON, and that is the honest mapping rather than a shortcut.
   * There is no separate escalation state on a follow-up; what escalating a follow-up means in
   * practice is handing it to somebody more senior and recording why, which is exactly what
   * `assign` does - and unlike a local 'Escalated' string, the new owner actually sees it in
   * their own queue.
   */
  confirmEscalate() {
    const modal = this.activeModal();
    const target = this.escalateTo();
    const reason = this.escalateReason().trim();
    if (!modal || !target || !reason) {
      return;
    }

    const owner = this.ownerOptions().find((option) => option.label === target);
    if (!owner) {
      this.showToast('Choose somebody to escalate to.');
      return;
    }

    this.runBatch(
      modal.ids,
      (id) => {
        const row = this.followUpById(id);
        return this.api.assignFollowUp(id, {
          relationshipOwnerUserId: owner.value,
          relationshipOwnerName: owner.label,
          reason: `Escalated: ${reason}`,
          expectedVersion: row?.version ?? null,
        });
      },
      () => `Escalated to ${target}`,
    );
  }

  openCompletion(id: string) {
    const item = this.followUpById(id);
    this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
      queryParams: {
        followUpId: id,
        leadId: item?.recordType === 'Lead' ? item.recordId : null,
        donorId: item?.recordType === 'Donor' ? item.recordId : null,
      },
    });
  }

  openBulkComplete() {
    this.completionNote.set('');
    this.activeModal.set({ kind: 'complete', ids: [...this.selectedIds()] });
  }
  confirmBulkComplete() {
    const modal = this.activeModal();
    if (!modal) {
      return;
    }

    const outcome = this.completionNote().trim() || 'Completed from the follow-up queue.';

    this.runBatch(
      modal.ids,
      (id) => {
        const row = this.followUpById(id);
        return this.api.completeFollowUp(id, {
          completionOutcome: outcome,
          expectedVersion: row?.version ?? null,
        });
      },
      (count) => (count > 1 ? `${count} follow-ups marked complete` : 'Follow-up marked complete'),
      () => this.clearSelection(),
    );
  }

  /**
   * Runs one call per selected follow-up and reports the set.
   *
   * EACH FAILURE IS CAUGHT INTO A BOOLEAN rather than thrown. `forkJoin` abandons the whole set
   * on the first error, which would leave somebody unable to tell which of twelve follow-ups had
   * moved - so the set always completes and the message says how many did not.
   */
  private runBatch(
    ids: readonly string[],
    call: (id: string) => import('rxjs').Observable<unknown>,
    message: (count: number) => string,
    onDone?: () => void,
  ): void {
    const unique = [...new Set(ids)];
    if (unique.length === 0) {
      return;
    }

    this.loading.set(true);

    forkJoin(
      unique.map((id) =>
        call(id).pipe(
          map(() => true),
          catchError(() => of(false)),
        ),
      ),
    ).subscribe((results) => {
      const failed = results.filter((ok) => !ok).length;
      this.loading.set(false);
      this.closeModal();
      onDone?.();

      this.showToast(
        failed === 0
          ? message(unique.length)
          : `${unique.length - failed} of ${unique.length} updated; ${failed} could not be changed.`,
      );

      // RELOAD RATHER THAN PATCH. Completing or rescheduling changes the SLA badge and the
      // version, both of which the server decides.
      this.load();
    });
  }

  canExecute(f: FollowUp): boolean { return f.dependencyStatus !== 'Blocked'; }

  executeFollowUp(f: FollowUp) {
    if (!this.canExecute(f)) {
      this.showToast(`Execution blocked \u2014 ${f.dependencyBlockedReason ?? 'dependency not completed'}`);
      return;
    }
    this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
      queryParams: {
        followUpId: f.id,
        leadId: f.recordType === 'Lead' ? f.recordId : null,
        donorId: f.recordType === 'Donor' ? f.recordId : null,
      },
    });
  }

  toggleActionMenu(id: string, ev: Event) {
    ev.stopPropagation();
    this.openActionMenuId.set(this.openActionMenuId() === id ? null : id);
  }
  @HostListener('document:keydown.escape')
  onEscapeKey() {
    if (this.activeModal()) { this.closeModal(); return; }
    if (this.openActionMenuId()) { this.closeActionMenu(); return; }
    if (this.previewId()) { this.closePreview(); }
  }

  @HostListener('document:click')
  closeActionMenu() { this.openActionMenuId.set(null); }

  openHistory(id: string) { this.activeModal.set({ kind: 'history', ids: [id] }); }

  duplicateFollowUp(id: string) {
    this.router.navigate(['/app/don/follow-up-planner'], { queryParams: { mode: 'duplicate', sourceId: id } });
  }

  onStripDateClick(iso: string) {
    this.selectedStripDate.set(this.selectedStripDate() === iso ? null : iso);
    this.activeSavedViewId.set(null);
  }

  setQuickFilter(key: QuickFilterKey) {
    this.activeQuickFilter.set(this.activeQuickFilter() === key ? null : key);
    this.activeSavedViewId.set(null);
  }

  closeModal() { this.activeModal.set(null); }

  private draggingId: string | null = null;
  onDragStart(id: string, ev: DragEvent) {
    this.draggingId = id;
    ev.dataTransfer?.setData('text/plain', id);
  }
  onDropOnCompleted(ev: DragEvent) {
    ev.preventDefault();
    const id = this.draggingId ?? ev.dataTransfer?.getData('text/plain');
    if (id) this.openCompletion(id);
    this.draggingId = null;
  }
  allowDrop(ev: DragEvent) { ev.preventDefault(); }

  refresh() { this.load(); this.showToast('Queue refreshed'); }
  exportQueue() { this.showToast(`Exporting ${this.filteredFollowUps().length} follow-ups`); }
  createFollowUp() { this.router.navigate(['/app/don/follow-up-planner'], { queryParams: { mode: 'create' } }); }

  private showToast(message: string) {
    this.toastMessage.set(message);
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toastMessage.set(null), 3200);
  }

  formatDate(iso: string): string {
    return new Date(iso + 'T00:00:00').toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  slaClass(status: SlaStatus): string {
    return status === 'On Time' ? 'sla-ontime' : status === 'Approaching' ? 'sla-approaching' : 'sla-breached';
  }
  priorityClass(p: Priority): string { return `priority-${p.toLowerCase()}`; }
  statusClass(s: FollowUpStatus): string { return `status-${s.toLowerCase()}`; }
  healthClass(): string { return `health-${this.queueHealth().toLowerCase()}`; }

  isOverdue(f: FollowUp): boolean { return f.scheduledDate < TODAY_ISO && f.status === 'Pending'; }
  trackById(_index: number, item: FollowUp): string { return item.id; }
  trackByDay(_index: number, item: CalendarDay): string { return item.date; }
  trackByString(_index: number, item: string): string { return item; }
  maxBucket(): number {
    const b = this.agingBuckets();
    return Math.max(1, b.b0, b.b1, b.b2, b.b3);
  }
}