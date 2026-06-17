using AutoMapper;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Application;
using ProductCatalog.Domain;

namespace ProductCatalog.Tests;

public class ProductServiceUpdateTests
{
    private readonly IProductRepository _repo;
    private readonly IProductCache _cache;
    private readonly ProductService _sut;

    public ProductServiceUpdateTests()
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
    public async Task UpdateProductAsync_WhenProductNotFound_ThrowsProductNotFoundException()
    {
        // Arrange
        A.CallTo(() => _repo.GetById(99)).Returns<Product?>(null);
        var dto = new UpdateProductDto("NewName", 15m, 5);

        // Act
        var act = () => _sut.UpdateProductAsync(99, dto);

        // Assert
        await act.Should().ThrowAsync<ProductNotFoundException>();
    }

    [Fact]
    public async Task UpdateProductAsync_WhenProductFound_IncrementsVersion_CallsRepoUpdate_AndInvalidatesCache()
    {
        // Arrange
        var existing = new Product { Id = 1, Name = "Old", Price = 5m, Stock = 3, Version = 2 };
        A.CallTo(() => _repo.GetById(1)).Returns(existing);
        var dto = new UpdateProductDto("New", 10m, 7);

        // Act
        await _sut.UpdateProductAsync(1, dto);

        // Assert
        existing.Version.Should().Be(3, "service must increment version before persisting");
        A.CallTo(() => _repo.Update(existing)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _cache.RemoveAsync(CacheKeys.ForProduct(1), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateProductAsync_WhenProductFound_ReturnsDtoWithUpdatedFields()
    {
        // Arrange
        var existing = new Product { Id = 1, Name = "OldName", Price = 5m, Stock = 3, Version = 1 };
        A.CallTo(() => _repo.GetById(1)).Returns(existing);
        var dto = new UpdateProductDto("NewName", 20m, 50);

        // Act
        var result = await _sut.UpdateProductAsync(1, dto);

        // Assert
        result.Should().BeEquivalentTo(new ProductDto(1, "NewName", 20m, 50));
    }
}
