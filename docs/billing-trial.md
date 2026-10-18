# Pro trial and conversion

The trial uses the same Stripe Checkout subscription as a paid purchase. The customer chooses a configured monthly or annual price and adds a payment method in Checkout. Stripe creates one subscription with a 14-day trial; the chosen price is charged when the trial ends. Cancelling from the billing portal before then prevents that charge. Stripe is configured to cancel at trial end if no payment method is available.

The trial return page reports the confirmed end date when the webhook has arrived. Pro access is granted only from the server-side Stripe entitlement. If the webhook is delayed, the page reports that confirmation is pending. When a trial expires or is cancelled, the existing workspace remains stored and uses Free plan limits.

The server records trial use from subscription webhooks and reconciles it with Stripe subscription history before another trial can be created. Repeated trial requests for the same customer reuse the existing trial Checkout session, including its original price, even if the customer changes the monthly/annual selection. Trial requests share a Stripe idempotency key across prices to prevent simultaneous requests from creating separate trial sessions. Paid Checkout requests continue to match the selected price.

The server saves the confirmed subscription's renewal interval from Stripe and includes it in the billing summary. Trial renewal wording uses that interval independently of the pricing toggle, including after a fresh page load. If the interval is not yet known, the wording refers to the subscription charge without guessing its cadence.
