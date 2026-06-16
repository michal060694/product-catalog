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

namespace ProductCatalog.Tests.Services;

public class ProductServiceCreateTests
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly ISharedTaskStore _taskStore;
    private readonly IMapper _mapper;
    private readonly ProductService _sut;

    public ProductServiceCreateTests()
    {
        _repo  = A.Fake<IProductRepository>();
        _cache = A.Fake<IProductCache>();
        _taskStore = A.Fake<ISharedTaskStore>();
        _mapper = A.Fake<IMapper>();

        A.CallTo(() => _mapper.Map<Product>(A<CreateProductDto>._))
            .ReturnsLazily(call =>
            {
                var d = (CreateProductDto)call.Arguments[0]!;
                return new Product { Name = d.Name, Price = d.Price, Stock = d.Stock };
            });

        A.CallTo(() => _repo.Add(A<Product>._))
            .Invokes(call => ((Product)call.Arguments[0]!).Id = 42);

        A.CallTo(() => _mapper.Map<ProductDto>(A<Product>._))
            .ReturnsLazily(call =>
            {
                var p = (Product)call.Arguments[0]!;
                return new ProductDto(p.Id, p.Name, p.Price, p.Stock);
            });

        _sut = new ProductService(_repo, _cache, _taskStore, _mapper, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task Given_ValidDto_WhenCreateProductAsync_Then_AddsToRepo()
    {
        var dto = new CreateProductDto("Monitor", 1299.99m, 20);

        await _sut.CreateProductAsync(dto);

        A.CallTo(() => _repo.Add(A<Product>.That.Matches(p =>
                p.Name == "Monitor" &&
                p.Price == 1299.99m &&
                p.Stock == 20 &&
                p.CostPrice == 0m)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Given_ValidDto_WhenCreateProductAsync_Then_RemovesFromCache()
    {
        var dto = new CreateProductDto("Monitor", 1299.99m, 20);

        await _sut.CreateProductAsync(dto);

        A.CallTo(() => _cache.RemoveAsync(CacheKeys.ForProduct(42), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Given_ValidDto_WhenCreateProductAsync_Then_ReturnsProductDtoWithAssignedId()
    {
        var dto = new CreateProductDto("Monitor", 1299.99m, 20);

        var result = await _sut.CreateProductAsync(dto);

        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("Monitor");
        result.Price.Should().Be(1299.99m);
        result.Stock.Should().Be(20);
    }

    [Fact]
    public async Task Given_ValidDto_WhenCreateProductAsync_Then_NeverSetsCache()
    {
        var dto = new CreateProductDto("Monitor", 1299.99m, 20);

        await _sut.CreateProductAsync(dto);

        A.CallTo(() => _cache.SetAsync(A<string>._, A<Product>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
