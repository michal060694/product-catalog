namespace ProductCatalog.Infrastructure.Cache;

public class CacheSettings
{
    public double ProductTtlMinutes { get; set; } = 5;
    public double InFlightTimeoutSeconds { get; set; } = 30;
}
