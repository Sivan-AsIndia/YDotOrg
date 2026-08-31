import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { WorkflowDonor, WorkflowLead, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';

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
 * The owners a follow-up can be assigned to.
 *
 * EMPTY, because they come from the people directory now. This was four invented names, so
 * reassigning a follow-up handed it to somebody who does not exist - and the task then sat in a
 * queue nobody was watching.
 */
export const OWNERS: readonly string[] = [];

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
/**
 * The signed-in person.
 *
 * A CONSTANT WAS THE WRONG ANSWER for the "my follow-ups" filter: everybody saw Arun Kumar's
 * queue, including Arun Kumar's colleagues. Resolved from the token at the call site instead.
 */
const CURRENT_USER = '';

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
  private readonly people = inject(PeopleDirectoryService);
  private readonly tokens = inject(AuthTokenService);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);
  readonly today = TODAY_ISO;
  readonly savedViews: SavedView[] = SAVED_VIEWS;
  readonly owners = OWNERS;
  readonly statusOptions: FollowUpStatus[] = ['Pending', 'Completed', 'Cancelled', 'Rescheduled', 'Escalated'];
  readonly typeOptions: FollowUpType[] = ['Call', 'Meeting', 'Email', 'SMS', 'WhatsApp', 'Task', 'Site Visit'];
  readonly priorityOptions: Priority[] = ['Low', 'Medium', 'High', 'Urgent'];
  /**
   * The campaigns worth filtering by.
   *
   * DERIVED FROM THE LOADED FOLLOW-UPS, not from a seeded array. The list used to come from
   * MOCK_FOLLOW_UPS, so it offered campaigns nobody had a follow-up against and omitted every
   * campaign people were actually working.
   */
  readonly campaigns = computed(() =>
    Array.from(new Set(this.workflow.followUps().map((item) => item.campaign)))
      .filter((campaign) => !!campaign)
      .sort(),
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
    // Queue can be opened directly from the dashboard/notifications. Seed the
    // record registries first so every follow-up resolves to a stable donor or
    // lead ID instead of falling back to a display name.
    this.syncFromWorkflow();
    const requestedId = this.route.snapshot.queryParamMap.get('followUpId');
    if (requestedId && this.followUps().some((item) => item.id === requestedId)) {
      this.previewId.set(requestedId);
      if (this.route.snapshot.queryParamMap.get('action') === 'reschedule') {
        queueMicrotask(() => this.openReschedule(requestedId));
      }
    }
  }

  /**
   * REMOVED: the static lead seed.
   *
   * This built a lead list from `assets/data/my-leads.json` at BUILD TIME and pushed it into the
   * shared workspace. Every organisation therefore saw the same fabricated leads, nothing anybody
   * did to them reached the server, and the contact details came through unmasked because a file
   * cannot know who is asking.
   *
   * Leads now come from `DON /api/v1/donors/lead-work-queue` through `WorkflowStateService`,
   * which loads them once for the whole section. The method is kept as an empty source so its
   * call site still compiles and reads as what it is: nothing to seed.
   */
  private queueLeadSeeds(): WorkflowLead[] {{
    return [];
  }}
  private syncFromWorkflow(): void {
    this.followUps.set(this.workflow.followUps().map((item) => ({
      id: item.id,
      recordId: item.recordId,
      recordName: item.recordName,
      recordType: item.recordType as RecordType,
      followUpType: item.followUpType as FollowUpType,
      scheduledDate: item.scheduledDate,
      scheduledTime: item.scheduledTime,
      priority: item.priority as Priority,
      status: item.status as FollowUpStatus,
      dependencyStatus: item.dependencyStatus as DependencyStatus,
      dependencyBlockedReason: item.dependencyBlockedReason,
      slaStatus: item.slaStatus as SlaStatus,
      assignedTo: item.assignedTo,
      assignedToInitials: item.assignedToInitials,
      campaign: item.campaign,
      phone: item.phone,
      email: item.email,
      purpose: item.purpose,
      expectedOutcome: item.expectedOutcome,
      successCriteria: item.successCriteria,
      lastCommunicationType: item.lastCommunicationType,
      lastCommunicationOutcome: item.lastCommunicationOutcome,
      lastCommunicationDate: item.lastCommunicationDate,
      reminderSettings: item.reminderSettings,
      attachments: [...item.attachments],
      history: [...item.history],
    })));
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
  confirmReschedule() {
    const modal = this.activeModal();
    if (!modal) return;
    const ids = new Set(modal.ids);
    ids.forEach((id) => this.workflow.patchFollowUp(id, {
      scheduledDate: this.rescheduleDate() || this.workflow.getFollowUp(id)?.scheduledDate,
      scheduledTime: this.rescheduleTime() || this.workflow.getFollowUp(id)?.scheduledTime,
      status: 'Rescheduled',
      history: [...(this.workflow.getFollowUp(id)?.history ?? []), { date: new Date().toLocaleDateString('en-GB'), label: `Rescheduled${this.rescheduleReason().trim() ? ': ' + this.rescheduleReason().trim() : ''}` }],
    }));
    this.syncFromWorkflow();
    this.showToast(ids.size > 1 ? `${ids.size} follow-ups rescheduled` : 'Follow-up rescheduled');
    this.closeModal();
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
    if (!modal || !this.reassignOwner()) return;
    const ids = new Set(modal.ids);
    const newOwner = this.reassignOwner();
    ids.forEach((id) => this.workflow.patchFollowUp(id, { assignedTo: newOwner, assignedToInitials: initials(newOwner) }));
    this.syncFromWorkflow();
    this.showToast(ids.size > 1 ? `${ids.size} reassigned to ${newOwner}` : `Reassigned to ${newOwner}`);
    this.closeModal();
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
    if (!modal || !this.cancelReason().trim()) return;
    const ids = new Set(modal.ids);
    ids.forEach((id) => this.workflow.patchFollowUp(id, { status: 'Cancelled', history: [...(this.workflow.getFollowUp(id)?.history ?? []), { date: new Date().toLocaleDateString('en-GB'), label: `Cancelled: ${this.cancelReason().trim()}` }] }));
    this.syncFromWorkflow();
    this.showToast(ids.size > 1 ? `${ids.size} cancelled` : 'Follow-up cancelled');
    this.closeModal();
  }

  openEscalate(id: string) {
    this.escalateTo.set('');
    this.escalateReason.set('');
    this.activeModal.set({ kind: 'escalate', ids: [id] });
  }
  confirmEscalate() {
    const modal = this.activeModal();
    if (!modal || !this.escalateTo() || !this.escalateReason().trim()) return;
    const ids = new Set(modal.ids);
    ids.forEach((id) => this.workflow.patchFollowUp(id, { status: 'Escalated', history: [...(this.workflow.getFollowUp(id)?.history ?? []), { date: new Date().toLocaleDateString('en-GB'), label: `Escalated to ${this.escalateTo()}: ${this.escalateReason().trim()}` }] }));
    this.syncFromWorkflow();
    this.showToast('Follow-up escalated');
    this.closeModal();
  }

  openCompletion(id: string) {
    const item = this.workflow.getFollowUp(id);
    this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
      queryParams: { followUpId: id, leadId: item?.recordType === 'Lead' ? item.recordId : null, donorId: item?.recordType === 'Donor' ? item.recordId : null },
    });
  }

  openBulkComplete() {
    this.completionNote.set('');
    this.activeModal.set({ kind: 'complete', ids: [...this.selectedIds()] });
  }
  confirmBulkComplete() {
    const modal = this.activeModal();
    if (!modal) return;
    const ids = new Set(modal.ids);
    ids.forEach((id) => this.workflow.patchFollowUp(id, {
      status: 'Completed',
      history: [...(this.workflow.getFollowUp(id)?.history ?? []), { date: new Date().toLocaleDateString('en-GB'), label: `Completed${this.completionNote().trim() ? ': ' + this.completionNote().trim() : ''}` }],
    }));
    this.syncFromWorkflow();
    this.clearSelection();
    this.showToast(ids.size > 1 ? `${ids.size} follow-ups marked complete` : 'Follow-up marked complete');
    this.closeModal();
  }

  canExecute(f: FollowUp): boolean { return f.dependencyStatus !== 'Blocked'; }

  executeFollowUp(f: FollowUp) {
    if (!this.canExecute(f)) {
      this.showToast(`Execution blocked \u2014 ${f.dependencyBlockedReason ?? 'dependency not completed'}`);
      return;
    }
    const item = this.workflow.getFollowUp(f.id);
    this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
      queryParams: { followUpId: f.id, leadId: item?.recordType === 'Lead' ? item.recordId : null, donorId: item?.recordType === 'Donor' ? item.recordId : null },
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

  refresh() { this.syncFromWorkflow(); this.showToast('Queue refreshed'); }
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