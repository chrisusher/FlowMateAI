# Pro trial and conversion

The trial uses the same Stripe Checkout subscription as a paid purchase. The customer chooses a configured monthly or annual price and adds a payment method in Checkout. Stripe creates one subscription with a 14-day trial; the chosen price is charged when the trial ends. Canceling from the billing portal before then prevents that charge. Stripe is configured to cancel at trial end if no payment method is available.

The trial return page reports the confirmed end date when the webhook has arrived. Pro access is granted only from the server-side Stripe entitlement. If the webhook is delayed, the page reports that confirmation is pending. When a trial expires or is canceled, the existing workspace remains stored and uses Free plan limits.

The server records trial use from subscription webhooks and reconciles it with Stripe subscription history before another trial can be created. Repeated requests for the same customer and selected price reuse the open Checkout session; Stripe idempotency protects simultaneous session creation.
