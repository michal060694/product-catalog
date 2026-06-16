using AutoMapper;
using FakeItEasy;
using ProductCatalog.Domain.Repositories;
using ProductCatalog.Domain.TaskStore;

namespace ProductCatalog.Tests;

public abstract class BaseTests
{
    protected readonly IProductRepository _repo      = A.Fake<IProductRepository>();
    protected readonly IProductCache      _cache     = A.Fake<IProductCache>();
    protected readonly ISharedTaskStore   _taskStore = A.Fake<ISharedTaskStore>();
    protected readonly IMapper            _mapper    = A.Fake<IMapper>();
}
