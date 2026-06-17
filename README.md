# Product Catalog Cache API

> Interview Assignment — Caching Strategy & Consistency (.NET 8)

A Product Catalog REST API built to demonstrate production-grade caching correctness, consistency, invalidation, expiration, and concurrent-request safety.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 9 / ASP.NET Core Web API |
| Cache | `IMemoryCache` (wrapped behind `IProductCache`) |
| Validation | FluentValidation |
| Mapping | AutoMapper |
| Error Handling | Centralized middleware → RFC 7807 ProblemDetails |
| Testing | xUnit + FakeItEasy + FluentAssertions |
| Documentation | Swashbuckle (Swagger UI) |

---

## Project Structure

```
ProductCatalog.Domain          No dependencies — Entities, Interfaces, Exceptions
ProductCatalog.Application     Domain only — Services, DTOs, Validators, Mapping
ProductCatalog.Infrastructure  Domain only — Repository, Cache, SharedTaskStore
ProductCatalog.Api             Application + Infrastructure — Controllers, Middleware, DI
ProductCatalog.Tests           Unit tests — xUnit + FakeItEasy
```

**Architectural constraint:** `Application` has no reference to `Infrastructure`. They are connected only through DI in `Program.cs`.

---

## How to Run

**Prerequisites:** [.NET 9 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/michal060694/product-catalog
cd "Caching Strategy & Consistency/Project"

dotnet restore
dotnet build
dotnet run --project ProductCatalog.Api
```

Swagger UI is available at `https://localhost:7119/swagger` when running in Development mode.

---

## API Reference

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/products/{id}` | Fetch product by ID (cached) |
| `POST` | `/api/products` | Create a new product (invalidates cache) |
| `PUT` | `/api/products/{id}` | Update a product (invalidates cache) |

### Request & Response Examples

**POST /api/products**
```http
POST /api/products
Content-Type: application/json

{
  "name": "Laptop",
  "price": 4999.99,
  "costPrice": 3200.00,
  "stock": 10
}
```
```http
HTTP/1.1 201 Created
Location: /api/products/1

{
  "id": 1,
  "name": "Laptop",
  "price": 4999.99,
  "stock": 10
}
```

> `costPrice` is accepted on write but intentionally excluded from `ProductDto` — it is never exposed to the client or written to cache. For demonstration of ability to filter sensitive information from the Client

**GET /api/products/1**
```http
HTTP/1.1 200 OK

{
  "id": 1,
  "name": "Laptop",
  "price": 4999.99,
  "stock": 10
}
```

**PUT /api/products/1**
```http
PUT /api/products/1
Content-Type: application/json

{
  "name": "Gaming Laptop",
  "price": 5499.99,
  "costPrice": 3600.00,
  "stock": 8
}
```
```http
HTTP/1.1 200 OK

{
  "id": 1,
  "name": "Gaming Laptop",
  "price": 5499.99,
  "stock": 8
}
```

**Validation errors (400)**
```http
GET /api/products/0    → 400 Bad Request
GET /api/products/-5   → 400 Bad Request
GET /api/products/99   → 404 Not Found
```

---

## Cache Hit / Miss — End-to-End Flow

### Step 1 — First GET (Cache Miss)

```http
GET /api/products/1
```

```
[INFO] Cache MISS for key product:1
[INFO] InFlight CREATED for key product:1
[INFO] InFlight COMPLETED for key product:1
→ 200 OK  (fetched from repository, stored in cache)
```

### Step 2 — Second GET (Cache Hit)

```http
GET /api/products/1
```

```
[INFO] Cache HIT for key product:1
→ 200 OK  (served from cache — repository never called)
```

### Step 3 — Update Product (Cache Invalidated)

```http
PUT /api/products/1
```

```
[INFO] Cache INVALIDATED for key product:1 after update
→ 200 OK
```

### Step 4 — Next GET (Cache Miss again)

```http
GET /api/products/1
```

```
[INFO] Cache MISS for key product:1
[INFO] InFlight CREATED for key product:1
[INFO] InFlight COMPLETED for key product:1
→ 200 OK  (fetched from repository, re-cached)
```

---

## Key Design Decisions

### 1. Caching Abstraction — `IProductCache`

`IMemoryCache` is wrapped behind `IProductCache` (defined in `Domain`). This decouples the service layer from the infrastructure completely. Swapping to Redis requires only replacing `MemoryProductCache` — no changes to `ProductService`.

### 2. Expiration — Absolute Only

**Absolute expiration** (`5 minutes`, configurable via `CacheSettings`) was chosen over sliding expiration deliberately.

| | Absolute | Sliding |
|---|---|---|
| Staleness bound | Guaranteed | Unbounded for hot items |
| Predictability | High | Low |
| Troubleshooting | Easy | Hard |

A product catalog is read-heavy but not latency-critical on expiry. Absolute expiration ensures no entry lives beyond its TTL regardless of traffic, preventing silent long-lived stale reads.

### 3. Cache Invalidation — Explicit `Remove` on Mutation

On `PUT` and `POST`, the cache entry is **removed** rather than updated. The next `GET` repopulates from the repository (the single source of truth). This avoids the complexity and risk of synchronizing cache updates with partial writes.

### 4. Null / 404 Not Cached

If a product is not found in the repository, `null` is returned and nothing is written to cache. Caching a `null` result would turn a temporary "not found" into a permanent stale 404 until TTL expiry.

### 5. Cache Stampede Prevention — `SharedTaskStore`

`SharedTaskStore` uses `ConcurrentDictionary<string, Lazy<Task<Product?>>>` to coalesce concurrent in-flight requests for the same key into **one** repository call.

```
Without protection:  1,000 concurrent GET /api/products/1 → 1,000 repository hits
With SharedTaskStore: 1,000 concurrent GET /api/products/1 → 1 repository hit
```

A `Semaphore` was explicitly rejected: it serializes requests (queue), wastes threads, and requires careful release handling. `Lazy<Task<T>>` is lock-free and naturally shares the same `Task` reference.

### 6. Version Guard in `SetAsync`

`MemoryProductCache.SetAsync` checks the existing entry's `Version` before writing. If the cached version is equal to or newer than the incoming one, the write is skipped. This prevents a slow concurrent `GET` from overwriting a newer entry placed by a `PUT`-triggered invalidation + re-read cycle.

### 7. Security

- **Cache key** is `product:{id}` — deterministic and scope-safe for public catalog data. If per-user authorization is added, the key must become `user:{userId}:product:{id}` to prevent cross-user data leakage.
- **Cache poisoning** is prevented: `SetAsync` accepts only objects returned from the repository, never data from the HTTP request body.
- **DTO isolation**: `CostPrice` is present on the `Product` entity but excluded from `ProductDto`. It is never cached, never logged, never returned to the client.

---

## Test Coverage

```
dotnet test
```

| Suite | Covers |
|---|---|
| `ProductServiceCacheTests` | Cache hit / miss / not-found, null not cached |
| `MemoryProductCacheVersionGuardTests` | Version guard logic, TTL expiry |
| `ProductServiceCoalescingTests` | SharedTaskStore delegation, cache-hit bypasses store |
| `ProductServiceCreateTests` | POST flow, invalidation |
| `ProductServiceUpdateTests` | PUT flow, invalidation, not-found |
| `ConcurrencyTests` | Concurrent GET coalesces to one repo call |
| `ExceptionHandlingMiddlewareTests` | ProblemDetails shape for 404 / 400 / 500 |

All tests follow the `Given_When_Then` naming convention. Dependencies are mocked with FakeItEasy; no real HTTP stack is involved.

---

## AI Usage

AI tools (Claude) were used throughout this project as an engineering assistant — for architecture discussions, trade-off analysis, and code review. All design decisions were challenged, justified, and validated before implementation. The full interaction log is preserved in `DECISIONS.md`.
