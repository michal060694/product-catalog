# תכנית פיתוח — Product Catalog Cache API
### Vertical Slices · כל Increment ניתן להדגמה

---

## כללי

| | |
|---|---|
| **גישה** | Vertical Slices — מקצה לקצה בכל משימה |
| **אפשר לעצור** | אחרי Slice 5 — API מלא עובד עם Caching |

---

## Slice 1 — הקמת Solution וקשרים בין הפרויקטים

**מה בונים:**

```bash
# יצירת ה-Solution
dotnet new sln -n ProductCatalog

# יצירת הפרויקטים
dotnet new webapi -n ProductCatalog.Api          --no-openapi false
dotnet new classlib -n ProductCatalog.Application
dotnet new classlib -n ProductCatalog.Domain
dotnet new classlib -n ProductCatalog.Infrastructure
dotnet new xunit -n ProductCatalog.Tests

# הוספה ל-Solution
dotnet sln add ProductCatalog.Api/ProductCatalog.Api.csproj
dotnet sln add ProductCatalog.Application/ProductCatalog.Application.csproj
dotnet sln add ProductCatalog.Domain/ProductCatalog.Domain.csproj
dotnet sln add ProductCatalog.Infrastructure/ProductCatalog.Infrastructure.csproj
dotnet sln add ProductCatalog.Tests/ProductCatalog.Tests.csproj
```

**קשרים בין הפרויקטים (Project References):**

