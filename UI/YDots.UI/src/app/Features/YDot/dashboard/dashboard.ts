import { Component, AfterViewInit, computed, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthSessionService } from '../../../Shared/services/auth-session.service';
import { AuthTokenService } from '../../../Shared/services/auth-token.service';
import { PaymentApiService } from '../../../Service/payment-api.service';
import { CampaignApiService } from '../../../Service/campaign-api.service';
import { DonorApiService } from '../../../Service/donor-api.service';

declare var Highcharts: any;
declare var ApexCharts: any;

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterModule],
  templateUrl:'./dashboard.html',
  styleUrl: './dashboard.css',
})
export class DashboardComponent implements AfterViewInit, OnInit {
  private readonly router = inject(Router);
  private readonly sessionService = inject(AuthSessionService);
  private readonly tokens = inject(AuthTokenService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly campaignApi = inject(CampaignApiService);
  private readonly donorApi = inject(DonorApiService);

  // =============================================================================================
  // The headline figures
  //
  // WHAT THIS REPLACES. Four tiles of literals - 48.72 lakh reconciled, 1,240 donors, 18 active
  // campaigns, 24,582 beneficiaries - identical in every organisation, so a charity that had
  // taken no donations at all was shown somebody else's money on the first screen it ever saw.
  //
  // A FIGURE WITH NO SERVICE BEHIND IT IS NOT SHOWN. Beneficiaries and stock have no API on this
  // platform yet, so those tiles say so rather than inventing a number that looks authoritative.
  // =============================================================================================

  /** Null until the first answer arrives; zero is a real figure and must be distinguishable. */
  readonly donationTotal = signal<string | null>(null);
  readonly donorCount = signal<number | null>(null);
  readonly settledShare = signal<string | null>(null);
  readonly activeCampaigns = signal<number | null>(null);
  readonly closedCampaigns = signal<number | null>(null);
  readonly campaignTotal = signal<number | null>(null);

  readonly figuresLoading = signal(true);
  readonly figuresUnavailable = signal(false);

  /** True when the organisation genuinely has nothing yet - a new customer's honest empty state. */
  readonly hasNoActivity = computed(
    () =>
      !this.figuresLoading() &&
      (this.donorCount() ?? 0) === 0 &&
      (this.campaignTotal() ?? 0) === 0,
  );

  currentUser = signal<any>(null);
  userRole = signal('');
  loadingUser = signal(true);

  // Quick flow navigation cards based on the IAM document flow
  quickFlows = signal([
    { title: 'User Directory', route: '/app/administration/access/user-directory', icon: 'ri-contacts-book-line', desc: 'Manage all users and access', color: 'primary' },
    { title: 'Create User', route: '/app/administration/access/create-user', icon: 'ri-user-add-line', desc: 'Invite or create a new identity', color: 'success' },
    { title: 'Campaigns', route: '/app/fundraising/campaigns/campaign-register', icon: 'ri-megaphone-line', desc: 'Manage fundraising campaigns', color: 'info' },
    { title: 'Donors', route: '/app/fundraising/relationships/donor-360', icon: 'ri-heart-line', desc: 'View donor 360 profiles', color: 'warning' },
    { title: 'Finance', route: '/app/money/finance/finance-workbench', icon: 'ri-bank-line', desc: 'Financial workbench & reconciliations', color: 'danger' },
    // THE DONATION-INTENT DETAIL SCREEN IS GONE, so this tile pointed at a dead route. The
    // Payment Queue is where an operator starts in this module - it is the work list.
    { title: 'Donations', route: '/app/donations/payment-event-queue', icon: 'ri-hand-coin-line', desc: 'Payment queue & receipts', color: 'primary' },
    { title: 'Communications', route: '/app/communications/unified-inbox', icon: 'ri-chat-3-line', desc: 'Unified inbox & templates', color: 'info' },
    { title: 'Inventory', route: '/app/supply/inventory/inventory-overview', icon: 'ri-archive-line', desc: 'Blind-stick stock & warehouses', color: 'success' },
  ]);

  ngOnInit(): void {
    // The real sign-in flow stores the identity under `ydot.user` via AuthTokenService
    // (shape: SignedInUserResponse — displayName, email, username, roles: string[]).
    // Prefer it over the legacy `userData` keys, which the API login never writes.
    const signedIn = this.tokens.user();

    if (signedIn) {
      this.currentUser.set({
        displayName: signedIn.displayName,
        username: signedIn.username,
        email: signedIn.email,
        role: signedIn.roles && signedIn.roles.length > 0 ? signedIn.roles[0] : '',
        mfaStatus: '',
        mobileNumber: '',
        department: '',
        designation: '',
      });
      this.userRole.set(signedIn.roles && signedIn.roles.length > 0 ? signedIn.roles[0] : '');
      this.loadingUser.set(false);
      this.loadFigures();
      return;
    }

    this.loadFigures();

    // NO FALLBACK TO A STORED OR STATIC PROFILE.
    //
    // This used to read `userData` out of storage and, failing that, a static JSON profile —
    // so a signed-out or expired session greeted somebody by a name that was not theirs. The
    // signed-in identity is the only source; if there isn't one, the page shows nothing rather
    // than showing somebody else.
    this.loadingUser.set(false);
  }

  /**
   * Reads the organisation's own figures.
   *
   * THREE SERVICES, IN PARALLEL, AND ANY OF THEM MAY BE REFUSED. A user who can see donations but
   * not campaigns is a normal shape on this platform, so a 403 on one tile leaves that tile
   * unavailable and the others populated - a dashboard that fails whole because one permission is
   * missing is a dashboard most people cannot use.
   */
  private loadFigures(): void {
    this.figuresLoading.set(true);

    forkJoin({
      donations: this.paymentApi.getDonationStatistics().pipe(catchError(() => of(null))),
      campaigns: this.campaignApi.getCampaignStatistics().pipe(catchError(() => of(null))),
      donors: this.donorApi
        .searchDonors({ page: 1, pageSize: 1 })
        .pipe(catchError(() => of(null))),
    }).subscribe((result) => {
      this.figuresLoading.set(false);

      if (!result.donations && !result.campaigns && !result.donors) {
        this.figuresUnavailable.set(true);
        return;
      }

      if (result.donations) {
        this.donationTotal.set(result.donations.netAmount.display);

        // SETTLED AS A SHARE OF WHAT WAS RECORDED. Undefined at zero rather than shown as 0%,
        // because "0% settled" reads as a problem and "no donations yet" does not.
        const recorded = result.donations.totalCount;
        this.settledShare.set(
          recorded > 0
            ? `${Math.round((result.donations.settledCount / recorded) * 100)}%`
            : null,
        );
      }

      if (result.campaigns) {
        this.activeCampaigns.set(result.campaigns.active);
        this.closedCampaigns.set(result.campaigns.closed);
        this.campaignTotal.set(result.campaigns.total);
      }

      if (result.donors) {
        this.donorCount.set(result.donors.totalCount);
      }
    });
  }

  ngAfterViewInit(): void {

    // reload social media init script
    const script = document.createElement('script');

    script.src = 'assets/js/dashboard/social-media.init.js';

    script.type = 'text/javascript';

    script.onload = () => {
      console.log('Charts Loaded');
    };

    document.body.appendChild(script);
  }

  navigateTo(route: string): void {
    this.router.navigate([route]);
  }

  getInitials(name: string): string {
    if (!name) return 'U';
    return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);
  }

  signOut(): void {
    this.sessionService.endSession();
    sessionStorage.removeItem('userData');
    sessionStorage.removeItem('loginResponse');
    sessionStorage.removeItem('authToken');
    sessionStorage.removeItem('refreshToken');
    sessionStorage.removeItem('sessionId');
    sessionStorage.removeItem('mfaChallenge');
    sessionStorage.removeItem('challengeToken');
    sessionStorage.removeItem('mfaRemainingAttempts');
    localStorage.removeItem('userData');
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('sessionId');
    this.router.navigate(['/auth/sign-in']);
  }
}