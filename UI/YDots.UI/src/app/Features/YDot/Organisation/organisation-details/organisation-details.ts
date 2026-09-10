import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { map } from 'rxjs';
import { OrganisationStateService, EditableOrganisationFields } from '../../../../Service/organisation-state.service';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';
import { ORGANISATION_TYPES, LEGAL_STRUCTURES, verificationStatusLabel, verificationBadgeClass, OrganisationType, LegalStructure } from '../../../../Shared/models/organisation.model';


@Component({
  selector: 'app-organisation-details',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './organisation-details.html',
  styleUrl: './organisation-details.css',
})
export class OrganisationDetailsComponent implements AfterViewInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly orgState = inject(OrganisationStateService);

  protected readonly organisationTypes = ORGANISATION_TYPES;
  protected readonly legalStructures = LEGAL_STRUCTURES;

  /** Country, state and city from the GlobalMaster catalogue, cascading for every country. */
  protected readonly geo = createGeoCascade();

  private readonly organisationId = toSignal(this.route.paramMap.pipe(map((p) => p.get('organisationId') ?? '')), { initialValue: '' });
  protected readonly organisation = computed(() => this.orgState.getById(this.organisationId()));
  protected readonly notFound = computed(() => !!this.organisationId() && !this.organisation());
  protected readonly audit = computed(() => this.orgState.auditFor(this.organisationId()));

  protected verificationLabel(): string {
    const org = this.organisation();
    return org ? verificationStatusLabel(org.status) : '';
  }
  protected verificationClass(): string {
    const org = this.organisation();
    return org ? verificationBadgeClass(org.status) : 'org-badge-muted';
  }

  ngAfterViewInit(): void {
    const section = this.route.snapshot.queryParamMap.get('section');
    if (section) {
      setTimeout(() => this.scrollToSection(section), 150);
    }
  }

  protected scrollToSection(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // ----- Edit mode -----
  protected readonly editing = signal(false);
  protected readonly editValues = signal<EditableOrganisationFields>({});

  protected startEdit(): void {
    const org = this.organisation();
    if (!org) return;

    // The record holds names, not ids, so the cascade is rebuilt by matching them back to the
    // catalogue. A stored state that has since been retired simply leaves that box empty.
    this.geo.restore(org.country, org.state, org.city);

    this.editValues.set({
      name: org.name,
      organisationType: org.organisationType,
      legalStructure: org.legalStructure,
      registrationNumber: org.registrationNumber,
      registrationDate: org.registrationDate,
      addressLine1: org.addressLine1,
      addressLine2: org.addressLine2,
      country: org.country,
      state: org.state,
      city: org.city,
      pinCode: org.pinCode,
      email: org.email,
      phone: org.phone,
      alternatePhone: org.alternatePhone,
      website: org.website,
      panTaxId: org.panTaxId,
      ownerName: org.ownerName,
      ownerEmail: org.ownerEmail,
      ownerMobile: org.ownerMobile,
      ownerDesignation: org.ownerDesignation,
    });
    this.editing.set(true);
  }
  protected cancelEdit(): void {
    this.editing.set(false);
  }
  protected setField<K extends keyof EditableOrganisationFields>(key: K, value: EditableOrganisationFields[K]): void {
    this.editValues.update((v) => ({ ...v, [key]: value }));
  }
  protected get editType(): OrganisationType {
    return (this.editValues().organisationType as OrganisationType) ?? 'Non-Profit / NGO';
  }
  protected get editLegal(): LegalStructure {
    return (this.editValues().legalStructure as LegalStructure) ?? 'Trust';
  }
  /** True when the catalogue knows this country's subdivisions. Was `editIsIndia`. */
  protected readonly hasStateOptions = computed(() => this.geo.hasStates());

  protected readonly editAvailableCities = computed(() => this.geo.cityNames());

  protected onEditCountryChange(value: string): void {
    this.editValues.update((v) => ({ ...v, country: value, state: '', city: '' }));
    this.geo.selectCountry(value);
  }

  protected onEditStateChange(value: string): void {
    this.editValues.update((v) => ({ ...v, state: value, city: '' }));
    this.geo.selectState(value);
  }
  protected saveEdit(): void {
    const org = this.organisation();
    const values = this.editValues();
    if (!org || !values.name?.trim()) return;
    this.orgState.update(org.id, values, 'Super Admin');
    this.editing.set(false);
  }

  // ----- Admin status actions -----
  protected canActivate(): boolean {
    const org = this.organisation();
    return !!org && this.orgState.canTransition(org.status, 'Active');
  }
  protected canSuspend(): boolean {
    const org = this.organisation();
    return !!org && this.orgState.canTransition(org.status, 'Suspended');
  }
  protected canDeactivate(): boolean {
    const org = this.organisation();
    return !!org && this.orgState.canTransition(org.status, 'Deactivated');
  }
  protected activate(): void {
    const org = this.organisation();
    if (org) this.orgState.changeAdminStatus(org.id, 'Active', 'Super Admin');
  }
  protected suspend(): void {
    const org = this.organisation();
    if (org) this.orgState.changeAdminStatus(org.id, 'Suspended', 'Super Admin');
  }
  protected deactivate(): void {
    const org = this.organisation();
    if (org) this.orgState.changeAdminStatus(org.id, 'Deactivated', 'Super Admin');
  }

  protected canVerify(): boolean {
    const org = this.organisation();
    return org?.status === 'Pending Verification';
  }
  protected openVerification(): void {
    const org = this.organisation();
    if (org) this.router.navigate(['/app/administration/organisation/organisations', org.id, 'verify']);
  }
}
