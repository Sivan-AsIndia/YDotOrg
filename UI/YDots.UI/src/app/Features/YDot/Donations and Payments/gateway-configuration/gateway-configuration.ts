import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  GatewayAccountResponse,
  UpsertGatewayAccountRequest,
  canPerform,
} from '../../../../Shared/models/payment.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { GeoMasterService } from '../../../../Shared/services/geo-master.service';

type UiState = 'loading' | 'ready' | 'empty' | 'no-access' | 'error';

/**
 * Payment gateway configuration: which merchant account an organisation's donations settle into.
 *
 * THE MOST CONSEQUENTIAL SCREEN IN THE MODULE, and it had no component at all - the menu node
 * pointed at a route that rendered nothing. Everything here decides where money ends up, which is
 * why the menu entry is off by default and why every field below carries a warning rather than a
 * label.
 *
 * NO SECRET EVER REACHES THIS SCREEN. The fields are REFERENCES - the names of configuration keys
 * the server resolves at the point of use - not the keys themselves. The API returns `hasApiKey`
 * and `hasWebhookSecret` as booleans for exactly this reason: a screen that could display an API
 * key is a screen that puts it in a browser's memory, in a screenshot, and in whatever the user
 * pastes into a support ticket.
 *
 * TEST MODE IS SHOWN LOUDLY. A test account that looks live is how an organisation reports income
 * that never arrived - the donations appear, the receipts issue, and the bank account stays empty.
 * The natural key is (organisation, gateway, test mode), so a live and a test account for the same
 * provider coexist deliberately.
 */
@Component({
  selector: 'app-gateway-configuration',
  imports: [CommonModule, FormsModule],
  templateUrl: './gateway-configuration.html',
  styleUrl: './gateway-configuration.css',
})
export class GatewayConfigurationComponent {
  private readonly geoMasters = inject(GeoMasterService);

  private readonly paymentApi = inject(PaymentApiService);
  private readonly toast = inject(ToastService);
  private readonly tokens = inject(AuthTokenService);

  protected readonly pageTitle = 'Payment gateway';
  protected readonly pageSubtitle =
    'Which merchant account this organisation’s donations settle into.';

  protected readonly uiState = signal<UiState>('loading');
  protected readonly accounts = signal<readonly GatewayAccountResponse[]>([]);
  protected readonly errorMessage = signal('');
  protected readonly lastRefresh = signal('');

  /**
   * What this caller may do.
   *
   * VIEWING AND MANAGING ARE SEPARATE. Somebody supporting donors needs to know which gateway is
   * configured and whether it is in test mode; changing where the money goes is a different
   * decision entirely.
   */
  protected readonly permissions = computed(() => ({
    view: this.tokens.hasAnyPermission('pay.gateway.view'),
    manage: this.tokens.hasAnyPermission('pay.gateway.manage'),
  }));

  // ================= The editor =================

  protected readonly editorOpen = signal(false);

  /** The account being edited, or null while adding a new one. */
  protected readonly editing = signal<GatewayAccountResponse | null>(null);

  protected readonly formGatewayName = signal('');
  protected readonly formMerchantId = signal('');
  protected readonly formCurrency = signal('INR');
  protected readonly formApiKeyReference = signal('');
  protected readonly formWebhookSecretReference = signal('');
  protected readonly formIsTestMode = signal(true);
  protected readonly formIsActive = signal(false);
  protected readonly formReturnUrl = signal('');
  protected readonly formWebhookUrl = signal('');
  protected readonly formValidityMinutes = signal(60);
  protected readonly formEnabledMethods = signal('');
  protected readonly formNotes = signal('');
  protected readonly formSubmitted = signal(false);
  protected readonly saving = signal(false);

  /** Typed to confirm going live. A checkbox is too easy to tick by accident for this. */
  protected readonly goLiveConfirmation = signal('');

  protected readonly gatewayOptions: readonly string[] = ['Razorpay', 'Stripe', 'PayU', 'Cashfree'];

