using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Services.Repositories;

namespace API.Security;

internal static class AccountOperation
{
    private static readonly object ItemKey = new();

    public static async Task<bool> BeginAsync(HttpRequestData request, string userId, CancellationToken cancellationToken)
    {
        var markers = request.FunctionContext.InstanceServices.GetRequiredService<IAccountDeletionMarkerRepository>();
        var operationId = Guid.NewGuid().ToString("N");

        if (!await markers.TryBeginOperationAsync(userId, operationId, cancellationToken))
        {
            return false;
        }

        request.FunctionContext.Items[ItemKey] = (markers, userId, operationId);

        return true;
    }

    public static async Task CompleteAsync(FunctionContext context)
    {
        if (context.Items.Remove(ItemKey, out var value) &&
            value is ValueTuple<IAccountDeletionMarkerRepository, string, string> operation)
        {
            await operation.Item1.EndOperationAsync(operation.Item2, operation.Item3, CancellationToken.None);
        }
    }
}
