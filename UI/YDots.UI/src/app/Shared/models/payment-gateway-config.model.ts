/**
 * The payment gateway configuration contract.
 *
 * HAND-WRITTEN RATHER THAN GENERATED, like `api-response.model.ts` beside it, because the
 * generated `iam-contract.model.ts` is regenerated from the OpenAPI document and these types are
 * new. The field names below are the ones the API actually sends — camelCase, matching the
 * server's serializer — and a name invented on this side would simply be `undefined` at runtime.
 *
 * THE ONE THING TO NOTICE ABOUT THE SHAPES. `PaymentGatewayConfiguration` has NO credential
 * field, and that is not an omission: the API never returns one. What comes back is
 * `apiKeyHint` — a masked fragment such as `rzp_test_........4f2a` — and three booleans saying
 * whether each secret is set. The request type has the credentials because they travel ONE WAY,
 * and a `null` on any of the three means "leave whatever is stored alone" rather than "clear it".
 */

/** Sandbox or Production, as the API spells them. */
export type PaymentGatewayEnvironment = 'Sandbox' | 'Production';

/** One provider on the dropdown, as the server describes it. */
export interface PaymentGatewayProviderOption {
  /** The enum name: `Razorpay`, `Stripe`. Sent back as-is when saving. */
  code: string;
  name: string;
  /**
   * Whether the payments service speaks this provider's own API.
   *
   * FALSE IS NOT A REASON TO HIDE THE OPTION. An organisation records where its money is meant
   * to go before an adapter exists; what the form must not do is let somebody configure a
   * provider, see it marked active, and find out from a failed donation.
   */
  hasAdapter: boolean;
  /** What the provider itself calls the public half: "Key Id", "Publishable key". */
  apiKeyLabel: string;
  secretKeyLabel: string | null;
  merchantIdLabel: string | null;
  /** The prefix a sandbox key carries, where the provider uses one. */
  testKeyPrefix: string | null;
  /** The prefix a live key carries. What the environment warning is checked against. */
  liveKeyPrefix: string | null;
  documentationUrl: string | null;
}

export interface PaymentGatewayEventOption {
  code: string;
  name: string;
  description: string;
}

export interface PaymentGatewayMethodOption {
  code: string;
  name: string;
}

/** What the form needs before a provider has been chosen. */
export interface PaymentGatewayCatalogue {
  providers: PaymentGatewayProviderOption[];
  webhookEvents: PaymentGatewayEventOption[];
  paymentMethods: PaymentGatewayMethodOption[];
  /**
   * The address to paste into the provider's dashboard, with `{provider}` where the provider's
   * own name goes. The payments service reads the provider from the ROUTE so it knows which
   * signing secret to check a signature against before trusting the body.
   */
  webhookUrlTemplate: string | null;
}

/** One stored configuration, as the screen sees it. No credential appears on this type. */
export interface PaymentGatewayConfiguration {
  id: string;
  tenantId: string;
  organisationName: string | null;
  organisationCode: string | null;
  /** The enum name. `provider` is what is sent back; `providerName` is what is shown. */
  provider: string;
  providerName: string;
  environment: PaymentGatewayEnvironment;
  displayName: string | null;
  merchantId: string | null;
  /** A masked fragment. Enough to recognise a key, never enough to use one. */
  apiKeyHint: string | null;
  hasApiKey: boolean;
  hasSecretKey: boolean;
  webhookUrl: string | null;
  hasWebhookSecret: boolean;
  subscribedEvents: string[];
  settlementCurrencyCode: string;
  returnUrl: string | null;
  paymentLinkValidityMinutes: number;
  enabledMethods: string[];
  isActive: boolean;
  /** False when the payments service has no adapter for this provider. */
  isAdapterAvailable: boolean;
  lastTestedAtUtc: string | null;
  lastTestSucceeded: boolean | null;
  lastTestMessage: string | null;
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  /** Send back as `expectedVersion` on the next write. A stale one answers 409. */
  version: number;
  /** What the SERVER says this caller may do. Render buttons from it. */
  permittedActions: string[];

