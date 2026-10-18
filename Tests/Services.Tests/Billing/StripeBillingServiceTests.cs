using System.Text;
using System.Security.Cryptography;
using Services.Billing;
using Services.Database;
using Services.Repositories;
using Services.Coach;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Models;
using Services.Tests.Doubles;

namespace Services.Tests.Billing;

[TestFixture]
public sealed class StripeBillingServiceTests
{
    private StubBillingRepository? _repository;

    [SetUp]
    public void SetupEachTest()
    {
        _repository = new StubBillingRepository();
    }

    [Test]
    public async Task RepeatedCheckoutReusesOpenSessionAndStableIdempotencyKey()
    {
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository!);

        var first = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);
        var retry = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);

        Assert.That(first.Succeeded, Is.True);
        Assert.That(retry.Url, Is.EqualTo(first.Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
        Assert.That(handler.CheckoutIdempotencyKeys, Has.Count.EqualTo(1));
        Assert.That(handler.CheckoutIdempotencyKeys[0], Does.Not.Contain("auth0|customer"));
    }

    [Test]
    public async Task CheckoutBlocksDifferentPriceWhileAnotherCheckoutIsOpen()
    {
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository!);

        var monthly = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);
        var annual = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", true);
        var monthlyRetry = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);

        Assert.That(monthly.Url, Is.EqualTo("https://checkout.example.test/session/price_monthly"));
        Assert.That(annual.Succeeded, Is.False);
        Assert.That(annual.Code, Is.EqualTo(BillingActionCode.CheckoutInProgress));
        Assert.That(monthlyRetry.Url, Is.EqualTo(monthly.Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TrialCheckoutReusesExistingTrialWhenSelectedIntervalChanges(bool annual)
    {
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository!);

        var first = await service.StartTrialAsync("user", null, annual);
        var switched = await service.StartTrialAsync("user", null, !annual);
        var retry = await service.StartTrialAsync("user", null, !annual);

        Assert.That(first.Succeeded, Is.True);
        Assert.That(switched.Url, Is.EqualTo(first.Url));
        Assert.That(retry.Url, Is.EqualTo(first.Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
        Assert.That(handler.OpenCheckoutSessions.Count(session => session.Trial), Is.EqualTo(1));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task ConcurrentSubscriptionRequestsCannotCreateTwoSessions(bool firstTrial, bool secondTrial)
    {
        var bothRequestsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestsStarted = 0;
        var handler = new StripeHandler
        {
            BeforeCheckoutCreation = cancellationToken =>
            {
                if (Interlocked.Increment(ref requestsStarted) == 2)
                {
                    bothRequestsStarted.TrySetResult();
                }

                return bothRequestsStarted.Task.WaitAsync(cancellationToken);
            }
        };
        _repository!.Entitlement.StripeCustomerId = "cus_existing";
        var service = CreateService(handler, _repository);

        var results = await Task.WhenAll(
            service.CreateCheckoutAsync("user", null, false, trial: firstTrial),
            service.CreateCheckoutAsync("user", null, true, trial: secondTrial)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(handler.CheckoutIdempotencyKeys.Distinct().Count(), Is.EqualTo(1));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
        Assert.That(results.Count(result => result.Succeeded), Is.EqualTo(1));

        var successfulIndex = Array.FindIndex(results, result => result.Succeeded);
        var retry = await service.CreateCheckoutAsync("user", null, successfulIndex == 1, trial: successfulIndex == 0 ? firstTrial : secondTrial);
        Assert.That(retry.Url, Is.EqualTo(results.Single(result => result.Succeeded).Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CheckoutBlocksTheOppositeSubscriptionCheckoutType(bool existingTrial)
    {
        var handler = new StripeHandler();
        handler.OpenCheckoutSessions.Add(new("subscription", "price_monthly", "https://checkout.example.test/existing", existingTrial));
        var service = CreateService(handler, _repository!);

        var result = await service.CreateCheckoutAsync("user", null, false, trial: !existingTrial);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Code, Is.EqualTo(BillingActionCode.CheckoutInProgress));
        Assert.That(result.Url, Is.Null);
        Assert.That(handler.CheckoutSessionCreations, Is.Zero);
        Assert.That(handler.OpenCheckoutSessions, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MatchingCheckoutDoesNotHideAnExistingOppositeType(bool trial)
    {
        var handler = new StripeHandler();
        handler.OpenCheckoutSessions.Add(new("subscription", "price_monthly", "https://checkout.example.test/matching", trial));
        handler.OpenCheckoutSessions.Add(new("subscription", "price_monthly", "https://checkout.example.test/opposite", !trial));
        var service = CreateService(handler, _repository!);

        var result = await service.CreateCheckoutAsync("user", null, false, trial: trial);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Code, Is.EqualTo(BillingActionCode.CheckoutInProgress));
        Assert.That(handler.CheckoutSessionCreations, Is.Zero);
    }

    [Test]
    public async Task CheckoutDoesNotReuseOpenSessionOutsideSubscriptionMode()
    {
        var handler = new StripeHandler();
        handler.OpenCheckoutSessions.Add(new StripeHandler.OpenCheckoutSession("payment", "price_monthly", "https://checkout.example.test/wrong-mode"));
        var service = CreateService(handler, _repository!);

        var result = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);

        Assert.That(result.Url, Is.EqualTo("https://checkout.example.test/session/price_monthly"));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
    }

    [Test]
    public async Task EntitledCustomerIsSentToBillingPortalInsteadOfAnotherCheckout()
    {
        var handler = new StripeHandler();
        var repository = new StubBillingRepository
        {
            Entitlement = new BillingEntitlementDocument
            {
                UserId = "user",
                StripeCustomerId = "cus_existing",
                SubscriptionStatus = "trialing"
            }
        };
        var service = CreateService(handler, repository);

        var result = await service.CreateCheckoutAsync("user", null, false);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Url, Is.EqualTo("https://billing.example.test/portal"));
        Assert.That(handler.Requests.Any(path => path.StartsWith("checkout/sessions", StringComparison.Ordinal)), Is.False);
        Assert.That(handler.Requests.Any(path => path == "billing_portal/sessions"), Is.True);
    }

    [TestCase("trialing")]
    [TestCase("active")]
    public async Task UsedTrialWithEntitledSubscriptionOpensPortal(string status)
    {
        _repository!.Entitlement.StripeCustomerId = "cus_existing";
        _repository.Entitlement.SubscriptionStatus = status;
        _repository.Entitlement.TrialUsed = true;
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository);

        var result = await service.StartTrialAsync("user", null, false);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Url, Is.EqualTo("https://billing.example.test/portal"));
        Assert.That(handler.CheckoutSessionCreations, Is.Zero);
    }

    [TestCase("none")]
    [TestCase("canceled")]
    public async Task UsedTrialWithoutEntitledSubscriptionRemainsIneligible(string status)
    {
        _repository!.Entitlement.SubscriptionStatus = status;
        _repository.Entitlement.TrialUsed = true;
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository);

        var result = await service.StartTrialAsync("user", null, false);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Code, Is.EqualTo(BillingActionCode.TrialUsed));
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TrialCancellationUrlPreservesSelectedBillingInterval(bool annual)
    {
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository!);

        await service.StartTrialAsync("user", null, annual);

        Assert.That(handler.LastCheckoutCancelUrl,
            Is.EqualTo($"https://app.example.test/pricing?checkout=trial-cancelled&annual={annual.ToString().ToLowerInvariant()}"));
    }

    [TestCase("active", false)]
    [TestCase("active", true)]
    [TestCase("trialing", false)]
    [TestCase("trialing", true)]
    public async Task StripeConfirmedSubscriptionIsPersistedBeforeOpeningPortalWhenWebhookIsDelayed(string status, bool trial)
    {
        var handler = new StripeHandler
        {
            SubscriptionsJson = """
                {"data":[
                    {"id":"sub_ended","customer":"cus_existing","status":"canceled","trial_start":1700000000,"current_period_end":1701209600},
                    {"id":"sub_confirmed","customer":"cus_existing","status":"$STATUS","trial_start":null,"current_period_end":1792329600,
                     "items":{"data":[{"price":{"recurring":{"interval":"year","interval_count":1}}}]}}
                ]}
                """.Replace("$STATUS", status, StringComparison.Ordinal)
        };
        var repository = new StubBillingRepository
        {
            Entitlement = new BillingEntitlementDocument
            {
                UserId = "user",
                StripeCustomerId = "cus_existing",
                SubscriptionStatus = "none"
            }
        };
        handler.BeforePortalCreation = () =>
        {
            var saved = repository.SavedEntitlements.Single();
            Assert.That(saved.Plan, Is.EqualTo("Pro"));
            Assert.That(saved.SubscriptionStatus, Is.EqualTo(status));
            Assert.That(saved.StripeCustomerId, Is.EqualTo("cus_existing"));
            Assert.That(saved.StripeSubscriptionId, Is.EqualTo("sub_confirmed"));
            Assert.That(saved.PeriodEndsAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1792329600)));
            Assert.That(saved.TrialUsed, Is.True);
            Assert.That(saved.SubscriptionInterval, Is.EqualTo(BillingInterval.Annual));
        };
        var service = CreateService(handler, repository);

        var result = trial
            ? await service.StartTrialAsync("user", null, false)
            : await service.CreateCheckoutAsync("user", null, false);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Url, Is.EqualTo("https://billing.example.test/portal"));
        Assert.That(handler.Requests.Any(path => path == "billing_portal/sessions"), Is.True);
        Assert.That(handler.CheckoutSessionCreations, Is.Zero);

        var summary = await service.GetSummaryAsync("user");
        var reservation = await service.ConsumePromptAsync("user", "2026-10");
        Assert.That(summary.Plan, Is.EqualTo("Pro"));
        Assert.That(summary.Status, Is.EqualTo(status));
        Assert.That(summary.PromptLimit, Is.EqualTo(100));
        Assert.That(summary.SubscriptionInterval, Is.EqualTo(BillingInterval.Annual));
        Assert.That(reservation.Limit, Is.EqualTo(100));

        var subscriptionQueries = handler.Requests.Count(path => path.StartsWith("subscriptions?", StringComparison.Ordinal));
        await service.CreateCheckoutAsync("user", null, false);
        Assert.That(handler.Requests.Count(path => path.StartsWith("subscriptions?", StringComparison.Ordinal)), Is.EqualTo(subscriptionQueries));
    }

    [TestCase("month", 1, BillingInterval.Monthly)]
    [TestCase("year", 1, BillingInterval.Annual)]
    [TestCase("month", 12, BillingInterval.Annual)]
    [TestCase("month", 6, null)]
    [TestCase("week", 1, null)]
    public async Task SubscriptionWebhookPersistsConfirmedRenewalInterval(string interval, int count, BillingInterval? expected)
    {
        _repository!.Entitlement.SubscriptionInterval = BillingInterval.Annual;
        var service = CreateService(new StripeHandler(), _repository);
        var payload = """
            {"id":"evt_subscription","type":"customer.subscription.updated","data":{"object":{
                "id":"sub_confirmed","customer":"cus_existing","metadata":{"user_id":"user"},
                "status":"trialing","trial_start":1791120000,"trial_end":1792329600,
                "items":{"data":[{"price":{"recurring":{"interval":"$INTERVAL","interval_count":$COUNT}}}]}
            }}}
            """.Replace("$INTERVAL", interval, StringComparison.Ordinal).Replace("$COUNT", count.ToString(), StringComparison.Ordinal);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_test_placeholder"), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        var accepted = await service.ProcessWebhookAsync(payload, $"t={timestamp},v1={Convert.ToHexString(digest)}");
        var summary = await service.GetSummaryAsync("user");

        Assert.That(accepted, Is.True);
        Assert.That(_repository.SavedEntitlements.Single().SubscriptionInterval, Is.EqualTo(expected));
        Assert.That(summary.SubscriptionInterval, Is.EqualTo(expected));
        Assert.That(summary.PeriodEndsAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1792329600)));
    }

    [Test]
    public async Task SubscriptionWebhookPersistsScheduledCancellationForBillingSummary()
    {
        var service = CreateService(new StripeHandler(), _repository!);
        var payload = """
            {"id":"evt_subscription_cancel","type":"customer.subscription.updated","data":{"object":{
                "id":"sub_confirmed","customer":"cus_existing","metadata":{"user_id":"user"},
                "status":"active","cancel_at_period_end":true,"current_period_end":1792329600,
                "items":{"data":[{"price":{"recurring":{"interval":"month","interval_count":1}}}]}
            }}}
            """;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_test_placeholder"), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        var accepted = await service.ProcessWebhookAsync(payload, $"t={timestamp},v1={Convert.ToHexString(digest)}");
        var summary = await service.GetSummaryAsync("user");

        Assert.That(accepted, Is.True);
        Assert.That(summary.CancelAtPeriodEnd, Is.True);
        Assert.That(_repository!.SavedEntitlements.Single().CancelAtPeriodEnd, Is.True);
    }

    [Test]
    public async Task ConcurrentPromptReservationsNeverExceedTheServerPlanLimit()
    {
        var service = CreateService(new StripeHandler(), _repository!);

        var reservations = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => service.ConsumePromptAsync("user", "2026-10")));

        Assert.That(reservations.Count(reservation => reservation.Allowed), Is.EqualTo(10));
        Assert.That(_repository!.Entitlement.PromptCount, Is.EqualTo(10));
        Assert.That(reservations.Where(reservation => reservation.Allowed).All(reservation => reservation.Limit == 10), Is.True);
    }

    [Test]
    public async Task PromptReservationResetsOnMonthRolloverAndUsesPersistedPlan()
    {
        var repository = new StubBillingRepository
        {
            Entitlement = new BillingEntitlementDocument
            {
                UserId = "user",
                Plan = "Pro",
                PromptMonth = "2026-09",
                PromptCount = 100
            }
        };
        var service = CreateService(new StripeHandler(), repository);

        var reservation = await service.ConsumePromptAsync("user", "2026-10");

        Assert.That(reservation.Allowed, Is.True);
        Assert.That(reservation.Used, Is.EqualTo(1));
        Assert.That(reservation.Limit, Is.EqualTo(100));
        Assert.That(repository.Entitlement.PromptMonth, Is.EqualTo("2026-10"));
    }

    [Test]
    public async Task ReleasingFailedPromptReservationRestoresMonthlyUsage()
    {
        var service = CreateService(new StripeHandler(), _repository!);

        var reservation = await service.ConsumePromptAsync("user", "2026-10");
        await service.ReleasePromptAsync("user", "2026-10");
        var retry = await service.ConsumePromptAsync("user", "2026-10");

        Assert.That(reservation.Allowed, Is.True);
        Assert.That(_repository!.Entitlement.PromptCount, Is.EqualTo(1));
        Assert.That(retry.Allowed, Is.True);
    }

    [Test]
    public async Task ProviderFailureReleasesReservedPromptUsage()
    {
        var billing = CreateService(new StripeHandler(), _repository!);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Foundry:Endpoint"] = "https://foundry.example.test",
            ["Foundry:Deployment"] = "coach",
            ["Foundry:ApiKey"] = "placeholder"
        }).Build();

        var coach = new FoundryFocusCoachService(
            configuration,
            new TestHttpClientFactory(new StripeHandler()),
            new StubWorkspaceService(),
            billing);

        Assert.ThrowsAsync<HttpRequestException>(() => coach.AskAsync("user", "Help me choose a task.", []));
        Assert.That(_repository!.Entitlement.PromptCount, Is.Zero);
    }

    private static StripeBillingService CreateService(StripeHandler handler, IBillingRepository repository)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_placeholder",
            ["Stripe:WebhookSecret"] = "whsec_test_placeholder",
            ["Stripe:MonthlyPriceId"] = "price_monthly",
            ["Stripe:AnnualPriceId"] = "price_annual",
            ["WebApp:Origin"] = "https://app.example.test"
        }).Build();

        return new(new FlowMateEnvironment(configuration), configuration, new TestHttpClientFactory(handler), repository);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StripeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Form, string Url)> _checkoutRequests = [];
        public List<string> Requests { get; } = [];
        public List<string> CheckoutIdempotencyKeys { get; } = [];

        public int CheckoutSessionCreations
        {
            get; private set;
        }

        public string SubscriptionsJson { get; init; } = "{\"data\":[]}";
        public Action? BeforePortalCreation
        {
            get; set;
        }

        public List<OpenCheckoutSession> OpenCheckoutSessions { get; } = [];
        public Func<CancellationToken, Task>? BeforeCheckoutCreation { get; init; }
        public string? LastCheckoutCancelUrl { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/').Replace("v1/", "", StringComparison.Ordinal);
            Requests.Add(path);

            if (request.RequestUri.AbsolutePath.EndsWith("/customers", StringComparison.Ordinal))
            {
                return JsonResponse("{\"id\":\"cus_created\"}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/subscriptions", StringComparison.Ordinal))
            {
                return JsonResponse(SubscriptionsJson);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/checkout/sessions", StringComparison.Ordinal)
                && request.Method == HttpMethod.Get)
            {
                var sessions = OpenCheckoutSessions.Select(session => new
                {
                    mode = session.Mode,
                    url = session.Url,
                    metadata = new { flowmate_trial = session.Trial ? "true" : "false" },
                    line_items = new
                    {
                        data = new[]
                        {
                            new
                            {
                                price = new
                                {
                                    id = session.PriceId
                                }
                            }
                        }
                    }
                });

                return JsonResponse(JsonSerializer.Serialize(new
                {
                    data = sessions
                }));
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/checkout/sessions", StringComparison.Ordinal))
            {
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);

                if (BeforeCheckoutCreation is not null)
                {
                    await BeforeCheckoutCreation(cancellationToken);
                }

                var key = request.Headers.GetValues("Idempotency-Key").Single();

                lock (_checkoutRequests)
                {
                    CheckoutIdempotencyKeys.Add(key);

                    if (_checkoutRequests.TryGetValue(key, out var previous))
                    {
                        return previous.Form == form
                            ? JsonResponse(JsonSerializer.Serialize(new { url = previous.Url }))
                            : new HttpResponseMessage(HttpStatusCode.BadRequest)
                            {
                                Content = new StringContent("{\"error\":{\"message\":\"Idempotency key reused with different parameters.\"}}")
                            };
                    }

                    CheckoutSessionCreations++;
                    LastCheckoutCancelUrl = FormValue(form, "cancel_url");
                    var priceId = FormValue(form, "line_items[0][price]")!;
                    var url = $"https://checkout.example.test/session/{priceId}";
                    var trial = FormValue(form, "metadata[flowmate_trial]") == "true";
                    OpenCheckoutSessions.Add(new OpenCheckoutSession("subscription", priceId, url, trial));
                    _checkoutRequests.Add(key, (form, url));

                    return JsonResponse(JsonSerializer.Serialize(new { id = "cs_test", url }));
                }
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/billing_portal/sessions", StringComparison.Ordinal))
            {
                BeforePortalCreation?.Invoke();

                return JsonResponse("{\"url\":\"https://billing.example.test/portal\"}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);…9393 tokens truncated…entSubscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue or SubscriptionStatus.Unpaid or SubscriptionStatus.Incomplete or SubscriptionStatus.Paused;

    private string EndDateMessage(string prefix) => BillingState?.PeriodEndsAt is { } end
        ? $"{prefix} {end.ToLocalTime():d}."
        : $"{prefix} date unavailable. Refresh billing status for the latest details.";

    private string TrialStatusDetail() => BillingState?.PeriodEndsAt is { } end
        ? $"Your trial ends {end.ToLocalTime():d}. Unless you cancel first, the {RenewalChargeLabel} charge follows then."
        : "Your trial end date is unavailable. Refresh billing status for the latest details.";

    public async Task ShowCheckoutReturn(CheckoutOutcome outcome)
    {
        switch (outcome)
        {
            case CheckoutOutcome.Return:
                _checkoutPending = !HasEntitledSubscription;
                BillingMessage = _checkoutPending
                    ? "Payment is processing. Pro access will appear once Stripe confirms your subscription."
                    : "Your Pro subscription is active.";
                break;
            case CheckoutOutcome.Cancelled:
                _checkoutPending = false;
                BillingMessage = "Checkout was cancelled. No subscription was started.";
                break;
            case CheckoutOutcome.TrialReturn:
                _checkoutPending = !HasEntitledSubscription;
                BillingMessage = _checkoutPending
                    ? "Your trial is being set up. Pro access will appear once Stripe confirms your subscription."
                    : $"Your 14-day Pro trial is active{TrialEndMessage()}. No charge is due until the trial ends.";
                break;
            case CheckoutOutcome.TrialCancelled:
                _checkoutPending = false;
                BillingMessage = "Trial checkout was cancelled. No trial or subscription was started.";
                break;
            default:
                return;
        }

        if ((outcome is CheckoutOutcome.Return or CheckoutOutcome.TrialReturn) && BillingState is not null)
        {
            Store.Data.Plan = BillingPlanExtensions.ParseOrFree(BillingState.Plan);
            await Store.SaveAsync();
        }

        NotifyChanged();
    }

    private string TrialEndMessage() => BillingState?.PeriodEndsAt is { } end
        ? $" until {end.ToLocalTime():d}"
        : "";

    public async Task RefreshSubscriptionStatusAsync()
    {
        try
        {
            BillingState = await client.GetSummaryAsync();

            if (BillingState is not null)
            {
                Store.Data.Plan = BillingPlanExtensions.ParseOrFree(BillingState.Plan);
                await Store.SaveAsync();
            }

            _checkoutPending = !HasEntitledSubscription;
            BillingMessage = _checkoutPending
                ? "Payment is still processing. Try checking again in a moment."
                : "Your Pro subscription is active.";
        }
        catch (HttpRequestException)
        {
            BillingMessage = "Subscription status could not be refreshed. Try again in a moment.";
        }
        finally
        {
            NotifyChanged();
        }
    }

    public async Task StartTrial()
    {
        if (_checkoutInProgress || !HasSelectedPrice || BillingState?.TrialUsed == true || HasEntitledSubscription)
        {
            return;
        }

        _checkoutInProgress = true;
        NotifyChanged();

        try
        {
            var result = await client.StartTrialAsync(Annual);

            if (!result.Succeeded)
            {
                BillingMessage = result.Message;

                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                navigation.NavigateTo(result.Url, forceLoad: true);

                return;
            }

            BillingMessage = result.Message;
        }
        catch (HttpRequestException)
        {
            BillingMessage = "Billing is temporarily unavailable.";
        }
        finally
        {
            _checkoutInProgress = false;
            NotifyChanged();
        }
    }

    public async Task BuyPro()
    {
        if (_checkoutInProgress)
        {
            return;
        }

        if (CanManageSubscription)
        {
            await OpenBillingPortal();

            return;
        }

        if (!HasSelectedPrice)
        {
            return;
        }

        _checkoutInProgress = true;
        NotifyChanged();

        try
        {
            var result = await client.CheckoutAsync(Annual);

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Url))
            {
                navigation.NavigateTo(result.Url, forceLoad: true);
            }
            else
            {
                BillingMessage = result.Message;
            }
        }
        catch (HttpRequestException) { BillingMessage = "Checkout is temporarily unavailable."; }
        finally
        {
            _checkoutInProgress = false;
            NotifyChanged();
        }
    }

    public async Task OpenBillingPortal()
    {
        try
        {
            var result = await client.PortalAsync();

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Url))
            {
                navigation.NavigateTo(result.Url, forceLoad: true);
            }
            else
            {
                BillingMessage = result.Message;
            }
        }
        catch (HttpRequestException) { BillingMessage = "Billing is temporarily unavailable."; }
        finally { NotifyChanged(); }
    }
}
