using Shared.Enums;

namespace Shared.Config;

public class TestConfig
{
    public EnvironmentType Environment { get; set; } = EnvironmentType.Local;

    public Guid UserId { get; set; } = Guid.Empty;

    public bool LocalSetup()
    {
        switch (Environment)
        {
            case EnvironmentType.Local:
            case EnvironmentType.Development:
                return true;
            default:
                return false;
        }
    }
}
