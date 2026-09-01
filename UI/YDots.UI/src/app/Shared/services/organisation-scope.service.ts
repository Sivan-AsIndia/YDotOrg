import { Injectable, computed, effect, inject } from '@angular/core';
import { AuthTokenService } from './auth-token.service';

/**
 * The one place that notices the operating Organisation has changed.
 *
 * WHY THIS EXISTS
 * ---------------
 * Almost everything this app holds in memory belongs to ONE Organisation: the sidebar, the
 * campaign register, the donor and lead queues, the people picker. None of it is valid a
 * millisecond after a SuperAdmin steps into a different Organisation, and none of it was being
 * thrown away — a switch re-issued the token and left every cached signal exactly where it was.
 * The visible symptom was a sidebar: enter TEN001, walk back to Manage Organisations, and TEN001's
 * TenantAdmin menu was still on the screen. The invisible ones were worse, because a campaign list
 * that belongs to the previous Organisation does not announce itself.
 *
 * WHY IT WATCHES THE TOKEN RATHER THAN BEING TOLD
 * -----------------------------------------------
 * The obvious design is for the switcher to call everybody who needs to know. That is what the
 * code did, and it is why this bug existed: there were THREE places that switched Organisation —
 * the top-bar switcher, the sign-in picker and the Organisation directory — and only two of them
 * remembered to reload the navigation. A fourth path, the token refresh, could change the
 * Organisation without any of them being involved.
 *
 * So the trigger is the stored Organisation itself. Whatever changes it — a switch, an exit, a
 * refreshed token, a fresh sign-in — this service notices, and everything registered here is
 * told. A new call site cannot forget, because there is nothing for it to remember.
 *
 * THIS IS NOT A SECURITY BOUNDARY. Dropping a cache is housekeeping; the Organisation that governs
 * a request is the one inside the signed token, and every endpoint re-derives it server-side.
 * What this buys is that the screen agrees with the token.
 */
@Injectable({ providedIn: 'root' })
export class OrganisationScopeService {
  /** The scope key when a global caller is standing outside every Organisation. */
  static readonly PLATFORM = 'platform';

  private readonly tokens = inject(AuthTokenService);

  private readonly listeners = new Set<(scope: string | null) => void>();

  /** The Organisation the session is operating in, or null at platform scope. */
  readonly organisationId = computed(() => this.tokens.tenant()?.tenantId ?? null);

  /**
   * What "the current scope" is, as one comparable value.
   *
   * An Organisation id inside one, `PLATFORM` outside one, and null when nobody is signed in.
   * Three states rather than two, because "at platform level" and "signed out" want opposite
   * treatment: the first reloads, the second discards and waits.
   */
  readonly scope = computed<string | null>(() =>
    this.tokens.user() === null
      ? null
      : this.organisationId() ?? OrganisationScopeService.PLATFORM);

  /**
   * The last scope the listeners were told about.
   *
   * Seeded with the scope in force when this service is first injected, so the effect below does
   * not announce a "change" on its first run — at that point nothing has changed, and a caller
   * that has just loaded its own data would load it a second time.
   */
  private lastAnnounced: string | null = this.scope();

  constructor() {
    effect(() => {
      const scope = this.scope();

      if (scope === this.lastAnnounced) {
        return;
      }

      this.lastAnnounced = scope;

      // Copied before iterating: a listener is entitled to register another one.
      for (const listener of [...this.listeners]) {
        listener(scope);
      }
    });
  }

  /**
   * Registers state that belongs to one Organisation and must not survive a switch.
   *
   * The callback fires ONLY on an actual change, never on registration. A service registering
   * from its own constructor has just loaded, or is about to, and does not want to be told to do
   * it again — it wants to be told the next time the answer stops being true.
   *
   * The scope being moved TO is passed: null means signed out, which is a reason to discard
   * without re-fetching, because there is no longer anybody to fetch for.
   *
   * Registrations are never removed. Every caller is a root-provided singleton that lives as long
   * as the tab does, so there is nothing to unregister and a Set is enough to keep a service that
   * somehow registered twice from being called twice.
   */
  onOrganisationChange(listener: (scope: string | null) => void): void {
    this.listeners.add(listener);
  }
}
