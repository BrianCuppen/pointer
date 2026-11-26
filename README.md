# Pointer

Multi-tenant appointment booking for small local shops (barbershops and salons first) in Belgium and the Netherlands.

Each shop gets a hosted booking page and an embeddable button for its own website. Customers pick an open slot and pay a deposit; the shop decides what happens to the deposit on a no-show.

"Pointer" is a working name. See [CLAUDE.md](CLAUDE.md) for the spec, hard rules, architecture and MVP scope.

## Layout

```
src/Pointer.Core   Domain model, EF Core DbContext + migrations, booking logic (no web dependencies)
src/Pointer.Web    ASP.NET Core host: Razor Pages, endpoints, configuration
tests/Pointer.Tests  xUnit tests, run against a real PostgreSQL database
```

## Prerequisites

- .NET 8 SDK
- PostgreSQL 16 (or Docker: `cp .env.example .env`, set a password, then `docker compose up -d`)

## Run locally

Configuration comes from environment variables. Nothing secret is committed.

```
export ConnectionStrings__Default="Host=localhost;Database=pointer_dev;Username=postgres;Password=<your local password>"
dotnet run --project src/Pointer.Web
```

In Development the app applies migrations on startup. Open http://localhost:5137 and http://localhost:5137/healthz.

## Tests

The tests create a throwaway database per run, apply the real migrations, and drop it afterwards.

```
export POINTER_TEST_DB="Host=localhost;Username=postgres;Password=<your local password>"
dotnet test
```

## Migrations

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/Pointer.Core --startup-project src/Pointer.Web --output-dir Data/Migrations
```

## Container

```
docker build -t pointer .
docker run -p 8080:8080 -e ConnectionStrings__Default="..." -e Database__MigrateOnStartup=true pointer
```

CI (`.github/workflows/ci.yml`) builds and tests every push and pull request against PostgreSQL, builds the container image, and publishes it to `ghcr.io/<owner>/pointer` on pushes to `main`.

## Configuration

| Key | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings__Default` | none (required) | PostgreSQL connection string |
| `Database__MigrateOnStartup` | `false` (`true` in Development) | Apply EF Core migrations when the app starts |
| `Bookings__PaymentHoldMinutes` | `30` | How long a slot is held while the customer pays the deposit (Stripe Checkout needs at least 30) |
| `Availability__SlotStepMinutes` | `15` | Distance between offered start times |
| `Availability__MinimumNoticeMinutes` | `60` | How soon a slot can start |
| `Availability__BookingHorizonDays` | `60` | How far ahead customers can book |
| `Fees__PerDepositFeeCents` | `30` | Our application fee per paid deposit, in cents |
| `Stripe__SecretKey` | none | Platform secret key. `sk_test_...` everywhere except production |
| `Stripe__WebhookSecret` | none | Signing secret of the Connect webhook endpoint |

## Stripe setup (test mode)

1. In the Stripe dashboard (test mode), enable Connect, and enable Bancontact, iDEAL and cards for connected accounts under Settings > Payment methods.
2. Add a webhook endpoint for **connected accounts** (Connect events) pointing at `https://<host>/webhooks/stripe`, with the events `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `checkout.session.expired` and `charge.refunded`.
3. Set `Stripe__SecretKey` and `Stripe__WebhookSecret` as environment variables.
4. Locally: `stripe listen --forward-connect-to localhost:5137/webhooks/stripe` prints a webhook secret to use.

Until shop onboarding exists in the admin, set a shop's `payment_account_id` (acct_...) by hand.
