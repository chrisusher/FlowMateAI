using Shared.Enums;
using Shared.Models;
using Services.Billing;

namespace Services.Users;

public sealed class AccountDeletionService(
    IBillingService billing,
    IUserDataResetService dataReset,
    IAuth0IdentityDeletionService identity) : IAccountDeletionService
{
    public async Task<AccountDeletionResult> DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!identity.IsConfigured)
        {
            return new(false, AccountDeletionStage.Identity,
                "Account deletion is temporarily unavailable. Contact support to complete the verified request.");
        }

        var billingResult = await billing.CancelSubscriptionAsync(userId, cancellationToken);

        if (!billingResult.Succeeded)
        {
            return new(false, AccountDeletionStage.Billing,
                billingResult.Message ?? "The subscription could not be cancelled, so no account data was removed.");
        }

        UserDataResetResult data;

        try
        {
            data = await dataReset.ResetAsync(userId, cancellationToken);
        }
        catch (Shared.Exceptions.UserDataResetException exception)
        {
            return new(false, AccountDeletionStage.ApplicationData,
                "Some account data could not be removed. Retry the deletion request or contact support.", exception.Completed);
        }

        try
        {
            await identity.DeleteAsync(userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, AccountDeletionStage.Identity,
                "Your application data was removed, but the sign-in identity still needs to be deleted. Retry the request or contact support.", data);
        }

        return new(true, AccountDeletionStage.Completed, "Your FlowMate account has been deleted.", data);
    }
}
