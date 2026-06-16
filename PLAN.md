# Development Plan — Product Catalog Cache API
### Vertical Slices · Every Increment is Demo-Ready

---

## General

| | |
|---|---|
| **Approach** | Vertical Slices — End-to-end per task |
| **Can stop** | After Slice 5 — Full API working with Caching |

---

## Slice 1 — Solution Setup and Project References

**What we build:**

```bash
# Create the Solution
dotnet new sln -n ProductCatalog

# Create the Projects
dotnet new webapi -n ProductCatalog.Api          --no-openapi false
dotnet new classlib -n ProductCatalog.Application
dotnet new classlib -n ProductCatalog.Domain
dotnet new classlib -n ProductCatalog.Infrastructure
dotnet new xunit -n ProductCatalog.Tests

# Add to Solution
dotnet sln add ProductCatalog.Api/ProductCatalog.Api.csproj
dotnet sln add ProductCatalog.Application/ProductCatalog.Application.csproj
dotnet sln add ProductCatalog.Domain/ProductCatalog.Domain.csproj
dotnet sln add ProductCatalog.Infrastructure/ProductCatalog.Infrastructure.csproj
dotnet sln add ProductCatalog.Tests/ProductCatalog.Tests.csproj
```

**Project References:**

```
Domain        ← No dependencies (the base)
Application   ← Domain
Infrastructure← Domain
Api           ← Application + Infrastructure
Tests         ← Application + Infrastructure
```

```bash
# Api → Application + Infrastructure
dotnet add ProductCatalog.Api reference ProductCatalog.Application
dotnet add ProductCatalog.Api reference ProductCatalog.Infrastructure

# Application → Domain
dotnet add ProductCatalog.Application reference ProductCatalog.Domain

# Infrastructure → Domain
dotnet add ProductCatalog.Infrastructure reference ProductCatalog.Domain

# Tests → all
dotnet add ProductCatalog.Tests reference ProductCatalog.Application
dotnet add ProductCatalog.Tests reference ProductCatalog.Infrastructure
```

**NuGet Packages:**

```bash
# Application
dotnet add ProductCatalog.Application package AutoMapper
dotnet add ProductCatalog.Application package FluentValidation.AspNetCore

# Infrastructure
dotnet add ProductCatalog.Infrastructure package Microsoft.Extensions.Caching.Memory
dotnet add ProductCatalog.Infrastructure package Microsoft.Extensions.Logging.Abstractions

# Api
dotnet add ProductCatalog.Api package Swashbuckle.AspNetCore

# Tests
dotnet add ProductCatalog.Tests package FakeItEasy
dotnet add ProductCatalog.Tests package FluentAssertions
dotnet add ProductCatalog.Tests package Microsoft.AspNetCore.Mvc.Testing
```

**What you can demo:**
- `dotnet build` — Everything compiles without errors
- No circular references — Domain doesn't reference any other project

---

## Slice 2 — GET Product (without Cache)

**What we build:**
- `Product.cs` — Entity with Id, Name, Price, CostPrice, Stock, Version
- `IProductRepository` — GetById, Add, Update
- `InMemoryProductRepository` — seed of 3 products
- `ProductDto.cs`
- `ProductProfile.cs` mapper from Entity to Dto
- `IProductService` + `ProductService.GetProduct` — calls Repo directly
- `ProductsController` — GET /api/products/{id}
- `Program.cs` + basic DI
- `ApplicationServiceExtensions.cs` - used for DI registration from Application

**Design Decisions:**

| Topic | Decision |
|---|---|
| Repository Storage | `ConcurrentDictionary` — built-in thread-safe, suitable for InMemory |

**What you can demo:**
- Swagger: GET → 200 OK with ProductDto
- GET on non-existing ID → 404
---

## Slice 3 — Cache Layer (Hit / Miss)

**What we build:**
- `IProductCache` — Get, Set, Remove
- `CacheKeys.cs` — `ForProduct(Guid id)`
- `MemoryProductCache` — wraps IMemoryCache + Absolute Expiration
- `ProductService.GetProduct` — Cache → Miss → Repo → Set Cache
- Logger: `"Cache HIT"` / `"Cache MISS"`

**Design Decisions:**

| Topic | Decision |
|---|---|
| Cache Implementation | `IMemoryCache` |
| Cache Abstraction | `IProductCache` — allows switching to Redis in the future without changes in Application |
| Expiration | Absolute Expiration — fixed, predictable lifetime |
| Null Caching | Disabled — we don't store `null` in cache |
| 404 Caching | Disabled — non-existing products are not stored in cache |
| Future Cache Migration | Redis-ready — only replace `MemoryProductCache`, everything else stays |

**What you can demo:**
- Request 1 → log "Cache MISS" → comes from Repo
- Request 2 → log "Cache HIT" → comes from Cache
- TTL expiry → MISS again after expiration

---

## Slice 4 — POST (Create + Invalidation)

**What we build:**
- `CreateProductDto.cs`
- `CreateProductDtoValidator` — FluentValidation (Name not empty, Price > 0, Stock >= 0)
- `ProductService.CreateProduct` — Add to Repo → Remove from Cache
- `ProductProfile.cs` — AutoMapper
- Controller: POST /api/products → 201 Created + Location header

**Design Decisions:**

| Topic | Decision |
|---|---|
| Validation | FluentValidation — input validation at the system boundary, not in Entity |
| Mapping | AutoMapper — DTO protects against exposing sensitive data from the Entity |
| Invalidation | Remove only — we don't populate cache for a product that may not be requested |

**What you can demo:**
- POST product → 201 Created
- GET immediately after → 200 (comes from Repo because Cache was cleared)
- POST with empty Name → 400 ValidationError

