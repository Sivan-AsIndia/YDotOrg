import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';

/* ============================================================
   SCR-UX-001 — My Workspace
   Route: /workspace/my-workspace   View permission: ux.my-workspace.view
   Purpose (4.1): Prioritised tasks, follow-ups, exceptions,
   approvals and recent records.
   ============================================================ */

type WorkCategory = 'tasks' | 'approvals' | 'followups' | 'exceptions';
type WorkPriority = 'low' | 'medium' | 'high';
type WorkStatus = 'pending' | 'in-progress' | 'completed';
/** Server-derived SLA / due state (4.1.2 "Due state" + "SLA state"). */
type SlaTone = 'ok' | 'warn' | 'breach';
/** Required UI states (4.1.4). Default surface is `ready`. */
type UiState = 'loading' | 'empty' | 'no-access' | 'ready';

/**
 * A single prioritised work item.
 * Every read-only field from the 4.1.2 field-and-control contract is carried
 * on the model — Work reference, Title, Business record, Module, Status,
 * Due date and time, Assigned owner, SLA state and Recent activity — so no
 * listed information is lost even where a column is not surfaced on desktop.
 */
interface WorkItem {
  readonly id: string;
  readonly reference: string; // Work reference (read-only, server-derived)
  readonly title: string; // Title (read-only)
  readonly businessRecord: string; // Business record (read-only)
  readonly module: string; // Module (read-only)
  readonly category: WorkCategory; // grouping used by the Filter action
  readonly typeLabel: string; // Work type (controlled catalogue label)
  readonly icon: string; // remixicon class for the item glyph
  readonly iconTone: 'ink' | 'good' | 'info' | 'gold' | 'error';
  readonly dueLabel: string; // Due date and time (read-only)
  readonly dueState: 'overdue' | 'due-today' | 'upcoming'; // Due state
  readonly priority: WorkPriority; // Priority
  readonly status: WorkStatus; // Status (read-only badge)
  readonly owner: string; // Assigned owner (read-only)
  readonly slaLabel: string; // SLA state (read-only badge)
  readonly slaTone: SlaTone;
  readonly recentActivity: string; // Recent activity (read-only)
}

/** A tab in the Context-and-filters strip (4.1.1) with its scope-qualified total. */
interface PriorityTab {
  readonly key: 'all' | WorkCategory;
  readonly label: string;
  readonly count: number;
}

/** A recently touched business record (Main work → "recent records"). */
interface RecentRecord {
  readonly id: string;
  readonly title: string;
  readonly subtitle: string;
  readonly timestamp: string;
  readonly icon: string;
  readonly tone: 'meadow' | 'gold' | 'info' | 'olive' | 'sage';
}

/** A scope-qualified total shown in the "At a glance" summary. */
interface GlanceMetric {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  readonly icon: string;
  readonly tone: 'good' | 'gold' | 'info' | 'error';
}

@Component({
  selector: 'app-work-space',
  imports: [CommonModule],
  templateUrl: './work-space.html',
  styleUrl: './work-space.css',
})
export class WorkSpaceComponent {
  // ---- Task header (4.1.1): title, owner, freshness ----
  protected readonly pageTitle = 'My Workspace';
  /** Accountable owner (task-header required element, 4.1.1). */
  protected readonly owner = 'Sophie Bennett';

  /** Current surface state (4.1.4). Toggle to demonstrate each required state. */
  protected readonly uiState = signal<UiState>('ready');

  // ---- Application-shell primary action + row/workflow actions (4.1.3) ----
  /** ux.my-workspace.view gates discovery and open. */
  protected readonly canView = signal(true);
  /** ux.my-workspace.delegate-where-permitted gates the row overflow action. */
  protected readonly canDelegate = signal(true);

  // ---------- Context and filters (4.1.1): scope-qualified totals ----------
  protected readonly tabs: PriorityTab[] = [
    { key: 'all', label: 'All', count: 12 },
    { key: 'tasks', label: 'Tasks', count: 12 },
    { key: 'approvals', label: 'Approvals', count: 5 },
    { key: 'followups', label: 'Follow Ups', count: 7 },
    { key: 'exceptions', label: 'Exceptions', count: 3 },
  ];

  /** Currently selected filter tab (Filter action, 4.1.3). */
  protected readonly activeTab = signal<PriorityTab['key']>('all');

  /** Freshness marker (Task header + Context "last refresh"). */
  protected readonly lastRefresh = signal('just now');

