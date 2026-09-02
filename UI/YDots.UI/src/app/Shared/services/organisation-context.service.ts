import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';
import { AuthApiService } from '../../Service/auth-api.service';
import {
  SelectTenantResponse,
  TenantContextResponse,
  TenantOptionResponse,
} from '../models/auth.model';
import { AuthTokenService } from './auth-token.service';
import { NavigationService } from './navigation.service';
import { OrganisationScopeService } from './organisation-scope.service';

/**
 * Which Organisation the session is working inside, and how to change it.
 *
 * WHAT THIS IS NOT
 * ----------------
 * It is not the security boundary. The Organisation that governs a request is the one inside the
 * signed access token, which the browser cannot alter without invalidating the signature. What is
 * held here is a copy, kept so the shell can render a name and a switcher without a round trip.
 * Editing it in dev tools changes a label and nothing else — the very next API call still runs
 * against whatever the token says.
 *
 * WHY SWITCHING IS A SERVER CALL AND NOT A SETTER
 * -----------------------------------------------
 * `select()` asks the server to re-issue the access token against a different Organisation. The
 * server checks the caller is genuinely global scope before honouring it, and answers with a new
 * signed token. There is deliberately no way to change the operating Organisation from this side
 * alone: a client-side switch would be a label change over data the API would still refuse.
 *
 * AND SELECTING DOES NOT CHANGE WHO YOU ARE. A root user has no Organisation of their own and
 * never acquires one by looking at somebody's data — their user record is untouched by every call
 * below. Selecting sets the operating context for the session, which is a different thing from
 * ownership and is worth keeping separate.
 *
 * SWITCHING IS NOT DONE UNTIL THE NEW MENU IS IN HAND
 * ---------------------------------------------------
 * `select()` and `exitToPlatform()` do not complete on the new token. They complete once the
 * navigation for the Organisation they moved to has been fetched, because every caller's next act
 * is to send somebody to `landingRoute()` — a route that belongs to the tree they are waiting for.
 * Leaving that to the caller is what produced the bug this addresses: three call sites, two that
 * remembered to reload the menu and one that did not.
 */
@Injectable({ providedIn: 'root' })
export class OrganisationContextService {
  private readonly authApi = inject(AuthApiService);
  private readonly tokens = inject(AuthTokenService);
  private readonly navigation = inject(NavigationService);
  private readonly organisationScope = inject(OrganisationScopeService);

  /** The Organisations a global caller may step into. Empty for everybody else. */
  private readonly selectableState = signal<TenantOptionResponse[]>([]);
  readonly selectable = this.selectableState.asReadonly();

  constructor() {
    // THE SWITCHER LIST IS DROPPED ON EVERY SCOPE CHANGE, and it has to be, because the only
    // thing that refills it is the switcher finding it empty - see `onSwitcherOpened`. Held
    // across a switch it went stale in three ways that all show on the screen: an Organisation
    // approved since it was fetched still read "review only", one created since was missing
    // altogether, and the whole platform's list of Organisations sat in memory after sign-out,
    // waiting for whoever signed in next in the same tab.
    //
    // Dropping rather than re-fetching: for nearly everybody the answer is "you cannot switch",
    // and the list is wanted only when somebody actually opens the switcher.
    this.organisationScope.onOrganisationChange(() => this.selectableState.set([]));
  }

  private readonly loadingState = signal(false);
  readonly loading = this.loadingState.asReadonly();

  /** The Organisation in force, as the server last reported it. */
  readonly current = computed<TenantContextResponse | null>(() => this.tokens.tenant());

  readonly currentId = computed(() => this.current()?.tenantId ?? null);
  readonly currentName = computed(() => this.current()?.tenantName ?? '');
  readonly currentCode = computed(() => this.current()?.tenantCode ?? '');

  /**
   * True when a global caller has stepped into an Organisation.
   *
   * The shell shows the "acting as" banner off this, so a root user is never in any doubt whose
   * data is on the screen. That is the difference between a deliberate administrative action and
   * an accident.
   */
  readonly isActingInOrganisation = computed(() => this.current()?.isTenantMode === true);

  /** True when the caller may switch at all — only a global-scope caller can. */
  readonly canSwitch = computed(() => this.tokens.isSuperAdmin());

  /**
   * Loads the Organisations this caller may enter.
   *
   * Called by the switcher when it opens rather than on every page, because for the great
   * majority of people the answer is "none" and asking would be a wasted request on every load.
   */
  loadSelectable(): Observable<TenantOptionResponse[]> {
    this.loadingState.set(true);

    return this.authApi.getSelectableOrganisations().pipe(
      tap({
        next: (options) => {
          this.selectableState.set(options);
          this.loadingState.set(false);
        },
        error: () => this.loadingState.set(false),
      }),
    );
  }

  /**
   * Steps into an Organisation.
   *
   * The new token is stored by the auth service, so everything downstream — the interceptor, the
   * navigation call, every screen — is operating in the new Organisation from the next request
   * onwards. The new navigation is then fetched before this completes, so a caller that lands on
   * `landingRoute()` is landing on the route the NEW Organisation named.
   */
  select(tenantId: string): Observable<SelectTenantResponse> {
    return this.withNavigation(this.authApi.selectOrganisation(tenantId));
  }

  /**
   * Steps back out to platform scope.
   *
   * Offered only while actually inside an Organisation — see `isActingInOrganisation` — because
   * there is nothing to leave otherwise.
   */
  exitToPlatform(): Observable<SelectTenantResponse> {
    return this.withNavigation(this.authApi.exitOrganisation());
  }

  /**
   * Chains the navigation reload onto a switch, and hands the switch's own answer back.
   *
   * THE MENU FAILING DOES NOT FAIL THE SWITCH. The token has already been re-issued by the time
   * this runs; reporting an error would tell the caller the switch did not happen when it plainly
   * did, and send them down an error path that leaves the session in the new Organisation with a
   * screen insisting it is in the old one. A sidebar that could not be fetched is a sidebar the
   * person can retry.
   */
  private withNavigation(
    switching: Observable<SelectTenantResponse>): Observable<SelectTenantResponse> {
    return switching.pipe(
      switchMap((result) =>
        this.navigation.load().pipe(
          catchError(() => of(null)),
          map(() => result))),
    );
  }

  /** Whether an Organisation can actually be worked in, as opposed to merely reviewed. */
  isOperable(option: TenantOptionResponse): boolean {
    return option.isOperable === true;
  }
}
