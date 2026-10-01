using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Services.Database;
using Services.Repositories;
using Shared.Contracts;

namespace Services.Billing;

public sealed class StripeBillingService(
    IConfiguration configuration,
    IHttpClientFactory clients,
    IBillingRepository repository) : IBillingService
{
    private const string StripeApi = "https://api.stripe.com/v1/";

    public async Task<BillingSummary> GetSummaryAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        if (entitlement.PromptMonth != month)
        {
            entitlement.PromptMonth = month;
            entitlement.PromptCount = 0;
            await repository.SaveEntitlementAsync(entitlement, cancellationToken);
        }
        return new(entitlement.Plan, entitlement.SubscriptionStatus, entitlement.TrialUsed, entitlement.PeriodEndsAt,
            entitlement.PromptCount, entitlement.Plan == "Pro" ? 100 : 10);
    }

    public async Task<(bool Allowed, int Used, int Limit)> ConsumePromptAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        if (entitlement.PromptMonth != month)
        { entitlement.PromptMonth = month; entitlement.PromptCount = 0; }
        var limit = entitlement.Plan == "Pro" ? 100 : 10;

        if (entitlement.PromptCount >= limit)
            return (false, entitlement.PromptCount, limit);
        entitlement.PromptCount++;
        await repository.SaveEntitlementAsync(entitlement, cancellationToken);
        return (true, entitlement.PromptCount, limit);
    }

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
                continue;
            using var response = await SendAsync(HttpMethod.Get, $"prices/{Uri.EscapeDataString(id)}", null, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
                continue;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            var currency = root.GetProperty("currency").GetString() ?? "gbp";
            var amount = root.GetProperty("unit_amount").GetInt64();
            var actualInterval = root.GetProperty("recurring").GetProperty("interval").GetString() ?? interval;
            prices.Add(new(actualInterval, currency, amount, FormatPrice(currency, amount, actualInterval)));
        }
        return prices;
    }

    public async Task<BillingActionResponse> StartTrialAsync(string userId, string? email, CancellationToken cancellationToken = default)
    {
        var priceId = configuration["Stripe:MonthlyPriceId"];

        if (string.IsNullOrWhiteSpace(priceId))
            return Failure("billing_unavailable", "Pro pricing is not configured yet.");
        var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);

        if (entitlement.TrialUsed)
            return Failure("trial_used", "Your 14-day trial has already been used.");
        try
        {
            var customer = await EnsureCustomerAsync(entitlement, userId, email, cancellationToken);
            var form = new Dictionary<string, string>
            {
                ["customer"] = customer,
                ["items[0][price]"] = priceId,
                ["trial_period_days"] = "14",
                ["trial_settings[end_behavior][missing_payment_method]"] = "cancel",
                ["metadata[user_id]"] = userId
            };
            using var response = await SendAsync(HttpMethod.Post, "subscriptions", form, $"flowmate-trial-{userId}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Failure("stripe_error", StripeError(body));
            using var subscription = JsonDocument.Parse(body);
            entitlement.StripeSubscriptionId = subscription.RootElement.GetProperty("id").GetString() ?? "";
            entitlement.SubscriptionStatus = subscription.RootElement.GetProperty("status").GetString() ?? "trialing";
            entitlement.Plan = IsEntitled(entitlement.SubscriptionStatus) ? "Pro" : "Free";
            entitlement.TrialUsed = true;
            entitlement.PeriodEndsAt = UnixTime(subscription.RootElement, "current_period_end");
            await repository.SaveEntitlementAsync(entitlement, cancellationToken);
            return new(true, null, null, "Your 14-day Pro trial has started.");
        }
        catch (HttpRequestException) { return Failure("stripe_unavailable", "Stripe could not be reached. Try again in a moment."); }
    }

    public async Task<BillingActionResponse> CreateCheckoutAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default)
    {
        var priceId = configuration[annual ? "Stripe:AnnualPriceId" : "Stripe:MonthlyPriceId"];
        var origin = configuration["WebApp:Origin"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(priceId) || string.IsNullOrWhiteSpace(origin))
            return Failure("billing_unavailable", "Checkout is not configured yet.");
        try
        {
            var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);
            var customer = await EnsureCustomerAsync(entitlement, userId, email, cancellationToken);
            var form = new Dictionary<string, string>
            {
                ["mode"] = "subscription",
                ["customer"] = customer,
                ["line_items[0][price]"] = priceId,
                ["line_items[0][quantity]"] = "1",
                ["success_url"] = origin + "/pricing?checkout=return",
                ["cancel_url"] = origin + "/pricing?checkout=cancelled",
                ["subscription_data[metadata][user_id]"] = userId
            };
            using var response = await SendAsync(HttpMethod.Post, "checkout/sessions", form, $"flowmate-checkout-{userId}-{(annual ? "annual" : "monthly")}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Failure("stripe_error", StripeError(body));
            using var session = JsonDocument.Parse(body);
            return new(true, session.RootElement.GetProperty("url").GetString(), null, null);
        }
        catch (HttpRequestException) { return Failure("stripe_unavailable", "Stripe could not be reached. Try again in a moment."); }
    }

    public async Task<BillingActionResponse> CreatePortalAsync(string userId, CancellationToken cancellationToken = default)
    {
        var origin = configuration["WebApp:Origin"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(origin))
            return Failure("billing_unavailable", "Billing is not configured yet.");
        var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);

        if (string.IsNullOrWhiteSpace(entitlement.StripeCustomerId))
            return Failure("customer_missing", "No billing account is linked yet.");
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "billing_portal/sessions", new Dictionary<string, string>
            {
                ["customer"] = entitlement.StripeCustomerId,
                ["return_url"] = origin + "/settings"
            }, null, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Failure("stripe_error", StripeError(body));
            using var session = JsonDocument.Parse(body);
            return new(true, session.RootElement.GetProperty("url").GetString(), null, null);
        }
        catch (HttpRequestException) { return Failure("stripe_unavailable", "Stripe could not be reached. Try again in a moment."); }
    }

    public async Task<bool> ProcessWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default)
    {
        if (!VerifySignature(payload, signature, configuration["Stripe:WebhookSecret"]))
            return false;
        using var eventDoc = JsonDocument.Parse(payload);
        var root = eventDoc.RootElement;
        var eventId = root.GetProperty("id").GetString();

        if (string.IsNullOrWhiteSpace(eventId) || await repository.HasProcessedEventAsync(eventId, cancellationToken))
            return true;
        var type = root.GetProperty("type").GetString() ?? "";

        if (type.StartsWith("customer.subscription.", StringComparison.Ordinal))
        {
            var subscription = root.GetProperty("data").GetProperty("object");
            var userId = subscription.GetProperty("metadata").TryGetProperty("user_id", out var user) ? user.GetString() : null;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var entitlement = await repository.GetEntitlementAsync(userId, cancellationToken);
                entitlement.StripeSubscriptionId = subscription.GetProperty("id").GetString() ?? "";
                entitlement.StripeCustomerId = subscription.GetProperty("customer").GetString() ?? entitlement.StripeCustomerId;
                entitlement.SubscriptionStatus = subscription.GetProperty("status").GetString() ?? "canceled";
                entitlement.Plan = IsEntitled(entitlement.SubscriptionStatus) ? "Pro" : "Free";
                entitlement.TrialUsed |= subscription.TryGetProperty("trial_start", out _);
                entitlement.PeriodEndsAt = UnixTime(subscription, "current_period_end");
                await repository.SaveEntitlementAsync(entitlement, cancellationToken);
            }
        }
        await repository.MarkEventProcessedAsync(eventId, cancellationToken);
        return true;
    }

    private async Task<string> EnsureCustomerAsync(BillingEntitlementDocument entitlement, string userId, string? email, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(entitlement.StripeCustomerId))
            return entitlement.StripeCustomerId;
        var fields = new Dictionary<string, string> { ["metadata[user_id]"] = userId };

        if (!string.IsNullOrWhiteSpace(email))
            fields["email"] = email;
        using var response = await SendAsync(HttpMethod.Post, "customers", fields, $"flowmate-customer-{userId}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(StripeError(body));
        using var customer = JsonDocument.Parse(body);
        entitlement.StripeCustomerId = customer.RootElement.GetProperty("id").GetString() ?? "";
        await repository.SaveEntitlementAsync(entitlement, cancellationToken);
        return entitlement.StripeCustomerId;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Dictionary<string, string>? fields, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var secret = configuration["Stripe:SecretKey"];

        if (string.IsNullOrWhiteSpace(secret))
            throw new HttpRequestException("Stripe secret key is missing.");
        using var request = new HttpRequestMessage(method, StripeApi + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(secret + ":")));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        if (fields is not null)
            request.Content = new FormUrlEncodedContent(fields);
        return await clients.CreateClient().SendAsync(request, cancellationToken);
    }

    private static bool VerifySignature(string payload, string? header, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header))
            return false;
        var values = header.Split(',').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).ToArray();
        var timestampText = values.FirstOrDefault(part => part[0] == "t")?[1];

        if (!long.TryParse(timestampText, out var timestamp) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 300)
            return false;
        var signedPayload = timestampText + "." + payload;
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedPayload));
        foreach (var signature in values.Where(part => part[0] == "v1").Select(part => part[1]))
        {
            try
            { if (CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(signature))) return true; }
            catch (FormatException) { }
        }
        return false;
    }

    private static bool IsEntitled(string status) => status is "active" or "trialing";
    private static DateTimeOffset? UnixTime(JsonElement root, string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64()) : null;
    private static string FormatPrice(string currency, long amount, string interval) { var formatted = (amount / 100m).ToString("0.##", CultureInfo.InvariantCulture); var symbol = currency.ToLowerInvariant() switch { "gbp" => "£", "usd" => "$", "eur" => "€", _ => currency.ToUpperInvariant() + " " }; return interval == "year" ? $"{symbol}{formatted} / year" : $"{symbol}{formatted} / month"; }
    private static string StripeError(string body) { try { using var document = JsonDocument.Parse(body); return document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "Stripe rejected the request."; } catch { return "Stripe rejected the request."; } }
    private static BillingActionResponse Failure(string code, string message) => new(false, null, code, message);
}
