# CLAUDE.md

Guidance for Claude when working in this repository.

## Project

A multi-tenant appointment booking service for small local shops (barbershops and salons first, any appointment-based business later). Each shop gets a hosted booking page and an embeddable button for its own website. Customers pick an open slot and pay a deposit. The shop decides what happens to the deposit on a no-show.

Working name: `Bookings` (rename when a product name is chosen).

## Business context

- Target: small shops in Belgium (Dutch-speaking first) that find the big booking platforms too complex or too expensive.
- Sales: direct and personal, selling to local shops I already deal with and setting the product up for them. The first goal is 5 paying pilot shops, not scale.
- Revenue: one-time setup fee plus a small monthly subscription per shop, plus a modest per-deposit fee that covers payment processing costs.
- Niche first: barbershops and salons. The data model stays generic (services, durations, staff, opening hours, slots) so other appointment businesses can be added later without a rewrite.

## Hard rules

1. **Never hold customer money.** All deposits go through Mollie Connect or Stripe Connect (split payments). The customer's payment goes to the shop's connected account and our fee is deducted automatically. Do not build any flow where funds land in our own account first.
2. **Bancontact must work**, along with iDEAL and cards, since this is for Belgium and the Netherlands.
3. **No double bookings.** Enforce this in the database (constraint or transaction with locking), not only in application code.
4. **Payment webhooks must be idempotent.** The same event can arrive more than once and out of order.
5. **No-show rule is set per shop.** Default: the shop keeps the deposit on a no-show. Free-cancellation window is configurable. Show the rules clearly to the customer before payment.
6. **GDPR:** we process the shop's customers' personal data. Keep stored data minimal, support deletion and export, and keep a data processing agreement template in `docs/`.
7. **Secrets** (API keys, webhook secrets, connection strings) come from environment variables or a secrets manager. Never commit them. Use test keys everywhere except production.

## Architecture (initial decisions)

- One multi-tenant web application, not one deployment per shop.
- Backend: ASP.NET Core Web API (.NET 8) with Entity Framework Core and PostgreSQL.
- Public booking page per shop at `{shop-slug}.<domain>` plus a one-line embeddable script or iframe for the shop's own site. Availability is always read live from the API, so the page is not purely static.
- Frontend: keep it simple. Server-rendered or a small SPA, decide before starting. No native app.
- Deployment: a single container on a small VPS, Fly.io or Railway. No Kubernetes until there is a real need for it.
- Reminders: email first (confirmation plus reminder). SMS or WhatsApp later as a paid add-on, since each message costs money.
- All times stored in UTC. Each shop has an IANA time zone, and the booking page displays in the shop's time zone. Test around DST changes.

## Core domain model (draft)

- `Shop`: slug, name, time zone, language, payment account id, no-show policy, cancellation window.
- `Service`: shop, name, duration, price, deposit amount.
- `Staff`: shop, name, working hours. Optional in v1, with a single implicit staff member as the default.
- `OpeningHours` and `TimeOff` (days off, holidays) per shop or staff member.
- `Booking`: service, staff, start and end (UTC), status (`pending_payment`, `confirmed`, `cancelled`, `completed`, `no_show`), customer contact details, payment reference.
- `Payment`: booking, provider, provider payment id, amount, platform fee, status.

## MVP scope

In:
1. Shop admin: services, durations, opening hours, days off.
2. Public booking page and embeddable button.
3. Deposit payment via Mollie or Stripe Connect, with a configurable no-show policy.
4. Email confirmation and reminder.
5. Basic calendar view for the owner, with the option to mark no-shows.

Out for now:
- Multi-staff scheduling, native mobile apps, SMS, loyalty programs, reporting dashboards, multi-location shops, any support for businesses outside Belgium and the Netherlands.

## Conventions

- Language for code, comments and commit messages: English. User-facing text is in Dutch, with the strings kept in resource files so French and English can be added.
- Keep controllers thin and put booking and availability logic in a testable service layer.
- Write tests for availability calculation, double-booking prevention, deposit and refund state transitions, and webhook handling. These are where bugs cost money.
- Prefer small, reviewable changes. Run the tests before saying something is done.
- Do not add new dependencies without a short reason.

## Commands (fill in once the project exists)

```
# build
dotnet build

# run tests
dotnet test

# run locally
dotnet run --project src/Bookings.Api

# database migrations
dotnet ef migrations add <Name> --project src/Bookings.Api
dotnet ef database update --project src/Bookings.Api
```

## Open questions

- Product name and domain.
- Mollie Connect or Stripe Connect (compare fees and Belgian payment method support before choosing).
- Frontend approach (server-rendered pages or a small SPA).
- Exact pricing: setup fee, monthly fee, per-deposit fee.
- Whether to reuse code from the existing barbershop booking sites or start clean.
