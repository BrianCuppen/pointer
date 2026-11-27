# Pointer

Appointment booking for small local shops (barbershops and salons first) in Belgium and the Netherlands. A concept project: a complete, working MVP built to show how such a system can be done properly, not a commercial product.

Each shop gets a hosted booking page and a one-line embed for its own website. Customers pick an open slot and pay a deposit with Bancontact, iDEAL or card. The shop decides what happens to the deposit on a no-show. "Pointer" is a working name. The full spec and hard rules are in [CLAUDE.md](CLAUDE.md).

> Screenshots will be added here. The pages are intentionally unstyled: the visual design comes from Figma later.

## What it does

**For customers** (Dutch UI)
- Pick a service, a day and a time on `/{shop}`. Times are read live and shown in the shop's time zone.
- See the no-show and cancellation rules before paying, then pay the deposit through Stripe Checkout.
- Get a confirmation email with a calendar file, a reminder before the appointment, and a link to cancel. Cancelling inside the free window refunds the deposit automatically.

**For shop owners** (`/beheer`)
- A week calendar with every booking, its deposit status, and buttons to cancel, mark completed or mark as no-show.
- Services (duration, price, deposit), opening hours, days off, no-show policy, cancellation window, and which emails to receive (instant or a 07:00 daily overview).
- Customer data requests (GDPR): look up an email address, download everything as JSON, or erase it.
- An embed snippet for the shop's own website.

## How it works

| Concern | Approach |
| --- | --- |
| No double bookings | A PostgreSQL exclusion constraint (`EXCLUDE USING gist` on staff and the booking's time range, ignoring cancelled rows), so the database rejects overlaps even under concurrent requests. |
| Slot holds | A booking starts as `pending_payment` with a 30 minute hold. Expired holds are released before new ones are taken. |
| Money | Stripe Connect direct charges: the deposit goes straight to the shop's connected account, and the platform fee is taken as an application fee. The platform never holds customer money. |
| Webhooks | Idempotent and order-independent: each event id is stored once, rows are locked `FOR UPDATE`, and statuses only move forward. A payment that arrives after its hold expired either reclaims the slot or is refunded. |
| Emails | A transactional outbox: booking changes write `notifications` rows in the same transaction, and a background worker claims them with `SKIP LOCKED` and sends through Brevo. Each email is sent at most once per booking and type, and it is re-checked at send time. |
| Time | Everything is stored in UTC. Each shop has an IANA time zone, with tests around the spring and autumn clock changes. |
| Tenancy | One application for all shops. Composite foreign keys keep each shop's rows apart, and the admin always takes the shop from the sign-in cookie. |
| Privacy | Minimal customer data, export and erasure built in, email bodies never stored, and a [data processing agreement template](docs/verwerkersovereenkomst.md). |

## Tech stack

- .NET 8, ASP.NET Core Razor Pages, htmx (vendored) for the live time picker
- Entity Framework Core with PostgreSQL 16 (Npgsql, snake_case naming)
- Stripe.net (Connect, Checkout, webhooks), Brevo transactional email over HTTP
- xUnit tests against a real PostgreSQL database (no in-memory fakes for the data layer)
- Docker image, GitHub Actions CI (tests plus image build, published to GHCR from `main`)

## Try the demo

The quickest way needs only Docker:

```
git clone https://github.com/BrianCuppen/pointer.git
cd pointer
docker compose up --build
```

When the log says `Now listening`, open:

- Booking page: http://localhost:8080/kapper-demo
- Admin: http://localhost:8080/beheer, signing in as `demo@pointer.test` with password `demo-only-password`

The demo shop comes with services, a day off and a week of made-up bookings. Payments run in **demo mode**: instead of Stripe you land on a local "Demobetaling" page where you can pay, let the payment fail, or cancel, and the booking reacts exactly as it would to Stripe's webhooks. Emails are written to the log (`docker compose logs app`). Stop with Ctrl+C; `docker compose down -v` also deletes the demo data.

Without Docker: start PostgreSQL (`docker compose up db`, or your own), then

```
export ConnectionStrings__Default="Host=localhost;Database=pointer_dev;Username=postgres;Password=local-demo-only"
dotnet run --project src/Pointer.Web
```

and use http://localhost:5137 instead. Development uses the same demo shop and demo payments (see `src/Pointer.Web/appsettings.Development.json`). To try real Stripe test payments, set `Payments__Mode=stripe` and see [Stripe setup](#stripe-setup-test-mode).

To create a real shop instead: `POINTER_OWNER_PASSWORD=... dotnet run --project src/Pointer.Web -- create-shop <slug> "<name>" <owner email>`.

## Layout

```
src/Pointer.Core   Domain model, EF Core DbContext + migrations, booking logic (no web dependencies)
src/Pointer.Web    ASP.NET Core host: Razor Pages, endpoints, configuration
tests/Pointer.Tests  xUnit tests, run against a real PostgreSQL database
```

## Prerequisites

- .NET 8 SDK
- PostgreSQL 16 (or Docker: `docker compose up -d db`)

Configuration comes from environment variables. Nothing secret is committed.

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
| `Payments__Mode` | `stripe` (`demo` in Development and Docker Compose) | `demo` replaces Stripe with a local fake checkout. No money moves; never use it where real customers book |
| `Fees__PerDepositFeeCents` | `30` | Our application fee per paid deposit, in cents |
| `Notifications__PublicBaseUrl` | `http://localhost:5137` | Base URL for links in emails |
| `Notifications__WorkerEnabled` | `true` | Run the background job that sends emails and queues reminders |
| `Email__BrevoApiKey` | none | Brevo API key. Without it, emails are only logged (subject only) |
| `Email__FromAddress` / `Email__FromName` | `no-reply@example.com` / `Pointer` | Sender |
| `Stripe__SecretKey` | none | Platform secret key. `sk_test_...` everywhere except production |
| `Stripe__WebhookSecret` | none | Signing secret of the Connect webhook endpoint |

## Emails

Emails follow [docs/notifications-decisions.md](docs/notifications-decisions.md). Booking changes write rows to a `notifications` outbox in the same transaction; a background worker sends them through Brevo. Each message is sent at most once per booking and type, the body is never stored, and texts live in `src/Pointer.Core/Notifications/EmailTexts.resx` (Dutch).

## Stripe setup (test mode)

1. In the Stripe dashboard (test mode), enable Connect, and enable Bancontact, iDEAL and cards for connected accounts under Settings > Payment methods.
2. Add a webhook endpoint for **connected accounts** (Connect events) pointing at `https://<host>/webhooks/stripe`, with the events `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `checkout.session.expired` and `charge.refunded`.
3. Set `Stripe__SecretKey` and `Stripe__WebhookSecret` as environment variables.
4. Locally: `stripe listen --forward-connect-to localhost:5137/webhooks/stripe` prints a webhook secret to use.

Shop onboarding to Stripe (Express account links) is not built yet. Set a shop's Stripe account id (`acct_...`) under Instellingen in the admin.

## Not in this MVP

Multi-staff scheduling, SMS or WhatsApp reminders, Stripe onboarding from the admin, reporting, multiple locations, and French or English texts (the strings are already in resource files).