  /**
   * The currencies a gateway may be configured for, from the GlobalMaster catalogue.
   *
   * Was five literals. A gateway is configured against the currencies the platform actually
   * knows how to price, so reading them from the catalogue is the only way the two can agree -
   * and a currency added on the Masters screen becomes configurable without a release.
   */
  protected readonly currencyOptions = signal<readonly string[]>([]);

  protected readonly methodOptions: readonly string[] = [
    'card',
    'netbanking',
    'upi',
    'wallet',
    'emi',
  ];

  constructor() {
    // The currency catalogue, loaded once. `GeoMasterService` caches it and never throws, so a
    // failure here leaves the dropdown empty rather than breaking the page.
    this.geoMasters
      .getCurrencies()
      .subscribe((currencies) => this.currencyOptions.set(currencies.map((currency) => currency.code)));

    if (!this.permissions().view) {
      this.uiState.set('no-access');
      return;
    }

    this.load();
  }

  // ================= Loading =================

  protected load(): void {
    if (!this.permissions().view) {
      this.uiState.set('no-access');
      return;
    }

    this.uiState.set('loading');

    this.paymentApi.getGatewayAccounts().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.lastRefresh.set(this.nowLabel());
        this.uiState.set(accounts.length === 0 ? 'empty' : 'ready');
      },
      error: (error) => {
        this.accounts.set([]);
        this.errorMessage.set(
          apiErrorMessage(error, 'The gateway configuration could not be loaded.'),
        );
        this.uiState.set('error');
      },
    });
  }

  // ================= Derived =================

  /**
   * The live account currently taking donations.
   *
   * THERE SHOULD BE AT MOST ONE. Two active live accounts for one organisation means donations
   * splitting between two merchant accounts depending on which the server picked, which is very
   * hard to notice and very unpleasant to unpick at the year end.
   */
  protected readonly liveAccount = computed(
    () => this.accounts().find((account) => account.isActive && !account.isTestMode) ?? null,
  );

  protected readonly hasMultipleLiveAccounts = computed(
    () => this.accounts().filter((account) => account.isActive && !account.isTestMode).length > 1,
  );

  /**
   * Whether donations can actually be taken right now.
   *
   * THE SCREEN LEADS WITH THIS, because it is the question everybody arriving here is really
   * asking. An organisation with a configured but inactive gateway looks configured and takes no
   * money.
   */
  protected readonly canTakeDonations = computed(() => {
    const live = this.liveAccount();

    return !!live && live.hasApiKey && live.hasWebhookSecret;
  });

  /** Why donations cannot be taken, when they cannot. */
  protected readonly blockedReason = computed(() => {
    const live = this.liveAccount();

    if (!live) {
      const testOnly = this.accounts().some((account) => account.isTestMode && account.isActive);

      return testOnly
        ? 'Only a TEST account is active. Donations will appear to succeed and no money will '
          + 'reach the bank account.'
        : 'No live gateway account is active, so the public donation form cannot take payments.';
    }

    if (!live.hasApiKey) {
      return 'The live account has no API key reference configured, so the provider will refuse '
        + 'every payment request.';
    }

    if (!live.hasWebhookSecret) {
      return 'The live account has no webhook secret reference. Payments may be taken, but the '
        + 'provider’s confirmations cannot be verified - so donations will sit unconfirmed.';
    }

    return '';
  });

  // ================= The editor =================

  protected openAdd(): void {
    if (!this.permissions().manage) {
      return;
    }

    this.editing.set(null);
    this.formGatewayName.set('');
    this.formMerchantId.set('');
    this.formCurrency.set('INR');
    this.formApiKeyReference.set('');
    this.formWebhookSecretReference.set('');

    // A NEW ACCOUNT STARTS IN TEST MODE AND INACTIVE. The safe default is the one where a mistake
    // costs nothing: an account created live and active would start taking real money before
    // anybody had checked the merchant id.
    this.formIsTestMode.set(true);
    this.formIsActive.set(false);

    this.formReturnUrl.set('');
    this.formWebhookUrl.set('');
    this.formValidityMinutes.set(60);
    this.formEnabledMethods.set('');
    this.formNotes.set('');
    this.formSubmitted.set(false);
    this.goLiveConfirmation.set('');
    this.editorOpen.set(true);
  }

  protected openEdit(account: GatewayAccountResponse): void {
    if (!this.permissions().manage) {
      return;
    }

    this.editing.set(account);
    this.formGatewayName.set(account.gatewayName);
    this.formMerchantId.set(account.merchantId);
    this.formCurrency.set(account.settlementCurrencyCode);

    // BLANK, NOT THE STORED VALUE. The API returns booleans rather than the references, and
    // pre-filling something plausible would be a lie about what is configured. Left empty means
    // "leave it as it is"; typing a value replaces it.
    this.formApiKeyReference.set('');
    this.formWebhookSecretReference.set('');

    this.formIsTestMode.set(account.isTestMode);
    this.formIsActive.set(account.isActive);
    this.formReturnUrl.set(account.returnUrl ?? '');
    this.formWebhookUrl.set(account.webhookUrl ?? '');
    this.formValidityMinutes.set(account.paymentLinkValidityMinutes);
    this.formEnabledMethods.set(account.enabledMethods.join(', '));
    this.formNotes.set(account.notes ?? '');
    this.formSubmitted.set(false);
    this.goLiveConfirmation.set('');
    this.editorOpen.set(true);
  }

  protected closeEditor(): void {
    this.editorOpen.set(false);
    this.editing.set(null);
  }

  // ================= Validation =================

  protected readonly gatewayNameValid = computed(() => this.formGatewayName().trim().length > 0);
  protected readonly merchantIdValid = computed(() => this.formMerchantId().trim().length > 0);
  protected readonly currencyValid = computed(() => this.formCurrency().trim().length === 3);

  protected readonly validityValid = computed(() => {
    const minutes = Number(this.formValidityMinutes());

    return Number.isFinite(minutes) && minutes >= 5 && minutes <= 10080;
  });

  /**
   * A URL, if one is given.
   *
   * HTTPS ONLY for the webhook. A provider posting a payment confirmation over plain HTTP is one
   * anybody on the path can read and forge, and the confirmation is what turns a payment into a
   * recorded donation.
   */
  protected readonly returnUrlValid = computed(() => this.urlValid(this.formReturnUrl(), false));
  protected readonly webhookUrlValid = computed(() => this.urlValid(this.formWebhookUrl(), true));

  /**
   * Whether this save is switching an account to LIVE and ACTIVE.
   *
   * The one change on this screen that starts moving real money, so it is the one that asks the
   * person to type a confirmation rather than tick a box.
   */
  protected readonly isGoingLive = computed(() => {
    if (this.formIsTestMode() || !this.formIsActive()) {
      return false;
    }

    const existing = this.editing();

    return !existing || existing.isTestMode || !existing.isActive;
  });

  protected readonly goLiveConfirmed = computed(
    () => this.goLiveConfirmation().trim().toUpperCase() === 'GO LIVE',
  );

  protected readonly formValid = computed(
    () =>
      this.gatewayNameValid()
      && this.merchantIdValid()
      && this.currencyValid()
      && this.validityValid()
      && this.returnUrlValid()
      && this.webhookUrlValid()
      && (!this.isGoingLive() || this.goLiveConfirmed()),
  );

  // ================= Saving =================

  /**
   * Saves the account.
   *
   * AN UPSERT KEYED ON (ORGANISATION, GATEWAY, TEST MODE). There is no id in the request because
   * that is the natural key - which is also why switching an account between test and live mode
   * addresses a different record, and the screen says so before somebody does it.
   *
   * THE SECRET REFERENCES ARE OMITTED WHEN BLANK, so an edit that does not touch them leaves them
   * exactly as they were. Sending an empty string would clear a working key and stop every payment.
   */
  protected save(): void {
    this.formSubmitted.set(true);

    if (!this.permissions().manage || !this.formValid()) {
      return;
    }

    const existing = this.editing();

    const request: UpsertGatewayAccountRequest = {
      gatewayName: this.formGatewayName().trim(),
      merchantId: this.formMerchantId().trim(),
      settlementCurrencyCode: this.formCurrency().trim().toUpperCase(),
      isTestMode: this.formIsTestMode(),
      isActive: this.formIsActive(),
      returnUrl: this.formReturnUrl().trim() || null,
      webhookUrl: this.formWebhookUrl().trim() || null,
      paymentLinkValidityMinutes: Number(this.formValidityMinutes()),
      enabledMethods: this.formEnabledMethods().trim() || null,
      notes: this.formNotes().trim() || null,
      expectedVersion: existing?.version ?? null,
    };

    if (this.formApiKeyReference().trim()) {
      request.apiKeyReference = this.formApiKeyReference().trim();
    }

    if (this.formWebhookSecretReference().trim()) {
      request.webhookSecretReference = this.formWebhookSecretReference().trim();
    }

    this.saving.set(true);

    this.paymentApi.saveGatewayAccount(request).subscribe({
      next: (account) => {
        this.saving.set(false);
        this.closeEditor();

        this.toast.show(
          account.isTestMode ? 'Test account saved' : 'Live account saved',
          account.isActive && !account.isTestMode
            ? `${account.gatewayName} is now taking live donations for this organisation.`
            : `${account.gatewayName} has been saved. It is ${account.isActive ? 'active' : 'inactive'}.`,
          account.isActive && !account.isTestMode ? 'warning' : 'success',
        );

        this.load();
      },
      error: (error) => {
        this.saving.set(false);

        const code =
          typeof error === 'object' && error !== null && 'errorCode' in error
            ? (error as { errorCode?: string }).errorCode
            : undefined;

        if (code === 'CONCURRENCY_CONFLICT') {
          this.toast.show(
            'Configuration changed',
            'Somebody else changed this gateway account while you were editing it. Reloading so '
            + 'you can see what they did before saving over it.',
            'warning',
          );

          this.closeEditor();
          this.load();
          return;
        }

        this.toast.show(
          'Could not save',
          apiErrorMessage(error, 'The gateway account could not be saved.'),
          'error',
        );
      },
    });
  }

  /**
   * Takes an account out of service.
   *
   * IT DEACTIVATES RATHER THAN DELETES, and the API has no delete. Every donation ever taken
   * through this account references it, and removing the record would orphan them - the money
   * would still have arrived, and nothing would say through what.
   */
  protected deactivate(account: GatewayAccountResponse): void {
    if (!this.permissions().manage) {
      return;
    }

    this.paymentApi
      .saveGatewayAccount({
        gatewayName: account.gatewayName,
        merchantId: account.merchantId,
        settlementCurrencyCode: account.settlementCurrencyCode,
        isTestMode: account.isTestMode,
        isActive: false,
        returnUrl: account.returnUrl,
        webhookUrl: account.webhookUrl,
        paymentLinkValidityMinutes: account.paymentLinkValidityMinutes,
        enabledMethods: account.enabledMethods.join(', ') || null,
        notes: account.notes,
        expectedVersion: account.version,
      })
      .subscribe({
        next: () => {
          this.toast.show(
            'Account deactivated',
            `${account.gatewayName} will no longer take new donations. Existing donations are `
            + 'unaffected.',
            'success',
          );

          this.load();
        },
        error: (error) =>
          this.toast.show(
            'Could not deactivate',
            apiErrorMessage(error, 'The gateway account could not be deactivated.'),
            'error',
          ),
      });
  }

  // ================= Helpers =================

  protected can(account: GatewayAccountResponse | null, action: string): boolean {
    return canPerform(account?.permittedActions, action);
  }

  protected when(value: string | null | undefined): string {
    if (!value) {
      return '—';
    }

    return new Date(value).toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private urlValid(value: string, requireHttps: boolean): boolean {
    const trimmed = value.trim();

    if (!trimmed) {
      return true;
    }

    try {
      const url = new URL(trimmed);

      return requireHttps ? url.protocol === 'https:' : true;
    } catch {
      return false;
    }
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
