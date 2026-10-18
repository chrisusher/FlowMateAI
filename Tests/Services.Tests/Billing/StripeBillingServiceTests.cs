using System.Text;
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
    public async Task CheckoutDoesNotReuseOpenSessionForDifferentPrice()
    {
        var handler = new StripeHandler();
        var service = CreateService(handler, _repository!);

        var monthly = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);
        var annual = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", true);
        var annualRetry = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", true);

        Assert.That(monthly.Url, Is.EqualTo("https://checkout.example.test/session/price_monthly"));
        Assert.That(annual.Url, Is.EqualTo("https://checkout.example.test/session/price_annual"));
        Assert.That(annualRetry.Url, Is.EqualTo(annual.Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(2));
        Assert.That(handler.CheckoutIdempotencyKeys, Has.Count.EqualTo(2));
        Assert.That(handler.CheckoutIdempotencyKeys[0], Is.Not.EqualTo(handler.CheckoutIdempotencyKeys[1]));
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

    [TestCase("active", false)]
    [TestCase("active", true)]
    [TestCase("trialing", false)]
    [TestCase("trialing", true)]
    public async Task StripeConfirmedSubscriptionIsPersistedBeforeOpeningPortalWhenWebhookIsDelayed(string status, bool trial)
    {
        var handler = new StripeHandler
        {
            SubscriptionsJson = $$"""
                {"data":[
                    {"id":"sub_ended","customer":"cus_existing","status":"canceled","trial_start":1700000000,"current_period_end":1701209600},
                    {"id":"sub_confirmed","customer":"cus_existing","status":"{{status}}","trial_start":null,"current_period_end":1792329600}
                ]}
                """
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
        Assert.That(reservation.Limit, Is.EqualTo(100));

        var subscriptionQueries = handler.Requests.Count(path => path.StartsWith("subscriptions?", StringComparison.Ordinal));
        await service.CreateCheckoutAsync("user", null, false);
        Assert.That(handler.Requests.Count(path => path.StartsWith("subscriptions?", StringComparison.Ordinal)), Is.EqualTo(subscriptionQueries));
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
                CheckoutSessionCreations++;
                CheckoutIdempotencyKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                var priceId = FormValue(form, "line_items[0][price]")!;
                var url = $"https://checkout.example.test/session/{priceId}";
                OpenCheckoutSessions.Add(new OpenCheckoutSession("subscription", priceId, url));

                return JsonResponse(JsonSerializer.Serialize(new
                {
                    id = "cs_test",
                    url
                }));
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/billing_portal/sessions", StringComparison.Ordinal))
            {
                BeforePortalCreation?.Invoke();

                return JsonResponse("{\"url\":\"https://billing.example.test/portal\"}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string? FormValue(string body, string key) => body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == key)
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        public sealed record OpenCheckoutSession(string Mode, string PriceId, string Url);
    }

    private sealed class StubWorkspaceService : IWorkspaceService
    {
        public Task<WorkspaceResponse> GetAsync(string ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceResponse(new WorkspaceSnapshot(), "revision"));

        public Task<WorkspaceSaveResult> SaveAsync(string ownerId, WorkspaceSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
