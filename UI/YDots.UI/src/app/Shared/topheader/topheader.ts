import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { apiErrorMessage } from '../models/api-response.model';
import { TenantOptionResponse } from '../models/auth.model';
import { AuthSessionService } from '../services/auth-session.service';
import { AuthTokenService } from '../services/auth-token.service';
import { NavigationService } from '../services/navigation.service';
import { OrganisationContextService } from '../services/organisation-context.service';
import { ToastService } from '../services/toast.service';

/**
 * The signed-in identity in the top bar, the Organisation switcher, and sign-out.
 *
 * WHERE THE NAME COMES FROM
 * -------------------------
 * One place: `AuthTokenService`, which holds what the API returned at sign-in. An earlier version
 * tried `sessionStorage.userData`, then `sessionStorage.loginResponse`, then fell back to a JSON
 * file of sample data — so a failed sign-in could still leave a plausible-looking name in the
 * header, and the header could disagree with the rest of the app. A signal means the value is
 * live: sign out, and this updates by itself with no event wiring.
 *
 * THE ORGANISATION SWITCHER
 * -------------------------
 * Only a root user sees it, because only a root user has anything to switch between. Choosing an
 * Organisation asks the SERVER to re-issue the access token against it; there is no client-side
 * setting that could be flipped instead. The switcher then reloads the navigation, because what
 * a person may see genuinely differs between Organisations.
 *
 * And selecting an Organisation does not change who the person is. A root user has no
 * Organisation of their own and never acquires one by looking at somebody's data — which is why
 * the bar shows "working inside X" rather than presenting it as their own.
 */
@Component({
  selector: 'app-topheader',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './topheader.html',
  styleUrl: './topheader.css',
})
export class TopheaderComponent implements OnDestroy {
  private readonly tokens = inject(AuthTokenService);
  private readonly session = inject(AuthSessionService);
  private readonly organisations = inject(OrganisationContextService);
  private readonly navigation = inject(NavigationService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  private readonly destroy$ = new Subject<void>();

  readonly username = computed(() => this.tokens.user()?.displayName ?? 'User');
  readonly userEmail = computed(() => this.tokens.user()?.email ?? '');
  readonly userRole = computed(() => this.tokens.roles()[0] ?? '');

  // ---- Organisation switcher -----------------------------------------------------------------
  readonly canSwitchOrganisation = computed(() => this.organisations.canSwitch());
  readonly currentOrganisation = computed(() => this.organisations.currentName());
  readonly isActingInOrganisation = computed(() => this.organisations.isActingInOrganisation());
  readonly selectableOrganisations = computed(() => this.organisations.selectable());
  readonly loadingOrganisations = computed(() => this.organisations.loading());

  readonly switchingTo = signal<string | null>(null);

  /** True while the exit-to-platform call is in flight, so the item cannot be double-clicked. */
  readonly leaving = signal(false);

  readonly initials = computed(() => {
    const name = this.username();

    return name
      .split(' ')
      .filter(Boolean)
      .map((part) => part[0])
      .join('')
      .toUpperCase()
      .slice(0, 2);
  });

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Loads the list when the switcher is opened, not on every page.
   *
   * For nearly everybody the answer is "you cannot switch", so asking on each page load would be
   * a wasted request on the great majority of visits.
   */
  onSwitcherOpened(): void {
    if (!this.canSwitchOrganisation() || this.selectableOrganisations().length > 0) {
      return;
    }

    this.organisations.loadSelectable().pipe(takeUntil(this.destroy$)).subscribe({
      error: (error: unknown) =>
        this.toast.show('Could not load organisations', apiErrorMessage(error), 'error'),
    });
  }

  /**
   * Steps into an Organisation.
   *
   * The navigation is reloaded straight afterwards because the menu is Organisation-specific:
   * keeping the previous one would show links that now lead nowhere.
   */
  switchTo(option: TenantOptionResponse): void {
    if (!option.tenantId || this.switchingTo()) {
      return;
    }

    this.switchingTo.set(option.tenantId);

    this.organisations
      .select(option.tenantId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({
            next: () => {
              this.switchingTo.set(null);
              this.toast.show(
                'Organisation selected',
                `You are now working inside ${option.name}.`,
                'success');
              void this.router.navigate([this.navigation.landingRoute()]);
            },
            error: () => {
              // The switch itself worked; only the menu did not come back. Sending the person to
              // the dashboard is better than leaving them on a page from the previous
              // Organisation with a stale sidebar.
              this.switchingTo.set(null);
              void this.router.navigate(['/app/dashboard']);
            },
          });
        },
        error: (error: unknown) => {
          this.switchingTo.set(null);
          this.toast.show('Could not switch', apiErrorMessage(error), 'error');
        },
      });
  }

  /**
   * Steps back out to platform level.
   *
   * WHY THIS IS HERE. Entering an Organisation replaces the sidebar with that Organisation's menu,
   * so the platform branch — Organisations, Approval Queue, the catalogues, Platform Audit —
   * disappears the moment you arrive, and nothing in the page offered a way back. Signing out and
   * in again was the only exit, which also meant the token kept naming that Organisation the whole
   * time, stamping its id onto anything the root user did next.
   *
   * Lands on the Organisations directory rather than the dashboard: somebody who just left an
   * Organisation is almost always going to another one, or to the queue that sent them there.
   */
  exitOrganisation(): void {
    if (this.leaving()) {
      return;
    }

    this.leaving.set(true);

    this.organisations
      .exitToPlatform()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({
            next: () => {
              this.leaving.set(false);
              this.toast.show(
                'Back at platform level',
                'You are no longer working inside an organisation.',
                'success');
              void this.router.navigate(['/app/administration/organisation/directory']);
            },
            error: () => {
              // Leaving worked; only the menu did not come back. The directory is still the right
              // destination, and a refresh will rebuild the sidebar.
              this.leaving.set(false);
              void this.router.navigate(['/app/administration/organisation/directory']);
            },
          });
        },
        error: (error: unknown) => {
          this.leaving.set(false);
          this.toast.show('Could not leave the organisation', apiErrorMessage(error), 'error');
        },
      });
  }

  /** Whether this Organisation can be worked in, or only reviewed. */
  isOperable(option: TenantOptionResponse): boolean {
    return this.organisations.isOperable(option);
  }

  /**
   * Signs out of this device.
   *
   * The service tells the server first, which revokes the session and clears the HttpOnly refresh
   * cookie — the part JavaScript cannot do for itself. Clearing only the browser copy would leave
   * a live session and a live cookie behind on the server.
   */
  signOut(): void {
    this.navigation.clear();
    this.session.endSession();
  }

  /** Signs out everywhere, for a lost or shared device. */
  signOutEverywhere(): void {
    this.navigation.clear();
    this.session.endSession(true);
  }
}
