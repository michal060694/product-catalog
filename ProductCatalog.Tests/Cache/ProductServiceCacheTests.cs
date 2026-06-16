using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Application.Services;
using ProductCatalog.Domain.Cache;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Domain.TaskStore;

namespace ProductCatalog.Tests.Cache;

public class ProductServiceCacheTests
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly ISharedTaskStore _taskStore;
    private readonly IMapper _mapper;
    private readonly ProductService _sut;

    public ProductServiceCacheTests()
    {
        _repo = A.Fake<IProductRepository>();
        _cache = A.Fake<IProductCache>();
        _taskStore = A.Fake<ISharedTaskStore>();
        _mapper = A.Fake<IMapper>();

        A.CallTo(() => _taskStore.GetOrAddAsync(A<string>._, A<Func<Task<Product?>>>._))
            .ReturnsLazily(call =>
            {
                var factory = (Func<Task<Product?>>)call.Arguments[1]!;
                return factory();
            });

        A.CallTo(() => _mapper.Map<ProductDto>(A<object>._))
            .ReturnsLazily(call =>
            {
                var p = (Product)call.Arguments[0]!;
                return new ProductDto(p.Id, p.Name, p.Price, p.Stock);
            });

        _sut = new ProductService(_repo, _cache, _taskStore, _mapper, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task Given_CacheMiss_WhenGetProductAsync_Then_FetchesFromRepo_AndCaches()
    {
        var product = new Product { Id = 1, Name = "Laptop", Price = 4999.99m, Stock = 10 };
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _repo.GetById(1)).Returns(product);

        var result = await _sut.GetProductAsync(1);

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Laptop");
        A.CallTo(() => _cache.SetAsync(CacheKeys.ForProduct(1), product, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Given_CacheHit_WhenGetProductAsync_Then_ReturnsFromCache_AndSkipsRepo()
    {
        var cachedProduct = new Product { Id = 2, Name = "Keyboard", Price = 349.90m, Stock = 50 };
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(2), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(cachedProduct));

        var result = await _sut.GetProductAsync(2);

        result.Should().NotBeNull();
        result!.Id.Should().Be(2);
        A.CallTo(() => _repo.GetById(A<int>._)).MustNotHaveHappened();
        A.CallTo(() => _cache.SetAsync(A<string>._, A<Product>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Given_ProductNotFound_WhenGetProductAsync_Then_ReturnsNull_AndDoesNotCache()
    {
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(99), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _repo.GetById(99)).Returns(null);

        var result = await _sut.GetProductAsync(99);

        result.Should().BeNull();
        A.CallTo(() => _cache.SetAsync(A<string>._, A<Product>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
