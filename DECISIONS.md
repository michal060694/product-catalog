# DECISIONS.md — Architecture Decisions & Engineering Notes

> Product Catalog Cache API — Caching Strategy & Consistency Assignment

---

## Summary

| # | Topic | Decision / Status | Section |
|---|---|---|---|
| 1 | Cache Invalidation | Remove-only after `POST`/`PUT` — no write-back | [1.1](#11-cache-invalidation--remove-only-no-write-back) |
| 2 | Stampede Prevention | `SharedTaskStore` (`Lazy<Task>`) — no Semaphore | [1.2](#12-stampede-prevention--sharedtaskstore-no-semaphore) |
| 3 | GET/PUT Race Condition | Generation Guard in `SetAsync` — stale write rejected | [1.3](#13-generation-guard-in-setasync) |
| 4 | Expiration Strategy | Absolute TTL only — no Sliding Expiration | [1.4](#14-absolute-expiration-only) |
| 5 | Redis Readiness | `IProductCache` abstraction — swap requires one file | [1.5](#15-iproductcache-abstraction-redis-ready) |
| 6 | Thread-Safe Repository | `ConcurrentDictionary` + `Interlocked` ID generation | [1.6](#16-concurrentdictionary-for-repository) |
| 7 | Sensitive Data | `CostPrice` excluded from all DTOs, responses, and logs | [1.7](#17-costprice--sensitive-field-never-exposed) |
| 8 | Null / 404 Caching | Not cached — prevents masking a future creation | [1.8](#18-null--404--not-cached) |
| 9 | TOCTOU | Per-key lock; upgrade path → Striped Locking (64 stripes) | [3](#3-toctou-time-of-check--time-of-use) |
| 10 | Cache Poisoning | Generation counter in `RemoveAsync` → checked in `SetAsync` | [4](#4-cache-poisoning-after-invalidation) |
| 11–17 | High-Scale Architecture | Redis Cluster, distributed coalescing, persistent DB, resilience, observability, rate limiting, proactive cache refresh | [5](#5-what-would-change-at-high-scale) |
| 18 | Null Caching (future) | 30 s configurable TTL + explicit removal on `POST` | [6.1](#61-short-lived-null-caching-negative-caching) |
| 19 | Idempotency (future) | `Idempotency-Key` header on `POST` | [6.2](#62-idempotency-key-for-post) |
| 20 | Decorator Pattern (future) | `ProductCacheDecorator` — separates cache from business logic | [6.3](#63-decorator-pattern--productcachedecorator) |
| 21 | Health Checks (future) | `/health` — memory, repository, Redis | [6.4](#64-health-checks) |
| 22 | Docker (future) | `Dockerfile` + `docker-compose.yml` with Redis container | [6.5](#65-docker--containerization) |
| 23 | Test Coverage (future) | Full unit + integration tests with `WebApplicationFactory` | [6.6](#66-broader-test-coverage) |
| 24 | Value Objects (future) | `ProductId`, `Money`, `StockQuantity` — invariants live in the type | [6.7](#67-value-objects-for-domain-primitives) |
| 25 | Generation Dictionary Lifecycle (future) | Cleanup on DELETE + global store in distributed cache | [6.8](#68-generation-dictionary-lifecycle) |

---

## 1. Key Decisions Made

### 1.1 Cache Invalidation — Remove Only (No Write-Back)

- **Status:** APPROVED
- **Decision:** After `POST` and `PUT`, only `RemoveAsync` is called — no new value is written to cache after mutation.
- **Consequences:**
  - Pros: Eliminates race window — a concurrent `GET` in-flight before `PUT` cannot overwrite the freshly-invalidated entry with a stale value
  - Cons: First read after any mutation always hits the repository (one extra round-trip)
- **Guard against stale in-flight writes:** see [Section 4](#4-cache-poisoning-after-invalidation)

---

### 1.2 Stampede Prevention — `SharedTaskStore` (No Semaphore)

- **Status:** APPROVED
- **Context/Decision:** `ConcurrentDictionary<string, Lazy<Task<Product?>>>` guarantees that 100 concurrent requests for the same uncached product produce **exactly one** repository call. `Lazy<T>` provides atomic creation semantics without a lock or semaphore. The inflight task is removed immediately after completion via `ContinueWith`.
- **Solution:** `SharedTaskStore.GetOrAddAsync` wraps the factory in `Lazy<Task>` — all concurrent callers await the same Task instance.
- **Consequences:**
  - Pros: Non-blocking (no thread queuing vs. Semaphore); N concurrent misses → 1 repository call; no explicit lock needed
  - Cons: More complex than a simple lock; task cleanup in `ContinueWith` must be reliable to avoid memory leaks

---

### 1.3 Generation Guard in `SetAsync`

- **Status:** APPROVED
- **Decision:** A `GET` issued before a `PUT` may return from the repository *after* the `PUT` has already invalidated the cache. The guard in `SetAsync` rejects the write in that case — full mechanism described in [Section 4](#4-cache-poisoning-after-invalidation).
- **Consequences:**
  - Pros: Generation check is cache-state-aware; stale in-flight writes are silently discarded
  - Cons: Generation counter grows monotonically and is never reset (acceptable for in-process lifetime; see [6.9](#69-generation-dictionary-lifecycle) for the upgrade)

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
- **Decision:** `Product.CostPrice` is internal cost data — never mapped to a DTO, never logged.
- **How:** `ProductProfile` declares `Ignore()` for `CostPrice`; no log statement references it anywhere in the codebase.
- **Consequences:**
  - Pros: Internal cost data cannot accidentally leak to clients; opt-in mapping makes omission the safe default

---

### 1.8 Null / 404 — Not Cached

- **Status:** APPROVED
- **Context/Decision:** When a product is not found.
- **Solution:** The factory in `SharedTaskStore` throws `ProductNotFoundException` on null — `SetAsync` is never reached for missing products and nothing is written to cache.
- **Consequences:**
  - Pros: Newly created products become accessible immediately — no TTL wait for a null sentinel to expire
  - Cons: Every lookup for a non-existent ID hits the repository; see [Section 6.1](#61-short-lived-null-caching-negative-caching) for the upgrade

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

- **Status:** APPROVED
- **Problem:** Without synchronization, two concurrent threads can both read same version in `SetAsync`, both conclude they are allowed to write, and one of them overwrites the other with a stale value — a classic Check-Then-Act race.
- **Solution:** A per-key lock is acquired inside `SetAsync` before reading and comparing Tokens. Only the thread holding the lock may proceed to write, making the check and the write a single atomic operation.
- **Consequences:**
  - Pros: Eliminates the version-overwrite race with zero external dependencies
  - Cons: In high-throughput systems, per-key locking can become a bottleneck. The upgrade path is **Striped Locking** — a fixed-size array of locks (e.g., 64 stripes) where `lock = stripes[key.GetHashCode() % 64]`, which reduces contention by 64× while remaining transparent to callers

---

## 4. Cache Poisoning After Invalidation

- **Status:** APPROVED
- **Problem:** After `PUT` calls `RemoveAsync`, a slow in-flight `GET` (whose factory ran before the `PUT`) could call `SetAsync` with an outdated value and re-poison the cache with stale data.
- **Solution:** `RemoveAsync` increments a generation counter for the key (`_generations[key]`) under a per-key lock. The in-flight `GET` factory captures `expectedGeneration` before querying the repository. When it later calls `SetAsync`, the guard `_generations.GetValueOrDefault(key, 0L) != expectedGeneration` evaluates to `true` and the write is rejected.
- **Consequences:**
  - Pros: Stale in-flight tasks can never poison the cache after an invalidation, regardless of timing
  - Cons: Adds a generation counter per key that must be maintained alongside the cache entry; lock scope must cover both the generation check and the write to remain race-free

---

## 5. Things I would consider adding if this were scaled up

> Structural prerequisites for running correctly across multiple instances under sustained load — not polish items.

| # | Topic | Why it matters at scale |
|---|---|---|
| 5.1 | **Redis Cluster + Pub/Sub Invalidation** | `IMemoryCache` is per-process — a `PUT` on one instance doesn't invalidate the others. Redis Pub/Sub broadcasts invalidation events to the entire fleet. `IProductCache` abstraction already makes this a one-file swap. |
| 5.2 | **Distributed Request Coalescing** | `SharedTaskStore` prevents stampedes within one process only. Across N instances, N concurrent misses still produce N repository calls. Requires a distributed lock (e.g., Redis `SET NX`) to coalesce across the fleet. |
| 5.3 | **Persistent Database + Read Replicas** | `ConcurrentDictionary` loses state on restart and isn't shared between instances. A real DB with read replicas handles durable storage and horizontal read scaling. |
| 5.4 | **Resilience — Circuit Breaker + Retry** | Without it, a single DB hiccup queues requests until thread-pool exhaustion. |
| 5.5 | **Observability — Distributed Tracing + Metrics** | The system is currently a black box. Cache hit rate, stampede events, and per-layer latency (p99) are invisible without OpenTelemetry traces and Prometheus metrics. |
| 5.6 | **Rate Limiting + Backpressure** | A single client can saturate the thread pool. A bounded request queue with per-IP limits rejects excess traffic with `429`/`503` before it reaches the DB. |
---

## 6. What Would Be Added With More Time

### 6.1 Short-Lived Null Caching (Negative Caching)

**What:** When a product ID does not exist, store a typed sentinel value in `IProductCache` with a short dedicated TTL (e.g., `NullTtlSeconds = 30`). On `POST`, explicitly remove the sentinel for that key.  
**Need it addresses:** Repeated lookups for non-existent IDs (e.g., bot traffic, stale client references) reach the repository on every request — a cache that only stores found products cannot shield against this.
- Pros: Eliminates redundant repository hits for non-existent IDs
- Cons: A product created within the TTL window is unreachable until the sentinel expires — mitigated by explicit removal on `POST`

---

### 6.2 Idempotency Key for `POST`

**What:** Accept an `Idempotency-Key` header on `POST /api/v1/products`. Middleware intercepts the header, stores the response DTO in cache on the first request, and returns the cached response on replay without touching the service.  
**Need it addresses:** Client retries and double-clicks can create duplicate products — there is currently no protection against a `POST` being delivered more than once.
- Pros: Prevents duplicate product creation transparently; no service-layer changes required
- Cons: Extra cache key namespace; TTL management for idempotency keys; client must generate and send the header

---

### 6.3 Decorator Pattern — `ProductCacheDecorator`

**What:** Extract all caching and coalescing logic from `ProductService` into a `ProductCacheDecorator : IProductService` that wraps the inner service. Register it as the outer binding in DI — no changes to either class's internals.  
**Need it addresses:** `ProductService` currently mixes business logic with caching concerns, which limits independent testability and violates SRP as the service grows.
- Pros: Clean SRP separation; cache behavior independently testable; swappable without touching business logic
- Cons: Extra class to maintain.

---

### 6.4 Health Checks

**What:** Add a `/health` endpoint via `services.AddHealthChecks()` with custom `IHealthCheck` implementations for memory pressure, repository reachability, and (when present) Redis connectivity.  
**Need it addresses:** There is currently no operational signal for whether the service and its dependencies are healthy — a requirement for any Kubernetes liveness/readiness probe setup.
- Pros: Kubernetes-ready probes; early warning on cache or storage degradation

---

### 6.5 Docker / Containerization

**What:** Provide a multi-stage `Dockerfile` (build + runtime) and a `docker-compose.yml` with a `redis:alpine` service and an environment variable pointing the API at it.  
**Need it addresses:** Developers currently cannot test the distributed cache scenario locally without a separately managed Redis instance — there is no reproducible environment definition.
- Pros: Reproducible local environment; enables end-to-end testing of the Redis path without a remote instance

---

### 6.6 Broader Test Coverage

**What:** Two layers of additional tests:
1. **Unit tests** — full per-method coverage for every service, repository, and cache method, including all error paths and edge cases currently not exercised
2. **Integration tests** — `WebApplicationFactory<Program>` that spins up the full app in-process and drives it via `HttpClient`, covering middleware, routing, DI wiring, and the full HTTP pipeline end-to-end

**Need it addresses:** Unit tests ensure every code path is exercised in isolation; integration tests catch regressions that unit tests cannot — misconfigured DI, incorrect middleware ordering, or routing mistakes.
- Pros: Unit tests are fast and pinpoint failures precisely; integration tests reflect real behavior and validate the wiring between layers

---

### 6.7 Value Objects for Domain Primitives

**What:** Replace `int Id`, `decimal Price`, and `int Stock` on `Product` with three `record` Value Objects — `ProductId`, `Money`, `StockQuantity` — each validating its invariant in its constructor. DTOs stay as primitives; AutoMapper bridges the two with `ConvertUsing` converters.  
**Need it addresses:** `Price`, `Stock`, and `Id` currently travel as raw primitives — a caller can write `new Product { Price = -50m }` and the compiler says nothing. Business rules are scattered across validators instead of living in the type.
- Pros: Invalid state cannot be constructed; compile-time prevents passing a `productName` where an `id` is expected; single source of truth for each invariant
- Cons: AutoMapper 16.x requires non-trivial configuration to map Value Objects into positional `record` DTOs — `ConvertUsing` type maps interact with constructor resolution in non-obvious ways

---

### 6.8 Generation Dictionary Lifecycle

**What:** Two improvements to the `_generations` dictionary maintained in `MemoryProductCache`:

1. **Cleanup on DELETE** — When a `DELETE /api/v1/products/{id}` endpoint is added, `RemoveAsync` should also call `_generations.TryRemove(key, out _)` after evicting the cache entry. Today the generation counter for a deleted product stays in the dictionary indefinitely. With cleanup on DELETE the dictionary size stays exactly in sync with the number of live products in the repository — no unbounded growth.

2. **Global store in distributed cache** — When migrating to Redis, the `_generations` dictionary must move to a shared store rather than remaining a local `ConcurrentDictionary`. In a multi-instance deployment each node holds its own private counter, so a `PUT` on instance A increments only that node's generation, while instance B's in-flight `GET` compares against its own stale counter and wrongly accepts a stale write. Storing generations in Redis gives all instances a single source of truth.

> **Note:** the per-key `lock` already used in `SetAsync` and `RemoveAsync` ensures that no two threads can read-modify-write the generation counter at the same time — so duplicate increments and lost updates are already prevented for the in-process case. This same mutual-exclusion guarantee must be preserved (e.g., via Redis `WATCH`/transaction or a distributed lock) when moving to a shared store.

- Pros: Dictionary stays bounded; invalidation semantics are correct across all instances in a distributed deployment
- Cons: Redis-based generation management adds latency per cache write (one round-trip for the generation check); distributed locking increases implementation complexity

---

## 7. Common Cache Bug Checklist

| Bug | Mitigation in This Project |
|---|---|
| **Stale Data** | Absolute TTL + invalidation on write |
| **Cache Stampede** | `SharedTaskStore` — single inflight task per key |
| **GET/PUT Race Condition** | Version Guard in `SetAsync` |
| **Cache Poisoning** | Generation counter incremented in `RemoveAsync` — stale in-flight `SetAsync` calls rejected |
| **Caching Null** | Not cached (intentional); see [6.1](#61-short-lived-null-caching-negative-caching) for the upgrade |
| **Expiration Strategy** | Absolute expiration only — predictable staleness window |
| **Cache Key Collision** | `CacheKeys.ForProduct(id)` → `"product:{id}"` — namespace-scoped, extendable to `product:{tenantId}:{id}` |
| **Memory Cache vs Distributed Cache** | `IProductCache` abstraction — Redis swap requires one file change |

*Document authored by Michal — June 2026*
