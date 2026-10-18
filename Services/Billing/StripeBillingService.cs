using System.Security.Cryptography;
using System.Text;
using ChrisUsher.Core.Shared.Currency;
using ChrisUsher.Core.Shared.Enums;
using Services.Database;
using Services.Repositories;

namespace Services.Billing;

public sealed class StripeBillingService(
    FlowMateEnvironment environment,
    IConfiguration configuration,
    IHttpClientFactory clients,
    IBillingRepository repository) : IBillingService
{
    private const string STRIPE_API = "https://api.stripe.com/v1/";

    public async Task<BillingSummary> GetSummaryAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entitlement = await ReconcileEntitlementAsync(userId, null, cancellationToken);
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var used = string.Equals(entitlement.PromptMonth, month, StringComparison.Ordinal) ? entitlement.PromptCount : 0;
        var limit = BillingPlanExtensions.ParseOrFree(entitlement.Plan) == BillingPlan.Pro ? 100 : 10;

        return new(entitlement.Plan, entitlement.SubscriptionStatus, entitlement.TrialUsed, entitlement.PeriodEndsAt, used, limit, entitlement.SubscriptionInterval);
    }

    public async Task<(bool Allowed, int Used, int Limit)> ConsumePromptAsync(string userId, string month, CancellationToken cancellationToken = default)
    {
        return await repository.TryConsumePromptAsync(userId, month, freeLimit: 10, proLimit: 100, cancellationToken: cancellationToken);
    }

    public Task ReleasePromptAsync(string userId, string month, CancellationToken cancellationToken = default) =>
        repository.ReleasePromptAsync(userId, month, cancellationToken);

    public async Task<IReadOnlyList<BillingPrice>> GetPricesAsync(CancellationToken cancellationToken = default)
    {
        var prices = new List<BillingPrice>();

        foreach (var (id, interval) in new[]
        {
            (configuration["Stripe:MonthlyPriceId"], "month"),
            (configuration["Stripe:AnnualPriceId"], "year")
        })
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }
            using var response = await SendAsync(HttpMethod.Get, $"prices/{Uri.EscapeDataString(id)}", null, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));

            var root = document.RootElement;
            var currency = root.GetProperty("currency").GetString() ?? "gbp";
            var amount = root.GetProperty("unit_amount").GetInt64();
            var actualInterval = root.GetProperty("recurring").GetProperty("interval").GetString() ?? interval;

            var currencyCode = Enum.TryParse<CurrencyCode>(currency, true, out var parsedCurrency)
                ? parsedCurrency
                : CurrencyCode.Unknown;

            var display = CurrencyLogic.FormatPrice(amount / 100m, currencyCode);
            prices.Add(new(actualInterval, currency, amount, $"{display} / {(actualInterval == "year" ? "year" : "month")}"));
        }

        return prices;
    }

    public Task<BillingActionResponse> StartTrialAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default) =>
        CreateCheckoutAsync(userId, email, annual, cancellationToken, trial: true);

    public async Task<BillingActionResponse> CreateCheckoutAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default, bool trial = false)
    {
        var priceId = configuration[annual ? "Stripe:AnnualPriceId" : "Stripe:MonthlyPriceId"];
        var appBaseUrl = environment.GetWebAppBaseUrl();

        if (string.IsNullOrWhiteSpace(priceId) || string.IsNullOrWhiteSpace(appBaseUrl))
        {
            return Failure(BillingActionCode.BillingUnavailable, "Checkout is not configured yet.");
        }

        try
        {
            var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);

            if (IsEntitled(entitlement.SubscriptionStatus))
            {
                return await CreatePortalAsync(userId, cancellationToken);
            }

            if (trial && entitlement.TrialUsed)
            {
                return Failure(BillingActionCode.TrialUsed, "Your 14-day trial has already been used.");
            }

            var customer = await EnsureCustomerAsync(entitlement, userId, email, cancellationToken);
            entitlement = await ReconcileEntitlementAsync(userId, customer, cancellationToken);

            if (IsEntitled(entitlement.SubscriptionStatus))
            {
                return await CreatePortalAsync(userId, cancellationToken);
            }

            if (trial && entitlement.TrialUsed)
            {
                return Failure(BillingActionCode.TrialUsed, "Your 14-day trial has already been used.");
            }

            var existingCheckout = await GetExistingCheckoutResponseAsync(customer, priceId, trial, cancellationToken);

            if (existingCheckout is not null)
            {
                return existingCheckout;
            }

            var form = new Dictionary<string, string>
            {
                ["mode"] = "subscription",
                ["customer"] = customer,
                ["line_items[0][price]"] = priceId,
                ["line_items[0][quantity]"] = "1",
                ["success_url"] = appBaseUrl + (trial ? $"/pricing?checkout=trial-return&annual={annual.ToString().ToLowerInvariant()}" : "/pricing?checkout=return"),
                ["cancel_url"] = appBaseUrl + (trial ? $"/pricing?checkout=trial-cancelled&annual={annual.ToString().ToLowerInvariant()}" : "/pricing?checkout=cancelled"),
                ["subscription_data[metadata][user_id]"] = userId,
                ["metadata[flowmate_trial]"] = trial ? "true" : "false"
            };

            if (trial)
            {
                form["subscription_data[trial_period_days]"] = "14";
                form["subscription_data[trial_settings][end_behavior][missing_payment_method]"] = "cancel";
            }

            using var response = await SendAsync(HttpMethod.Post, "checkout/sessions", form, CheckoutIdempotencyKey(userId), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(BillingActionCode.StripeError, StripeError(body));
            }
            using var session = JsonDocument.Parse(body);

            return new(true, session.RootElement.GetProperty("url").GetString(), null, null);
        }
        catch (HttpRequestException)
        {
            return Failure(BillingActionCode.StripeUnavailable, "Stripe could not be reached. Try again in a moment.");
        }
    }

    private async Task<BillingEntitlementDocument> ReconcileEntitlementAsync(string userId, string? expectedCustomer, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            // Read the revision BEFORE querying Stripe so an older provider response cannot win a later write.
            var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);
            var customer = entitlement.StripeCustomerId;

            if (string.IsNullOrWhiteSpace(customer)
                || (expectedCustomer is not null && !string.Equals(customer, expectedCustomer, StringComparison.Ordinal)))
            {
                return entitlement;
            }

            var revision = entitlement.ETag;
            var subscriptions = await GetStripeSubscriptionsAsync(customer, cancellationToken);
            var current = subscriptions
                .OrderBy(subscription => subscription.GetProperty("status").GetString() is "canceled" or "incomplete_expired" ? 1 : 0)
                .ThenByDescending(subscription => UnixTime(subscription, "created"))
                .ThenByDescending(subscription => subscription.GetProperty("id").GetString(), StringComparer.Ordinal)
                .Select(subscription => (JsonElement?)subscription).FirstOrDefault();

            entitlement.TrialUsed |= subscriptions.Any(subscription =>
                subscription.TryGetProperty("trial_start", out var trialStart) && trialStart.ValueKind == JsonValueKind.Number);

            if (current is { } subscription)
            {
                ApplySubscription(entitlement, subscription);
            }
            else
            {
                entitlement.StripeSubscriptionId = "";
                entitlement.SubscriptionStatus = "none";
                entitlement.Plan = BillingPlan.Free.ToString();
                entitlement.PeriodEndsAt = null;
                entitlement.SubscriptionInterval = null;
            }

            if (await repository.TrySaveEntitlementAsync(entitlement, revision, cancellationToken))
            {
                return entitlement;
            }
            // Even unchanged state is conditionally written, fencing slower workers with older Stripe responses.
        }

        throw new InvalidOperationException("Billing changed repeatedly. Retry the request.");
    }

    private async Task<List<JsonElement>> GetStripeSubscriptionsAsync(string customerId, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        string? cursor = null;

        do
        {
            var path = $"subscriptions?customer={Uri.EscapeDataString(customerId)}&status=all&limit=100"
                + (cursor is null ? "" : $"&starting_after={Uri.EscapeDataString(cursor)}");
            using var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            var next = new List<JsonElement>();

            foreach (var subscription in root.GetProperty("data").EnumerateArray())
            {
                if (!string.Equals(subscription.GetProperty("customer").GetString(), customerId, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(subscription.GetProperty("id").GetString())
                    || string.IsNullOrWhiteSpace(subscription.GetProperty("status").GetString()))
                {
                    throw new InvalidOperationException("Stripe returned an invalid subscription.");
                }

                next.Add(subscription.Clone());
            }

            result.AddRange(next);
            var hasMore = root.TryGetProperty("has_more", out var more) && more.GetBoolean();
            var nextCursor = next.Count > 0 ? next[^1].GetProperty("id").GetString() : null;

            if (hasMore && (nextCursor is null || nextCursor == cursor))
            {
                throw new InvalidOperationException("Stripe returned an incomplete subscription list.");
            }

            cursor = hasMore ? nextCursor : null;
        }
        while (cursor is not null);

        return result;
    }

    private async Task<BillingActionResponse?> GetExistingCheckoutResponseAsync(string customerId, string priceId, bool trial, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"checkout/sessions?customer={Uri.EscapeDataString(customerId)}&status=open&limit=100&expand%5B%5D=data.line_items", null, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(StripeError(await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));

        if (!document.RootElement.TryGetProperty("data", out var sessions) || sessions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        BillingActionResponse? existingCheckout = null;

        foreach (var session in sessions.EnumerateArray())
        {
            if (!session.TryGetProperty("mode", out var mode)
                || mode.ValueKind != JsonValueKind.String
                || mode.GetString() != "subscription")
            {
                continue;
            }

            var sessionIsTrial = session.TryGetProperty("metadata", out var metadata)
                && metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty("flowmate_trial", out var sessionTrial)
                && sessionTrial.ValueKind == JsonValueKind.String
                && sessionTrial.GetString() == "true";

            if (sessionIsTrial != trial
                || !session.TryGetProperty("url", out var url)
                || url.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(url.GetString()))
            {
                return CheckoutConflict();
            }

            if (trial)
            {
                // A trial keeps its original price, even when the pricing toggle changes.
                existingCheckout = new(true, url.GetString(), null, null);

                continue;
            }

            if (!session.TryGetProperty("line_items", out var lineItems)
                || lineItems.ValueKind != JsonValueKind.Object
                || !lineItems.TryGetProperty("data", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return CheckoutConflict();
            }

            var hasRequestedPrice = items.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("price", out var price)
                && (price.ValueKind == JsonValueKind.String
                    ? string.Equals(price.GetString(), priceId, StringComparison.Ordinal)
                    : price.ValueKind == JsonValueKind.Object
                        && price.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                        && string.Equals(id.GetString(), priceId, StringComparison.Ordinal)));

            if (!hasRequestedPrice)
            {
                return CheckoutConflict();
            }

            existingCheckout = new(true, url.GetString(), null, null);
        }

        return existingCheckout;
    }

    private static BillingActionResponse CheckoutConflict() => Failure(BillingActionCode.CheckoutInProgress,
        "You already have an open checkout with a different billing selection. Return to that selection to finish it, or wait for the checkout to expire before starting another subscription.");

    private static string CheckoutIdempotencyKey(string userId)
    {
        // All subscription requests share a key to prevent competing paid and trial sessions.
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();
        var utcDay = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        return $"flowmate-checkout-{requestHash}-{utcDay}";
    }

    public async Task<BillingActionResponse> CreatePortalAsync(string userId, CancellationToken cancellationToken = default)
    {
        var appBaseUrl = environment.GetWebAppBaseUrl();

        if (string.IsNullOrWhiteSpace(appBaseUrl))
        {
            return Failure(BillingActionCode.BillingUnavailable, "Billing is not configured yet.");
        }
        var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);

        if (string.IsNullOrWhiteSpace(entitlement.StripeCustomerId))
        {
            return Failure(BillingActionCode.CustomerMissing, "No billing account is linked yet.");
        }

        try
        {
            using var response = await SendAsync(HttpMethod.Post, "billing_portal/sessions", new Dictionary<string, string>
            {
                ["customer"] = entitlement.StripeCustomerId,
                ["return_url"] = appBaseUrl + "/settings"
            }, null, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(BillingActionCode.StripeError, StripeError(body));
            }
            using var session = JsonDocument.Parse(body);

            return new(true, session.RootElement.GetProperty("url").GetString(), null, null);
        }
        catch (HttpRequestException)
        {
            return Failure(BillingActionCode.StripeUnavailable, "Stripe could not be reached. Try again in a moment.");
        }
    }

    public async Task<bool> ProcessWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default)
    {
        if (!VerifySignature(payload, signature, configuration["Stripe:WebhookSecret"]))
        {
            return false;
        }

        JsonDocument eventDoc;

        try
        {
            eventDoc = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        using (eventDoc)
        {
            var root = eventDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())
                || !root.TryGetProperty("type", out var eventType) || eventType.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var eventId = id.GetString()!;

            if (await repository.HasProcessedEventAsync(eventId, cancellationToken))
            {
                return true;
            }

            var type = eventType.GetString()!;

            if (type.StartsWith("customer.subscription.", StringComparison.Ordinal)
                || type is "invoice.paid" or "invoice.payment_failed" or "checkout.session.completed" or "checkout.session.async_payment_succeeded")
            {
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                    || !data.TryGetProperty("object", out var item) || item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("customer", out var customer) || customer.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(customer.GetString()))
                {
                    return false;
                }

                var customerId = customer.GetString()!;
                using var response = await SendAsync(HttpMethod.Get, $"customers/{Uri.EscapeDataString(customerId)}", null, null, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var customerDoc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                var customerRoot = customerDoc.RootElement;

                if (customerRoot.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                    && metadata.TryGetProperty("user_id", out var user) && user.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(user.GetString()))
                {
                    // Current customer metadata and the stored mapping must agree; obsolete customers cannot rebind an account.
                    await ReconcileEntitlementAsync(user.GetString()!, customerId, cancellationToken);
                }
            }

            // Reconciliation has no external side effects. If this write fails, retrying safely reconciles again.
            await repository.MarkEventProcessedAsync(eventId, cancellationToken);

            return true;
        }
    }

    private static void ApplySubscription(BillingEntitlementDocument entitlement, JsonElement subscription)
    {
        entitlement.StripeSubscriptionId = subscription.GetProperty("id").GetString() ?? "";
        entitlement.StripeCustomerId = subscription.GetProperty("customer").GetString() ?? entitlement.StripeCustomerId;
        entitlement.SubscriptionStatus = subscription.GetProperty("status").GetString() ?? "cancelled";
        entitlement.Plan = (IsEntitled(entitlement.SubscriptionStatus) ? BillingPlan.Pro : BillingPlan.Free).ToString();
        entitlement.TrialUsed |= subscription.TryGetProperty("trial_start", out var trialStart) && trialStart.ValueKind == JsonValueKind.Number;
        entitlement.PeriodEndsAt = (entitlement.SubscriptionStatus == "trialing" ? UnixTime(subscription, "trial_end") : null)
            ?? UnixTime(subscription, "current_period_end")
            ?? ReadItemPeriodEnd(subscription);
        entitlement.SubscriptionInterval = ReadSubscriptionInterval(subscription);
    }

    private static DateTimeOffset? ReadItemPeriodEnd(JsonElement subscription)
    {
        if (!subscription.TryGetProperty("items", out var items) || !items.TryGetProperty("data", out var data))
        {
            return null;
        }

        return data.EnumerateArray().Select(item => UnixTime(item, "current_period_end")).Min();
    }

    private static BillingInterval? ReadSubscriptionInterval(JsonElement subscription)
    {
        if (!subscription.TryGetProperty("items", out var items)
            || !items.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("price", out var price)
                || price.ValueKind != JsonValueKind.Object
                || !price.TryGetProperty("recurring", out var recurring)
                || recurring.ValueKind != JsonValueKind.Object
                || !recurring.TryGetProperty("interval", out var interval))
            {
                continue;
            }

            var count = recurring.TryGetProperty("interval_count", out var intervalCount) ? intervalCount.GetInt32() : 1;

            return (interval.GetString(), count) switch
            {
                ("month", 1) => BillingInterval.Monthly,
                ("year", 1) or ("month", 12) => BillingInterval.Annual,
                _ => null
            };
        }

        return null;
    }

    private async Task<string> EnsureCustomerAsync(BillingEntitlementDocument entitlement, string userId, string? email, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(entitlement.StripeCustomerId))
        {
            return entitlement.StripeCustomerId;
        }
        var fields = new Dictionary<string, string>
        {
            ["metadata[user_id]"] = userId
        };

        if (!string.IsNullOrWhiteSpace(email))
        {
            fields["email"] = email;
        }
        using var response = await SendAsync(HttpMethod.Post, "customers", fields, $"flowmate-customer-{userId}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(StripeError(body));
        }
        using var customer = JsonDocument.Parse(body);
        entitlement.StripeCustomerId = customer.RootElement.GetProperty("id").GetString() ?? "";
        await repository.SaveEntitlementAsync(entitlement, cancellationToken);

        return entitlement.StripeCustomerId;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Dictionary<string, string>? fields, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var secret = configuration["Stripe:SecretKey"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new HttpRequestException("Stripe secret key is missing.");
        }
        using var request = new HttpRequestMessage(method, STRIPE_API + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(secret + ":")));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (fields is not null)
        {
            request.Content = new FormUrlEncodedContent(fields);
        }

        return await clients.CreateClient().SendAsync(request, cancellationToken);
    }

    private static bool VerifySignature(string payload, string? header, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header))
        {
            return false;
        }
        var values = header.Split(',')
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToArray();

        var timestampText = values.FirstOrDefault(part => part[0] == "t")?[1];

        if (!long.TryParse(timestampText, out var timestamp) || timestamp < DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300
            || timestamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300)
        {
            return false;
        }
        var signedPayload = timestampText + "." + payload;
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedPayload));

        foreach (var signature in values.Where(part => part[0] == "v1").Select(part => part[1]))
        {
            try
            {
                if (CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(signature)))
                {

                    return true;
                }
            }
            catch (FormatException) { }
        }

        return false;
    }

    private static bool IsEntitled(string status) => SubscriptionEntitlementExtensions.FromProviderStatus(status) == SubscriptionEntitlement.Entitled;

    private static DateTimeOffset? UnixTime(JsonElement root, string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64()) : null;


    private static string StripeError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "Stripe rejected the request.";
        }
        catch
        {

            return "Stripe rejected the request.";
        }
    }
    private static BillingActionResponse Failure(BillingActionCode code, string message) => new(false, null, code, message);
}
