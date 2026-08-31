import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

/**
 * SCR-UX-006 — Notification centre.
 *
 * Faithful implementation of section 4.6 of the YDot Practical UI/UX Generation
 * Specification. Every region (4.6.1), field (4.6.2), action (4.6.3), UI state
 * (4.6.4), responsive/accessibility rule (4.6.5) and validation/confirmation
 * pattern (4.6.6) below maps directly to the controlled contract. No content or
 * behaviour outside that contract is added.
 *
 *  Route            : /workspace/notification-centre
 *  Purpose          : Present informational alerts separately from actionable tasks.
 *  Primary users    : All users
 *  View permission  : ux.notification-centre.view
 *  Primary action   : Read
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.6.4, plus the settled "ready" surface. */
type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Notification type — the effective approved catalogue (4.6.2 Notification type). */
type NotificationType = 'Action Required' | 'Update' | 'Mention' | 'System';

/** Read state values used by the Read-state filter (4.6.2 Read state). */
type ReadFilter = 'All' | 'Unread' | 'Read';

/** Effective permission set for the acting person (4.6.3). */
interface EffectivePermissions {
  readonly view: boolean; // ux.notification-centre.view
  readonly read: boolean; // ux.notification-centre.read
  readonly managePreference: boolean; // ux.notification-centre.manage-preference
}

/**
 * A single notification. Title, Summary, Source record, Created time, Actionable
 * task reference and Channel preference are all read-only, server-derived and
 * immutable in this view (4.6.2).
 */
interface NotificationRecord {
  readonly reference: string; // stable reference
  readonly type: NotificationType; // Notification type (catalogue)
  readonly title: string; // Notification title (read-only)
  readonly summary: string; // Summary (read-only)
  readonly sourceRecord: string; // Source record (read-only, stable value)
  readonly created: string; // Created time (read-only, ISO)
  readonly createdLabel: string; // Created time display
  readonly actionableTaskRef: string; // Actionable task reference (read-only)
  readonly channelPreference: string; // Channel preference (read-only)
  readonly ownerReference: string; // owner reference for effective-scope filtering
  readonly icon: string; // display glyph (visual only)
  readonly openLabel: string; // Open-source control label (Review / View / Open)
  read: boolean; // Read state (the only mutable value, via the Read action)
}

@Component({
  selector: 'app-notification-centre',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './notification-centre.html',
  styleUrl: './notification-centre.css',
})
export class NotificationCentreComponent {
  // ================= Task header (4.6.1) =================
  protected readonly pageTitle = 'Notification centre';
  protected readonly pageSubtitle = 'Stay updated with important alerts, actions and updates across YDot.';
  protected readonly stableReference = 'NOTIF-CENTRE-2026-0001';
  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'Sophie Bennett · Programme Manager';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Active effective scope — shown in the shell and qualifies every total (4.6.1). */
  protected readonly activeScope = 'YDot Foundation · Maharashtra';

  /** Last refresh time — server-derived, read-only freshness evidence (4.6.1). */
  protected readonly lastRefresh = signal('Today, 09:32 AM · IST');

