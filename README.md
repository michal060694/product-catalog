# Product Catalog Cache API

> Interview Assignment — Caching Strategy & Consistency (.NET 9)

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
cd "ProductCatalog"

dotnet restore
dotnet build
dotnet run --project ProductCatalog.Api
```

Swagger UI is available at `http://localhost:5115/swagger` when running in Development mode.

---

## API Reference

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/products/{id}` | Fetch product by ID (cached) |
| `POST` | `/api/v1/products` | Create a new product (invalidates cache) |
| `PUT` | `/api/v1/products/{id}` | Update a product (invalidates cache) |

### Request & Response Examples

**POST /api/v1/products**
```http
POST /api/v1/products
Content-Type: application/json

{
  "name": "Laptop",
  "price": 4999.99,
  "stock": 10
}
```
```http
HTTP/1.1 201 Created
Location: /api/v1/products/1

{
  "id": 1,
  "name": "Laptop",
  "price": 4999.99,
  "stock": 10
}
```

> `costPrice` is accepted on write but intentionally excluded from `ProductDto` — it is never exposed to the client or written to cache. For demonstration of ability to filter sensitive information from the Client

**GET /api/v1/products/1**
```http
HTTP/1.1 200 OK

{
  "id": 1,
  "name": "Laptop",
  "price": 4999.99,
  "stock": 10
}
```

**PUT /api/v1/products/1**
```http
PUT /api/v1/products/1
Content-Type: application/json

{
  "name": "Gaming Laptop",
  "price": 5499.99,
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
GET /api/v1/products/0    → 400 Bad Request
GET /api/v1/products/-5   → 400 Bad Request
GET /api/v1/products/99   → 404 Not Found
```

---

## Cache Hit / Miss — End-to-End Flow

### Step 1 — First GET (Cache Miss)

```http
GET /api/v1/products/1
```

```
[INFO] Cache MISS for Product ID: 1. Fetching from repository.
[INFO] InFlight CREATED for cache key product:1.
[INFO] InFlight COMPLETED for cache key product:1.
→ 200 OK  (fetched from repository, stored in cache)
```

### Step 2 — Second GET (Cache Hit)

```http
GET /api/v1/products/1
```

```
[INFO] Cache HIT for Product ID: 1.
→ 200 OK  (served from cache — repository never called)
```

### Step 3 — Update Product (Cache Invalidated)

```http
PUT /api/v1/products/1
```

```
[INFO] Cache INVALIDATED for Product ID: 1 after update.
→ 200 OK
```

### Step 4 — Next GET (Cache Miss again)

```http
GET /api/v1/products/1
```

```
[INFO] Cache MISS for Product ID: 1. Fetching from repository.
[INFO] InFlight CREATED for cache key product:1.
[INFO] InFlight COMPLETED for cache key product:1.
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
| Troubleshooting | Easy | Hard |
| Safety Net | Forces refresh after TTL even if invalidation fails | Hot items may remain stale indefinitely if invalidation fails |

A product catalog is read-heavy but not latency-critical on expiry. Absolute expiration ensures no entry lives beyond its TTL regardless of traffic, preventing silent long-lived stale reads.

### 3. Cache Invalidation — Explicit `Remove` on Mutation

On `PUT` and `POST`, the cache entry is **removed** rather than updated. The next `GET` repopulates from the repository (the single source of truth). This avoids the complexity and risk of synchronizing cache updates with partial writes.

### 4. Null / 404 Not Cached

If a product is not found in the repository, `null` is returned and nothing is written to cache. Caching a `null` result would turn a temporary "not found" into a permanent stale 404 until TTL expiry.

### 5. Cache Stampede Prevention — `SharedTaskStore`

`SharedTaskStore` uses `ConcurrentDictionary<string, Lazy<Task<Product?>>>` to coalesce concurrent in-flight requests for the same key into **one** repository call.

```
Without protection:  1,000 concurrent GET /api/v1/products/1 → 1,000 repository hits
With SharedTaskStore: 1,000 concurrent GET /api/v1/products/1 → 1 repository hit
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

Due to time constraints, only a targeted subset of unit tests was written — enough to demonstrate the testing approach and validate the core mechanics.

#### `Services/` — Unit tests for the application service layer

| Suite | Covers |
|---|---|
| `ProductServiceGetTests` | Cache hit returns DTO without calling repo; cache miss calls repo and populates cache; missing product throws `ProductNotFoundException` |
| `ProductServiceCreateTests` | `Add` is called on repo exactly once; cache key is invalidated after creation; returned DTO contains correct mapped fields |
| `ProductServiceUpdateTests` | Missing product throws `ProductNotFoundException`; found product increments version, calls `Update`, and invalidates cache; returned DTO reflects updated fields |

#### `StaleDataExamples/` — Scenario tests demonstrating concurrency problems and their solutions

| Suite | Scenario demonstrated |
|---|---|
| `CoalescingTests` | **Cache stampede** — 10 concurrent requests for the same uncached key; `SharedTaskStore` coalesces them into a single factory call |
| `StaleCacheWriteTests` | **Stale write after invalidation** — Thread A's GET is in-flight when Thread B's PUT invalidates the cache; the generation guard rejects Thread A's stale write |
| `ConcurrentDictionaryTests` | **Concurrent repository access** — simultaneous Add / Read / Update operations produce no exceptions and preserve data integrity |
| `TocTouTests` | **TOCTOU (Time-Of-Check-To-Time-Of-Use)** — N concurrent cache misses race to the repository; `SharedTaskStore` ensures the repo is called exactly once and the cache is written exactly once |

All tests follow the `Given_When_Then` naming convention. Dependencies are mocked with FakeItEasy where needed; scenario tests use real infrastructure implementations to demonstrate actual behavior.

---

## AI Usage

Throughout this project, various AI tools were utilized as engineering assistants, with ultimate architectural ownership and decision-making remaining entirely mine.

The development workflow followed a structured, multi-LLM strategy:

**Architecture & Planning:** Initial design concepts were brainstormed with GPT. To resolve complex edge cases and architectural disagreements, Gemini was used for cross-verification. I made the final calls on all design decisions.

**Task Breakdown & Implementation:** Once the architecture was finalized, Claude was leveraged to break down the scope into an Agile methodology, ensuring each slice yielded a testable, independent deliverable for QA. The core implementation of each task was then co-authored with Claude.

**Scenario Building:** The scenario/workflow orchestration was developed almost entirely by me, as Claude struggled to model these interactions without introducing over-engineering and clutter.

A complete log of the interaction history and trade-off analysis is maintained in `DECISIONS.md`.

