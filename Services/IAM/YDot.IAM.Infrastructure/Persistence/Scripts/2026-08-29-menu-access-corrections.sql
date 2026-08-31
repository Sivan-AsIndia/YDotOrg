-- =====================================================================================
-- YDot IAM - navigation access corrections
-- Target   : PostgreSQL (IAM database)
-- Date     : 2026-08-29
--
-- WHAT THIS IS FOR. The IAM seeder already reconciles iam_menu_definitions and
-- iam_tenant_menus against MenuCatalogue.cs on every service start, so a normal
-- deployment applies both corrections below with no SQL at all. This script exists for
-- an environment that cannot be restarted or redeployed yet, and for anybody who wants
-- to see exactly what the reconciliation will do before it runs.
--
-- IT IS IDEMPOTENT. Every statement is a conditional UPDATE or a DELETE of rows that
-- should not exist; running it twice changes nothing the second time. Running it and
-- THEN restarting the service is also safe - the seeder reaches the same end state.
--
-- Correction 1 : ADMINISTRATION heading no longer requires IAM.View.
--                A child whose parent is filtered out is never reached by the tree
--                builder, so the heading's gate was hiding ADMIN_MY_SECURITY - which
--                carries no permission and is marked mandatory precisely because every
--                signed-in person must be able to change their own password and manage
--                their second factor. DONOR_PORTAL_USER, the one seeded role without
--                IAM.View, could not reach My Security at all.
--
-- Correction 2 : WS_GLOBAL_SEARCH is switched off by default.
--                The component injects no service and renders invented records, and it
--                carried no permission code - so it was the only node in the catalogue
--                that appeared in EVERY sidebar, Standard User and Donor Portal User
--                included.
-- =====================================================================================

BEGIN;

-- -------------------------------------------------------------------------------------
-- 1. Administration heading: drop the IAM.View requirement.
--     "version" is an EF concurrency token, so it is incremented by hand here exactly as
--     SaveChanges would.
-- -------------------------------------------------------------------------------------
UPDATE iam_menu_definitions
SET required_permission_code = NULL,
    updated_at_utc           = now(),
    version                  = version + 1
WHERE code = 'ADMINISTRATION'
  AND required_permission_code IS NOT NULL;

-- -------------------------------------------------------------------------------------
-- 2. Global Search: no longer enabled by default.
-- -------------------------------------------------------------------------------------
UPDATE iam_menu_definitions
SET is_enabled_by_default = FALSE,
    updated_at_utc        = now(),
    version               = version + 1
WHERE code = 'WS_GLOBAL_SEARCH'
  AND is_enabled_by_default IS DISTINCT FROM FALSE;

-- -------------------------------------------------------------------------------------
-- 3. Remove the per-Organisation rows that would otherwise keep Global Search visible.
--
--     THIS STEP IS THE ONE THAT ACTUALLY HIDES IT. MenuBuilderService.IsEnabledForTenant
--     consults iam_tenant_menus FIRST and only falls back to is_enabled_by_default when
--     no row exists. Every Organisation provisioned while the node was enabled holds a
--     row saying "visible", so step 2 alone would change nothing for them.
--
--     ONLY SYSTEM-GENERATED ROWS ARE REMOVED, matching ReconcileTenantMenusAsync
--     exactly. A row an operator created deliberately from Menu Mapping is left alone -
--     that is somebody's explicit decision, and this script does not overrule it.
--     Use the SELECT in the verification block below to find any that remain.
-- -------------------------------------------------------------------------------------
DELETE FROM iam_tenant_menus tm
USING iam_menu_definitions md
WHERE tm.menu_definition_id = md.id
  AND md.code = 'WS_GLOBAL_SEARCH'
  AND tm.is_system_generated = TRUE;

-- -------------------------------------------------------------------------------------
-- 4. Role-level mappings for the same node, if any were ever written.
--     Harmless to leave, but they refer to a node nobody should now be offered.
-- -------------------------------------------------------------------------------------
DELETE FROM iam_role_menus rm
USING iam_menu_definitions md
WHERE rm.menu_definition_id = md.id
  AND md.code = 'WS_GLOBAL_SEARCH';

COMMIT;

-- =====================================================================================
-- VERIFICATION. Run after the transaction commits; all three should read as described.
-- =====================================================================================

-- Expect exactly one row, with required_permission_code NULL.
SELECT code, name, required_permission_code, is_enabled_by_default, is_mandatory
FROM iam_menu_definitions
WHERE code IN ('ADMINISTRATION', 'ADMIN_MY_SECURITY', 'WS_GLOBAL_SEARCH')
ORDER BY code;

-- Expect ZERO rows. Anything returned is an operator-created override that survived
-- step 3 on purpose - decide per Organisation whether to keep it.
SELECT t.code AS organisation, md.code AS menu, tm.is_enabled, tm.is_system_generated
FROM iam_tenant_menus tm
JOIN iam_menu_definitions md ON md.id = tm.menu_definition_id
JOIN iam_tenants t           ON t.id = tm.tenant_id
WHERE md.code = 'WS_GLOBAL_SEARCH';

-- Sanity check on the fix that matters: the Donor Portal User role, and the My Security
-- node it could not previously reach. The node requires no permission; the heading above
-- it now requires none either, so the pair is reachable by any authenticated user.
SELECT r.code AS role_code, r.name, COUNT(rp.id) AS granted_permissions
FROM iam_roles r
LEFT JOIN iam_role_permissions rp ON rp.role_id = r.id AND rp.is_denied = FALSE
WHERE r.code = 'DONOR_PORTAL_USER'
GROUP BY r.code, r.name;
