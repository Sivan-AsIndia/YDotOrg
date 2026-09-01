import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { AuthApiService } from '../../Service/auth-api.service';
import { MenuNode, NavigationResponse } from '../models/auth.model';
import { OrganisationScopeService } from './organisation-scope.service';

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
 *
 * AND IT RELOADS ITSELF, which is the part that was wrong. The tree used to be a plain cache that
 * whoever switched Organisation was expected to refresh by hand, so it was only ever as correct as
 * the least careful call site — and the Organisation directory, one of the three places that can
 * switch, never refreshed it at all. Stepping into TEN001 from there left the platform menu up;
 * walking back to Manage Organisations afterwards left TEN001's menu up. The tree is now KEYED to
 * the Organisation it was built for and reloads whenever that changes, whoever changed it.
 */
@Injectable({ providedIn: 'root' })
export class NavigationService {
  private readonly authApi = inject(AuthApiService);
  private readonly organisationScope = inject(OrganisationScopeService);

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
   * The Organisation the tree in hand was requested for.
   *
   * Stamped when the request STARTS rather than when it lands, and that ordering is the whole
   * point of it. A switch calls `load()` directly so the caller can wait for the new menu before
   * navigating; the change notification below then arrives a tick later and finds the scope
   * already claimed, so it stands down instead of firing a second identical request.
   */
  private loadedForScope: string | null = null;

  constructor() {
    // The first tree, for whatever scope we are already in. Skipped when nobody is signed in yet:
    // the sign-in screen has no sidebar, and the notification below covers the moment they are.
    this.reloadForCurrentScope();

    this.organisationScope.onOrganisationChange((scope) => {
      if (scope === null) {
        // Signed out. Forget the tree so the next person never sees the last one's menu.
        this.clear();
        this.loadedForScope = null;
        return;
      }

      if (scope === this.loadedForScope) {
        // An explicit switch has already started this fetch. See `loadedForScope`.
        return;
      }

      // The previous Organisation's menu goes NOW, not when the replacement lands. A spinner for
      // half a second is honest; another Organisation's navigation is not.
      this.clear();
      this.reloadForCurrentScope();
    });
  }

  /**
   * Loads the tree.
   *
   * Called once when this service is first injected, and again after every Organisation change.
   * Failure is not fatal: an empty sidebar is a poor experience, not a broken app, and the routes
   * still work.
   */
  load(): Observable<NavigationResponse> {
    // Captured, not re-read on arrival: see the staleness check below.
    const requestedForScope = this.organisationScope.scope();

    this.loadedForScope = requestedForScope;
    this.loadingState.set(true);
    this.failedState.set(false);

    return this.authApi.getNavigation().pipe(
      tap({
        next: (navigation) => {
          if (this.isStale(requestedForScope)) {
            return;
          }

          this.navigationState.set(navigation);
          this.loadingState.set(false);
        },
        error: () => {
          if (this.isStale(requestedForScope)) {
            return;
          }

          this.loadingState.set(false);
          this.failedState.set(true);
        },
      }),
    );
  }

  /**
   * Whether a response has been overtaken by a later Organisation.
   *
   * TWO REQUESTS CAN BE IN THE AIR AT ONCE and they can land in either order. Refreshing the page
   * while standing in TEN001 on a platform route is the ordinary way to produce it: this service
   * asks for TEN001's tree, the route's guard then steps out to platform level and asks for that
   * one, and if the first reply is the slower of the two it would otherwise be applied last and
   * win. Which Organisation a reply belongs to is not in the reply, so it is remembered here.
   */
  private isStale(requestedForScope: string | null): boolean {
    return this.loadedForScope !== requestedForScope;
  }

  /** Forgets the tree. Called on sign-out, so the next person never sees the last one's menu. */
  clear(): void {
    this.navigationState.set(null);
    this.failedState.set(false);
  }

  /**
   * Fetches the tree for the scope in force, unless that fetch is already under way.
   *
   * Silent about failure on purpose: the caller here is a lifecycle notification with nobody to
   * report to. The `failed` signal is what the sidebar renders, and `retry()` is what a person
   * presses.
   */
  private reloadForCurrentScope(): void {
    const scope = this.organisationScope.scope();

    if (scope === null || scope === this.loadedForScope) {
      return;
    }

    this.load().subscribe({ error: () => undefined });
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
