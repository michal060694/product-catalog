using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Application.Services;
using ProductCatalog.Domain.Cache;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Domain.Exceptions;
using ProductCatalog.Domain.TaskStore;

namespace ProductCatalog.Tests.TaskStore;

public class ProductServiceCoalescingTests :BaseTests
{
   
    private readonly ProductService _sut;

    public ProductServiceCoalescingTests()
    {
        A.CallTo(() => _mapper.Map<ProductDto>(A<object>._))
            .ReturnsLazily(call =>
            {
                var p = (Product)call.Arguments[0]!;
                return new ProductDto(p.Id, p.Name, p.Price, p.Stock);
            });

        _sut = new ProductService(_repo, _cache, _taskStore, _mapper, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task Given_CacheMiss_WhenGetProductAsync_Then_DelegatesTo_TaskStore()
    {
        var product = new Product { Id = 1, Name = "Laptop", Price = 4999.99m, Stock = 10 };
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _taskStore.GetOrAddAsync(CacheKeys.ForProduct(1), A<Func<Task<Product?>>>._))
            .Returns(Task.FromResult<Product?>(product));

        var result = await _sut.GetProductAsync(1);

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        A.CallTo(() => _taskStore.GetOrAddAsync(CacheKeys.ForProduct(1), A<Func<Task<Product?>>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Given_CacheHit_WhenGetProductAsync_Then_NeverCallsTaskStore()
    {
        var cached = new Product { Id = 2, Name = "Keyboard", Price = 349.90m, Stock = 50 };
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(2), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(cached));

        await _sut.GetProductAsync(2);

        A.CallTo(() => _taskStore.GetOrAddAsync(A<string>._, A<Func<Task<Product?>>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Given_TaskStore_ReturnsNull_WhenGetProductAsync_Then_ThrowsProductNotFoundException()
    {
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(99), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _taskStore.GetOrAddAsync(CacheKeys.ForProduct(99), A<Func<Task<Product?>>>._))
            .Returns(Task.FromResult<Product?>(null));

        await _sut.Invoking(s => s.GetProductAsync(99))
            .Should().ThrowAsync<ProductNotFoundException>();
    }

    [Fact]
    public async Task Given_CacheMiss_WhenGetProductAsync_Then_MapsProductToDto()
    {
        var product = new Product { Id = 3, Name = "Mouse", Price = 199m, Stock = 75 };
        A.CallTo(() => _cache.GetAsync(CacheKeys.ForProduct(3), A<CancellationToken>._))
            .Returns(Task.FromResult<Product?>(null));
        A.CallTo(() => _taskStore.GetOrAddAsync(CacheKeys.ForProduct(3), A<Func<Task<Product?>>>._))
            .Returns(Task.FromResult<Product?>(product));

        var result = await _sut.GetProductAsync(3);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Mouse");
        result.Price.Should().Be(199m);
        result.Stock.Should().Be(75);
    }
}
