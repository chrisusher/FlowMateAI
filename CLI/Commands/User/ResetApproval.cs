namespace CLI.Commands.User;

internal static class ResetApproval
{
    public static bool TryApprove(TextReader input, TextWriter output, bool inputRedirected)
    {
        if (inputRedirected)
        {
            return false;
        }

        output.Write("Delete this user's FlowMate data? [y/N] ");
        output.Flush();
        var response = input.ReadLine()?.Trim();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
