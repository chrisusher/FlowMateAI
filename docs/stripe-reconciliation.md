# Stripe entitlement reconciliation

The code for #24 treats a webhook as a request to reconcile a customer, rather than applying the subscription snapshot embedded in the event. Stripe does not guarantee [delivery order](https://docs.stripe.com/webhooks#event-ordering), and can deliver an event more than once.

## Processing and concurrency

1. Verify the signature over the raw payload and enforce the five-minute timestamp tolerance. Invalid signatures or malformed event envelopes return `400`.
2. Look up the event ID. An already recorded event returns successfully without further provider calls or entitlement writes.
3. For subscription, invoice payment, and Checkout completion events, retrieve the current Stripe customer's `metadata.user_id`. Reconcile only when that account's stored customer mapping matches. An obsolete customer or an unrelated Stripe customer cannot rebind an account; subscription snapshot metadata cannot select another account.
4. Read the entitlement and its Cosmos ETag **before** listing the customer's current subscriptions. Read every page with `status=all`. Prefer the newest non-terminal subscription; otherwise retain the newest ended subscription's status. `active` and `trialing` grant Pro, following the existing policy. Preserve trial usage across all subscriptions and preserve monthly prompt usage.
5. Conditionally replace the entitlement using the read ETag. A competing reconciliation or prompt reservation makes the write fail. Reload both the entitlement and Stripe state before retrying, up to eight attempts. Even an unchanged entitlement is written conditionally so a slower worker cannot apply an older response after it.
6. Record the event ID after reconciliation succeeds. Only a duplicate-insert conflict is ignored. Provider errors, storage failures, and exhausted concurrency retries propagate as a failed request, allowing Stripe to retry.

Entitlement replacement and the event marker use separate containers. A failure between these writes deliberately leaves the event unacknowledged. Replay repeats a current-state reconciliation; it never creates a charge, subscription, or notification. Do not add non-idempotent side effects between these writes without a durable outbox or an equivalent atomic design.

## Missed deliveries and operation

An authenticated billing-summary read reconciles an already linked customer before returning status, limits, interval, and period end. The pricing page's refresh action and a fresh session therefore recover missed trial, renewal, payment-failure, cancellation, and interval-change events. Checkout's existing Stripe check uses the same reconciliation path. Both legacy subscription-level and current item-level period-end fields are supported.

This is reconciliation on access, not a scheduled sweep of dormant accounts. A Stripe outage makes refresh fail with the existing retry feedback and leaves the persisted entitlement unchanged. Quota and workspace enforcement continue to read persisted entitlements; a dormant account is updated by the next successful webhook or billing refresh.

Register snapshot events at `/api/v1/webhooks/stripe`: `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`, `customer.subscription.paused`, `customer.subscription.resumed`, `invoice.paid`, `invoice.payment_failed`, `checkout.session.completed`, and `checkout.session.async_payment_succeeded`. Additional `customer.subscription.*` snapshot events also trigger reconciliation. Keep secrets and customer references private.

## Verification

`StripeReconciliationTests` exercises the production billing service with signed deliveries, a deterministic Stripe HTTP double, and a revision-aware repository double. It covers duplicates, concurrent duplicates, delayed/out-of-order events, old subscriptions, missed-delivery refresh, pagination, customer isolation, provider failures, partial persistence failure, trial/payment/cancellation states, and interval changes. `BillingRepositoryTests` verifies the production EF write's ETag, full-document update, conflict handling, failure propagation, and detachment using a recording context. These tests make no Stripe payments and do not require a Cosmos emulator.

Run the service regressions with `dotnet test --project Tests/Services.Tests/Services.Tests.csproj`. For environments where the test runner's named pipes are restricted, build the project and run `dotnet Tests/Services.Tests/bin/Debug/net10.0/Services.Tests.dll` directly.

Before launch, verify signed deliveries and replay on disposable sandbox accounts against the deployed Function App and Cosmos, including a missed delivery followed by billing refresh. Live endpoint registration/delivery remains part of #23. The first real payment and its account-to-entitlement reconciliation must still be verified privately and recorded as a redacted result in #2; deterministic code tests do not establish that live acceptance criterion.
