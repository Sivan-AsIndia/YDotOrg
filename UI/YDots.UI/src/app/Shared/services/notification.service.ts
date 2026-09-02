import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignRole, CurrentUserService } from './current-user.service';

/** The lifecycle events that fan out as notifications. */
export type CampaignEventKind =
  | 'created'
  | 'submitted'
  | 'approved'
  | 'scheduled'
  | 'activated'
  | 'paused'
  | 'resumed'
  | 'closed'
  | 'cancelled';

/** A single in-app notification, addressed to a role and/or a specific user reference. */
export interface AppNotification {
  readonly id: string;
  readonly kind: CampaignEventKind | 'system';
  readonly title: string;
  readonly body: string;
  readonly time: string;
  readonly read: boolean;
  /** Delivered to everyone holding this role (e.g. every Super Admin). */
  readonly toRole?: CampaignRole;
  /** Delivered to this specific user reference (e.g. the campaign's owner). */
  readonly toRef?: string;
}

/** The minimal campaign shape the fan-out needs. */
export interface CampaignEventTarget {
  readonly code: string;
  readonly name: string;
  readonly ownerReference: string;
  readonly managerReference?: string;
}

/**
 * Single, shared source of truth for in-app notifications. Surfaced through the
 * top-header bell. Encodes the campaign notification routing rules:
 *  - every campaign notification is copied to the Super Admin;
 *  - activation / cancellation is sent to the campaign's owner;
 *  - activation / pause / resume / close is sent to the campaign's manager.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly user = inject(CurrentUserService);
  private counter = 0;

  private readonly items = signal<readonly AppNotification[]>([
    {
      id: 'seed-3',
      kind: 'submitted',
      title: 'Campaign submitted for approval',
      body: 'Clean Water Initiative (CAMP-2025-0012) is awaiting your approval.',
      time: 'Today, 09:20 AM',
      read: false,
      toRole: 'Super Admin',
    },
    {
      id: 'seed-2',
      kind: 'activated',
      title: 'Campaign activated',
      body: 'Educate a Child 2025 (CAMP-2025-0011) is now live.',
      time: 'Yesterday, 04:05 PM',
      read: false,
      toRole: 'Approver',
    },
    {
      id: 'seed-1',
      kind: 'approved',
      title: 'Campaign approved',
      body: 'Food for All (CAMP-2025-0018) was approved and scheduled.',
      time: 'Yesterday, 11:30 AM',
      read: true,
      toRole: 'Super Admin',
    },
  ]);

  /** Every notification (all recipients) — used only for diagnostics/testing. */
  readonly all = computed(() => this.items());

  /** Notifications addressed to the current session (by role or user reference). */
  readonly forCurrentUser = computed<readonly AppNotification[]>(() => {
    const role = this.user.role();
    const ref = this.user.reference();
    return this.items().filter((n) => (n.toRole && n.toRole === role) || (n.toRef && n.toRef === ref));
  });

  readonly unreadCount = computed(() => this.forCurrentUser().filter((n) => !n.read).length);

  private now(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private push(entry: Omit<AppNotification, 'id' | 'time' | 'read'>): void {
    this.counter += 1;
    const notification: AppNotification = {
      ...entry,
      id: `ntf-${Date.now()}-${this.counter}`,
      time: this.now(),
      read: false,
    };
    this.items.update((cur) => [notification, ...cur]);
  }

  /** Human-readable title/body per event. */
  private describe(event: CampaignEventKind, c: CampaignEventTarget): { title: string; body: string } {
    const ref = `${c.name} (${c.code})`;
    switch (event) {
      case 'created':
        return { title: 'Campaign created', body: `${ref} was created.` };
      case 'submitted':
        return { title: 'Campaign submitted for approval', body: `${ref} is awaiting approval.` };
      case 'approved':
        return { title: 'Campaign approved', body: `${ref} was approved.` };
      case 'scheduled':
        return { title: 'Campaign scheduled', body: `${ref} is now scheduled.` };
      case 'activated':
        return { title: 'Campaign activated', body: `${ref} is now live.` };
      case 'paused':
        return { title: 'Campaign paused', body: `${ref} was paused.` };
      case 'resumed':
        return { title: 'Campaign resumed', body: `${ref} was resumed.` };
      case 'closed':
        return { title: 'Campaign closed', body: `${ref} was closed.` };
      case 'cancelled':
        return { title: 'Campaign cancelled', body: `${ref} was cancelled.` };
    }
  }

  /**
   * Fan a campaign lifecycle event out to the right recipients per the routing
   * rules. Every event copies to the Super Admin; owner and manager are added
   * for the events that concern them.
   */
  emitCampaignEvent(campaign: CampaignEventTarget, event: CampaignEventKind): void {
    const { title, body } = this.describe(event, campaign);

    // Rule: every notification is copied to the Super Admin.
    this.push({ kind: event, title, body, toRole: 'Super Admin' });

    // Rule: activation / cancellation is sent to the campaign's owner.
    if (event === 'activated' || event === 'cancelled') {
      this.push({ kind: event, title, body, toRef: campaign.ownerReference });
    }

    // Rule: activation / pause / resume / close is sent to the campaign's manager.
    if (event === 'activated' || event === 'paused' || event === 'resumed' || event === 'closed') {
      if (campaign.managerReference) {
        this.push({ kind: event, title, body, toRef: campaign.managerReference });
      } else {
        this.push({ kind: event, title, body, toRole: 'Approver' });
      }
    }
  }

  markRead(id: string): void {
    this.items.update((cur) => cur.map((n) => (n.id === id ? { ...n, read: true } : n)));
  }

  markAllReadForCurrentUser(): void {
    const role = this.user.role();
    const ref = this.user.reference();
    this.items.update((cur) =>
      cur.map((n) => ((n.toRole === role || n.toRef === ref) ? { ...n, read: true } : n)),
    );
  }
}
