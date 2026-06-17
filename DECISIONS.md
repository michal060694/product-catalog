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

- **Status:** APPROVED
- **Context/Decision:** After `POST` and `PUT`, only `RemoveAsync` is called. No new value is written to cache after mutation.
- **Solution:** `ProductService` calls `_cache.RemoveAsync(key, cancellationToken)` after every write. `RemoveAsync` increments `_generations[key]` under a per-key lock. Before querying the repository, the caller snapshots `expectedGeneration` via `GetGenerationAsync`; `SetAsync` rejects the write if `_generations[key] != expectedGeneration`.
- **Consequences:**
  - Pros: Eliminates race window — a concurrent `GET` inflight before `PUT` cannot overwrite the freshly-invalidated entry with a stale value
  - Cons: First read after any mutation always hits the repository (one extra round-trip)

---

### 1.2 Stampede Prevention — `SharedTaskStore` (No Semaphore)

- **Status:** APPROVED
- **Context/Decision:** `ConcurrentDictionary<string, Lazy<Task<Product?>>>` guarantees that 100 concurrent requests for the same uncached product produce **exactly one** repository call. `Lazy<T>` provides atomic creation semantics without a lock or semaphore. The inflight task is removed immediately after completion via `ContinueWith`.
- **Solution:** `SharedTaskStore.GetOrAddAsync` wraps the factory in `Lazy<Task>` — all concurrent callers await the same Task instance.
- **Consequences:**
  - Pros: Non-blocking (no thread queuing vs. Semaphore); N concurrent misses → 1 repository call; no explicit lock needed
  - Cons: More complex than a simple lock; task cleanup in `ContinueWith` must be reliable to avoid memory leaks

---

### 1.3 Generation + Version Guard in `SetAsync`

- **Status:** APPROVED
- **Context/Decision:** A `GET` issued before a `PUT` may return from the repository *after* the `PUT` has already invalidated the cache. Two guards defend against this inside `MemoryProductCache.SetAsync`, applied in order under a per-key lock.
- **Solution:**
  **Primary — Generation check:** `if (_generations.GetValueOrDefault(key, 0L) != expectedGeneration) → abort`. The caller snapshots `expectedGeneration` before hitting the repository; any `RemoveAsync` in between increments the counter, making the snapshot stale.
- **Consequences:**
  - Pros: Generation check is cache-state-aware and invalidation-aware.
  - Cons: generation counter grows monotonically and is never reset (acceptable for in-process lifetime)

---

### 1.4 Absolute Expiration Only

- **Status:** APPROVED
- **Decision:** TTL is fixed at `ProductTtlMinutes` (default: 5 min, configured to 1 min in `appsettings.json`). No `SlidingExpiration`.
- **Consequences:**
  - Pros: Hard freshness guarantee — maximum staleness is always bounded; simple to reason about
  - Cons: Frequently-accessed items are evicted at fixed intervals even when hot

---

### 1.5 `IProductCache` Abstraction (Redis-Ready)

- **Status:** APPROVED
- **Context:** `IMemoryCache` is never used outside `Infrastructure`. All cache operations go through `IProductCache`.
- **How:** `IProductCache` defined in `Domain`; `MemoryProductCache` in `Infrastructure` is the only class that touches `IMemoryCache`.
- **Consequences:**
  - Pros: Switching to Redis requires replacing only `MemoryProductCache` — zero changes in `Application` or `Domain`
  - Cons: no cons

---

### 1.6 `ConcurrentDictionary` for Repository

- **Status:** APPROVED
- **Problem:** The repository is accessed concurrently by multiple requests. A regular Dictionary<TKey,TValue> is not thread-safe, so simultaneous reads and writes can lead to race conditions, corrupted internal state, exceptions, or lost updates. ID generation also must remain unique under concurrent POST requests.
- **Solution:** Use `ConcurrentDictionary<int, Product>` for the in-memory store and `Interlocked.Increment(ref _nextId)` for ID generation.
- **Consequences:**
  - Pros: - Thread-safe reads and writes without explicit locks; Concurrent POST requests cannot generate duplicate IDs; Prevents data corruption and collection-state exceptions under load; Simpler implementation than manual lock management.
  - Cons: State lost on restart; single-instance only(intended for practice only, not for a Production system)

---

### 1.7 `CostPrice` — Sensitive Field Never Exposed

- **Status:** APPROVED
- **Context/Decision:** `Product.CostPrice` is a Sensitive data.
- **Solution:** `ProductProfile` declares Ignore(), Not logged anywhere in the codebase.
- **Consequences:**
  - Pros: Internal cost data cannot accidentally leak to clients; opt-in mapping makes omission the safe default
  - Cons: no cons

---

### 1.8 Null / 404 — Not Cached

