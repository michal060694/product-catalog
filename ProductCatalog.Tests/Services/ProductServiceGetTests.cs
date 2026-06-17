using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Application.Mappings;
using ProductCatalog.Application.Services;
using ProductCatalog.Domain.Cache;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Exceptions;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Domain.TaskStore;

namespace ProductCatalog.Tests.Services;

public class ProductServiceGetTests
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly ISharedTaskStore _taskStore;
    private readonly ProductService _sut;

    public ProductServiceGetTests()
    {
        _repo      = A.Fake<IProductRepository>();
        _cache     = A.Fake<IProductCache>();
        _taskStore = A.Fake<ISharedTaskStore>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg => cfg.AddProfile<ProductProfile>());
        var mapper = services.BuildServiceProvider().GetRequiredService<IMapper>();

        _sut = new ProductService(_repo, _cache, _taskStore, mapper, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task GetProductAsync_WhenCacheHit_ReturnsMappedDto_AndDoesNotCallRepo()
    {
        // Arrange
        var cached = new Product { Id = 1, Name = "Widget", Price = 9.99m, Stock = 10, CostPrice = 5m };
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .Returns(cached);

        // Act
        var result = await _sut.GetProductAsync(1);

        // Assert
        result.Should().BeEquivalentTo(new ProductDto(1, "Widget", 9.99m, 10));
        A.CallTo(() => _repo.GetById(A<int>._)).MustNotHaveHappened();
        A.CallTo(() => _taskStore.GetOrAddAsync(A<string>._, A<Func<Task<Product?>>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task GetProductAsync_WhenCacheMiss_AndProductExists_CallsRepo_SetsCache_AndReturnsDto()
    {
        // Arrange
        var product = new Product { Id = 1, Name = "Widget", Price = 9.99m, Stock = 10 };
        const long generation = 0L;

        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _cache.GetGenerationAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .Returns(generation);
        A.CallTo(() => _repo.GetById(1))
            .Returns(product);
        A.CallTo(() => _taskStore.GetOrAddAsync(A<string>._, A<Func<Task<Product?>>>._))
            .ReturnsLazily(call => call.Arguments.Get<Func<Task<Product?>>>(1)!());

        // Act
        var result = await _sut.GetProductAsync(1);

        // Assert
        result.Should().BeEquivalentTo(new ProductDto(1, "Widget", 9.99m, 10));
        A.CallTo(() => _repo.GetById(1)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _cache.SetAsync(CacheKeys.ForProduct(1), product, generation, CancellationToken.None))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetProductAsync_WhenCacheMiss_AndProductNotFound_ThrowsProductNotFoundException()
    {
        // Arrange
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(99), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _cache.GetGenerationAsync(CacheKeys.ForProduct(99), A<CancellationToken>._))
            .Returns(0L);
        A.CallTo(() => _repo.GetById(99))
            .Returns<Product?>(null);
        A.CallTo(() => _taskStore.GetOrAddAsync(A<string>._, A<Func<Task<Product?>>>._))
            .ReturnsLazily(call => call.Arguments.Get<Func<Task<Product?>>>(1)!());

        // Act
        var act = () => _sut.GetProductAsync(99);

        // Assert
        await act.Should().ThrowAsync<ProductNotFoundException>();
    }
}
