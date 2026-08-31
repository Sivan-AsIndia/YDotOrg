import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { AuthApiService } from '../../Service/auth-api.service';
import {
  SelectTenantResponse,
  TenantContextResponse,
  TenantOptionResponse,
} from '../models/auth.model';
import { AuthTokenService } from './auth-token.service';

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
 */
@Injectable({ providedIn: 'root' })
export class OrganisationContextService {
  private readonly authApi = inject(AuthApiService);
  private readonly tokens = inject(AuthTokenService);

  /** The Organisations a global caller may step into. Empty for everybody else. */
  private readonly selectableState = signal<TenantOptionResponse[]>([]);
  readonly selectable = this.selectableState.asReadonly();

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
   * onwards. Callers normally reload the navigation afterwards, because what a person may see
   * differs between Organisations.
   */
  select(tenantId: string): Observable<SelectTenantResponse> {
    return this.authApi.selectOrganisation(tenantId);
  }

  /**
   * Steps back out to platform scope.
   *
   * Offered only while actually inside an Organisation — see `isActingInOrganisation` — because
   * there is nothing to leave otherwise.
   */
  exitToPlatform(): Observable<SelectTenantResponse> {
    return this.authApi.exitOrganisation();
  }

  /** Whether an Organisation can actually be worked in, as opposed to merely reviewed. */
  isOperable(option: TenantOptionResponse): boolean {
    return option.isOperable === true;
  }
}
