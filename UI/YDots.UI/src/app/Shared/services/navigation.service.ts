import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { AuthApiService } from '../../Service/auth-api.service';
import { MenuNode, NavigationResponse } from '../models/auth.model';

/**
 * The sidebar, as the server decides it.
 *
 * WHY THE MENU IS NOT WRITTEN IN THE TEMPLATE
 * -------------------------------------------
 * What a person may see depends on four things, and only the server knows all four: what the
 * product HAS, what this Organisation has TURNED ON, what their role was GIVEN, and what
 * permissions they actually HOLD. A menu written in HTML knows none of them, so it shows every
 * link to everybody and relies on each screen to be empty for the people who should not be there
 * — which is a worse experience and a much wider surface to get wrong.
 *
 * THE TREE IS NOT A SECURITY BOUNDARY, and it is worth being explicit about that. Hiding a link
 * is a courtesy. Every endpoint behind it re-checks its own permission on every request, so a
 * hand-typed URL reaches a screen that cannot load anything. If the menu were the control,
 * editing this object in dev tools would be an attack; because it is not, it is a way of making
 * your own sidebar untidy.
 *
 * IT IS RELOADED AFTER AN ORGANISATION SWITCH, because the answer genuinely differs between
 * Organisations: the same root user sees different navigation inside TEN001 and TEN002.
 */
@Injectable({ providedIn: 'root' })
export class NavigationService {
  private readonly authApi = inject(AuthApiService);

  private readonly navigationState = signal<NavigationResponse | null>(null);
  readonly navigation = this.navigationState.asReadonly();

  private readonly loadingState = signal(false);
  readonly loading = this.loadingState.asReadonly();

  private readonly failedState = signal(false);
  readonly failed = this.failedState.asReadonly();

  readonly menu = computed<MenuNode[]>(() => this.navigationState()?.menu ?? []);
  readonly landingRoute = computed(() => this.navigationState()?.landingRoute ?? '/app/dashboard');
  readonly organisationName = computed(() => this.navigationState()?.tenantName ?? '');
  readonly isTenantMode = computed(() => this.navigationState()?.isTenantMode === true);

  /**
   * Loads the tree.
   *
   * Called once when the shell mounts, and again after every Organisation switch. Failure is not
   * fatal: an empty sidebar is a poor experience, not a broken app, and the routes still work.
   */
  load(): Observable<NavigationResponse> {
    this.loadingState.set(true);
    this.failedState.set(false);

    return this.authApi.getNavigation().pipe(
      tap({
        next: (navigation) => {
          this.navigationState.set(navigation);
          this.loadingState.set(false);
        },
        error: () => {
          this.loadingState.set(false);
          this.failedState.set(true);
        },
      }),
    );
  }

  /** Forgets the tree. Called on sign-out, so the next person never sees the last one's menu. */
  clear(): void {
    this.navigationState.set(null);
    this.failedState.set(false);
  }

  /**
   * Turns the server's icon name into the class this theme uses.
   *
   * The API names icons in a neutral vocabulary — "users", "shield", "building" — rather than in
   * one icon set's class names. That is deliberate: the icon set is a choice this client makes,
   * and a server that emitted `ri-user-line` would have made it on the client's behalf and
   * would need changing the day the theme did.
   */
  iconClass(icon: string | null | undefined): string {
    return NavigationService.ICONS[icon ?? ''] ?? 'ri-circle-line';
  }

  /**
   * A stable DOM id for a collapsible group.
   *
   * Bootstrap's collapse plugin targets by id, so every group needs one that is unique and does
   * not change between renders — the menu code is exactly that, and using the array index would
   * break the moment the tree is filtered differently for somebody else.
   */
  collapseId(node: MenuNode): string {
    return 'menu-' + (node.code ?? node.id ?? '').toLowerCase().replace(/[^a-z0-9]+/g, '-');
  }

  private static readonly ICONS: Record<string, string> = {
    bell: 'ri-notification-3-line',
    book: 'ri-book-2-line',
    briefcase: 'ri-briefcase-line',
    building: 'ri-building-4-line',
    calendar: 'ri-calendar-line',
    'check-circle': 'ri-checkbox-circle-line',
    'check-square': 'ri-checkbox-line',
    clipboard: 'ri-clipboard-line',
    clock: 'ri-time-line',
    'corner-up-left': 'ri-corner-up-left-line',
    'credit-card': 'ri-bank-card-line',
    database: 'ri-database-2-line',
    'dollar-sign': 'ri-money-dollar-circle-line',
    edit: 'ri-edit-line',
    eye: 'ri-eye-line',
    file: 'ri-file-line',
    'file-text': 'ri-file-text-line',
    flag: 'ri-flag-line',
    'git-branch': 'ri-git-branch-line',
    globe: 'ri-global-line',
    grid: 'ri-dashboard-line',
    heart: 'ri-heart-line',
    home: 'ri-home-4-line',
    inbox: 'ri-inbox-line',
    info: 'ri-information-line',
    key: 'ri-key-2-line',
    layers: 'ri-stack-line',
    layout: 'ri-layout-line',
    list: 'ri-list-check',
    lock: 'ri-lock-line',
    map: 'ri-map-2-line',
    'map-pin': 'ri-map-pin-line',
    'message-circle': 'ri-message-3-line',
    package: 'ri-box-3-line',
    'plus-circle': 'ri-add-circle-line',
    'refresh-cw': 'ri-refresh-line',
    search: 'ri-search-line',
    send: 'ri-send-plane-line',
    settings: 'ri-settings-3-line',
    'share-2': 'ri-share-line',
    shield: 'ri-shield-check-line',
    shuffle: 'ri-shuffle-line',
    sliders: 'ri-equalizer-line',
    tag: 'ri-price-tag-3-line',
    'trending-up': 'ri-line-chart-line',
    truck: 'ri-truck-line',
    user: 'ri-user-line',
    'user-plus': 'ri-user-add-line',
    users: 'ri-group-line',
  };
}