  // ---------- Main work: prioritised queue (server-scoped) ----------
  protected readonly workItems: WorkItem[] = [
    {
      id: 'wi-001',
      reference: 'BLIND-STICK-2026-0178',
      title: 'Beneficiary Onboarding',
      businessRecord: 'Beneficiary 2026-0178',
      module: 'Beneficiary',
      category: 'approvals',
      typeLabel: 'Approval',
      icon: 'ri-file-text-line',
      iconTone: 'ink',
      dueLabel: 'Today, 11:30 AM',
      dueState: 'due-today',
      priority: 'high',
      status: 'pending',
      owner: 'Sophie Bennett',
      slaLabel: 'Due in 2h',
      slaTone: 'warn',
      recentActivity: 'Submitted for approval · 20 min ago',
    },
    {
      id: 'wi-002',
      reference: 'DONATION-2026-0921',
      title: 'Corporate Donation',
      businessRecord: 'Donation 2026-0921',
      module: 'Fundraising',
      category: 'followups',
      typeLabel: 'Review',
      icon: 'ri-checkbox-circle-line',
      iconTone: 'error',
      dueLabel: 'Today, 02:00 PM',
      dueState: 'due-today',
      priority: 'high',
      status: 'in-progress',
      owner: 'Sophie Bennett',
      slaLabel: 'Due in 4h',
      slaTone: 'warn',
      recentActivity: 'Review started · 1h ago',
    },
    {
      id: 'wi-003',
      reference: 'FUNDRAISE-2026-0456',
      title: 'Campaign Verification',
      businessRecord: 'Campaign 2026-0456',
      module: 'Fundraising',
      category: 'tasks',
      typeLabel: 'Task',
      icon: 'ri-file-list-3-line',
      iconTone: 'good',
      dueLabel: 'Tomorrow, 10:00 AM',
      dueState: 'upcoming',
      priority: 'medium',
      status: 'in-progress',
      owner: 'Sophie Bennett',
      slaLabel: 'On track',
      slaTone: 'ok',
      recentActivity: 'Assigned · Yesterday',
    },
    {
      id: 'wi-004',
      reference: 'BLIND-STICK-2026-0176',
      title: 'Stock Allocation',
      businessRecord: 'Stock 2026-0176',
      module: 'Inventory',
      category: 'followups',
      typeLabel: 'Follow Up',
      icon: 'ri-checkbox-line',
      iconTone: 'info',
      dueLabel: '23 Jul 2026',
      dueState: 'upcoming',
      priority: 'medium',
      status: 'pending',
      owner: 'Sophie Bennett',
      slaLabel: 'On track',
      slaTone: 'ok',
      recentActivity: 'Follow-up raised · 2 days ago',
    },
    {
      id: 'wi-005',
      reference: 'EXPENSE-2026-0887',
      title: 'Field Visit Expense',
      businessRecord: 'Expense 2026-0887',
      module: 'Expenses',
      category: 'approvals',
      typeLabel: 'Approval',
      icon: 'ri-bill-line',
      iconTone: 'gold',
      dueLabel: '24 Jul 2026',
      dueState: 'upcoming',
      priority: 'low',
      status: 'pending',
      owner: 'Sophie Bennett',
      slaLabel: 'On track',
      slaTone: 'ok',
      recentActivity: 'Submitted · 3 days ago',
    },
  ];

  /** Work items visible for the currently selected filter tab. */
  protected readonly filteredItems = computed(() => {
    const tab = this.activeTab();
    if (tab === 'all') {
      return this.workItems;
    }
    return this.workItems.filter((item) => item.category === tab);
  });

  // ---------- At a glance (Related & totals): scope-qualified counts ----------
  protected readonly glanceMetrics: GlanceMetric[] = [
    { key: 'tasks', label: 'Tasks Assigned', value: 12, icon: 'ri-time-line', tone: 'good' },
    { key: 'approvals', label: 'Approvals Pending', value: 5, icon: 'ri-checkbox-circle-line', tone: 'gold' },
    { key: 'followups', label: 'Follow Ups', value: 7, icon: 'ri-check-double-line', tone: 'info' },
    { key: 'exceptions', label: 'Exceptions', value: 3, icon: 'ri-error-warning-line', tone: 'error' },
  ];

  // ---------- Recent records ----------
  protected readonly recentRecords: RecentRecord[] = [
    { id: 'rr-001', title: 'DONATION-2026-0920', subtitle: 'Individual Donation', timestamp: 'Today, 09:15 AM', icon: 'ri-archive-2-line', tone: 'meadow' },
    { id: 'rr-002', title: 'BENEFICIARY-2026-0632', subtitle: 'John Paul', timestamp: 'Today, 08:42 AM', icon: 'ri-group-line', tone: 'gold' },
    { id: 'rr-003', title: 'EXPENSE-2026-0886', subtitle: 'Travel Expense', timestamp: 'Yesterday, 05:30 PM', icon: 'ri-file-text-line', tone: 'info' },
    { id: 'rr-004', title: 'CAMPAIGN-2026-0341', subtitle: 'Education Drive', timestamp: 'Yesterday, 04:10 PM', icon: 'ri-calendar-event-line', tone: 'olive' },
    { id: 'rr-005', title: 'STOCK-2026-0267', subtitle: 'Blind Stick Stock', timestamp: '19 Jul 2026', icon: 'ri-grid-line', tone: 'sage' },
  ];

  // ---------- Actions (4.1.3) ----------

  /** Filter action — refreshes only the authorised, in-scope queue. */
  protected selectTab(key: PriorityTab['key']): void {
    this.activeTab.set(key);
  }

  /** Open work (primary, page/row) — opens the authorised record in scope. */
  protected openWork(item: WorkItem): void {
    console.info('Open work', item.reference);
  }

  /** Delegate where permitted — row overflow, gated by delegate permission. */
  protected onOverflowClick(item: WorkItem): void {
    if (!this.canDelegate()) {
      return;
    }
    console.info('Delegate where permitted', item.reference);
  }
}
