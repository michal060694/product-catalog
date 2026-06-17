using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Application;
using ProductCatalog.Domain;

namespace ProductCatalog.Tests;

public class ProductServiceCreateTests
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly ProductService _sut;

    public ProductServiceCreateTests()
    {
        _repo  = A.Fake<IProductRepository>();
        _cache = A.Fake<IProductCache>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg => cfg.AddProfile<ProductProfile>());
        var mapper = services.BuildServiceProvider().GetRequiredService<IMapper>();

        _sut = new ProductService(_repo, _cache, A.Fake<ISharedTaskStore>(), mapper, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task CreateProductAsync_Always_CallsRepoAddOnce()
    {
        // Arrange
        var dto = new CreateProductDto("Widget", 9.99m, 100);

        // Act
        await _sut.CreateProductAsync(dto);

        // Assert
        A.CallTo(() => _repo.Add(A<Product>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateProductAsync_Always_RemovesProductKeyFromCache()
    {
        // Arrange
        var dto = new CreateProductDto("Widget", 9.99m, 100);
        A.CallTo(() => _repo.Add(A<Product>._))
            .Invokes(call => call.Arguments.Get<Product>(0)!.Id = 42);

        // Act
        await _sut.CreateProductAsync(dto);

        // Assert
        A.CallTo(() => _cache.RemoveAsync(CacheKeys.ForProduct(42), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateProductAsync_Always_ReturnsMappedDtoWithCorrectData()
    {
        // Arrange
        var dto = new CreateProductDto("Widget", 9.99m, 100);
        A.CallTo(() => _repo.Add(A<Product>._))
            .Invokes(call => call.Arguments.Get<Product>(0)!.Id = 42);

        // Act
        var result = await _sut.CreateProductAsync(dto);

        // Assert
        result.Should().BeEquivalentTo(new ProductDto(42, "Widget", 9.99m, 100));
    }
}
