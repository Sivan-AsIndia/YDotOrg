import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { map } from 'rxjs';
import { OrganisationStateService } from '../../../../Service/organisation-state.service';
import { verificationStatusLabel, verificationBadgeClass } from '../../../../Shared/models/organisation.model';


@Component({
  selector: 'app-verification-approval',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './verification-approval.html',
  styleUrl: './verification-approval.css',
})
export class VerificationApprovalComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly orgState = inject(OrganisationStateService);

  private readonly organisationId = toSignal(this.route.paramMap.pipe(map((p) => p.get('organisationId') ?? '')), { initialValue: '' });
  protected readonly organisation = computed(() => this.orgState.getById(this.organisationId()));
  protected readonly notFound = computed(() => !!this.organisationId() && !this.organisation());
  /** Shown when the page is opened without an :organisationId (e.g. straight from the sidebar menu). Pending verifications first. */
  protected readonly verificationQueue = computed(() => {
    const all = this.orgState.records();
    return [...all.filter((r) => r.status === 'Pending Verification'), ...all.filter((r) => r.status !== 'Pending Verification')];
  });

  protected verificationLabel(): string {
    const org = this.organisation();
    return org ? verificationStatusLabel(org.status) : '';
  }
  protected verificationClass(): string {
    const org = this.organisation();
    return org ? verificationBadgeClass(org.status) : 'org-badge-muted';
  }

  protected formattedAddress(): string {
    const org = this.organisation();
    if (!org) return '—';
    const lines = [org.addressLine1, org.addressLine2].filter((l) => l.trim());
    const cityLine = [org.city, org.state, org.country].filter((l) => l.trim()).join(', ');
    const parts = [...lines, cityLine, org.pinCode].filter((p) => p.trim());
    return parts.length ? parts.join(', ') : 'Not provided';
  }

  protected readonly approvalCheck = computed(() => {
    const org = this.organisation();
    return org ? this.orgState.canApprove(org) : { ok: false };
  });

  protected readonly alreadyProcessed = computed(() => {
    const org = this.organisation();
    return !!org && org.status !== 'Pending Verification';
  });

  // ----- Approve -----
  protected readonly approving = signal(false);
  protected readonly approveError = signal('');

  protected approve(): void {
    const org = this.organisation();
    if (!org || this.approving()) return;
    this.approving.set(true);
    this.approveError.set('');
    setTimeout(() => {
      const result = this.orgState.approve(org.id, 'Super Admin');
      this.approving.set(false);
      if (!result.ok) {
        this.approveError.set(result.message ?? 'Unable to approve this organisation.');
        return;
      }
      this.router.navigate(['/app/administration/organisation/organisations', org.id]);
    }, 500);
  }

  // ----- Request Changes -----
  protected readonly showRequestChanges = signal(false);
  protected readonly changeReason = signal('');
  protected readonly requestTouched = signal(false);
  protected readonly requesting = signal(false);

  protected get changeReasonError(): string {
    return this.requestTouched() && !this.changeReason().trim() ? 'A change reason is required.' : '';
  }

  protected openRequestChanges(): void {
    this.changeReason.set('');
    this.requestTouched.set(false);
    this.showRequestChanges.set(true);
  }
  protected closeRequestChanges(): void {
    this.showRequestChanges.set(false);
  }
  protected submitRequestChanges(): void {
    this.requestTouched.set(true);
    const org = this.organisation();
    if (!org || !this.changeReason().trim() || this.requesting()) return;
    this.requesting.set(true);
    setTimeout(() => {
      const result = this.orgState.requestChanges(org.id, 'Super Admin', this.changeReason());
      this.requesting.set(false);
      if (result.ok) {
        this.showRequestChanges.set(false);
        this.router.navigate(['/app/administration/organisation/organisations', org.id]);
      }
    }, 500);
  }
}