```
Domain        ← אין תלויות (הבסיס)
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

# Tests → כולם
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

**מה אפשר להדגים:**
- `dotnet build` — הכל עובר compile ללא שגיאות
- אין circular references — Domain לא מכיר אף פרויקט אחר

---

## Slice 2 — GET Product (ללא Cache)

**מה בונים:**
- `Product.cs` — Entity עם Id, Name, Price, Stock, Version
- `IProductRepository` — GetById, Add, Update
- `InMemoryProductRepository` — seed של 3 מוצרים
- `ProductDto.cs`
- `IProductService` + `ProductService.GetProduct` — קורא ישירות ל-Repo
- `ProductsController` — GET /api/products/{id}
- `Program.cs` + DI בסיסי

**החלטות עיצוב:**

| נושא | החלטה |
|---|---|
| Repository Storage | `ConcurrentDictionary` — thread-safe מובנה, מתאים ל-InMemory |

**מה אפשר להדגים:**
- Swagger: GET → 200 OK עם ProductDto
- GET על ID לא קיים → 404

---

## Slice 3 — Cache Layer (Hit / Miss)

**מה בונים:**
- `IProductCache` — Get, Set, Remove
- `CacheKeys.cs` — `ForProduct(Guid id)`
- `MemoryProductCache` — עוטף IMemoryCache + Absolute Expiration
- `ProductService.GetProduct` — Cache → Miss → Repo → Set Cache
- Logger: `"Cache HIT"` / `"Cache MISS"`

**החלטות עיצוב:**

| נושא | החלטה |
|---|---|
| Cache Implementation | `IMemoryCache` |
| Cache Abstraction | `IProductCache` — מאפשר החלפה ל-Redis בעתיד ללא שינוי ב-Application |
| Expiration | Absolute Expiration — זמן חיים קבוע, צפוי |
| Null Caching | Disabled — לא שומרים `null` בcache |
| 404 Caching | Disabled — מוצר לא קיים לא נשמר בcache |
| Future Cache Migration | Redis-ready — מחליפים רק את `MemoryProductCache`, הכל אחר נשאר |

**מה אפשר להדגים:**
- בקשה 1 → log "Cache MISS" → מגיע מ-Repo
- בקשה 2 → log "Cache HIT" → מגיע מ-Cache
- TTL expiry → MISS שוב אחרי פקיעה

---

## Slice 4 — POST (יצירה + Invalidation)

**מה בונים:**
- `CreateProductDto.cs`
- `CreateProductDtoValidator` — FluentValidation (Name לא ריק, Price > 0, Stock >= 0)
- `ProductService.CreateProduct` — Add ל-Repo → Remove מ-Cache
- `ProductProfile.cs` — AutoMapper
- Controller: POST /api/products → 201 Created + Location header

**החלטות עיצוב:**

| נושא | החלטה |
|---|---|
| Validation | FluentValidation — אימות קלטים בגבול המערכת, לא ב-Entity |
| Mapping | AutoMapper — DTO מגן על חשיפת מידע רגיש מה-Entity |
| Invalidation | Remove בלבד — לא מאכלסים cache על מוצר שאולי לא יבוקש |

**מה אפשר להדגים:**
- POST מוצר → 201 Created
- GET מיד אחריו → 200 (מגיע מ-Repo כי Cache נוקה)
- POST עם Name ריק → 400 ValidationError

---

## Slice 5 — PUT (עדכון + Cache Invalidation)

**מה בונים:**
- `UpdateProductDto.cs`
- `UpdateProductDtoValidator` — FluentValidation
- `ProductService.UpdateProduct` — Update ב-Repo → `Remove` מ-Cache (לא Set!)
- Version bump על ה-Entity — פתרון ל-Race Condition של GET+PUT במקביל
- Controller: PUT /api/products/{id} → 200 OK

**החלטות עיצוב:**

| נושא | החלטה |
|---|---|
| Update Strategy | Invalidate ולא Update — לא מבזבזים זיכרון על מוצרים שאולי לא יבוקשו שוב |
| Race Condition Protection | Versioning — `Set` ב-Cache בודק שה-version מה-Repo ≥ ל-version ב-Cache |

**מה אפשר להדגים:**
- PUT מוצר → cache נמחק → GET מיד אחריו → fresh מ-Repo
- PUT על ID לא קיים → 404

---

## Slice 6 — Request Coalescing (SharedTaskStore) ⭐

**מה בונים:**
- `ISharedTaskStore` — GetOrAdd, Remove
- `SharedTaskStore` — `ConcurrentDictionary<string, Task<ProductDto?>>`
- `ProductService.GetProduct` — Cache Miss → GetOrAdd:
  - Task קיים? → await אותו (reuse)
  - Task לא קיים? → צור חדש → Repo → Set Cache → Remove Task
- Logger: `"InFlight CREATED"` / `"InFlight REUSED"` / `"InFlight COMPLETED"`

**החלטות עיצוב:**

| נושא | החלטה |
|---|---|
| Stampede Prevention | `SharedTaskStore` — משתף Task אחד לכל הבקשות במקביל, לא Semaphore שחוסם |

**מה אפשר להדגים:**
- 100 concurrent GETs על אותו מוצר → רק **1** קריאה ל-Repo
- Logs מראים: CREATED אחד + 99 × REUSED

---

## Slice 7 — Error Handling Middleware

**מה בונים:**
- `ExceptionHandlingMiddleware` — תפיסה מרכזית:
  - `ProductNotFoundException` → 404 ProblemDetails
  - `ValidationException` → 400 + שגיאות לפי שדה
  - `Exception` → 500 (ללא stack trace בתגובה)
- `ApplicationBuilderExtensions` — `UseExceptionHandling()`
- `ServiceCollectionExtensions` — DI מלא מסודר

**החלטות עיצוב:**

| נושא | החלטה |
|---|---|
| Error Handling | Middleware מרכזי — לא try/catch בכל Controller |

**מה אפשר להדגים:**
- כל שגיאה מחזירה ProblemDetails מסודר
- אין חשיפת פרטים פנימיים ב-500
- DTO מונע חשיפת מידע רגיש מה-Entity

**נושאי אבטחה:**
> - Cache Key מורכב מ-`product:{id}` — אם היו הרשאות, היינו מוסיפים `userId` למפתח
> - Cache Poisoning: אנחנו מעדכנים Cache **רק** ממה שחוזר מ-Repository, לא מקלט המשתמש
> - FluentValidation מאמת קלטים בגבול המערכת

---

## Slice 8 — Tests

**מה בונים:**

| קובץ | תרחישים |
|---|---|
| `CacheTests.cs` | Hit, Miss, TTL expiry, Invalidation after PUT/POST |
| `ConcurrencyTests.cs` | 100 concurrent → 1 Task נוצר, Task נמחק אחרי השלמה, Race GET+PUT |
| `ProductServiceTests.cs` | GetProduct (hit/miss), CreateProduct, UpdateProduct, Not Found |
| `ExceptionHandlingTests.cs` | 404, 400 עם validation errors, 500 |

**מה אפשר להדגים:**
- `dotnet test` — all green
- Mock על `IProductCache` ו-`ISharedTaskStore` (לא על `IMemoryCache` ישירות)

---

## Bonus — DELETE Cache Endpoint (אופציה C)
> **רק אם נשאר זמן** — לא חלק מה-core

- `DELETE /api/cache/{id}` — Admin endpoint לניקוי Cache ידני
- אם היו הרשאות: Key = `userId + productId`

---

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

## מבנה הפרויקט

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

## סיכום

| Slice | תוצר |
|---|---|
| 1 | Solution + Projects + References |
| 2 | GET עובד מקצה לקצה |
| 3 | Cache Hit/Miss עם logs |
| 4 | POST + Validation |
| 5 | PUT + Invalidation |
| 6 | Request Coalescing ⭐ |
| 7 | Error Middleware |
| 8 | Tests — all green |

> 💡 **אפשר לעצור אחרי Slice 5** — יש API מלא עובד עם Caching. Slices 6-8 מוסיפים את הדברים הכי מתקדמים.