  /** Effective permissions decided server-side; the client mirrors the same decision (4.6.3, 4.6.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    read: true,
    managePreference: true,
  };

  /** Lifecycle states in which workflow actions (Read / Manage preference) are permitted (4.6.3). */
  private readonly workflowPermittedStates = ['Active'];
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState);

  // ================= Notification type catalogue (4.6.2) =================
  /** Only current, effective, approved catalogue values are used (4.6.2). */
  protected readonly typeCatalogue: readonly NotificationType[] = ['Action Required', 'Update', 'Mention', 'System'];

  // ================= Main work: informational alerts (4.6.1 + 4.6.2) =================
  /**
   * The notification set inside the actor's effective data scope (4.6 Data scope).
   * All display fields are read-only and server-derived; only Read state changes,
   * and only through the Read action.
   */
  protected readonly notifications = signal<NotificationRecord[]>([
    {
      reference: 'NOTIF-2026-0921',
      type: 'Action Required',
      title: 'Approval request pending',
      summary: 'Rahul Sharma has requested your approval for DONATION-2026-0921.',
      sourceRecord: 'DONATION-2026-0921',
      created: '2026-07-18T09:32',
      createdLabel: 'Today · 09:32 AM',
      actionableTaskRef: 'TASK-2026-4471',
      channelPreference: 'In-app, Email',
      ownerReference: 'USR-0114',
      icon: 'ri-checkbox-circle-line',
      openLabel: 'Review',
      read: false,
    },
    {
      reference: 'NOTIF-2026-0919',
      type: 'Action Required',
      title: 'New beneficiary verification',
      summary: '12 new beneficiary verifications are awaiting review.',
      sourceRecord: 'BENEFICIARY-QUEUE-2026',
      created: '2026-07-18T08:45',
      createdLabel: 'Today · 08:45 AM',
      actionableTaskRef: 'TASK-2026-4468',
      channelPreference: 'In-app',
      ownerReference: 'USR-0114',
      icon: 'ri-team-line',
      openLabel: 'Review',
      read: false,
    },
    {
      reference: 'NOTIF-2026-0705',
      type: 'Update',
      title: 'Donation received',
      summary: '₹75,600 received for DONATION-2026-0705 from John Paul.',
      sourceRecord: 'DONATION-2026-0705',
      created: '2026-07-18T07:15',
      createdLabel: 'Today · 07:15 AM',
      actionableTaskRef: '—',
      channelPreference: 'In-app, Email',
      ownerReference: 'USR-0114',
      icon: 'ri-file-list-3-line',
      openLabel: 'Open',
      read: false,
    },
    {
      reference: 'NOTIF-2026-0688',
      type: 'Update',
      title: 'Campaign milestone achieved',
      summary: 'Blind Stick Distribution Drive Jul 2026 has reached 60% of its target.',
      sourceRecord: 'CAMP-2026-0178',
      created: '2026-07-17T18:20',
      createdLabel: 'Yesterday · 06:20 PM',
      actionableTaskRef: '—',
      channelPreference: 'In-app',
      ownerReference: 'USR-0142',
      icon: 'ri-megaphone-line',
      openLabel: 'Open',
      read: false,
    },
    {
      reference: 'NOTIF-2026-0456',
      type: 'System',
      title: 'Task completed',
      summary: 'Verify documents for donation drive DONATION-DRIVE-2026-0456.',
      sourceRecord: 'TASK-2026-0456',
      created: '2026-07-17T16:10',
      createdLabel: 'Yesterday · 04:10 PM',
      actionableTaskRef: 'TASK-2026-0456',
      channelPreference: 'In-app',
      ownerReference: 'USR-0114',
      icon: 'ri-checkbox-line',
      openLabel: 'Open',
      read: true,
    },
    {
      reference: 'NOTIF-2026-0642',
      type: 'Action Required',
      title: 'High priority exception',
      summary: '3 high priority exceptions require your attention.',
      sourceRecord: 'EXCEPTION-QUEUE-2026',
      created: '2026-07-17T11:08',
      createdLabel: 'Yesterday · 11:08 AM',
      actionableTaskRef: 'TASK-2026-4402',
      channelPreference: 'In-app, Email',
      ownerReference: 'USR-0114',
      icon: 'ri-error-warning-line',
      openLabel: 'View',
      read: false,
    },
    {
      reference: 'NOTIF-2026-0631',
      type: 'Mention',
      title: 'You were mentioned',
      summary: 'Arun Verma mentioned you on RECON-2026-0442: "please confirm the July batch".',
      sourceRecord: 'RECON-2026-0442',
      created: '2026-07-17T10:24',
      createdLabel: 'Yesterday · 10:24 AM',
      actionableTaskRef: '—',
      channelPreference: 'In-app, Email',
      ownerReference: 'USR-0142',
      icon: 'ri-at-line',
      openLabel: 'Open',
      read: false,
    },
    {
      reference: 'NOTIF-2026-0610',
      type: 'System',
      title: 'System maintenance scheduled',
      summary: 'Planned maintenance on 25 Jul 2026 from 11:00 PM to 02:00 AM.',
      sourceRecord: 'SYS-MAINT-2026-0025',
      created: '2026-07-17T09:30',
      createdLabel: 'Yesterday · 09:30 AM',
      actionableTaskRef: '—',
      channelPreference: 'In-app',
      ownerReference: 'USR-0114',
      icon: 'ri-notification-3-line',
      openLabel: 'Open',
      read: true,
    },
    {
      reference: 'NOTIF-2026-0918',
      type: 'Update',
      title: 'Acknowledgement letter generated',
      summary: 'Acknowledgement letter for DONATION-2026-0918 has been generated.',
      sourceRecord: 'DONATION-2026-0918',
      created: '2026-07-16T19:45',
      createdLabel: '16 Jul 2026 · 07:45 PM',
      actionableTaskRef: '—',
      channelPreference: 'In-app, Email',
      ownerReference: 'USR-0177',
      icon: 'ri-mail-check-line',
      openLabel: 'Open',
      read: true,
    },
  ]);

  // ================= Context and filters (4.6.1 + 4.6.2) =================

  /** Saved filter — the quick-filter bar over Read state and Notification type (4.6.1 saved filter). */
  protected readonly savedFilter = signal<'all' | ReadFilter | NotificationType>('all');

  /** Free-text search across the notification scope (4.6.1 Context and filters — search). */
  protected readonly searchTerm = signal('');

  /** Read state — search-select / radio decision using only current catalogue values (4.6.2 Read state). */
  protected readonly readStateOptions: readonly ReadFilter[] = ['All', 'Unread', 'Read'];
  protected readonly readStateFilter = signal<ReadFilter>('All');

  /** Notification type — searchable controlled choice; effective approved catalogue (4.6.2 Notification type). */
  protected readonly typeFilter = signal<Record<NotificationType, boolean>>({
    'Action Required': true,
    Update: true,
    Mention: true,
    System: true,
  });
  protected readonly typeQuery = signal('');
  protected readonly typeResults = computed(() => {
    const q = this.typeQuery().trim().toLowerCase();
    if (!q) {
      return this.typeCatalogue;
    }
    return this.typeCatalogue.filter((t) => t.toLowerCase().includes(q));
  });

  /** Date range — date picker with a time-zone label; operating time zone (4.6.2 Date range). */
  protected readonly rangeStart = signal('2026-07-12');
  protected readonly rangeEnd = signal('2026-07-18');

  /** Human-readable, interpreted date range shown before submit (4.6.2 Date range). */
  protected readonly interpretedRange = computed(
    () => `${this.formatDate(this.rangeStart())} – ${this.formatDate(this.rangeEnd())} · ${this.operatingTimeZone}`,
  );

  /** True when the range is impossible (end before start); blocks Filter submit (4.6.2, 4.6.6 invalid value). */
  protected readonly rangeInvalid = computed(() => new Date(this.rangeEnd()) < new Date(this.rangeStart()));

  /** Whether the filters drawer (secondary region) is open on tablet/mobile (4.6.5). */
  protected readonly filtersPanelOpen = signal(true);
  protected toggleFiltersPanel(): void {
    this.filtersPanelOpen.update((v) => !v);
  }

  /** Sort order for the informational list (4.6.1 Context and filters). */
  protected readonly sortNewestFirst = signal(true);
  protected toggleSort(): void {
    this.sortNewestFirst.update((v) => !v);
  }

  // ----- Quick-filter tabs: Read state + Notification type, with scoped totals (4.6.1) -----
  protected selectTab(value: 'all' | ReadFilter | NotificationType): void {
    this.savedFilter.set(value);
    if (value === 'all') {
      this.readStateFilter.set('All');
      this.typeFilter.set({ 'Action Required': true, Update: true, Mention: true, System: true });
    } else if (value === 'Unread' || value === 'Read') {
      this.readStateFilter.set(value);
      this.typeFilter.set({ 'Action Required': true, Update: true, Mention: true, System: true });
    } else {
      // A notification-type tab.
      this.readStateFilter.set('All');
      this.typeFilter.set({
        'Action Required': value === 'Action Required',
        Update: value === 'Update',
        Mention: value === 'Mention',
        System: value === 'System',
      });
    }
    this.uiState.set('ready');
  }

  protected toggleType(type: NotificationType): void {
    this.typeFilter.update((cur) => ({ ...cur, [type]: !cur[type] }));
    this.savedFilter.set('all');
  }

  /** Scoped totals qualified by the effective scope (4.6.1 totals; 4.6.4 Empty guidance). */
  protected readonly totalInScope = computed(() => this.notifications().length);
  protected readonly unreadCount = computed(() => this.notifications().filter((n) => !n.read).length);
  protected readonly typeCount = (type: NotificationType) =>
    this.notifications().filter((n) => n.type === type).length;

  /** Active-filter summary chips, qualified by scope (4.6.1 active-filter summary). */
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.readStateFilter() !== 'All') {
      chips.push({ key: 'read', label: `Read state: ${this.readStateFilter()}` });
    }
    const selectedTypes = this.typeCatalogue.filter((t) => this.typeFilter()[t]);
    if (selectedTypes.length !== this.typeCatalogue.length) {
      chips.push({ key: 'type', label: `Type: ${selectedTypes.join(', ') || 'None'}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: "${this.searchTerm().trim()}"` });
    }
    chips.push({ key: 'date', label: `Date: ${this.formatDate(this.rangeStart())} – ${this.formatDate(this.rangeEnd())}` });
    return chips;
  });

  protected removeFilterChip(key: string): void {
    if (key === 'read') {
      this.readStateFilter.set('All');
    } else if (key === 'type') {
      this.typeFilter.set({ 'Action Required': true, Update: true, Mention: true, System: true });
    } else if (key === 'search') {
      this.searchTerm.set('');
    } else if (key === 'date') {
      this.rangeStart.set('2026-07-12');
      this.rangeEnd.set('2026-07-18');
    }
    this.savedFilter.set('all');
  }

  /** The filtered, sorted notification set for the current scope and filters (server-side in production; 4.6.1). */
  protected readonly visibleNotifications = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const readState = this.readStateFilter();
    const types = this.typeFilter();
    const start = new Date(this.rangeStart());
    const end = new Date(this.rangeEnd());
    end.setHours(23, 59, 59, 999);

    const rows = this.notifications().filter((n) => {
      if (q && !(n.title.toLowerCase().includes(q) || n.summary.toLowerCase().includes(q) || n.reference.toLowerCase().includes(q) || n.sourceRecord.toLowerCase().includes(q))) {
        return false;
      }
      if (readState === 'Unread' && n.read) {
        return false;
      }
      if (readState === 'Read' && !n.read) {
        return false;
      }
      if (!types[n.type]) {
        return false;
      }
      const d = new Date(n.created);
      if (d < start || d > end) {
        return false;
      }
      return true;
    });

    const dir = this.sortNewestFirst() ? -1 : 1;
    return [...rows].sort((a, b) => (new Date(a.created).getTime() - new Date(b.created).getTime()) * dir);
  });

  protected readonly visibleCount = computed(() => this.visibleNotifications().length);

  // ================= Filter action (4.6.3) =================
  /** Filter — any authorised state; server-side filtering; confirmed by the refreshed list (4.6.3). */
  protected readonly filterAllowed = computed(() => this.permissions.view && this.uiState() !== 'no-access');
  protected applyFilter(): void {
    if (this.rangeInvalid()) {
      // Impossible range → validation state (4.6.4 / 4.6.6 invalid value).
      this.uiState.set('validation');
      return;
    }
    this.uiState.set(this.visibleNotifications().length === 0 ? 'empty' : 'ready');
  }
  /** Clearing a filter is explicit and returns focus predictably (4.6.1). */
  protected resetFilters(): void {
    this.searchTerm.set('');
    this.readStateFilter.set('All');
    this.typeFilter.set({ 'Action Required': true, Update: true, Mention: true, System: true });
    this.rangeStart.set('2026-07-12');
    this.rangeEnd.set('2026-07-18');
    this.savedFilter.set('all');
    this.uiState.set('ready');
  }

  // ================= Read action (4.6.3) — with review + explicit confirmation (4.6.6 high-risk) =================
  protected readonly readAllowed = computed(
    () => this.permissions.read && this.inWorkflowState() && this.uiState() !== 'no-access',
  );

  /** The pending Read decision — null when no review is open. A "bulk" target marks every unread record. */
  protected readonly readDialogOpen = signal(false);
  protected readonly pendingRead = signal<{ reference: string | 'bulk'; title: string; count: number } | null>(null);

  /** Request Read on one record; opens the Decision / review dialog (4.6.1 Decision / review). */
  protected requestRead(record: NotificationRecord): void {
    if (!this.readAllowed() || record.read) {
      return;
    }
    this.pendingRead.set({ reference: record.reference, title: record.title, count: 1 });
    this.readDialogOpen.set(true);
  }

  /** Request Read on every unread record in scope ("Mark all as read") — high-risk, requires confirmation. */
  protected requestReadAll(): void {
    const count = this.unreadCount();
    if (!this.readAllowed() || count === 0) {
      return;
    }
    this.pendingRead.set({ reference: 'bulk', title: 'All unread notifications', count });
    this.readDialogOpen.set(true);
  }

  protected cancelRead(): void {
    this.readDialogOpen.set(false);
    this.pendingRead.set(null);
  }

  /** Explicit confirmation before the committed Read change; refresh only authorised records (4.6.3, 4.6.6). */
  protected confirmRead(): void {
    const pending = this.pendingRead();
    if (!pending) {
      return;
    }
    this.notifications.update((list) =>
      list.map((n) => {
        if (pending.reference === 'bulk') {
          return n.read ? n : { ...n, read: true };
        }
        return n.reference === pending.reference ? { ...n, read: true } : n;
      }),
    );
    this.lastConfirmedResult.set({
      reference: pending.reference === 'bulk' ? this.stableReference : pending.reference,
      state: 'Read',
      time: this.lastRefresh(),
      pending: 'None',
      next: 'Review remaining action-required items',
    });
    this.readDialogOpen.set(false);
    this.pendingRead.set(null);
    this.uiState.set('success');
  }

  // ================= Open source action (4.6.3) =================
  protected readonly openSourceAllowed = computed(() => this.permissions.view && this.uiState() !== 'no-access');

  /** The last source record opened — held as persistent confirmation, never a toast alone (4.6.3, 4.6.4 Success). */
  protected readonly lastOpenedSource = signal<{ notification: string; source: string } | null>(null);

  protected openSource(record: NotificationRecord): void {
    if (!this.openSourceAllowed()) {
      return;
    }
    // Also marks the informational alert as read once its source is opened, then records a
    // persistent confirmation (production would route to the source record and re-check scope).
    this.notifications.update((list) =>
      list.map((n) => (n.reference === record.reference ? { ...n, read: true } : n)),
    );
    this.lastOpenedSource.set({ notification: record.reference, source: record.sourceRecord });
  }
  protected dismissOpenedSource(): void {
    this.lastOpenedSource.set(null);
  }

  // ================= Manage preference action (4.6.3) — Channel preference (4.6.2) =================
  protected readonly managePreferenceAllowed = computed(
    () => this.permissions.managePreference && this.inWorkflowState() && this.uiState() !== 'no-access',
  );

  protected readonly preferenceDialogOpen = signal(false);
  /** Channel preference toggles for the acting person (4.6.2 Channel preference). */
  protected readonly channelInApp = signal(true);
  protected readonly channelEmail = signal(true);
  protected readonly channelSms = signal(false);
  protected readonly preferenceReference = signal('');

  protected openPreference(): void {
    if (!this.managePreferenceAllowed()) {
      return;
    }
    this.preferenceDialogOpen.set(true);
  }
  protected cancelPreference(): void {
    this.preferenceDialogOpen.set(false);
  }
  /** At least one channel must remain enabled (4.6.6 required field / invalid value). */
  protected readonly preferenceValid = computed(
    () => this.channelInApp() || this.channelEmail() || this.channelSms(),
  );
  /** Confirm the preference change and show a persistent, stable confirmation (4.6.3, 4.6.4 Success). */
  protected confirmPreference(): void {
    if (!this.preferenceValid()) {
      return;
    }
    this.preferenceReference.set(`PREF-2026-${String(1000 + Math.floor(Math.random() * 9000))}`);
    this.lastConfirmedResult.set({
      reference: this.preferenceReference(),
      state: 'Preference saved',
      time: this.lastRefresh(),
      pending: 'None',
      next: 'Return to notification centre',
    });
    this.preferenceDialogOpen.set(false);
    this.uiState.set('success');
  }
  protected channelSummary(): string {
    const parts: string[] = [];
    if (this.channelInApp()) parts.push('In-app');
    if (this.channelEmail()) parts.push('Email');
    if (this.channelSms()) parts.push('SMS');
    return parts.join(', ') || 'None';
  }

  // ================= UI state demonstrability (4.6.4 / 4.6.7) =================
  protected readonly uiState = signal<UiState>('ready');
  protected readonly uiStates: readonly UiState[] = [
    'ready',
    'loading',
    'empty',
    'validation',
    'duplicate',
    'no-access',
    'conflict',
    'dependency-failure',
    'success',
  ];
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  // ================= Decision / review helper (4.6.1 region 5) =================
  /** Before-and-after values for the pending Read decision (4.6.1 Decision / review). */
  protected readonly pendingReadReview = computed(() => {
    const p = this.pendingRead();
    if (!p) {
      return null;
    }
    return {
      target: p.reference === 'bulk' ? `${p.count} unread notification(s)` : p.title,
      before: 'Unread',
      after: 'Read',
      permission: 'ux.notification-centre.read',
      evidence: p.reference === 'bulk' ? this.stableReference : p.reference,
      reason: 'Acknowledge informational alert(s)',
      resultingState: 'Read',
    };
  });

  // ================= Related and history (4.6.1 region 6) =================
  /** Each tab carries its own permission and scope enforcement (4.6.1). */
  protected readonly relatedTabs = [
    {
      key: 'linked',
      label: 'Linked records',
      permitted: true,
      rows: [
        { primary: 'DONATION-2026-0921', secondary: 'Approval requested', meta: 'Donation · Pending' },
        { primary: 'RECON-2026-0442', secondary: 'July settlement batch', meta: 'Reconciliation · Open' },
      ],
    },
    {
      key: 'documents',
      label: 'Documents',
      permitted: true,
      rows: [{ primary: 'Acknowledgement letter — 0918', secondary: 'PDF · 96 KB', meta: 'Generated 16 Jul 2026' }],
    },
    {
      key: 'activity',
      label: 'Activity',
      permitted: true,
      rows: [
        { primary: 'Notification read', secondary: 'NOTIF-2026-0456 marked read', meta: 'S. Bennett · 09:31 IST' },
        { primary: 'Preference updated', secondary: 'Email channel enabled', meta: 'S. Bennett · 09:20 IST' },
      ],
    },
    {
      key: 'integration',
      label: 'Integration status',
      permitted: true,
      rows: [{ primary: 'Notification delivery', secondary: 'Healthy', meta: 'Last sync 09:28 IST' }],
    },
    {
      key: 'support',
      label: 'Support correlation',
      permitted: true,
      rows: [{ primary: 'INT-77213', secondary: 'Email delivery retry', meta: 'Open · correlated' }],
    },
    {
      key: 'audit',
      label: 'Audit chronology',
      permitted: true,
      rows: [{ primary: 'Centre opened', secondary: 'ux.notification-centre.view granted', meta: 'S. Bennett · 09:30 IST' }],
    },
  ] as const;
  protected readonly visibleRelatedTabs = computed(() => this.relatedTabs.filter((t) => t.permitted));
  protected readonly activeRelatedTab = signal<string>('linked');
  protected readonly activeRelatedRows = computed(
    () => this.visibleRelatedTabs().find((t) => t.key === this.activeRelatedTab())?.rows ?? [],
  );
  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  // ================= Persistent outcome (4.6.1 region 7) =================
  /** Holds the most recent confirmed action result; survives navigation (4.6.4 Success). */
  protected readonly lastConfirmedResult = signal<{
    reference: string;
    state: string;
    time: string;
    pending: string;
    next: string;
  } | null>(null);

  protected readonly persistentOutcome = computed(() => {
    const last = this.lastConfirmedResult();
    return {
      reference: last?.reference ?? this.stableReference,
      state: last?.state ?? this.lifecycleState,
      effectiveTime: last?.time ?? this.lastRefresh(),
      downstreamStatus: last?.pending ?? (this.unreadCount() > 0 ? `${this.unreadCount()} unread in scope` : 'All read'),
      owner: this.owner,
      nextAction: last?.next ?? 'Review action-required notifications',
    };
  });

  // ================= Formatting helpers =================
  protected typeClass(type: NotificationType): string {
    switch (type) {
      case 'Action Required':
        return 'tag-action';
      case 'Update':
        return 'tag-update';
      case 'Mention':
        return 'tag-mention';
      case 'System':
        return 'tag-system';
    }
  }
  protected typeIconClass(type: NotificationType): string {
    switch (type) {
      case 'Action Required':
        return 'ic-action';
      case 'Update':
        return 'ic-update';
      case 'Mention':
        return 'ic-mention';
      case 'System':
        return 'ic-system';
    }
  }
  private formatDate(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
