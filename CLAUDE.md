# CLAUDE.md — Product Catalog Cache API

## Project Overview

Implementation of the **Caching Strategy & Consistency** assignment in .NET 9 / ASP.NET Core.  
Goal: A Product Catalog REST API demonstrating In-Memory Caching, Cache Invalidation, Request Coalescing, and Generation-Based Race Condition Prevention.  
Full development plan: [PLAN.md](./PLAN.md).

---

## Project Structure

```
ProductCatalog.sln
│
├── ProductCatalog.Domain/
│   ├── Entities/
│   │   └── Product.cs                    ← Entity: Id, Name, Price, CostPrice, Stock (no Version field)
│   ├── Exceptions/
│   │   └── ProductNotFoundException.cs   ← Domain exception — caught by Middleware → 404
│   ├── Repositories/
│   │   ├── IProductRepository.cs         ← Data contract (GetById / Add / Update)
│   │   └── IProductCache.cs              ← Cache contract (GetAsync / GetGenerationAsync / SetAsync / RemoveAsync)
│   ├── Cache/
│   │   └── CacheKeys.cs                  ← Static function: ForProduct(id) → "product:{id}"
│   └── TaskStore/
│       └── ISharedTaskStore.cs           ← Request coalescing contract
│
├── ProductCatalog.Application/
│   ├── Services/
│   │   ├── IProductService.cs
│   │   └── ProductService.cs             ← Cache + coalescing + invalidation logic
│   ├── DTOs/
│   │   ├── ProductDto.cs                 ← record(Id, Name, Price, Stock) — exposed to clients
│   │   ├── CreateProductDto.cs           ← record(Name, Price, Stock)
│   │   └── UpdateProductDto.cs           ← record(Name, Price, Stock)
│   ├── Mappings/
│   │   └── ProductProfile.cs             ← AutoMapper: Product ↔ DTO (CostPrice hidden)
│   ├── Validators/
│   │   ├── CreateProductDtoValidator.cs
│   │   └── UpdateProductDtoValidator.cs
│   └── Extensions/
│       └── ApplicationServiceExtensions.cs ← AddApplication()
│
├── ProductCatalog.Infrastructure/
│   ├── Repositories/
│   │   └── InMemoryProductRepository.cs  ← ConcurrentDictionary + Interlocked ID generation
│   ├── Cache/
│   │   ├── MemoryProductCache.cs         ← IMemoryCache + Generation Guard (per-key lock) + AbsoluteExpiration
│   │   └── CacheSettings.cs              ← ProductTtlMinutes (default: 5) + InFlightTimeoutSeconds (default: 30)
│   ├── TaskStore/
│   │   └── SharedTaskStore.cs            ← ConcurrentDictionary<string, Lazy<Task<Product?>>> + timeout cleanup
│   └── Extensions/
│       └── InfrastructureServiceExtensions.cs ← AddInfrastructure()
│
├── ProductCatalog.Api/
│   ├── Controllers/
│   │   └── ProductsController.cs         ← Orchestration only, no business logic
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs ← ProductNotFoundException→404, ValidationException→400, Exception→500
│   ├── Extensions/
│   │   └── ApplicationBuilderExtensions.cs ← UseExceptionHandling()
│   ├── Program.cs
│   └── appsettings.json
│
└── ProductCatalog.Tests/
    ├── Services/
    │   ├── ProductServiceGetTests.cs      ← Cache HIT/MISS, repository used only on MISS
    │   ├── ProductServiceCreateTests.cs   ← Creation, insertion to repo, cache invalidation
    │   └── ProductServiceUpdateTests.cs   ← Update, cache invalidation, 404
    └── StaleDataExamples/
        ├── CoalescingTests.cs             ← 10 concurrent requests → factory called exactly once
        ├── StaleCacheWriteTests.cs        ← Generation guard rejects stale writes
        ├── ConcurrentDictionaryTests.cs   ← ConcurrentDictionary thread-safety demo
        └── TocTouTests.cs                 ← TOCTOU race condition demo
```

**One absolute rule:** `Application` has no knowledge of `Infrastructure`. They are connected exclusively through DI in `Program.cs`.

---

## Technology Stack

