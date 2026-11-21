using Services.Billing;
using Services.Users;
using Services.Repositories;
using Shared.Contracts;

namespace Services.Tests.Users;

[TestFixture]
public sealed class AccountDeletionServiceTests
{
    [Test]
    public async Task DeletionCancelsBillingBeforeRemovingDataAndIdentity()
    {
        var order = new List<string>();
        var billing = new StubBillingService(order);
        var data = new StubDataResetService(order);
        var identity = new StubIdentityDeletionService(order);

        var result = await new AccountDeletionService(billing, data, identity, new StubMarkers(order)).DeleteAsync("auth0|user-1");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(order, Is.EqualTo(new[] { "marker", "billing", "data", "identity" }));
        Assert.That(result.Stage, Is.EqualTo(AccountDeletionStage.Completed));
    }

    [Test]
    public async Task DeletionDoesNotRemoveDataWhenBillingCannotBeCancelled()
    {
        var order = new List<string>();
        var billing = new StubBillingService(order) { CancellationResult = new(false, null, BillingActionCode.StripeUnavailable, "Stripe is unavailable.") };
        var data = new StubDataResetService(order);
        var identity = new StubIdentityDeletionService(order);

        var result = await new AccountDeletionService(billing, data, identity, new StubMarkers(order)).DeleteAsync("auth0|user-1");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Stage, Is.EqualTo(AccountDeletionStage.Billing));
        Assert.That(order, Is.EqualTo(new[] { "marker", "billing" }));
    }

    [Test]
    public async Task DeletionDoesNotRemoveDataWhenAccessCannotBeDisabled()
    {
        var order = new List<string>();
        var result = await new AccountDeletionService(new StubBillingService(order), new StubDataResetService(order),
            new StubIdentityDeletionService(order), new FailingMarkers()).DeleteAsync("auth0|user-1");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Stage, Is.EqualTo(AccountDeletionStage.ApplicationData));
        Assert.That(order, Is.Empty);
    }

    [Test]
    public async Task DeletionWaitsForAnAlreadyAdmittedCheckout()
    {
        var order = new List<string>();
        var result = await new AccountDeletionService(new StubBillingService(order), new StubDataResetService(order),
            new StubIdentityDeletionService(order), new ActiveCheckoutMarkers()).DeleteAsync("auth0|user-1");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Stage, Is.EqualTo(AccountDeletionStage.ApplicationData));
        Assert.That(order, Is.Empty);
    }

    [Test]
    public async Task DeletionReportsRetryableIdentityFailureAfterDataCleanup()
    {
        var order = new List<string>();
        var identity = new StubIdentityDeletionService(order)
        {
            DeleteException = new InvalidOperationException("not configured")
        };

        var result = await new AccountDeletionService(new StubBillingService(order), new StubDataResetService(order), identity, new StubMarkers(order)).DeleteAsync("auth0|user-1");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Stage, Is.EqualTo(AccountDeletionStage.Identity));
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(order, Is.EqualTo(new[] { "marker", "billing", "data", "identity" }));
    }

    private sealed class StubMarkers(List<string> order) : IAccountDeletionMarkerRepository
    {
        public Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default)
        {
            order.Add("marker");

            return Task.FromResult(false);
        }
        public Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingMarkers : IAccountDeletionMarkerRepository
    {
        public Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default) => Task.FromException<bool>(new InvalidOperationException());
        public Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ActiveCheckoutMarkers : IAccountDeletionMarkerRepository
    {
        public Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubBillingService(List<string> order) : IBillingService
    {
        public BillingActionResponse CancellationResult { get; init; } = new(true, null, null, null);

        public Task<BillingActionResponse> CancelSubscriptionAsync(string userId, CancellationToken cancellationToken = default)
        {
            order.Add("billing");

            return Task.FromResult(CancellationResult);
        }

        public Task<BillingSummary> GetSummaryAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Allowed, int Used, int Limit)> ConsumePromptAsync(string userId, string month, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleasePromptAsync(string userId, string month, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BillingPrice>> GetPricesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BillingActionResponse> StartTrialAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BillingActionResponse> CreateCheckoutAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default, bool trial = false) => throw new NotSupportedException();
        public Task<BillingActionResponse> CreatePortalAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ProcessWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubDataResetService(List<string> order) : IUserDataResetService
    {
        public Task<UserDataResetResult> ResetAsync(string userId, CancellationToken cancellationToken = default)
        {
            order.Add("data");

            return Task.FromResult(new UserDataResetResult(1, 1, 1, 1));
        }
    }

    private sealed class StubIdentityDeletionService(List<string> order) : IAuth0IdentityDeletionService
    {
        public bool IsConfigured => true;
        public Exception? DeleteException
        {
            get; init;
        }

        public Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
        {
            order.Add("identity");

            return DeleteException is null ? Task.CompletedTask : Task.FromException(DeleteException);
        }
    }
}
