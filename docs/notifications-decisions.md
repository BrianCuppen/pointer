# Notifications: decisions (agreed 2026-11-26)

Approved by Zain. This is the spec for the emails PR.

## Rules
- Email only in v1. No SMS or WhatsApp in v1.
- Every message is triggered by a booking state change. Payment-driven messages are sent from the idempotent Stripe webhook handler, so each message is sent at most once per booking and event type (keep a sent-notifications record keyed by booking id + message type).
- Nothing is sent for `pending_payment` holds, except the "payment failed / slot released" email.
- All times in emails are shown in the shop's IANA time zone.
- Dutch first. All subjects and bodies live in resource files (nl now; fr/en later).
- Store as little as possible: customer name, email, phone (optional). Log message type, recipient and time, not the email body.

## To the shop owner
| Message | Trigger | Default |
|---|---|---|
| New booking | booking -> `confirmed` (deposit paid) | on, instant |
| Cancellation by customer | booking -> `cancelled` by customer, says whether the deposit is refunded or kept | on, instant |
| Daily overview | 07:00 shop local time, today's appointments; skip if none | off |

Owner setting: instant emails, daily overview, both, or none. Default: instant on, overview off.

## To the customer
| Message | Trigger | Content |
|---|---|---|
| Confirmation | booking -> `confirmed` | service, date/time, shop address, deposit paid, no-show and free-cancellation rules, cancel link, .ics attachment |
| Reminder | X hours before start, default 24, shop can set 2-48 | date/time, address, cancel link only if still inside the free-cancellation window |
| Cancellation confirmation | booking -> `cancelled` (by customer or shop) | refund status |
| Payment failed / slot released | payment failed or hold expired | link to book again |
| No-show notice | booking -> `no_show` | deposit kept under the shop's rules; default on |

Reminder is a scheduled background job; it must skip cancelled bookings and must not double-send after restarts.

## Provider
- Brevo (EU company, EU data storage) via its transactional API or SMTP. API key from environment variable. Free tier covers the pilots.
- Put sending behind an `IEmailSender` interface so the provider can be swapped and tests use a fake.
- Set up SPF/DKIM for the sending domain once the domain is chosen.

## Later (not v1)
- SMS reminders as a paid add-on: about €0.15 per SMS billed with the monthly invoice (cost roughly €0.07-0.10, verify current rates before launch). Offer when a pilot asks.
- WhatsApp: needs Meta business verification per shop; revisit after SMS.
- Web push for owners if email proves too noisy.

## Tests needed
- Webhook delivered twice -> one confirmation email.
- Out-of-order webhook events -> correct final message set.
- Reminder job: skips cancelled bookings, no double send, correct time across a DST change.
- Cancel link hidden after the free window closes.
