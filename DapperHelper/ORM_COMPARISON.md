# EF Core vs Dapper+QueryLib — Performance Comparison Report

## Overview

This project implements the same `IUserRepository` interface twice: once with **EF Core** and once
with a custom **Dapper + QueryLib** stack. Eight timed performance tests were run on two fixtures:

| Fixture | Rows | Iterations |
|---|---|---|
| `DatabaseFixture` (small) | 2 users, 2 tenants | 5 warm-up + 50 timed |
| `LargeDatasetFixture` | 100 000 users, 20 tenants | 2 warm-up + 3–20 timed |

All tests passed. Results are from a single run on Apple Silicon (M-series, SQLite in-memory).

---

## Small Dataset Results (2 rows, 50 iterations)

| Operation | EF Core ms/call | Dapper ms/call | EF/Dapper ratio |
|---|---|---|---|
| `GetAllAsync` | 0.196 | 0.027 | **7.30× slower** |
| `GetByIdAsync` | 0.245 | 0.092 | **2.67× slower** |
| `GetByCredentialAsync` | 0.277 | 0.150 | **1.85× slower** |
| `GetUserWithTenants` | 0.391 | 0.124 | **3.14× slower** |

**Dapper wins all four scenarios on the small dataset.**

---

## Large Dataset Results (100 000 rows)

| Operation | Iters | EF Core ms/call | Dapper ms/call | EF/Dapper ratio |
|---|---|---|---|---|
| `GetAllAsync` (full table load) | 3 | 263.76 | 143.49 | **1.84× slower** |
| `GetByIdAsync` (PK lookup, row 50 000) | 20 | 0.18 | 0.08 | **2.31× slower** |
| `GetByCredentialAsync` (table scan, no index) | 20 | 2.17 | 3.93 | **0.55× — EF faster** |
| `GetUserWithTenants` (3-table join, row 50 000) | 20 | 0.40 | 0.10 | **3.90× slower** |

---

## Analysis

### Where Dapper wins (and why)

**`GetAllAsync` — largest absolute gap at scale (1.84×, 263 ms vs 143 ms per call)**

EF Core loads every row through its change-tracker, allocates tracked `UserEntity` instances, and
runs its identity-resolution pipeline for each object. Dapper maps rows directly to POCOs with a
thin reflection layer. At 100 000 rows the overhead compounds: EF Core took 264 ms vs Dapper's
143 ms — a 120 ms per-call penalty on raw materialization alone.

**`GetUserWithTenants` — highest ratio at scale (3.90×)**

EF Core builds an anonymous-type projection (`new { u, ut, t }`) across three joined tables and
then performs in-memory reshaping into `CustomUserDto`. Dapper uses multi-mapping (`splitOn`) which
pipes columns directly into the target types in a single pass. For join-heavy queries the EF
overhead is proportionally largest because every returned row allocates three anonymous objects
before the final projection.

**`GetByIdAsync` — consistent 2.31–2.67× advantage**

A PK lookup returns one row. Even so, EF Core pays a fixed per-query cost: it checks the
change-tracker for a cached entity, decides it cannot be found, issues the query, and then registers
the result with the tracker before returning it. Dapper simply opens a connection and issues the
SQL. The ratio is stable between small and large datasets because the bottleneck is EF overhead, not
data volume.

**`GetAllAsync` small dataset — most extreme ratio (7.30×)**

On only 2 rows the query execution time is near-zero for both ORMs. The ratio blows out because EF
Core's fixed startup cost (query compilation, model validation, change-tracker init) is not amortised
over meaningful result-set work. Dapper's absolute time is ~0.027 ms/call; EF Core is ~0.196 ms/call.

### Where EF Core wins (and why)

**`GetByCredentialAsync` large dataset (EF 0.55× — EF is 1.81× faster)**

This is the only scenario where EF Core leads. The difference is SQL generation:

- **EF Core** (`FirstOrDefaultAsync`) emits `SELECT ... WHERE Email=@e AND Password=@p LIMIT 1`.
  SQLite stops scanning the 100 000-row table as soon as it finds the first match.
- **Dapper + QueryLib** (`QuerySingleOrDefaultAsync`) emits `SELECT * FROM Users WHERE Email=@e AND Password=@p`
  — **no LIMIT clause**. SQLite scans the entire table, returns every matching row across the wire, and
  Dapper picks the first one client-side.

The 1.81× EF win here is entirely an artifact of the QueryLib not emitting `LIMIT 1` for
single-row operations, not a fundamental EF Core strength. Adding `.Take(1)` to the Dapper query
would eliminate this gap and likely return it to Dapper's favour.

---

## Summary

| Characteristic | EF Core | Dapper + QueryLib |
|---|---|---|
| Raw throughput (full scan) | Slower (1.84×) | Faster — direct mapping |
| PK / indexed lookups | Slower (2.31–2.67×) | Faster — no change-tracker |
| Complex joins | Slower (3.14–3.90×) | Faster — multi-map splitOn |
| Unindexed single-row lookup | **Faster (0.55×)** | Slower — missing LIMIT 1 |
| Code verbosity | Low — LINQ, auto-schema | Higher — QueryLib DSL + splitOn |
| Query control | Implicit (LINQ→SQL) | Explicit — full SQL visible |

**Dapper wins 7 of 8 measured scenarios.** The single EF Core victory is not a fundamental
advantage but a bug in the QueryLib: `QueryBuilder.Build()` never appends `LIMIT 1`, so
`QuerySingleOrDefaultAsync` always fetches all matching rows. Fixing it with `.Take(1)` would
restore Dapper's lead across the board.

EF Core's trade-off is developer productivity: no manual SQL, automatic migrations, and first-class
LINQ support — at the cost of 2–7× slower per-call performance on the operations measured here.
Dapper's trade-off is the inverse: near-raw-SQL speed with full query control, at the cost of
explicit mapping and a custom query builder for parameterisation.
