# DapperHelper — Project Overview

## What this project is

DapperHelper is a **compile-safe query builder for Dapper**. Dapper is a micro-ORM that maps SQL
query results to C# objects, but it takes raw SQL strings — meaning a typo in a column name, a
missing quote, or a renamed property only fails at runtime. This project solves that by providing a
fluent, expression-driven API (`QueryLib`) that constructs parameterised SQL from C# lambda
expressions, so the compiler catches mistakes before the query ever runs.

The query builder lives alongside an EF Core implementation and a Raw Dapper implementation (hand-written SQL, no abstraction) of the same repository interface, making a direct three-way performance comparison possible.

---

## What was built

### QueryLib — the Dapper helper

The core library translates strongly-typed C# expressions into SQL at runtime.

**`QueryBuilder<T>`** is the entry point for simple single-table queries. Calls chain fluently:

```csharp
var query = QueryBuilder<UserEntity>.From()
    .Where(u => u.Email == email && u.Password == password)
    .Build();
// → SELECT * FROM Users WHERE (Email = @p0 AND Password = @p1)
```

**`JoinedQueryBuilder`** handles multi-table queries. Tables are aliased automatically (`t0`, `t1`,
`t2`, …) and every `Where`, `Select`, and `OrderBy` call is scoped to the correct alias:

```csharp
var query = QueryBuilder<UserEntity>.From()
    .LeftJoin<UserTenantsEntity>((u, ut) => u.Id == ut.UserId)
    .LeftJoin<UserTenantsEntity, TenantEntity>((ut, t) => ut.TenantId == t.Id)
    .Where(u => u.Id == id)
    .SelectFrom<UserEntity>(u => u.Id, u => u.Username, u => u.Email)
    .SelectFrom<UserTenantsEntity>(ut => ut.UserId, ut => ut.Status)
    .SelectFrom<TenantEntity>(t => t.Id, t => t.Name)
    .Build();
```

**`ExpressionParser`** walks the .NET expression tree and converts it to a SQL fragment. It
supports `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `.Contains()`, `.StartsWith()`,
`.EndsWith()`, and collection `IN (...)` — all producing parameterised placeholders, never
string-interpolated values.

**`ColumnResolver`** maps C# type/property names to table/column names, respecting
`[Table]`/`[Column]` data annotations when present and falling back to the property name otherwise.

**`ISqlDialect`** makes LIKE syntax and paging (`LIMIT`/`OFFSET` vs `OFFSET … ROWS FETCH NEXT`)
swappable per database. Implementations ship for SQLite, PostgreSQL, and SQL Server.

**`BuiltQuery`** is the output: a `Sql` string and a `Parameters` dictionary ready to pass directly
to any Dapper method (`QueryAsync`, `QuerySingleOrDefaultAsync`, etc.).

---

## Performance test results

Eight timed tests compare EF Core, Dapper+QueryLib, and Raw Dapper across two fixtures:

- **Small fixture**: 2 users, 50 timed iterations per test.
- **Large fixture**: 100 000 users, 3–20 timed iterations per test.

### Small dataset (2 rows)

| Operation | EF Core ms/call | Dapper+QueryLib ms/call | Raw Dapper ms/call |
|---|---|---|---|
| `GetAllAsync` | 0.568 | 0.112 | 0.087 |
| `GetByIdAsync` | 0.303 | 0.144 | 0.044 |
| `GetByCredentialAsync` | 0.381 | 0.142 | 0.028 |
| `GetUserWithTenants` | 0.563 | 0.164 | 0.032 |

Raw Dapper wins all four. EF Core is 5–17× slower than Raw Dapper and 2–5× slower than Dapper+QueryLib.

### Large dataset (100 000 rows)

| Operation | EF Core ms/call | Dapper+QueryLib ms/call | Raw Dapper ms/call |
|---|---|---|---|
| `GetAllAsync` | 284.98 | 169.40 | 168.76 |
| `GetByIdAsync` | 0.29 | 0.10 | 0.02 |
| `GetByCredentialAsync` | 2.35 | 4.03 | 3.67 |
| `GetUserWithTenants` | 0.33 | 0.12 | 0.03 |

Raw Dapper wins three of four large-dataset scenarios. EF Core wins only `GetByCredentialAsync`,
which is not a fundamental EF advantage — EF Core's `FirstOrDefaultAsync` emits `LIMIT 1` so
SQLite stops at the first match, whereas neither Dapper path appends `LIMIT 1`, forcing a full
table scan. Adding `.Take(1)` would close that gap.

The largest absolute gap is the full-table load: both Dapper variants materialise 100 000 rows in
~169 ms vs EF Core's ~285 ms, because EF Core runs every row through its change-tracker and
identity-resolution pipeline. On bulk loads the QueryLib abstraction adds no measurable overhead
over raw SQL. For fast single-row lookups the QueryLib's `Build()` call is visible (e.g. 0.10 ms
vs 0.02 ms for a PK lookup), though both remain well under 1 ms.

---

## Project structure

```
QueryLib/          — the Dapper query builder (ISqlDialect, QueryBuilder, JoinedQueryBuilder, ExpressionParser, …)
DAL/               — Dapper+QueryLib UserRepository
DAL.EF/            — EF Core UserRepository
DAL.RawDApper/     — Raw Dapper UserRepository (hand-written SQL, no abstraction)
DAL.Contracts/     — shared IUserRepository interface
Models/            — entities and DTOs
Tests/             — performance and correctness tests (xUnit, SQLite in-memory)
```