---

## Slice 5 — PUT (Update + Cache Invalidation)

**What we build:**
- `UpdateProductDto.cs`
- `UpdateProductDtoValidator` — FluentValidation
- `ProductService.UpdateProduct` — Update in Repo → `Remove` from Cache (not Set!)
- Version bump on Entity — solution for Race Condition of concurrent GET+PUT
- Controller: PUT /api/products/{id} → 200 OK

**Design Decisions:**

| Topic | Decision |
|---|---|
| Update Strategy | Invalidate instead of Update — don't waste memory on products that may not be requested again |
| Race Condition Protection | Versioning — `Set` in Cache checks that the version from Repo ≥ the version in Cache |

**What you can demo:**
- PUT product → cache deleted → GET immediately after → fresh from Repo
- PUT on non-existing ID → 404

---

## Slice 6 — Request Coalescing (SharedTaskStore) 

**What we build:**
- `ISharedTaskStore` — GetOrAdd, Remove
- `SharedTaskStore` — `ConcurrentDictionary<string, Task<ProductDto?>>`
- `ProductService.GetProduct` — Cache Miss → GetOrAdd:
  - Task exists? → await it (reuse)
  - Task doesn't exist? → create new → Repo → Set Cache → Remove Task
- Logger: `"InFlight CREATED"` / `"InFlight REUSED"` / `"InFlight COMPLETED"`

**Design Decisions:**

| Topic | Decision |
|---|---|
| Stampede Prevention | `SharedTaskStore` — shares one Task for all concurrent requests, not a Semaphore that blocks |

**What you can demo:**
- 100 concurrent GETs on the same product → only **1** call to Repo
- Logs show: 1 CREATED + 99 × REUSED

---

## Slice 7 — Error Handling Middleware

**What we build:**
- `ExceptionHandlingMiddleware` — centralized catching:
  - `ProductNotFoundException` → 404 ProblemDetails
  - `ValidationException` → 400 + errors per field
  - `Exception` → 500 (no stack trace in response)
- `ApplicationBuilderExtensions` — `UseExceptionHandling()`
- `ServiceCollectionExtensions` — full organized DI

**Design Decisions:**

| Topic | Decision |
|---|---|
| Error Handling | Central Middleware — no try/catch in every Controller |

**What you can demo:**
- Every error returns a structured ProblemDetails
- No internal details exposed in 500
- DTO prevents exposing sensitive data from the Entity

**Security topics:**
> - Cache Key is composed of `product:{id}` — if there were permissions, we would add `userId` to the key
> - Cache Poisoning: we update Cache **only** from what comes back from Repository, not from user input
> - FluentValidation validates inputs at the system boundary

---

## Slice 8 — Tests

**What we build:**

| File | Scenarios |
|---|---|
| `CacheTests.cs` | Hit, Miss, TTL expiry, Invalidation after PUT/POST |
| `ConcurrencyTests.cs` | 100 concurrent → 1 Task created, Task deleted after completion, Race GET+PUT |
| `ProductServiceTests.cs` | GetProduct (hit/miss), CreateProduct, UpdateProduct, Not Found |
| `ExceptionHandlingTests.cs` | 404, 400 with validation errors, 500 |

**What you can demo:**
- `dotnet test` — all green
- Mock on `IProductCache` and `ISharedTaskStore` (not on `IMemoryCache` directly)



## DI Registration (Program.cs)

```csharp
// Singleton — shared state
services.AddSingleton<IProductRepository, InMemoryProductRepository>();
services.AddSingleton<IProductCache, MemoryProductCache>();
services.AddSingleton<ISharedTaskStore, SharedTaskStore>();
services.AddMemoryCache();

// Scoped
services.AddScoped<IProductService, ProductService>();
services.AddAutoMapper(typeof(ProductProfile));
services.AddFluentValidationAutoValidation();
```

---

## Project Structure

```
src/
├── ProductCatalog.Api
│   ├── Controllers/ProductsController.cs
│   ├── Middleware/ExceptionHandlingMiddleware.cs
│   ├── Extensions/ServiceCollectionExtensions.cs
│   ├── Extensions/ApplicationBuilderExtensions.cs
│   └── Program.cs
├── ProductCatalog.Application
│   ├── Services/IProductService.cs
│   ├── Services/ProductService.cs
│   ├── DTOs/ProductDto.cs
│   ├── DTOs/CreateProductDto.cs
│   ├── DTOs/UpdateProductDto.cs
│   ├── Mapping/ProductProfile.cs
│   └── Validation/
├── ProductCatalog.Domain
│   ├── Entities/Product.cs
│   ├── Exceptions/ProductNotFoundException.cs
│   └── Contracts/
│       ├── IProductRepository.cs
│       ├── IProductCache.cs
│       └── ISharedTaskStore.cs
├── ProductCatalog.Infrastructure
│   ├── Repository/InMemoryProductRepository.cs
│   ├── Cache/MemoryProductCache.cs
│   ├── Cache/CacheKeys.cs
│   └── Concurrency/SharedTaskStore.cs
└── ProductCatalog.Tests
    ├── ProductServiceTests.cs
    ├── CacheTests.cs
    ├── ConcurrencyTests.cs
    └── ExceptionHandlingTests.cs
```

---

## Summary

| Slice | Output |
|---|---|
| 1 | Solution + Projects + References |
| 2 | GET working end-to-end |
| 3 | Cache Hit/Miss with logs |
| 4 | POST + Validation |
| 5 | PUT + Invalidation |
| 6 | Request Coalescing ⭐ |
| 7 | Error Middleware |
| 8 | Tests — all green |

