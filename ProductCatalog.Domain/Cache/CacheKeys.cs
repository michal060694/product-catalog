namespace ProductCatalog.Domain;

public static class CacheKeys
{
    public static string ForProduct(int id) => $"product:{id}";
}
