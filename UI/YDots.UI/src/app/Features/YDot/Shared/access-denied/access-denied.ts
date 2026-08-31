import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';

/**
 * Where a route guard sends somebody who is signed in but not allowed here.
 *
 * WHY THIS IS A SCREEN AND NOT A REDIRECT TO THE DASHBOARD
 * --------------------------------------------------------
 * Silently bouncing somebody to the dashboard makes a permission problem look like a broken link.
 * They click the same bookmark tomorrow and get bounced again, and nobody ever finds out that an
 * administrator needs to grant them something. Saying what happened, and what the missing
 * permission is called, turns it into a request somebody can actually action.
 *
 * WHAT IT DOES NOT DO is imply the page exists in a particular shape or hold any of its data —
 * it never loaded any. The guard runs before the component, so nothing was fetched.
 */
@Component({
  selector: 'app-access-denied',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './access-denied.html',
  styleUrl: './access-denied.css',
})
export class AccessDeniedComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tokens = inject(AuthTokenService);

  /** The page they were trying to reach, so support has something concrete to work from. */
  readonly attemptedUrl = signal(this.route.snapshot.queryParamMap.get('returnUrl') ?? '');

  /** The permission codes the guard wanted. Named because a request needs a name. */
  readonly requiredPermissions = signal(
    (this.route.snapshot.queryParamMap.get('required') ?? '')
      .split(',')
      .map((code) => code.trim())
      .filter(Boolean));

  readonly displayName = computed(() => this.tokens.displayName());
  readonly organisationName = computed(() => this.tokens.organisationName());

  /**
   * True when the caller is at global scope with no Organisation chosen.
   *
   * This is by far the most common reason a root user lands here, and it is not really a
   * permission problem at all — they simply have not said which Organisation they mean. Offering
   * the picker is a far better answer than telling them to contact an administrator about
   * themselves.
   */
  readonly needsOrganisation = computed(
    () => this.tokens.isSuperAdmin() && !this.tokens.tenant()?.tenantId);

  goBack(): void {
    void this.router.navigate(['/app/dashboard']);
  }

  chooseOrganisation(): void {
    void this.router.navigate(['/auth/select-organisation'], {
      queryParams: { returnUrl: this.attemptedUrl() || undefined },
    });
  }

  requestAccess(): void {
    void this.router.navigate(['/app/administration/access/access-request-and-approval']);
  }
}