- **Status:** APPROVED
- **Context/Decision:** When a product is not found.
- **Solution:** The factory in `SharedTaskStore` throws `ProductNotFoundException` on null — `SetAsync` is never reached for missing products and nothing is written to cache.
- **Consequences:**
  - Pros: Newly created products become accessible immediately — no TTL wait for a null sentinel to expire
  - Cons: Every lookup for a non-existent ID hits the repository; see [Section 5.2](#52-short-lived-null-caching-negative-caching) for the upgrade

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
| Two concurrent `PUT`s for the same key with different versions | Version Guard in `SetAsync` ensures only the higher version is persisted in cache | ✅ Handled |

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

**Threat:** After `PUT` calls `RemoveAsync`, a slow in-flight `GET` (whose factory ran before the `PUT`) could call `SetAsync` with an outdated value and re-poison the cache.

**Defense implemented:** `RemoveAsync` increments `_generations[key]` under a per-key lock. The in-flight `GET` factory captured `expectedGeneration` before it queried the repository — that snapshot is now stale. When the factory calls `SetAsync`, the primary guard `_generations.GetValueOrDefault(key, 0L) != expectedGeneration` evaluates to `true` and the write is rejected.

This ensures that even if a stale inflight task completes after the invalidation, it cannot write its outdated result back into the cache.

---

## 5. What Would Be Added With More Time

### 5.1 Redis — Distributed Cache

- **Status:** FUTURE
- **Context/Decision:** Implement `RedisProductCache : IProductCache` using `IDistributedCache` or `StackExchange.Redis`. `IMemoryCache` is per-process — in a multi-instance deployment each instance has its own cache island and invalidation does not propagate across instances.
- **Solution:** Replace `MemoryProductCache` with `RedisProductCache` and swap the DI registration in `AddInfrastructure()` — no other file changes.
- **Consequences:**
  - Pros: Shared cache state across all instances; invalidation is global; standard production pattern for scaled services
  - Cons: Infrastructure dependency (Redis instance); network latency vs. in-process; operational complexity

---

### 5.2 Short-Lived Null Caching (Negative Caching)

- **Status:** FUTURE
- **Context/Decision:** When a product ID does not exist, cache a sentinel `null` value for a short configurable TTL (e.g., `NullTtlSeconds = 30`). On `POST`, the null sentinel for that key is explicitly removed.
- **Solution:** Store a typed sentinel in `IProductCache` with a separate `NullTtlSeconds` TTL; call `RemoveAsync(key)` inside `CreateProduct` to unblock immediate access.
- **Consequences:**
  - Pros: Eliminates redundant repository hits for repeated non-existent ID lookups (e.g., bot traffic or stale client references)
  - Cons: A product created within the TTL window is unreachable until the sentinel expires — mitigated by explicit removal on `POST`

---

### 5.3 Idempotency Key for `POST`

- **Status:** FUTURE
- **Context/Decision:** Accept an `Idempotency-Key` header on `POST /api/products`. Store the key short-lived in cache and return the original response on replay.
- **Solution:** Middleware intercepts `Idempotency-Key`; on first request stores the response DTO in cache; on replay returns the cached response without hitting the service.
- **Consequences:**
  - Pros: Prevents duplicate product creation on client retries or double-clicks
  - Cons: Extra cache key namespace; TTL management for idempotency keys; client must generate and send the header

---

### 5.4 Decorator Pattern — `ProductCacheDecorator`

- **Status:** FUTURE
- **Context/Decision:** Extract caching responsibility from `ProductService` into a `ProductCacheDecorator` that wraps `IProductService`. `ProductService` would contain only business logic.
- **Solution:** Register `ProductCacheDecorator` as the outer `IProductService` in DI, wrapping the inner `ProductService` — no changes to either class's internals.
- **Consequences:**
  - Pros: Clean SRP separation; cache behavior independently testable; swappable without touching business logic
  - Cons: Extra class to maintain; DI registration becomes slightly more complex (decorator wiring)

---

### 5.5 Health Checks

- **Status:** FUTURE
- **Context/Decision:** Add `/health` endpoint via `services.AddHealthChecks()` with checks for memory pressure, repository reachability, and Redis connectivity.
- **Solution:** Register custom `IHealthCheck` implementations for each dependency and map `/health` in `Program.cs`.
- **Consequences:**
  - Pros: Kubernetes-ready liveness/readiness probes; early warning on cache or storage degradation
  - Cons: Health endpoint itself must be secured or rate-limited; memory check thresholds require tuning per environment

---

### 5.6 Docker / Containerization

- **Status:** FUTURE
- **Context/Decision:** Provide a `Dockerfile` and `docker-compose.yml` including a Redis container for local development.
- **Solution:** Multi-stage `Dockerfile` (build + runtime) + `docker-compose.yml` with `redis:alpine` service and an environment variable pointing the API at it.
- **Consequences:**
  - Pros: Reproducible environment; enables local testing of the distributed cache scenario without a remote Redis
  - Cons: Docker knowledge required; image size management; compose version compatibility

---

### 5.7 Broader Test Coverage

- **Status:** FUTURE
- **Context/Decision:** Full unit test coverage per method + integration tests with `WebApplicationFactory<Program>` covering the full HTTP pipeline, including middleware, routing, and DI.
- **Solution:** Add `WebApplicationFactory<Program>` test class; spin up the full app in-process and drive it via `HttpClient` without mocking the infrastructure.
- **Consequences:**
  - Pros: Catches regressions on the full request pipeline; integration tests reflect real behavior better than unit tests alone
  - Cons: Slower test suite; `WebApplicationFactory` setup requires careful port and configuration management

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
