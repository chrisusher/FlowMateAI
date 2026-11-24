using Shared.Enums;
using Shared.Models;

namespace Services.Tests.Workspaces;

public sealed class ProjectNameTests
{
    [TestCase(null, ProjectNameValidation.Required, "")]
    [TestCase("  ", ProjectNameValidation.Required, "")]
    [TestCase("  Project  ", ProjectNameValidation.Valid, "Project")]
    [TestCase("Project\nName", ProjectNameValidation.ContainsControlCharacters, "Project\nName")]
    public void ValidatesAndNormalises(string? input, ProjectNameValidation expected, string expectedName)
    {
        var result = ProjectName.Validate(input, out var normalised);

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(normalised, Is.EqualTo(expectedName));
    }

    [Test]
    public void EnforcesTheEightyCharacterLimitAfterTrimming()
    {
        Assert.That(ProjectName.Validate($" {new string('x', 80)} ", out _), Is.EqualTo(ProjectNameValidation.Valid));
        Assert.That(ProjectName.Validate(new string('x', 81), out _), Is.EqualTo(ProjectNameValidation.TooLong));
    }
}
