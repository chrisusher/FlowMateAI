using Shared.Enums;

namespace Shared.Models;

/// <summary>Normalises and validates names at both the client and API boundaries.</summary>
public static class ProjectName
{
    public static ProjectNameValidation Validate(string? value, out string normalised)
    {
        normalised = value?.Trim() ?? "";

        if (normalised.Length == 0)
        {
            return ProjectNameValidation.Required;
        }

        if (normalised.Length > 80)
        {
            return ProjectNameValidation.TooLong;
        }

        return normalised.Any(char.IsControl)
            ? ProjectNameValidation.ContainsControlCharacters
            : ProjectNameValidation.Valid;
    }
}
