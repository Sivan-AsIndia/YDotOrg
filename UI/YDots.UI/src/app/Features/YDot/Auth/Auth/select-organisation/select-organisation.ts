import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { apiErrorMessage } from '../../../../../Shared/models/api-response.model';
import { TenantOptionResponse } from '../../../../../Shared/models/auth.model';
import { AuthTokenService } from '../../../../../Shared/services/auth-token.service';
import { NavigationService } from '../../../../../Shared/services/navigation.service';
import { OrganisationContextService } from '../../../../../Shared/services/organisation-context.service';
import { ToastService } from '../../../../../Shared/services/toast.service';

/**
 * The Organisation picker a root user sees after signing in.
 *
 * WHY THIS SCREEN EXISTS AT ALL
 * -----------------------------
 * A root user belongs to no Organisation. Their account is genuinely global: one record, no
 * Organisation of its own, and no duplicate account per customer. That is what makes them able to
 * administer the whole platform — and it is also why every Organisation-scoped screen has nothing
 * to show until they say which Organisation they mean.
 *
 * So sign-in for them ends in `tenantSelectionRequired`. They are properly authenticated — the
 * token in that response is real and the platform screens work already — but the picker comes
 * first, because a dashboard scoped to nothing is not a useful place to land.
 *
 * WHAT CHOOSING ACTUALLY DOES, AND WHAT IT DOES NOT
 * -------------------------------------------------
 * It asks the server to re-issue the access token against that Organisation, on the same session.
 * The account's own record is untouched: selecting is an OPERATING CONTEXT, not ownership, and a
 * root user never acquires an Organisation by looking at somebody's data. That distinction is
 * what lets them step into TEN001, deal with something, and step into TEN002 a minute later
 * without either Organisation ever having a stray administrator attached to it.
 */
@Component({
  selector: 'app-select-organisation',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './select-organisation.html',
  styleUrl: './select-organisation.css',
})
export class SelectOrganisationComponent implements OnInit, OnDestroy {
  private readonly organisations = inject(OrganisationContextService);
  private readonly navigation = inject(NavigationService);
  private readonly tokens = inject(AuthTokenService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  private readonly destroy$ = new Subject<void>();

  readonly options = signal<TenantOptionResponse[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');
  readonly selecting = signal<string | null>(null);
  readonly search = signal('');

  private returnUrl: string | null = null;

  readonly displayName = computed(() => this.tokens.displayName());

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (!term) {
      return this.options();
    }

    return this.options().filter((option) =>
      (option.name ?? '').toLowerCase().includes(term)
      || (option.code ?? '').toLowerCase().includes(term)
      || (option.subdomain ?? '').toLowerCase().includes(term));
  });

  ngOnInit(): void {
    this.returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

    // Somebody who is not global scope has nothing to choose between and should not be here.
    if (!this.tokens.user()) {
      void this.router.navigate(['/auth/sign-in']);
      return;
    }

    if (!this.organisations.canSwitch()) {
      void this.router.navigateByUrl(this.returnUrl ?? '/app/dashboard');
      return;
    }

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    this.organisations
      .loadSelectable()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (options) => {
          this.options.set(options);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(
            apiErrorMessage(error, 'The organisations could not be loaded.'));
        },
      });
  }

  /**
   * Steps into an Organisation and reloads the navigation.
   *
   * The menu is Organisation-specific, so keeping the previous one would leave links that now
   * lead nowhere. Landing goes to whichever route the new navigation names as its landing page,
   * rather than always the dashboard: an Organisation still onboarding wants its profile screen.
   */
  select(option: TenantOptionResponse): void {
    if (!option.tenantId || this.selecting()) {
      return;
    }

    this.selecting.set(option.tenantId);
    this.errorMessage.set('');

    this.organisations
      .select(option.tenantId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({
            next: () => {
              this.selecting.set(null);
              this.toast.show(
                'Organisation selected',
                `You are now working inside ${option.name}.`,
                'success');

              void this.router.navigateByUrl(
                this.returnUrl ?? this.navigation.landingRoute());
            },
            error: () => {
              // The switch itself worked; only the menu did not come back. The dashboard is a
              // better landing than leaving somebody on the picker with a token they cannot see
              // they now hold.
              this.selecting.set(null);
              void this.router.navigate(['/app/dashboard']);
            },
          });
        },
        error: (error: unknown) => {
          this.selecting.set(null);
          this.errorMessage.set(apiErrorMessage(error, 'That organisation could not be opened.'));
        },
      });
  }

  /**
   * Carries on without choosing.
   *
   * The platform screens — the Organisation directory, the BusinessUnit, the platform audit
   * trail — work perfectly well at global scope, so somebody who came here to create an
   * Organisation rather than enter one should not be trapped.
   */
  continueAtPlatformLevel(): void {
    void this.router.navigate(['/app/administration/organisation/directory']);
  }

  /** Whether an Organisation can be worked in, or only reviewed. */
  isOperable(option: TenantOptionResponse): boolean {
    return this.organisations.isOperable(option);
  }

  statusClass(status: string | undefined): string {
    switch (status) {
      case 'active':
      case 'approved':
        return 'is-good';
      case 'submitted':
      case 'underReview':
      case 'resubmitted':
        return 'is-warn';
      case 'rejected':
      case 'suspended':
        return 'is-error';
      case 'archived':
        return 'is-muted';
      default:
        return 'is-info';
    }
  }
}
