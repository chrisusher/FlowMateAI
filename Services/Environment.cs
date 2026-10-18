namespace Services;

public class Environment
{
    private readonly IConfiguration _configuration;

    public Environment(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? GetWebAppBaseUrl()
    {
        var origin = _configuration["WebApp:Origin"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        var basePath = _configuration["WebApp:BasePath"]?.Trim('/');

        return string.IsNullOrWhiteSpace(basePath) ? origin : $"{origin}/{basePath}";
    }
}
