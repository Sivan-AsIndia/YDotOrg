import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { map } from 'rxjs';
import { OrganisationStateService } from '../../../../Shared/services/organisation-state.service';
// Canonical shared store — the SAME root instance used by Directory, Setup Wizard,
// Details and Verification & Approval, so records created this session are visible here.


@Component({
  selector: 'app-owner-login-entry',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './owner-login-entry.html',
  styleUrl: './owner-login-entry.css',
})
export class OwnerLoginEntryComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly orgState = inject(OrganisationStateService);

  private readonly organisationId = toSignal(this.route.paramMap.pipe(map((p) => p.get('organisationId') ?? '')), { initialValue: '' });
  protected readonly organisation = computed(() => this.orgState.getById(this.organisationId()));
  protected readonly notFound = computed(() => !!this.organisationId() && !this.organisation());
  /** Shown when the page is opened without an :organisationId (e.g. straight from the sidebar menu). */
  protected readonly selectableOrganisations = computed(() => this.orgState.records());

  protected back(): void {
    this.router.navigate(['/app/administration/organisation/organisations/create']);
  }
  protected continue(): void {
    const org = this.organisation();
    if (org) this.router.navigate(['/app/administration/organisation/organisations', org.id]);
  }
}