| Category | Technology | Version |
|---|---|---|
| Runtime | .NET 9 / ASP.NET Core | 9.0 |
| Cache | `IMemoryCache` wrapped in `IProductCache` | Microsoft.Extensions.Caching.Memory 10.0.9 |
| Mapping | AutoMapper | 16.1.1 |
| Validation | FluentValidation.AspNetCore | 11.3.1 |
| Testing | xUnit + FakeItEasy + FluentAssertions | 2.9.2 / 9.0.1 / 8.10.0 |
| API Docs | Swashbuckle.AspNetCore (Swagger) | 10.2.1 |
| Nullable | `<Nullable>enable</Nullable>` in all projects | — |

---

## API Endpoints

| Method | Route | Description | Response |
|---|---|---|---|
| `GET` | `/api/products/{id}` | Get product by ID (Cache-first) | `200 ProductDto` / `404` |
| `POST` | `/api/products` | Create new product + cache invalidation | `201 ProductDto` / `400` |
| `PUT` | `/api/products/{id}` | Update product + cache invalidation | `200 ProductDto` / `400` / `404` |

Swagger UI available at `/swagger` in the Development environment.

---

## Key Architecture Decisions

### Cache Invalidation — Remove Only
After `PUT`, only `RemoveAsync` is called.  
No new value is written to cache — the next value will be read from the Repository on the next request.  
**Reason:** Prevents race conditions between invalidation and cache updates.

### Absolute Expiration
`ProductTtlMinutes` (default: 5 minutes, actual in `appsettings.json`: 1 minute).  
No `SlidingExpiration` — TTL is guaranteed and calculated simply.

### Stampede Prevention — SharedTaskStore
`ConcurrentDictionary<string, Lazy<Task<Product?>>>`.  
Concurrent requests for the same uncached product produce **a single factory call**.  
No Semaphore, no lock — `Lazy` guarantees atomic creation and a shared Task.  
The Task is removed from the Dictionary in `ContinueWith` immediately after completion.  
A secondary timeout (`InFlightTimeoutSeconds`, default: 30s) also evicts the entry as a safety net.

### Generation Guard in Cache
`MemoryProductCache` maintains a `ConcurrentDictionary<string, long> _generations` and a per-key lock.

**Write path (`SetAsync`):** Under lock, checks:
```
if _generations[key] != expectedGeneration → do not write (key was invalidated since factory started)
```

**Invalidation path (`RemoveAsync`):** Under lock, removes the cached entry **and** increments the generation counter.

**How it prevents TOCTOU:** When `GetProductAsync` runs on a cache MISS, it captures the current generation *before* calling the repository factory. If a PUT invalidates the key while the factory is in flight, the generation increments. When the factory tries to write, it detects the mismatch and silently discards the stale value.

### CostPrice — Sensitive Data
`Product.CostPrice` is not mapped to `ProductDto`.  
`ProductProfile` ignores `CostPrice` in all mappings.  
**Never exposed to clients, logs, or cache responses.**

### Redis-Readiness
`IProductCache` serves as the abstraction layer.  
Switching to Redis = replacing `MemoryProductCache` with `RedisProductCache` only — no changes in Application.

---

## DI Registration (Lifetimes)

```csharp
// Program.cs → AddInfrastructure()
services.Configure<CacheSettings>(configuration.GetSection("Cache"));
services.AddMemoryCache();
services.AddSingleton<IProductRepository, InMemoryProductRepository>();  // shared state
services.AddSingleton<IProductCache, MemoryProductCache>();              // shared IMemoryCache
services.AddSingleton<ISharedTaskStore, SharedTaskStore>();              // shared in-flight dict

// Program.cs → AddApplication()
services.AddScoped<IProductService, ProductService>();
services.AddAutoMapper(cfg => cfg.AddProfile<ProductProfile>());
services.AddValidatorsFromAssemblyContaining<CreateProductDtoValidator>();
```

**Important:** `IProductRepository`, `IProductCache`, and `ISharedTaskStore` must be `Singleton` — they hold shared state. Registering them as `Scoped` will cause cache loss and concurrency bugs.

---

## Cache Flow — GET /api/v1/products/{id}

```
Request arrives
    │
    ▼
IProductCache.GetAsync(key)
    ├── HIT  → log "Cache HIT"  → Map → return ProductDto
    │
    └── MISS → log "Cache MISS"
                    │
                    ▼
          ISharedTaskStore.GetOrAddAsync(key, factory)
                    ├── InFlight EXISTS → log "InFlight REUSED" → await shared Task
                    │
                    └── InFlight NEW → log "InFlight CREATED"
                                        │
                                        ▼
                              gen = IProductCache.GetGenerationAsync(key)   ← capture generation
                                        │
                                        ▼
                              IProductRepository.GetById(id)
                                        ├── null → throw ProductNotFoundException → 404
                                        │
                                        └── Product → IProductCache.SetAsync(key, product, gen)
                                                            │ (Generation Guard: discard if gen changed)
                                                            ▼
                                                  return ProductDto
```

