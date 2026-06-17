# DECISIONS.md — Architecture Decisions & Engineering Notes

> Product Catalog Cache API — Caching Strategy & Consistency Assignment

---

## Summary

| # | Topic | Decision / Status | Section |
|---|---|---|---|
| 1 | Cache Invalidation | Remove-only after `POST`/`PUT` — no write-back | [1.1](#11-cache-invalidation--remove-only-no-write-back) |
| 2 | Stampede Prevention | `SharedTaskStore` (`Lazy<Task>`) — no Semaphore | [1.2](#12-stampede-prevention--sharedtaskstore-no-semaphore) |
| 3 | GET/PUT Race Condition | Version Guard in `SetAsync` — stale write rejected | [1.3](#13-version-guard-in-setasync) |
| 4 | Expiration Strategy | Absolute TTL only — no Sliding Expiration | [1.4](#14-absolute-expiration-only) |
| 5 | Redis Readiness | `IProductCache` abstraction — swap requires one file | [1.5](#15-iproductcache-abstraction-redis-ready) |
| 6 | Thread-Safe Repository | `ConcurrentDictionary` + `Interlocked` ID generation | [1.6](#16-concurrentdictionary-for-repository) |
| 7 | Sensitive Data | `CostPrice` excluded from all DTOs, responses, and logs | [1.7](#17-costprice--sensitive-field-never-exposed) |
| 8 | Null / 404 Caching | Not cached — prevents masking a future creation | [1.8](#18-null--404--not-cached) |
| 9 | TOCTOU | Per-key lock; upgrade path → Striped Locking (64 stripes) | [3](#3-toctou-time-of-check--time-of-use) |
| 10 | Cache Poisoning | `MinVersion` on `RemoveAsync` → checked in `SetAsync` | [4](#4-cache-poisoning-after-invalidation) |
| 11 | Redis (future) | `RedisProductCache : IProductCache` — zero Application changes | [5.1](#51-redis--distributed-cache) |
| 12 | Null Caching (future) | 30 s configurable TTL + explicit removal on `POST` | [5.2](#52-short-lived-null-caching-negative-caching) |
| 13 | Idempotency (future) | `Idempotency-Key` header on `POST` | [5.3](#53-idempotency-key-for-post) |
| 14 | Decorator Pattern (future) | `ProductCacheDecorator` — separates cache from business logic | [5.4](#54-decorator-pattern--productcachedecorator) |
| 15 | Health Checks (future) | `/health` — memory, repository, Redis | [5.5](#55-health-checks) |
| 16 | Docker (future) | `Dockerfile` + `docker-compose.yml` with Redis container | [5.6](#56-docker--containerization) |
| 17 | Test Coverage (future) | Full unit + integration tests with `WebApplicationFactory` | [5.7](#57-broader-test-coverage) |

---

## 1. Key Decisions Made

### 1.1 Cache Invalidation — Remove Only (No Write-Back)
After `POST` and `PUT`, only `RemoveAsync` is called. No new value is written to cache after mutation.  
**Why:** Write-back introduces a race window — a concurrent `GET` that was inflight before the `PUT` could overwrite the freshly-written cache entry with a stale value. Remove-only guarantees the next read always pulls from the repository.

### 1.2 Stampede Prevention — `SharedTaskStore` (No Semaphore)
`ConcurrentDictionary<string, Lazy<Task<Product?>>>` guarantees that 100 concurrent requests for the same uncached product produce **exactly one** repository call.  
**Why:** `Lazy<T>` provides atomic creation semantics without a lock or semaphore. The inflight task is removed immediately after completion via `ContinueWith`, so subsequent requests after the first one completes go through the normal cache path.

### 1.3 Version Guard in `SetAsync`
Before writing to cache, `MemoryProductCache.SetAsync` checks:  
```
if existing.Version >= product.Version → skip write
```
**Why:** A `GET` issued before a `PUT` may return from the repository *after* the `PUT` has already invalidated the cache and the next `GET` has populated it with a fresh value. Without the version guard, the slow `GET` would overwrite the cache with a stale product.

### 1.4 Absolute Expiration Only
TTL is fixed at `ProductTtlMinutes` (default: 5 min, configured to 1 min in `appsettings.json`). No `SlidingExpiration`.  
**Why:** Sliding expiration makes it impossible to reason about the maximum staleness window. A product accessed frequently could stay cached indefinitely. Absolute expiration provides a hard freshness guarantee.

### 1.5 `IProductCache` Abstraction (Redis-Ready)
`IMemoryCache` is never used outside `Infrastructure`. All cache operations go through `IProductCache`.  
**Why:** Switching to Redis requires replacing `MemoryProductCache` with `RedisProductCache` only — zero changes in `Application` or `Domain`. See [Section 5.1](#51-redis-distributed-cache).

### 1.6 `ConcurrentDictionary` for Repository
`InMemoryProductRepository` uses `ConcurrentDictionary<int, Product>` and `Interlocked` for ID generation.  
**Why:** A plain `Dictionary` is not thread-safe. Concurrent `POST` requests against a plain `Dictionary` cause data corruption and `KeyNotFoundException` under race conditions.

### 1.7 `CostPrice` — Sensitive Field Never Exposed
`Product.CostPrice` is not mapped to `ProductDto`. `ProductProfile` explicitly ignores it via `opt.Ignore()`.  
**Why:** Internal cost data must not leak to clients. No accidental mapping, no logging of this field anywhere in the codebase.

### 1.8 Null / 404 — Not Cached
When a product is not found, `ProductNotFoundException` is thrown. Nothing is written to cache.  
**Why:** Caching nulls indefinitely would mask the moment a product is actually created. See [Section 5.2](#52-short-lived-null-caching-negative-caching) for the production-grade improvement.

---

## 2. Race Condition Analysis

| Scenario | Outcome | Status |
|---|---|---|
| `GET` arrives before product is created | Repository returns `null` → `ProductNotFoundException` → `404` | ✅ Correct |
| `POST` writes to repository but hasn't returned yet | Any concurrent `GET` that reaches the repository after the write sees the product | ✅ Correct |
| `GET` populates cache while a concurrent `POST` is running | `POST` invalidates (Remove) after write; next `GET` fetches fresh value | ✅ Correct |
| Slow `GET` returns *after* a `PUT` has already updated the product | Version Guard in `SetAsync` prevents the stale value from entering cache | ✅ Handled |
| 100 concurrent `GET`s for the same uncached product | `SharedTaskStore` ensures exactly one repository call | ✅ Handled |
| `GET` and `PUT` for the same key run simultaneously | `PUT` removes cache entry; `GET` either hits the old value or re-fetches; Version Guard prevents regression | ✅ Handled |

---

## 3. TOCTOU (Time-of-Check / Time-of-Use)

A per-key lock is applied in `MemoryProductCache` to prevent the Check-Then-Act race inside `SetAsync`.  
**Why:** Without a lock, two concurrent threads can both read `existing.Version`, both conclude they should write, and one of them overwrites the other with a stale value.

**Production consideration:** In high-throughput systems a single global lock or per-key lock over a `ConcurrentDictionary` becomes a bottleneck. The upgrade path is **Striped Locking** — a fixed-size array of locks (e.g., 64 stripes) where `lock = stripes[key.GetHashCode() % 64]`. Benefits:
- Reduces contention by 64× compared to a single lock
- Eliminates the need to flush the `ConcurrentDictionary` on cleanup
- Transparent to callers — same interface, same behavior

---

## 4. Cache Poisoning After Invalidation

**Threat:** After `PUT` calls `RemoveAsync`, a slow in-flight `GET` (whose factory ran before the `PUT`) could call `SetAsync` with an outdated version and re-poison the cache.

**Defense implemented:** `SetAsync` stores `MinVersion` alongside the removal. On any subsequent `SetAsync`, if `product.Version < minVersion`, the write is rejected.

This ensures that even if a stale inflight task completes after the invalidation, it cannot write its outdated result back into the cache.

---

## 5. What Would Be Added With More Time

### 5.1 Redis — Distributed Cache
Implement `RedisProductCache : IProductCache` using `IDistributedCache` or `StackExchange.Redis`.  
**Why it matters:** `IMemoryCache` is per-process. In a multi-instance deployment (Kubernetes, App Service with multiple replicas), each instance has its own cache island — invalidation on one instance does not propagate to others. Redis is the shared source of truth.  
**Implementation cost:** Zero changes outside `Infrastructure` — swap one registration in `AddInfrastructure()`.

### 5.2 Short-Lived Null Caching (Negative Caching)
When a product ID does not exist, cache a sentinel `null` value for a short TTL (e.g., 30 seconds, configurable as `NullTtlSeconds`).  
**Why it matters:** A client or bot repeatedly requesting a non-existent ID causes a repository hit on every request — an expensive no-op. Negative caching eliminates redundant reads.  
**Why short TTL:** If the product is created later, the cache must expire quickly enough that the next `GET` retrieves the real value rather than the cached null. On `POST`, the null sentinel for that key is also explicitly removed.

### 5.3 Idempotency Key for `POST`
Accept an `Idempotency-Key` header on `POST /api/products`.  
**Why it matters:** A client that double-clicks or retries on a network timeout can create duplicate products. Storing the key (short-lived, in cache or a dedicated store) and returning the original response on replay prevents duplicates without requiring the client to detect them.

### 5.4 Decorator Pattern — `ProductCacheDecorator`
Extract the caching responsibility from `ProductService` into a `ProductCacheDecorator` that wraps `IProductService`.  
**Why:** `ProductService` currently holds both business logic and cache orchestration. Separating them via the Decorator pattern produces cleaner SRP — `ProductService` contains only business logic; `ProductCacheDecorator` handles all cache reads, writes, and invalidation.

### 5.5 Health Checks
Add `/health` endpoint via `services.AddHealthChecks()`.  
Checks to register:
- Memory pressure (`IMemoryCache` entry count / estimated size)
- Repository reachability (in production: database ping)
- Redis connectivity (if Redis is added)

### 5.6 Docker / Containerization
Provide a `Dockerfile` and `docker-compose.yml` (including Redis container).  
**Why it matters:** Reproducible environment, no "works on my machine" issues, and enables testing the distributed cache scenario locally.

### 5.7 Broader Test Coverage
- Unit test every method individually with the `// Arrange / // Act / // Assert` structure
- Edge cases: concurrent `POST` + `GET` during creation, version rollback attempts, `CancellationToken` propagation, validation boundary cases
- Integration tests: full HTTP pipeline with `WebApplicationFactory<Program>`

---

## 6. Common Cache Bug Checklist

| Bug | Mitigation in This Project |
|---|---|
| **Stale Data** | Absolute TTL + invalidation on write |
| **Cache Stampede** | `SharedTaskStore` — single inflight task per key |
| **GET/PUT Race Condition** | Version Guard in `SetAsync` |
| **Cache Poisoning** | `MinVersion` check after `RemoveAsync` |
| **Caching Null** | Not cached (intentional); see [5.2](#52-short-lived-null-caching-negative-caching) for the upgrade |
| **Expiration Strategy** | Absolute expiration only — predictable staleness window |
| **Cache Key Collision** | `CacheKeys.ForProduct(id)` → `"product:{id}"` — namespace-scoped, extendable to `product:{tenantId}:{id}` |
| **Memory Cache vs Distributed Cache** | `IProductCache` abstraction — Redis swap requires one file change |

---

## 7. Performance Notes

- Cache entries are **never stored permanently**. Every `POST` or `PUT` removes the key. The cache is populated lazily — only on the next `GET` after a miss.
- This approach trades a single extra read-latency on the first post-mutation request for guaranteed freshness and no stale-write risk.
- In read-heavy workloads, the HIT/MISS ratio will be high after warmup. The cost of the occasional cold read is negligible compared to the consistency guarantees gained.

---

*Document authored by Michal Ben Shalom — June 2026*