  /**
   * Where the row came from.
   *
   * `Configured` — entered on this screen. Editable, testable, deletable.
   *
   * `Deployment` — a gateway the payments service built from the deployment's own environment.
   * Every organisation on this platform has one of these; they are what took donations before
   * this screen existed. READ-ONLY here, because their credentials live in the environment and
   * the API can neither read nor change them. `permittedActions` is empty on these, so the
   * buttons take care of themselves.
   */
  source: PaymentGatewayConfigurationSource;

  /**
   * True on a `Deployment` row the organisation has since superseded with its own active
   * configuration. Shown greyed rather than hidden — somebody asking "why did the merchant
   * account change?" needs to see that the old one is still there and simply no longer in use.
   */
  isSuperseded: boolean;

  /**
   * For a `Deployment` row: the NAME of the configuration section its keys are deployed under,
   * such as `Razorpay`. Not a secret — it is the opposite of one, and it is what identifies the
   * credentials to whoever maintains the environment.
   */
  deploymentKeyReference: string | null;
}

export type PaymentGatewayConfigurationSource = 'Configured' | 'Deployment';

/**
 * What the form sends.
 *
 * `apiKey`, `secretKey` and `webhookSecret` are WRITE-ONLY and their `null` is meaningful:
 * omitting one leaves the stored credential untouched, which is what an edit to the webhook URL
 * has to do — the form was never given the key, so it cannot send it back. An empty string is
 * an explicit clear.
 */
export interface UpsertPaymentGatewayConfigurationRequest {
  /** Absent on a create. */
  id?: string | null;
  /** SUPERADMIN only. Ignored for everybody else, whose organisation comes from their token. */
  tenantId?: string | null;
  provider: string;
  environment: PaymentGatewayEnvironment;
  displayName?: string | null;
  merchantId?: string | null;
  apiKey?: string | null;
  secretKey?: string | null;
  webhookUrl?: string | null;
  webhookSecret?: string | null;
  subscribedEvents?: string[] | null;
  settlementCurrencyCode: string;
  returnUrl?: string | null;
  paymentLinkValidityMinutes: number;
  enabledMethods?: string[] | null;
  isActive: boolean;
  notes?: string | null;
  /** Absent means create; present means "update this version of it". */
  expectedVersion?: number | null;
  reason?: string | null;
}

export interface ChangePaymentGatewayStatusRequest {
  isActive: boolean;
  expectedVersion: number;
  reason?: string | null;
}

export interface DeletePaymentGatewayConfigurationRequest {
  expectedVersion: number;
  /** Required by the server. Removing where an organisation's money settles needs a reason. */
  reason: string;
}

/** One line of the change log. Credentials are masked on write, so none appears here. */
export interface PaymentGatewayAuditEntry {
  id: string;
  configurationId: string;
  tenantId: string;
  organisationName: string | null;
  provider: string;
  environment: string;
  /** `Created`, `Updated`, `Activated`, `Deactivated`, `Deleted`, `Tested`, `CredentialsRotated`. */
  action: string;
  fieldName: string | null;
  /** A readable label: "Webhook URL" rather than "WebhookUrl". */
  fieldLabel: string | null;
  oldValue: string | null;
  newValue: string | null;
  actorUserId: string | null;
  actorDisplayName: string | null;
  occurredAtUtc: string;
  reason: string | null;
  ipAddress: string | null;
  correlationId: string | null;
}

/** What the Test button gets back. Never carries a credential. */
export interface PaymentGatewayTestResult {
  configurationId: string;
  provider: string;
  environment: string;
  succeeded: boolean;
  message: string;
  /** What the provider created, where it created something. A Razorpay order id. */
  reference: string | null;
  durationMilliseconds: number;
  testedAtUtc: string;
}

export interface PaymentGatewayConfigurationFilter {
  tenantId?: string;
  provider?: string;
  environment?: string;
  isActive?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface PaymentGatewayAuditFilter {
  configurationId?: string;
  tenantId?: string;
  action?: string;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
}

/** The permission codes this screen checks. Mirrors `PermissionCodes` on the server. */
export const PaymentGatewayPermissions = {
  view: 'iam.payment-gateways.view',
  manage: 'iam.payment-gateways.manage',
  test: 'iam.payment-gateways.test',
  delete: 'iam.payment-gateways.delete',
} as const;