---

## Code Standards (Infraedge Clean Code Standards)

### SRP and Methods
- Each method does one thing only
- Method length: up to 40–60 lines, up to 3 levels of indentation
- Controllers are Orchestration only — no business logic

### Layers — Absolute Prohibitions
- `Domain` has no knowledge of `Infrastructure`, `Application`, or `Api`
- `Application` has no knowledge of `Infrastructure` or `Api`
- `IMemoryCache` does not appear outside `Infrastructure`
- `Product` Entity does not leave `Application` outward

### Validation
- Technical validation (null / format / range) — at the Edge only with FluentValidation
- Business validation — in Domain / Application
- No duplicated validation between layers

### DTOs
- Every DTO is defined as a `record`
- Entities are never exposed directly to clients
- All Entity → DTO mappings go through `ProductProfile` (AutoMapper)

### Async
- Every async method returns `Task` with `CancellationToken`
- Forbidden: `.Result`, `.Wait()`, `async void`
- `CancellationToken.None` is allowed **only** in `ContinueWith` of SharedTaskStore (cleanup that must run)

### Exception Handling
- `ProductNotFoundException` → caught by Middleware → `404 ProblemDetails`
- `ValidationException` → caught by Middleware → `400 ValidationProblemDetails` with field errors
- General `Exception` → caught by Middleware → `500 ProblemDetails` without stack trace
- Never swallow exceptions silently (empty catch)

### DI
- Constructor injection only
- No `new` for dependencies inside Services
- No `IServiceProvider.GetService` in business logic

### Nullability
- `<Nullable>enable</Nullable>` in all projects
- Methods that may return null are declared as `T?` and handled at the call site

### Naming
- `Async` suffix on every asynchronous method
- Names that express **intent**, not implementation
- Test names: `MethodName_Scenario_ExpectedBehavior` (Given-When-Then style)

### Logging
Structured logging with context. Required log tokens:
```
"Cache HIT for Product ID: {ProductId}."
"Cache MISS for Product ID: {ProductId}. Fetching from repository."
"InFlight CREATED for cache key {CacheKey}."
"InFlight REUSED for cache key {CacheKey}."
"InFlight COMPLETED for cache key {CacheKey}."
"Product created with ID: {ProductId}."
"Cache INVALIDATED for Product ID: {ProductId} after update."
```
Never log PII, cost prices (CostPrice), or secrets.

---

## Test Coverage

| File | What is tested |
|---|---|
| `Services/ProductServiceGetTests` | Cache HIT/MISS, repository used only on MISS |
| `Services/ProductServiceCreateTests` | Creation, insertion to repo, cache invalidation |
| `Services/ProductServiceUpdateTests` | Update, cache invalidation, 404 on missing product |
| `StaleDataExamples/CoalescingTests` | Concurrent requests → factory called exactly once |
| `StaleDataExamples/StaleCacheWriteTests` | Generation guard rejects stale writes after invalidation |
| `StaleDataExamples/ConcurrentDictionaryTests` | ConcurrentDictionary thread-safety under concurrent load |
| `StaleDataExamples/TocTouTests` | TOCTOU race condition demo and how generation guard prevents it |

Run with: `dotnet test`

---

## Security

| Threat | Defense |
|---|---|
| Cache Poisoning | Cache is updated **only** from Repository output, never from user input |
| Sensitive data in response | `CostPrice` is not mapped to DTO |
| Stack trace in production | Middleware returns a generic message on 500 |
| Cache key collision | `CacheKeys.ForProduct(id)` → `"product:{id}"` — extendable to `product:{tenantId}:{id}` |
| Input injection | FluentValidation validates at the system boundary (Edge) |
| TOCTOU stale write | Generation Guard in `SetAsync` rejects writes if key was invalidated mid-flight |

---

## What is **Forbidden** in This Project

- Business logic in `ProductsController` — it is Orchestration only
- Writing to cache after PUT — only `Remove`
- `SlidingExpiration` — only `AbsoluteExpiration`
- Storing `null` in cache (null/404 caching is disabled)
- Using Semaphore for stampede prevention — use `SharedTaskStore`
- `IMemoryCache` directly outside Infrastructure
- Exposing `Product` Entity outside Application
- `CostPrice` in any DTO, response, or log
