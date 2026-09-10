import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { OrganisationStateService } from '../../../../Service/organisation-state.service';
import { OrganisationRecord, verificationStatusLabel } from '../../../../Shared/models/organisation.model';


@Component({
  selector: 'app-super-admin-view',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './super-admin-view.html',
  styleUrl: './super-admin-view.css',
})
export class SuperAdminViewComponent {
  private readonly router = inject(Router);
  protected readonly orgState = inject(OrganisationStateService);

  protected readonly searchTerm = signal('');
  protected readonly typeFilter = signal('');
  protected readonly legalStructureFilter = signal('');
  protected readonly statusFilter = signal('');

  protected readonly filtered = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const type = this.typeFilter();
    const legal = this.legalStructureFilter();
    const status = this.statusFilter();

    return this.orgState.records().filter((r) => {
      if (q) {
        const matches =
          r.name.toLowerCase().includes(q) ||
          r.id.toLowerCase().includes(q) ||
          r.ownerName.toLowerCase().includes(q) ||
          r.ownerEmail.toLowerCase().includes(q) ||
          r.registrationNumber.toLowerCase().includes(q);
        if (!matches) return false;
      }
      if (type && r.organisationType !== type) return false;
      if (legal && r.legalStructure !== legal) return false;
      if (status && r.status !== status) return false;
      return true;
    });
  });

  protected hasActiveFilters(): boolean {
    return !!(this.searchTerm() || this.typeFilter() || this.legalStructureFilter() || this.statusFilter());
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.typeFilter.set('');
    this.legalStructureFilter.set('');
    this.statusFilter.set('');
  }

  protected viewOrganisation(id: string): void {
    this.router.navigate(['/app/administration/organisation/organisations', id]);
  }
  protected viewDocuments(id: string): void {
    this.router.navigate(['/app/administration/organisation/organisations', id], { queryParams: { section: 'documents' } });
  }
  protected viewHistory(id: string): void {
    this.router.navigate(['/app/administration/organisation/organisations', id], { queryParams: { section: 'audit' } });
  }
  protected canVerify(org: OrganisationRecord): boolean {
    return org.status === 'Pending Verification';
  }
  protected openVerification(id: string): void {
    this.router.navigate(['/app/administration/organisation/organisations', id, 'verify']);
  }

  protected canActivate(org: OrganisationRecord): boolean {
    return this.orgState.canTransition(org.status, 'Active');
  }
  protected canSuspend(org: OrganisationRecord): boolean {
    return this.orgState.canTransition(org.status, 'Suspended');
  }
  protected canDeactivate(org: OrganisationRecord): boolean {
    return this.orgState.canTransition(org.status, 'Deactivated');
  }

  protected activate(org: OrganisationRecord): void {
    this.orgState.changeAdminStatus(org.id, 'Active', 'Super Admin');
  }
  protected suspend(org: OrganisationRecord): void {
    this.orgState.changeAdminStatus(org.id, 'Suspended', 'Super Admin');
  }
  protected deactivate(org: OrganisationRecord): void {
    this.orgState.changeAdminStatus(org.id, 'Deactivated', 'Super Admin');
  }

  protected verificationLabel(org: OrganisationRecord): string {
    return verificationStatusLabel(org.status);
  }
}
