# DapperHelper — Project Overview

## What this project is

DapperHelper is a **compile-safe query builder for Dapper**. Dapper is a micro-ORM that maps SQL
query results to C# objects, but it takes raw SQL strings — meaning a typo in a column name, a
missing quote, or a renamed property only fails at runtime. This project solves that by providing a
fluent, expression-driven API (`QueryLib`) that constructs parameterised SQL from C# lambda
expressions, so the compiler catches mistakes before the query ever runs.

The query builder lives alongside a full EF Core implementation of the same repository interface,
which makes a direct apples-to-apples performance comparison possible.

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

Eight timed tests compare EF Core against Dapper+QueryLib across two fixtures:

- **Small fixture**: 2 users, 50 timed iterations per test.
- **Large fixture**: 100 000 users, 3–20 timed iterations per test.

### Small dataset (2 rows)

| Operation | EF Core ms/call | Dapper ms/call | Winner |
|---|---|---|---|
| `GetAllAsync` | 0.196 | 0.027 | Dapper **7.3×** faster |
| `GetByIdAsync` | 0.245 | 0.092 | Dapper **2.7×** faster |
| `GetByCredentialAsync` | 0.277 | 0.150 | Dapper **1.9×** faster |
| `GetUserWithTenants` | 0.391 | 0.124 | Dapper **3.1×** faster |

### Large dataset (100 000 rows)

| Operation | EF Core ms/call | Dapper ms/call | Winner |
|---|---|---|---|
| `GetAllAsync` | 263.76 | 143.49 | Dapper **1.8×** faster |
| `GetByIdAsync` | 0.18 | 0.08 | Dapper **2.3×** faster |
| `GetByCredentialAsync` | 2.17 | 3.93 | EF Core **1.8×** faster |
| `GetUserWithTenants` | 0.40 | 0.10 | Dapper **3.9×** faster |

Dapper wins 7 of 8 scenarios. The one EF Core win (`GetByCredentialAsync` on 100k rows) is not a
fundamental EF advantage — EF Core's `FirstOrDefaultAsync` emits `LIMIT 1` so SQLite stops at the
first match, whereas the current QueryLib build does not append `LIMIT 1` for single-row reads and
causes a full table scan. Adding `.Take(1)` to that query would close the gap.

The largest absolute gap is the full-table load: Dapper materialises 100 000 rows in ~143 ms vs EF
Core's ~264 ms, because EF Core runs every row through its change-tracker and identity-resolution
pipeline. The largest ratio is the 2-row `GetAllAsync` (7.3×), where query execution time is near
zero and the entire difference is EF Core's fixed per-query startup cost.

---

## Project structure

```
QueryLib/          — the Dapper query builder (ISqlDialect, QueryBuilder, JoinedQueryBuilder, ExpressionParser, …)
DAL/               — Dapper-based UserRepository using QueryLib
DAL.EF/            — EF Core UserRepository for comparison
DAL.Contracts/     — shared IUserRepository interface
Models/            — entities and DTOs
Tests/             — performance and correctness tests (xUnit, SQLite in-memory)
```
