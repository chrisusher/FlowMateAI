using Shared.Models;

namespace Shared.Exceptions;

public sealed class UserDataResetException(
    string stage,
    UserDataResetResult completed,
    Exception innerException)
    : Exception($"User data reset failed during {stage} cleanup.", innerException)
{
    public string Stage { get; } = stage;
    
    public UserDataResetResult Completed { get; } = completed;
}
