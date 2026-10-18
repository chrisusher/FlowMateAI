using System.Security.Cryptography;
using System.Text;
using Services.Billing;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Billing;

[TestFixture]
public sealed class StripeReconciliationTests
{
    [TestCase("trialing", "Pro")]
    [TestCase("active", "Pro")]
    [TestCase("past_due", "Free")]
    [TestCase("unpaid", "Free")]
    [TestCase("canceled", "Free")]
    [TestCase("incomplete_expired", "Free")]
    [TestCase("paused", "Free")]
    public async Task EventsUseCurrentStripeStateRatherThanTheirSnapshot(string status, string plan)
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider { Subscriptions = List(Subscription("sub_current", status)) };
        var service = CreateService(repository, handler);

        await Deliver(service, "evt_old", "customer.subscription.deleted");

        Assert.That(repository.Current.Plan, Is.EqualTo(plan));
        Assert.That(repository.Current.SubscriptionStatus, Is.EqualTo(status));
        Assert.That(repository.Current.StripeSubscriptionId, Is.EqualTo("sub_current"));
        Assert.That(repository.Current.TrialUsed, Is.True);
        Assert.That(repository.Current.PeriodEndsAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(status == "trialing" ? 1800000000 : 1801000000)));
        Assert.That(repository.Current.SubscriptionInterval, Is.EqualTo(BillingInterval.Annual));
    }

    [Test]
    public async Task SequentialReplayHasNoFurtherProviderOrEntitlementSideEffects()
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider();
        var service = CreateService(repository, handler);
        await Deliver(service, "evt_duplicate");
        var requests = handler.SubscriptionRequests;

        await Deliver(service, "evt_duplicate");

        Assert.That(repository.SuccessfulSaves, Is.EqualTo(1));
        Assert.That(repository.Processed, Has.Count.EqualTo(1));
        Assert.That(handler.SubscriptionRequests, Is.EqualTo(requests));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SlowerConcurrentDeliveryCannotOverwriteANewerReconciliation(bool duplicate)
    {
        var repository = new ReconciliationRepository();
        var oldRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Provider
        {
            OnSubscriptions = async request =>
            {
                if (request == 1)
                {
                    oldRead.SetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10));

                    return List(Subscription("sub_old", "trialing"));
                }

                return List(Subscription("sub_new", "active"));
            }
        };
        var first = Deliver(CreateService(repository, handler), "evt_first");
        await oldRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Deliver(CreateService(repository, handler), duplicate ? "evt_first" : "evt_second");
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(repository.Conflicts, Is.EqualTo(1));
        Assert.That(handler.SubscriptionRequests, Is.EqualTo(3));
        Assert.That(repository.Current.StripeSubscriptionId, Is.EqualTo("sub_new"));
        Assert.That(repository.Current.SubscriptionStatus, Is.EqualTo("active"));
        Assert.That(repository.Processed, Has.Count.EqualTo(duplicate ? 1 : 2));
        Assert.That(repository.Current.PromptCount, Is.EqualTo(7));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PersistenceFailureIsNotAcknowledgedAndReplayRecovers(bool failMarker)
    {
        var repository = new ReconciliationRepository { FailMarker = failMarker, FailSave = !failMarker };
        var service = CreateService(repository, new Provider());

        Assert.ThrowsAsync<IOException>(() => Deliver(service, "evt_retry"));
        Assert.That(repository.Processed, Is.Empty);
        Assert.That(repository.SuccessfulSaves, Is.EqualTo(failMarker ? 1 : 0));

        repository.FailMarker = false;
        repository.FailSave = false;
        await Deliver(service, "evt_retry");
        await Deliver(service, "evt_retry");

        Assert.That(repository.Current.Plan, Is.EqualTo("Pro"));
        Assert.That(repository.Current.PromptCount, Is.EqualTo(7));
        Assert.That(repository.Processed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task OldSubscriptionAndDelayedInvoiceCannotReplaceTheCurrentSubscription()
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider
        {
            Subscriptions = List(Subscription("sub_old", "canceled", 100), Subscription("sub_new", "active", 200))
        };
        var service = CreateService(repository, handler);

        await Deliver(service, "evt_new");
        await Deliver(service, "evt_old", "customer.subscription.deleted");
        await Deliver(service, "evt_invoice", "invoice.payment_failed");

        Assert.That(repository.Current.StripeSubscriptionId, Is.EqualTo("sub_new"));
        Assert.That(repository.Current.Plan, Is.EqualTo("Pro"));
    }

    [Test]
    public async Task SummaryRecoversMissedRenewalCancellationAndPlanChangesWithoutAWebhook()
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider();
        var service = CreateService(repository, handler);
        Assert.That((await service.GetSummaryAsync("user")).Plan, Is.EqualTo("Pro"));

        handler.Subscriptions = List(Subscription("sub_current", "past_due"));
        Assert.That((await service.GetSummaryAsync("user")).Plan, Is.EqualTo("Free"));
        handler.Subscriptions = List(Subscription("sub_current", "active").Replace("year", "month"));
        Assert.That((await service.GetSummaryAsync("user")).SubscriptionInterval, Is.EqualTo(BillingInterval.Monthly));
        handler.Subscriptions = List(Subscription("sub_current", "canceled"));
        Assert.That((await service.GetSummaryAsync("user")).Plan, Is.EqualTo("Free"));
        Assert.That(repository.Current.TrialUsed, Is.True);
        Assert.That(repository.Current.PromptCount, Is.EqualTo(7));
        Assert.That(repository.Processed, Is.Empty);
    }

    [Test]
    public async Task EveryPageIsReadBeforeChoosingCurrentSubscriptionOrTrialEligibility()
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider
        {
            OnSubscriptions = request => Task.FromResult(request == 1
                ? List(Subscription("sub_canceled", "canceled", 300)).Replace("false", "true")
                : List(Subscription("sub_active", "active", 200)))
        };

        await Deliver(CreateService(repository, handler), "evt_pages");

        Assert.That(handler.LastQuery, Does.Contain("starting_after=sub_canceled"));
        Assert.That(repository.Current.StripeSubscriptionId, Is.EqualTo("sub_active"));
        Assert.That(repository.Current.Plan, Is.EqualTo("Pro"));
    }

    [TestCase("different-user", "cus_existing")]
    [TestCase("user", "cus_obsolete")]
    public async Task CustomerOwnershipMismatchCannotChangeTheAccount(string customerOwner, string storedCustomer)
    {
        var repository = new ReconciliationRepository();
        repository.Current.StripeCustomerId = storedCustomer;
        var handler = new Provider { CustomerOwner = customerOwner };

        await Deliver(CreateService(repository, handler), "evt_foreign");

        Assert.That(repository.SuccessfulSaves, Is.Zero);
        Assert.That(handler.SubscriptionRequests, Is.Zero);
        Assert.That(repository.Current.Plan, Is.EqualTo("Free"));
    }

    [Test]
    public async Task ConcurrentPromptReservationIsPreservedWhenReconciliationRetries()
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider
        {
            OnSubscriptions = request =>
            {
                if (request == 1)
                {
                    repository.Current.PromptCount++;
                    repository.Current.ETag = "reserved-prompt";
                }

                return Task.FromResult(List(Subscription("sub_current", "active")));
            }
        };

        await Deliver(CreateService(repository, handler), "evt_quota");

        Assert.That(repository.Conflicts, Is.EqualTo(1));
        Assert.That(repository.Current.PromptCount, Is.EqualTo(8));
        Assert.That(repository.Current.Plan, Is.EqualTo("Pro"));
    }

    [Test]
    public async Task EmptySubscriptionListRevokesAccessWithoutResettingTrialOrUsage()
    {
        var repository = new ReconciliationRepository();
        repository.Current.Plan = "Pro";
        repository.Current.TrialUsed = true;
        var handler = new Provider { Subscriptions = List() };

        var summary = await CreateService(repository, handler).GetSummaryAsync("user");

        Assert.That(summary.Plan, Is.EqualTo("Free"));
        Assert.That(summary.Status, Is.EqualTo("none"));
        Assert.That(summary.TrialUsed, Is.True);
        Assert.That(repository.Current.PromptCount, Is.EqualTo(7));
        Assert.That(repository.Current.StripeCustomerId, Is.EqualTo("cus_existing"));
        Assert.That(repository.Current.StripeSubscriptionId, Is.Empty);
    }

    [Test]
    public void ForeignSubscriptionResponseIsNotAppliedOrAcknowledged()
    {
        var repository = new ReconciliationRepository();
        var handler = new Provider { Subscriptions = List(Subscription("sub_foreign", "active").Replace("cus_existing", "cus_foreign")) };

        Assert.ThrowsAsync<InvalidOperationException>(() => Deliver(CreateService(repository, handler), "evt_foreign_response"));
        Assert.That(repository.Processed, Is.Empty);
        Assert.That(repository.SuccessfulSaves, Is.Zero);
    }

    [Test]
    public void ProviderFailureDoesNotPersistOrAcknowledgeAnEvent()
    {
        var repository = new ReconciliationRepository();
        var service = CreateService(repository, new Provider { FailSubscriptions = true });

        Assert.ThrowsAsync<HttpRequestException>(() => Deliver(service, "evt_provider"));
        Assert.That(repository.Processed, Is.Empty);
        Assert.That(repository.SuccessfulSaves, Is.Zero);
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"id\":\"evt\",\"type\":\"invoice.paid\"}")]
    public async Task SignedMalformedPayloadIsRejected(string payload)
    {
        var repository = new ReconciliationRepository();
        var service = CreateService(repository, new Provider());

        Assert.That(await service.ProcessWebhookAsync(payload, Sign(payload)), Is.False);
        Assert.That(repository.Processed, Is.Empty);
    }

    [Test]
    public async Task InvalidAndStaleSignaturesAreRejected()
    {
        var service = CreateService(new ReconciliationRepository(), new Provider());
        var payload = Event("evt_bad");

        Assert.That(await service.ProcessWebhookAsync(payload, "t=1,v1=00"), Is.False);
        Assert.That(await service.ProcessWebhookAsync(payload, Sign(payload, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600)), Is.False);
        Assert.That(await service.ProcessWebhookAsync(payload, "t=-9223372036854775808,v1=00"), Is.False);
    }

    private static StripeBillingService CreateService(IBillingRepository repository, Provider handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_placeholder",
            ["Stripe:WebhookSecret"] = "whsec_placeholder"
        }).Build();

        return new(new FlowMateEnvironment(configuration), configuration, handler, repository);
    }

    private static Task<bool> Deliver(StripeBillingService service, string id, string type = "customer.subscription.updated")
    {
        var payload = Event(id, type);

        return service.ProcessWebhookAsync(payload, Sign(payload));
    }

    private static string Event(string id, string type = "customer.subscription.updated") => JsonSerializer.Serialize(new
    {
        id, type, data = new { @object = new { id = "sub_old", customer = "cus_existing", status = "canceled", metadata = new { user_id = "forged-user" } } }
    });

    private static string Sign(string payload, long? timestamp = null)
    {
        timestamp ??= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_placeholder"), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexString(digest)}";
    }

    private static string List(params string[] subscriptions) => "{\"data\":[" + string.Join(',', subscriptions) + "],\"has_more\":false}";

    private static string Subscription(string id, string status, long created = 100) => JsonSerializer.Serialize(new
    {
        id, customer = "cus_existing", status, created, trial_start = 1790000000, trial_end = 1800000000,
        items = new { data = new[] { new { current_period_end = 1801000000, price = new { recurring = new { interval = "year", interval_count = 1 } } } } }
    });

    private sealed class Provider : HttpMessageHandler, IHttpClientFactory
    {
        private int _subscriptionRequests;
        public int SubscriptionRequests => _subscriptionRequests;
        public string Subscriptions { get; set; } = List(Subscription("sub_current", "active"));
        public string CustomerOwner { get; init; } = "user";
        public bool FailSubscriptions { get; init; }
        public Func<int, Task<string>>? OnSubscriptions { get; init; }
        public string? LastQuery { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get), "Reconciliation must never charge or create a subscription.");
            string json;

            if (request.RequestUri!.AbsolutePath.Contains("/customers/", StringComparison.Ordinal))
            {
                json = JsonSerializer.Serialize(new { id = "cus_existing", metadata = new { user_id = CustomerOwner } });
            }
            else
            {
                LastQuery = request.RequestUri.Query;
                var count = Interlocked.Increment(ref _subscriptionRequests);

                if (FailSubscriptions)
                {
                    return new(HttpStatusCode.ServiceUnavailable);
                }

                json = OnSubscriptions is null ? Subscriptions : await OnSubscriptions(count);
            }

            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    private sealed class ReconciliationRepository : IBillingRepository
    {
        public BillingEntitlementDocument Current { get; private set; } = new()
        {
            UserId = "user", StripeCustomerId = "cus_existing", ETag = "0", PromptMonth = "2026-10", PromptCount = 7
        };
        public HashSet<string> Processed { get; } = [];
        public bool FailMarker { get; set; }
        public bool FailSave { get; set; }
        public int SuccessfulSaves { get; private set; }
        public int Conflicts { get; private set; }

        public Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default)
        {
            lock (Processed)
            {
                return Task.FromResult(userId == Current.UserId ? Clone(Current) : new BillingEntitlementDocument { UserId = userId });
            }
        }

        public Task<bool> TrySaveEntitlementAsync(BillingEntitlementDocument document, string? expectedRevision, CancellationToken cancellationToken = default)
        {
            lock (Processed)
            {
                if (FailSave)
                {
                    throw new IOException("Storage unavailable");
                }

                if (Current.ETag != expectedRevision)
                {
                    Conflicts++;

                    return Task.FromResult(false);
                }

                SuccessfulSaves++;
                document.ETag = SuccessfulSaves.ToString();
                Current = Clone(document);

                return Task.FromResult(true);
            }
        }

        public Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default)
        {
            lock (Processed)
            {
                return Task.FromResult(Processed.Contains(eventId));
            }
        }

        public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            lock (Processed)
            {
                if (FailMarker)
                {
                    throw new IOException("Event storage unavailable");
                }

                Processed.Add(eventId);

                return Task.CompletedTask;
            }
        }

        private static BillingEntitlementDocument Clone(BillingEntitlementDocument document) =>
            JsonSerializer.Deserialize<BillingEntitlementDocument>(JsonSerializer.Serialize(document))!;
    }
}
