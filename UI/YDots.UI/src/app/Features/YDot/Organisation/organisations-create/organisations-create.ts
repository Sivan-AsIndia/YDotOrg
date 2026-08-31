import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { OrganisationStateService } from '../../../../Service/organisation-state.service';
import { SearchableSelectComponent } from '../../../../Shared/components/searchable-select/searchable-select';
import { createGeoCascade } from '../../../../Shared/services/geo-cascade';
import { ORGANISATION_TYPES, LEGAL_STRUCTURES, OrganisationType, LegalStructure } from '../../../../Shared/models/organisation.model';


@Component({
  selector: 'app-organisations-create',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, SearchableSelectComponent],
  templateUrl: './organisations-create.html',
  styleUrl: './organisations-create.css',
})
export class OrganisationsCreateComponent {
  constructor() {
    // The form opens on India, so load its states straight away rather than leaving the second
    // box empty until somebody re-picks the country it already shows.
    this.geo.selectCountry(this.country());
  }

  private readonly router = inject(Router);
  protected readonly orgState = inject(OrganisationStateService);

  protected readonly organisationTypes = ORGANISATION_TYPES;
  protected readonly legalStructures = LEGAL_STRUCTURES;

  /**
   * Country, state and city, live from the GlobalMaster catalogue.
   *
   * WHAT THIS REPLACES: `COUNTRIES` — six hard-coded strings ending in "Other" — and
   * `INDIA_STATES`, sixteen of India's thirty-six subdivisions. The cascade below now works for
   * every country the platform is configured with rather than for India alone, and a state added
   * on the Masters screen shows up here without a rebuild.
   */
  protected readonly geo = createGeoCascade();

  // Organisation Information
  protected readonly name = signal('');
  protected readonly organisationType = signal<OrganisationType>('Non-Profit / NGO');
  protected readonly legalStructure = signal<LegalStructure>('Trust');
  protected readonly registrationNumber = signal('');
  protected readonly registrationDate = signal('');

  // Address — optional; Country/State/City cascade when Country is India.
  protected readonly addressLine1 = signal('');
  protected readonly addressLine2 = signal('');
  protected readonly country = signal('India');
  protected readonly state = signal('');
  protected readonly city = signal('');
  protected readonly pinCode = signal('');


  /**
   * Whether to draw a state DROPDOWN or a free-text box.
   *
   * This used to be `isIndia`, which is why every other country fell back to typing the state by
   * hand. The question the form actually needs answered is "does the catalogue know this
   * country's subdivisions" — true for India, the United States and Australia alike, and false
   * for Singapore, which genuinely has none.
   */
  protected readonly hasStateOptions = computed(() => this.geo.hasStates());

  protected readonly availableCities = computed(() => this.geo.cityNames());

  protected onCountryChange(value: string): void {
    this.country.set(value);
    this.state.set('');
    this.city.set('');
    this.geo.selectCountry(value);
  }

  protected onStateChange(value: string): void {
    this.state.set(value);
    this.city.set('');
    this.geo.selectState(value);
  }

  // Contact
  protected readonly email = signal('');
  protected readonly phone = signal('');
  protected readonly alternatePhone = signal('');
  protected readonly website = signal('');

  // Compliance
  protected readonly panTaxId = signal('');
  protected readonly twelveAApplicable = signal(false);
  protected readonly eightyGApplicable = signal(false);
  protected readonly fcraApplicable = signal(false);
  protected readonly gstApplicable = signal(false);

  // Owner
  protected readonly ownerName = signal('');
  protected readonly ownerEmail = signal('');
  protected readonly ownerMobile = signal('');
  protected readonly ownerDesignation = signal('');

  protected readonly touched = signal(false);
  protected readonly submitting = signal(false);

  private readonly emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  protected readonly nameError = computed(() => {
    if (!this.touched()) return '';
    if (!this.name().trim()) return 'Organisation name is required.';
    if (this.name().trim().length < 3) return 'Name must be at least 3 characters.';
    return '';
  });
  protected readonly duplicateError = computed(() => {
    if (!this.touched() || !this.name().trim()) return '';
    return this.orgState.isDuplicate(this.name(), this.registrationNumber()) ? 'An organisation with this name or registration number already exists.' : '';
  });
  protected readonly emailError = computed(() => {
    if (!this.touched()) return '';
    if (!this.email().trim()) return 'Email address is required.';
    if (!this.emailPattern.test(this.email().trim())) return 'Enter a valid email address.';
    return '';
  });
  protected readonly phoneError = computed(() => (this.touched() && !this.phone().trim() ? 'Phone number is required.' : ''));

  protected readonly ownerNameError = computed(() => (this.touched() && !this.ownerName().trim() ? 'Owner name is required.' : ''));
  protected readonly ownerEmailError = computed(() => {
    if (!this.touched()) return '';
    if (!this.ownerEmail().trim()) return 'Owner email / login ID is required.';
    if (!this.emailPattern.test(this.ownerEmail().trim())) return 'Enter a valid email address.';
    return '';
  });
  protected readonly ownerMobileError = computed(() => (this.touched() && !this.ownerMobile().trim() ? 'Owner mobile number is required.' : ''));

  /** Address (line 1/2, country, state, city, PIN) is intentionally optional — Super Admin can create the record with just identity/contact/owner details and fill address in later. */
  protected readonly formValid = computed(
    () =>
      !this.nameError() &&
      !this.duplicateError() &&
      !this.emailError() &&
      !this.phoneError() &&
      !this.ownerNameError() &&
      !this.ownerEmailError() &&
      !this.ownerMobileError(),
  );

  protected cancel(): void {
    this.router.navigate(['/app/administration/organisation/super-admin-view']);
  }

  protected submit(): void {
    this.touched.set(true);
    if (!this.formValid() || this.submitting()) return;

    this.submitting.set(true);
    setTimeout(() => {
      const record = this.orgState.create(
        {
          name: this.name(),
          organisationType: this.organisationType(),
          legalStructure: this.legalStructure(),
          registrationNumber: this.registrationNumber(),
          registrationDate: this.registrationDate(),
          addressLine1: this.addressLine1(),
          addressLine2: this.addressLine2(),
          country: this.country(),
          state: this.state(),
          city: this.city(),
          pinCode: this.pinCode(),
          email: this.email(),
          phone: this.phone(),
          alternatePhone: this.alternatePhone(),
          website: this.website(),
          panTaxId: this.panTaxId(),
          compliance: {
            twelveA: { applicable: this.twelveAApplicable(), number: '' },
            eightyG: { applicable: this.eightyGApplicable(), number: '' },
            fcra: { applicable: this.fcraApplicable(), number: '' },
            gst: { applicable: this.gstApplicable(), number: '' },
          },
          ownerName: this.ownerName(),
          ownerEmail: this.ownerEmail(),
          ownerMobile: this.ownerMobile(),
          ownerDesignation: this.ownerDesignation(),
        },
        'Super Admin',
      );
      this.submitting.set(false);
      this.router.navigate(['/app/administration/organisation/organisations', record.id, 'owner-access']);
    }, 600);
  }
}
