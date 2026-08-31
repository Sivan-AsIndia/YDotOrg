import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterModule } from '@angular/router';
import { Subject, forkJoin, takeUntil } from 'rxjs';
import { OrganisationApiService } from '../../../../Service/organisation-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  BusinessUnitResponse,
  OrganisationStatisticsResponse,
} from '../../../../Shared/models/iam-contract.model';

/**
 * The BusinessUnit: the platform itself.
 *
 * WHAT A BUSINESSUNIT IS, AND WHY THERE IS ONLY ONE
 * -------------------------------------------------
 * It is the root of the whole tenancy tree. Every Organisation is a subdomain beneath its root
 * domain — ten1.ngoplanet.com, ten2.ngoplanet.com — and every user, role and record in the system
 * ultimately hangs off it. The genuinely global tables carry a BusinessUnitId and no TenantId,
 * because they belong to the platform rather than to any one customer.
 *
 * This screen is where somebody confirms what the platform is configured as: its root domain,
 * how many Organisations it holds, and how they are distributed across the lifecycle. It is
 * read-only, because changing the root domain would invalidate every Organisation's web address
 * at once — that is a migration, not a form field.
 */
@Component({
  selector: 'app-business-unit',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './business-unit.html',
  styleUrl: './business-unit.css',
})
export class BusinessUnitComponent implements OnInit, OnDestroy {
  private readonly api = inject(OrganisationApiService);
  private readonly destroy$ = new Subject<void>();

  readonly businessUnit = signal<BusinessUnitResponse | null>(null);
  readonly statistics = signal<OrganisationStatisticsResponse | null>(null);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');

  readonly organisationCount = computed(() => this.businessUnit()?.tenantCount ?? 0);
  readonly maximumOrganisations = computed(() => this.businessUnit()?.maximumTenants ?? 0);

  /**
   * How full the platform is, when a ceiling has been set.
   *
   * Shown as a proportion rather than a raw pair because "180 of 200" only means something once
   * somebody has done the division — and the point of showing it at all is to notice before the
   * ceiling is reached, not after.
   */
  readonly capacityPercent = computed(() => {
    const maximum = this.maximumOrganisations();
    return maximum > 0 ? Math.min(100, Math.round((this.organisationCount() / maximum) * 100)) : 0;
  });

  readonly capacityClass = computed(() => {
    const percent = this.capacityPercent();
    return percent >= 90 ? 'bg-danger' : percent >= 75 ? 'bg-warning' : 'bg-success';
  });

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    // Both together: the platform's own record, and the distribution of the Organisations under
    // it. The screen is about the relationship between the two, so half of it is not useful.
    forkJoin({
      businessUnit: this.api.getBusinessUnit(),
      statistics: this.api.getStatistics(),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ businessUnit, statistics }) => {
          this.businessUnit.set(businessUnit);
          this.statistics.set(statistics);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The platform record could not be loaded.'));
        },
      });
  }

  statusClass(status: string | undefined): string {
    switch (status) {
      case 'active': return 'bg-success-subtle text-success';
      case 'suspended': return 'bg-danger-subtle text-danger';
      case 'archived': return 'bg-secondary-subtle text-secondary';
      default: return 'bg-info-subtle text-info';
    }
  }
}
