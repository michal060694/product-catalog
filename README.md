# Product Catalog API - Caching Strategy & Consistency

> Interview Assignment Solution (.NET 8)

## Overview

This project implements a Product Catalog API with a strong focus on caching correctness, consistency, invalidation, expiration policies, and concurrency handling.

The business domain is intentionally simple. The primary goal is to demonstrate architectural decisions and best practices around cache management rather than business complexity.

---

## Technologies

- .NET 8
- ASP.NET Core Web API
- IMemoryCache
- AutoMapper
- FluentValidation
- xUnit
- Dependency Injection

---

# Architecture

```text
ProductCatalog.Api
    ↓
ProductService
    ↓
IProductCache
    ↓
MemoryProductCache

ProductService
    ↓
IProductRepository
    ↓
InMemoryProductRepository
```

## Project Structure

```text
src/

ProductCatalog.Api
ProductCatalog.Application
ProductCatalog.Domain
ProductCatalog.Infrastructure
ProductCatalog.Tests
```

---

# Caching Strategy

Caching is applied only to:

GET /api/products/{id}

Cache Key:

```text
product:{id}
```

Example:

```text
product:42
```

### Cache Miss

```text
Request
 ↓
Cache Miss
 ↓
Repository
 ↓
Store in Cache
 ↓
Response
```

### Cache Hit

```text
Request
 ↓
Cache Hit
 ↓
Response
```

### Null Values

Null values are intentionally not cached to avoid stale 404 responses.

---

# Cache Invalidation

## Product Creation

POST /api/products

Relevant cache entries are invalidated when necessary.

## Product Update

PUT /api/products/{id}

The corresponding cache entry is removed.

### Why Remove Instead of Update?

The solution uses explicit invalidation instead of active cache updates.

Benefits:

- Lower memory usage
- Simpler consistency model
- Prevents stale data
- Cache is rebuilt only when needed

---

# Expiration Strategy

## Absolute Expiration

Example:

```csharp
TimeSpan.FromMinutes(5)
```

Reasons:

- Predictable behavior
- Easy troubleshooting
- Prevents long-lived stale data
- Appropriate for product catalog scenarios

---

# Advanced Requirement

## Cache Stampede Prevention

Implemented using a SharedTaskStore.

### Problem

Without protection:

```text
100,000 requests
      ↓
100,000 repository calls
```

### Solution

```text
100,000 requests
      ↓
1 repository call
      ↓
Shared Task
      ↓
100,000 responses
```

All concurrent requests for the same product share the same running task.

---

# Concurrency Analysis

## Scenario 1

GET before POST

Result:

```text
404 Not Found
```

Expected behavior.

---

## Scenario 2

POST completed repository write.

GET arrives immediately afterward.

Result:

Product is returned successfully.

---

## Scenario 3

GET creates cache while POST is running.

Handled safely.

---

## Scenario 4

Cache invalidation during GET.

Most critical race-condition scenario.

The design minimizes stale reads through invalidation and recreation from the repository source of truth.

---

## Scenario 5

Concurrent GET requests.

Handled by SharedTaskStore.

Only one repository call executes.

---

# Security Considerations

## Cache Keys

Current implementation:

```text
product:{id}
```

For authorization-based systems:

```text
user:{userId}:product:{id}
```

would be required to prevent cross-user leakage.

## Cache Poisoning

Cache entries are populated only from repository results.

External client input is never written directly into cache storage.

## DTO Usage

DTOs ensure only required fields are exposed.

## Validation

Implemented using FluentValidation.

---

# Observability

Recommended logging:

```text
Cache Hit
Cache Miss
Cache Invalidated
Cache Expired
Repository Access
```

These logs simplify diagnostics and troubleshooting.

---

# Running the Application

## Prerequisites

.NET 8 SDK

Verify installation:

```bash
dotnet --version
```

## Run

```bash
git clone <repository-url>

cd ProductCatalog

dotnet restore

dotnet run --project src/ProductCatalog.Api
```

---

# API Examples

## Create Product

```http
POST /api/products
```

```json
{
  "name": "Laptop",
  "price": 1200
}
```

---

## Get Product

```http
GET /api/products/1
```

---

## Update Product

```http
PUT /api/products/1
```

```json
{
  "name": "Gaming Laptop",
  "price": 1500
}
```

---

# Cache Hit / Miss Example

### First Request

```text
GET /api/products/1
```

Result:

```text
Cache Miss
Repository Access
Cache Store
```

### Second Request

```text
GET /api/products/1
```

Result:

```text
Cache Hit
No Repository Access
```

### Update Product

```text
PUT /api/products/1
```

Result:

```text
Cache Invalidated
```

### Next GET

```text
GET /api/products/1
```

Result:

```text
Cache Miss
Repository Access
Cache Recreated
```

---

# Tests

Included test coverage:

- Cache Hit behavior
- Cache Miss behavior
- Cache invalidation
- Concurrent request handling
- Exception handling
- Missing products
- Edge cases

Test Projects:

```text
ProductServiceTests
CacheTests
ConcurrencyTests
ExceptionHandlingTests
```

---

# AI Usage

AI tools were used as engineering assistants and discussion partners.

All architectural decisions were reviewed, challenged, and validated before implementation.

---

# Design Decisions Summary

| Area | Decision |
|--------|----------|
| Cache Type | IMemoryCache |
| Pattern | Decorator |
| Expiration | Absolute Expiration |
| Invalidation | Explicit Remove |
| Null Caching | Disabled |
| Stampede Protection | SharedTaskStore |
| Validation | FluentValidation |
| Mapping | AutoMapper |
| Storage | In-Memory Repository |
| Tests | xUnit |

---

# Future Improvements

- Distributed Cache abstraction
- Redis implementation
- Cache Metrics Dashboard
- OpenTelemetry integration
- Manual Cache Invalidation Endpoint
- ETag support
- Advanced cache versioning

---

This solution prioritizes correctness, consistency, maintainability, and predictable cache behavior while keeping business logic intentionally simple.
