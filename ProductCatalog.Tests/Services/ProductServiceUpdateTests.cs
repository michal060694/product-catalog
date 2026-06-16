using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Application.DTOs;
using ProductCatalog.Application.Services;
using ProductCatalog.Domain.Cache;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.Repositories;

namespace ProductCatalog.Tests.Services;

public class ProductServiceUpdateTests
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly IMapper _mapper;
    private readonly ProductService _sut;

    public ProductServiceUpdateTests()
    {
        _repo   = A.Fake<IProductRepository>();
        _cache  = A.Fake<IProductCache>();
        _mapper = A.Fake<IMapper>();

        A.CallTo(() => _mapper.Map(A<UpdateProductDto>._, A<Product>._))
            .Invokes(call =>
            {
                var dto = (UpdateProductDto)call.Arguments[0]!;
                var entity = (Product)call.Arguments[1]!;
                entity.Name  = dto.Name;
                entity.Price = dto.Price;
                entity.Stock = dto.Stock;
            });

        A.CallTo(() => _mapper.Map<ProductDto>(A<Product>._))
            .ReturnsLazily(call =>
            {
                var p = (Product)call.Arguments[0]!;
                return new ProductDto(p.Id, p.Name, p.Price, p.Stock);
            });

        _sut = new ProductService(_repo, _cache, _mapper, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task Given_ValidDto_WhenUpdateProductAsync_Then_UpdatesRepoAndBumpsVersion()
    {
        var existing = new Product { Id = 1, Name = "Old", Price = 100m, Stock = 5, Version = 1 };
        A.CallTo(() => _repo.GetById(1)).Returns(existing);

        var dto = new UpdateProductDto("New", 200m, 10);
        await _sut.UpdateProductAsync(1, dto);

        A.CallTo(() => _repo.Update(A<Product>.That.Matches(p =>
                p.Id      == 1    &&
                p.Name    == "New" &&
                p.Price   == 200m  &&
                p.Stock   == 10    &&
                p.Version == 2)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Given_ValidDto_WhenUpdateProductAsync_Then_RemovesFromCache()
    {
        var existing = new Product { Id = 1, Name = "Old", Price = 100m, Stock = 5, Version = 1 };
        A.CallTo(() => _repo.GetById(1)).Returns(existing);

        await _sut.UpdateProductAsync(1, new UpdateProductDto("New", 200m, 10));

        A.CallTo(() => _cache.RemoveAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Given_ValidDto_WhenUpdateProductAsync_Then_ReturnsUpdatedDto()
    {
        var existing = new Product { Id = 1, Name = "Old", Price = 100m, Stock = 5, Version = 1 };
        A.CallTo(() => _repo.GetById(1)).Returns(existing);

        var result = await _sut.UpdateProductAsync(1, new UpdateProductDto("New", 200m, 10));

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("New");
        result.Price.Should().Be(200m);
        result.Stock.Should().Be(10);
    }

    [Fact]
    public async Task Given_ProductNotFound_WhenUpdateProductAsync_Then_ReturnsNull()
    {
        A.CallTo(() => _repo.GetById(999)).Returns(null);

        var result = await _sut.UpdateProductAsync(999, new UpdateProductDto("X", 1m, 0));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Given_ProductNotFound_WhenUpdateProductAsync_Then_NeverUpdatesRepo()
    {
        A.CallTo(() => _repo.GetById(999)).Returns(null);

        await _sut.UpdateProductAsync(999, new UpdateProductDto("X", 1m, 0));

        A.CallTo(() => _repo.Update(A<Product>._)).MustNotHaveHappened();
    }
}
