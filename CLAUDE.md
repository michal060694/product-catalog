# CLAUDE.md — Product Catalog Cache API

## מה הפרויקט הזה

מימוש assignment של **Caching Strategy & Consistency** ב-ASP.NET Core 8+.
המטרה: Product Catalog API עם In-Memory Cache, Request Coalescing, Cache Invalidation ופתרון Race Conditions.
תכנית הפיתוח המפורטת נמצאת ב-[PLAN.md](./PLAN.md).

---

## מבנה הפרויקט

```
ProductCatalog.Domain          ← אין תלויות (Entities, Interfaces, Exceptions)
ProductCatalog.Application     ← Domain בלבד (Services, DTOs, Validators, Mapping)
ProductCatalog.Infrastructure  ← Domain בלבד (Repository, Cache, SharedTaskStore)
ProductCatalog.Api             ← Application + Infrastructure (Controllers, Middleware, DI)
ProductCatalog.Tests           ← Application + Infrastructure (xUnit + FakeItEasy)
```

**חוק אחד:** `Application` לא מכיר את `Infrastructure`. הם מחוברים רק דרך DI ב-`Program.cs`.

---

## Stack טכנולוגי

| שכבה | טכנולוגיה |
|---|---|
| Cache | `IMemoryCache` (עוטף ב-`IProductCache`) |
| Validation | FluentValidation |
| Mapping | AutoMapper |
| Testing | xUnit + FakeItEasy + FluentAssertions |
| Error Handling | Middleware מרכזי → ProblemDetails |
| Documentation | Swashbuckle (Swagger) |

---

## החלטות ארכיטקטורה מרכזיות

- **Cache Invalidation:** `Remove` בלבד — לא מעדכנים cache אחרי PUT/POST
- **Expiration:** Absolute Expiration (לא Sliding)
- **Stampede Prevention:** `SharedTaskStore` — `ConcurrentDictionary<string, Task<ProductDto?>>` — לא Semaphore
- **Race Condition:** Versioning על ה-Entity — `Set` ב-cache בודק שה-version ≥ לגרסה הנוכחית
- **Null/404 Caching:** Disabled — לא שומרים `null` ב-cache
- **Future:** Redis-ready — מחליפים רק את `MemoryProductCache`

---

## כללי קוד (Infraedge Clean Code Standards)

### חובה (Mandatory)

**SRP ומתודות:**
- כל מתודה עושה דבר אחד בלבד
- אורך מתודה עד 40–60 שורות, עד 3 רמות הזחה
- Controllers הם Orchestration בלבד — אין לוגיקה עסקית

**שכבות:**
- `Controller/Endpoint` — תזמור בלבד
- `Domain` — לוגיקה עסקית טהורה, ללא תלות ב-Infrastructure
- `Infrastructure` — מימושים טכניים בלבד
- אין זליגת מודלים בין שכבות

**Validation:**
- ולידציה טכנית (null/format/range) — ב-Edge בלבד עם FluentValidation
- ולידציה עסקית — ב-Domain/Application
- אין כפילות ולידציה

**DTOs:**
- DTOs מוגדרים כ-`record` בלבד
- Entities לא נחשפות ישירות ללקוח
- מיפוי מפורש Entity → DTO באמצעות AutoMapper

**Async:**
- תמיד `async Task` עם `CancellationToken`
- אסור `.Result` / `.Wait()` / `async void`
- CancellationToken עובר לכל הקריאות

**שגיאות:**
- Exceptions רק עבור כשלים לא צפויים (Infrastructure/System)
- כשלים צפויים (NotFound/Validation) — Exceptions שנתפסות ב-Middleware → ProblemDetails
- אין swallow חריגים ריק
- אין Stack Trace בתגובה ללקוח

**DI:**
- Constructor injection בלבד
- אין `new` ל-dependencies בתוך Services
- אין `IServiceProvider.GetService` בלוגיקה עסקית

**Nullability:**
- `<Nullable>enable</Nullable>` בכל הפרויקטים
- לא מחזירים `null` ללא טיפול ברור

**Naming:**
- סיומת `Async` למתודות אסינכרוניות
- שמות ברורים שמבטאים כוונה, לא מימוש
- `Given_When_Then` לשמות טסטים

**Logging:**
- Structured logging עם context
- אסור לכתוב Secrets/PII ללוגים
- Logger: `"Cache HIT"` / `"Cache MISS"` / `"InFlight CREATED"` / `"InFlight REUSED"`

### DI Registration (Lifetimes)

```csharp
// Singleton — shared state (חייב להיות Singleton!)
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

## אבטחה

- Cache Key: `product:{id}` — אם יהיו הרשאות, יש להרחיב ל-`product:{userId}:{id}`
- Cache Poisoning: מעדכנים Cache **רק** ממה שחוזר מ-Repository, לא מקלט המשתמש
- DTO מונע חשיפת מידע רגיש מה-Entity
- FluentValidation מאמת קלטים בגבול המערכת

---

## מה לא לעשות בפרויקט הזה

- אין לוגיקה עסקית ב-`ProductsController`
- אין לעדכן cache אחרי PUT/POST (רק `Remove`)
- אין `SlidingExpiration` — רק `AbsoluteExpiration`
- אין לאכלס cache על null / מוצר לא קיים
- אין להשתמש ב-Semaphore לפתרון cache stampede — יש `SharedTaskStore`
- אין `IMemoryCache` ישירות ב-`ProductService` — רק דרך `IProductCache`
- אין לחשוף `Product` entity ישירות — רק `ProductDto`
