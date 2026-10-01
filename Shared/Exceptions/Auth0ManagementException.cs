namespace Shared.Exceptions;

public sealed class Auth0ManagementException(string message) : Exception(message)
{
}
