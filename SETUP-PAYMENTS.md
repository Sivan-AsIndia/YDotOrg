# Getting the payment module working

For anyone bringing the stack up on a fresh machine with Docker Desktop.

**You need two files: `docker-compose.yml` and `.env`.** That is the whole list. If payments do not
work, it is almost certainly the `.env`.

---

## 1. Do not start from `.env.example`

`.env.example` ships with these **deliberately empty** — they are credentials and do not belong in
a committed file:

```
RAZORPAY_KEY_ID=
RAZORPAY_KEY_SECRET=
RAZORPAY_WEBHOOK_SECRET=
JWT_SIGNING_KEY=
IAM_SMTP_USERNAME=
IAM_SMTP_PASSWORD=
IAM_MAIL_FROM=
```

Copying `.env.example` to `.env` is the natural move and it is the one that breaks payments,
because the failure does not look like a missing credential. Compose builds the key as:

```yaml
PaymentGateways__Razorpay__ApiKey: "${RAZORPAY_KEY_ID:-}:${RAZORPAY_KEY_SECRET:-}"
```

With both empty that resolves to `":"` — which is **not blank**, so it sails past any
"is a credential set?" check, goes out to Razorpay, and comes back **401**. You get a gateway
error rather than a configuration error, which sends you looking in the wrong place.

**Get the real `.env` from whoever owns the environment.** At minimum:

- `RAZORPAY_KEY_ID` — a test key, `rzp_test_…`
- `RAZORPAY_KEY_SECRET`
- `JWT_SIGNING_KEY` — empty here breaks sign-in entirely, long before you reach payments

> **Test keys only.** `is_test_mode` on a gateway row means "sandbox row", *not* "test key" — the
> repository filters those out, so a row marked test mode is never selected and would take no
> payment at all. Test versus live is decided purely by the key prefix. Put an `rzp_live_` key in
> `.env` and the same row moves real money.

---

## 2. Bring it up

Put `docker-compose.yml` and `.env` in a folder together, open a terminal there, and:

```bash
# Pull the published images (9.1.3 or later - see the version note below)
docker compose pull

# Start everything
docker compose up -d

# Watch it come up; all eight should reach (healthy)
docker compose ps
```

Useful afterwards:

```bash
docker compose logs -f ydot-pay     # follow the payments service
docker compose restart ydot-pay     # force a re-check of gateway accounts
docker compose down                 # stop, keep the data
docker compose down -v              # stop and DELETE the database - see the warning below
```

> **`down -v` destroys the database, and it reaches further than the folder you run it in.** The
> compose project is named `ydot`, so every copy of this file shares one set of volumes: a
> throwaway copy in another folder is *not* isolated, and `down -v` there deletes the real stack's
> data. If you want a scratch copy, give it its own volumes by putting this in its `.env`:
>
> ```
> COMPOSE_PROJECT_NAME=ydot-scratch
> ```

> **Use 9.1.3 or later.** Everything in this document depends on it. `9.1.1` predates the gateway
> seeding entirely, and `9.1.2` only seeds once at startup - which loses a race against IAM on a
> first run, so a brand-new stack ends up with no gateway account. Both fail with
> `PAYMENT_GATEWAY_NOT_CONFIGURED` no matter how the `.env` is filled in. If payments fail, check
> what you are actually running first:
>
> ```bash
> docker inspect ydot-pay --format '{{.Config.Image}}'
> ```
>
> If that says anything below `9.1.3`, run `docker compose pull` and `docker compose up -d` again.

Ports: UI `6700` · IAM `6702` · CAM `6704` · DON `6706` · PAY `6708` · Postgres `6710` ·
Seq `6711` · MinIO `6712`/`6713`.

Reach the app on a tenant host — `http://ten1.localhost:6700` — not plain `localhost`. The
`.localhost` TLD resolves to `127.0.0.1` by itself; no hosts-file entry needed.

Test card `4111 1111 1111 1111`, any future expiry, any CVV.

---

## 3. The gateway account is created for you

A donation needs a row in `pay_gateway_accounts` naming the provider. Without one, every payment is
refused with `PAYMENT_GATEWAY_NOT_CONFIGURED` and the donor never leaves the page.

