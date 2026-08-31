-- =================================================================================================
-- Point every organisation at Razorpay, using TEST credentials.
--
-- WHY THIS SCRIPT EXISTS. The donation flow needs a row in pay_gateway_accounts naming the
-- provider; without one, POST /api/public/donations/{reference}/payment-link answers
-- PAYMENT_GATEWAY_NOT_CONFIGURED and the donor never leaves the page. Nothing creates that row
-- automatically: PaymentDbSeeder.SeedTestGatewayAccountAsync would, but it is never called, and it
-- deliberately refuses to sweep the tenant table on its own. On a real deployment an operator
-- creates the row on the Gateway configuration screen. This is the same thing for a dev box, where
-- clicking through the screen for each seeded organisation is the slow way to get to a test payment.
--
-- WHAT MAKES IT SAFE. The row holds no credential - only the NAME of the configuration section the
-- credentials live in ("Razorpay", which docker-compose fills from RAZORPAY_KEY_ID and
-- RAZORPAY_KEY_SECRET in .env). Point it at test keys and no real money can move; the database
-- itself contains nothing worth stealing either way.
--
-- WHY is_test_mode IS FALSE, WHICH LOOKS WRONG AND IS NOT.
--   is_test_mode marks a SANDBOX ROW, not a test key. GatewayAccountRepository.GetActiveForTenantAsync
--   filters on `IsActive && !IsTestMode` on purpose, so that a sandbox row can sit beside the live
--   one without a real donor ever being routed to it - which means a row marked is_test_mode is
--   never chosen by the payment path at all, and a "test mode" row would take no test payment
--   either. Test versus live is decided by the KEYS, and rzp_test_ keys move no real money.
--   Put a rzp_live_ key in .env and this same row is live. That is the one thing to check before
--   running this anywhere that is not a development machine.
--
-- USAGE
--   docker compose exec -T ydot-postgres \
--     psql -U postgres -d ydotphaseupdated < docker/sql/razorpay-test-gateway.sql
--
-- It is idempotent: run it again after adding an organisation and only the new one is affected.
-- =================================================================================================

BEGIN;

INSERT INTO pay_gateway_accounts (
    id,
    tenant_id,
    business_unit_id,
    gateway_name,
    merchant_id,
    api_key_reference,
    webhook_secret_reference,
    is_test_mode,
    is_active,
    settlement_currency_code,
    return_url,
    webhook_url,
    payment_link_validity_minutes,
    enabled_methods,
    notes,
    created_at_utc,
    created_by_user_id,
    version
)
SELECT
    gen_random_uuid(),
    tenant.id,
    tenant.business_unit_id,

    -- Must match RazorpayGateway.ProviderName exactly, or PaymentGatewayRouter falls through to
    -- HostedCheckoutGateway, which speaks a shape Razorpay has never implemented and 404s.
    'Razorpay',

    -- Recognisable at a glance as a development row, and unique per (gateway_name, merchant_id)
    -- as the schema requires.
    'RZP-TEST-' || UPPER(LEFT(REPLACE(tenant.id::text, '-', ''), 12)),

    -- The NAME of the configuration section, not a credential. Resolves to
    -- PaymentGateways:Razorpay:{ApiKey,BaseUrl,WebhookSecret}.
    'Razorpay',

    -- NULL means "the webhook secret is in the same section as the API key", which is what
    -- docker-compose supplies. A separate reference is only for providers that rotate it apart.
    NULL,

    -- Not a sandbox row. See the note at the top - the keys decide test versus live.
    FALSE,

    TRUE,

    -- MUST EQUAL THE CURRENCY THE INTENT IS RAISED IN or the payment link is refused before it is
    -- created: a merchant account cannot be paid out in a currency it does not settle.
    'INR',

    NULL,

    -- No webhook URL on a dev box: Razorpay cannot reach a machine that is not on the internet.
    -- The donation screen still confirms the payment, because the verify poll asks Razorpay
    -- directly rather than waiting to be told. Set this to
    -- https://<public host>/pay-api/webhooks/Razorpay anywhere Razorpay can actually call.
    NULL,

    60,
    'card,netbanking,upi,wallet',
    'Development row created by docker/sql/razorpay-test-gateway.sql. Holds no credentials; the '
        || 'keys come from RAZORPAY_KEY_ID and RAZORPAY_KEY_SECRET in .env. Use test keys only.',

    NOW(),
    '00000000-0000-0000-0000-000000000000'::uuid,
    1
FROM iam_tenants AS tenant
WHERE NOT EXISTS (
    -- The unique index is on (tenant_id, gateway_name, is_test_mode); matching it here is what
    -- makes a re-run a no-op instead of a constraint violation.
    SELECT 1
    FROM pay_gateway_accounts AS existing
    WHERE existing.tenant_id = tenant.id
      AND existing.gateway_name = 'Razorpay'
      AND existing.is_test_mode = FALSE
);

-- What the payment path will actually pick up. An organisation missing from this list has no
-- active non-test account and its donations will be refused.
SELECT
    tenant.name              AS organisation,
    account.gateway_name,
    account.merchant_id,
    account.api_key_reference,
    account.settlement_currency_code,
    account.is_active,
    account.is_test_mode
FROM pay_gateway_accounts AS account
JOIN iam_tenants AS tenant ON tenant.id = account.tenant_id
ORDER BY tenant.name, account.gateway_name;

COMMIT;