PAY creates that row itself — one active Razorpay account per organisation, idempotent, driven by
`PAY_SEED_GATEWAY_ACCOUNTS=true` in `.env`. **The row holds no credentials**: it stores
the *name* of the config section (`Razorpay`), and the key is read from the environment at the
moment of use.

Check it landed:

```bash
docker compose exec -T ydot-postgres psql -U postgres -d ydotphaseupdated \
  -c "select t.name, a.gateway_name, a.is_active
      from pay_gateway_accounts a join iam_tenants t on t.id = a.tenant_id;"
```

Two guards you should know about, because both are silent by design:

- **It refuses an `rzp_live_` key**, whatever `PAY_SEED_GATEWAY_ACCOUNTS` says. Auto-configuring
  every organisation is fine when the worst case is a declined test charge; it is not fine when
  the keys move real money. You will see a warning in `docker logs ydot-pay`.
- **It skips when the key is unusable** (including the `":"` case from section 1) rather than
  creating an account that cannot work.

**Organisations created later are picked up on their own**, within about two minutes. The seeding
runs as a background service rather than once at startup, so it also survives a first run where
PAY reaches the database before IAM has created it - you will see this in the log, and it resolves
itself in seconds:

```
[09:33:28 INF] No organisations exist yet, so no gateway accounts have been seeded. Watching for them.
[09:33:33 INF] Seeded 2 Razorpay gateway account(s) from configuration.
```

Turn `PAY_SEED_GATEWAY_ACCOUNTS=false` once the per-organisation Payment Gateway configuration
screen exists, and configure accounts there instead.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `PAYMENT_GATEWAY_NOT_CONFIGURED`, donor never leaves the page | No row in `pay_gateway_accounts` | Check `docker logs ydot-pay` for the seeder's warning — usually empty keys |
| `GATEWAY_401` in `docker logs ydot-pay` | Razorpay keys empty or wrong | Fill `RAZORPAY_KEY_ID` / `RAZORPAY_KEY_SECRET`, then `docker compose up -d ydot-pay` |
| Organisation created just now cannot take a payment | Seeding sweeps every two minutes | Wait, or `docker compose restart ydot-pay` to force it |
| Cannot sign in at all | `JWT_SIGNING_KEY` empty | Fill it, recreate the containers |
| Payment link refused before creation | Intent currency ≠ gateway `settlement_currency_code` (`INR`) | Raise the donation in INR, or change `PAY_SEED_GATEWAY_SETTLEMENT_CURRENCY` |
| Receipt issued but no document, `UnauthorizedAccessException` on `/var/ydot/receipts` | Named volume created `root`-owned; container runs as uid 1654 | Rebuild the PAY image — fix is in `Services/PAY/YDot.PAY.Api/Dockerfile`. If the volume already holds root-owned content, recreate it |
| Receipt e-mail not delivered, `5.7.1 Outbound sending is disabled` | Mail account has outbound sending switched off | A mail-provider setting, not a code issue. The receipt is still validly issued and shows in the undelivered queue |

---

## If you are building rather than pulling

Every service declares `build: context: .`. Unless you can pull `sivan67906/ydot-*:9.1.3` from
Docker Hub, Compose builds from source and you need **the whole repository**. Postgres is now the
stock `postgres:17-alpine` and needs nothing.

To publish a new version after a code change, bump the tag in both compose files and:

```bash
docker compose build ydot-iam ydot-cam ydot-don ydot-pay ydot-ui
for s in iam cam don pay ui; do docker push "sivan67906/ydot-$s:<new-tag>"; done
```

Always bump the tag rather than re-pushing an existing one — anyone holding the old tag locally
keeps it, and Compose will not re-pull a tag it already has.

---

## Two things that are *not* problems locally

**No webhook is registered, and none is needed.** Razorpay cannot call a machine that is not on the
internet, and the flow does not depend on it: the donor's browser is redirected back to
`/give/result`, and that page asks our server to verify. **Verification is a pull** — our server
calls Razorpay's API — so the outcome is confirmed with no inbound connectivity at any point.

A webhook is still worth registering in production, because it is the only thing that catches the
donor who pays and then closes the tab. Point it at
`https://<public host>/pay-api/webhooks/Razorpay` and put the same secret in
`RAZORPAY_WEBHOOK_SECRET` — without a matching secret the signature check fails and events are
stored but never acted on.

**`is_test_mode = false` on the seeded row is correct.** See the note in section 1.
