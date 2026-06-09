# EF Core vs Dapper+QueryLib vs Raw Dapper — Performance Comparison Report

## Overview

This project implements the same `IUserRepository` interface three ways:
- **EF Core** — LINQ queries through the change-tracker
- **Dapper + QueryLib** — Dapper with a custom query-builder abstraction
- **Raw Dapper** — Dapper with hand-written SQL strings, no abstraction layer

Eight timed performance tests were run on two fixtures:

| Fixture | Rows | Iterations |
|---|---|---|
| `DatabaseFixture` (small) | 2 users, 2 tenants | 5 warm-up + 50 timed |
| `LargeDatasetFixture` | 100 000 users, 20 tenants | 2 warm-up + 3–20 timed |

All 22 tests passed. Results are from a single run on Apple Silicon (M-series, SQLite in-memory).

---

## Small Dataset Results (2 rows, 50 iterations)

| Operation | EF Core ms/call | Dapper+QueryLib ms/call | Raw Dapper ms/call | EF/Dapper ratio | EF/Raw ratio |
|---|---|---|---|---|---|
| `GetAllAsync` | 0.568 | 0.112 | 0.087 | **5.08× slower** | **6.49× slower** |
| `GetByIdAsync` | 0.303 | 0.144 | 0.044 | **2.11× slower** | **6.81× slower** |
| `GetByCredentialAsync` | 0.381 | 0.142 | 0.028 | **2.68× slower** | **13.56× slower** |
| `GetUserWithTenants` | 0.563 | 0.164 | 0.032 | **3.43× slower** | **17.47× slower** |

**Raw Dapper wins all four scenarios. Dapper+QueryLib beats EF Core in all four.**

---

## Large Dataset Results (100 000 rows)

| Operation | Iters | EF Core ms/call | Dapper+QueryLib ms/call | Raw Dapper ms/call | EF/Dapper ratio | EF/Raw ratio |
|---|---|---|---|---|---|---|
| `GetAllAsync` (full table load) | 3 | 284.98 | 169.40 | 168.76 | **1.68× slower** | **1.69× slower** |
| `GetByIdAsync` (PK lookup, row 50 000) | 20 | 0.29 | 0.10 | 0.02 | **3.07× slower** | **13.16× slower** |
| `GetByCredentialAsync` (table scan, no index) | 20 | 2.35 | 4.03 | 3.67 | **0.58× — EF faster** | **0.64× — EF faster** |
| `GetUserWithTenants` (3-table join, row 50 000) | 20 | 0.33 | 0.12 | 0.03 | **2.66× slower** | **11.64× slower** |

---

## Analysis

### Where Raw Dapper wins (and why)

**Small dataset: extreme ratios (up to 17.47× over EF Core)**

On only 2 rows, query execution time is near-zero. EF Core's fixed startup cost (query compilation,
model validation, change-tracker init) is not amortised over any real result-set work, producing
large ratios. Raw Dapper's absolute times are in the 0.02–0.09 ms range — mostly connection-open
overhead with trivial SQL cost on top.

**`GetUserWithTenants` small dataset — 17.47× over EF, 5.09× over Dapper+QueryLib**

Raw Dapper writes the join query and `splitOn` mapping directly with no intermediate layer. The
QueryLib adds a small but measurable cost: it builds a `SqlQuery` object, runs `Build()`, then
hands the string to Dapper. For a 3-table join returning 2 rows that overhead (0.032 ms vs 0.164 ms)
is proportionally visible.

**`GetByIdAsync` and `GetUserWithTenants` large dataset (13.16× and 11.64× over EF)**

At scale on indexed lookups that return a single row, the query itself is sub-millisecond. The ratio
gap between Raw Dapper (0.02–0.03 ms) and Dapper+QueryLib (0.10–0.12 ms) is the cost of the
QueryLib's `Build()` path each call.

### Where Dapper+QueryLib wins over EF Core (and why)

**`GetAllAsync` — significant absolute gap at scale (1.68×, 285 ms vs 169 ms per call)**

EF Core loads every row through its change-tracker, allocates tracked `UserEntity` instances, and
runs its identity-resolution pipeline per object. Dapper maps rows directly to POCOs. At 100 000
rows this compounds: EF Core took 285 ms vs ~169 ms for both Dapper variants, a 116 ms per-call
penalty. The Dapper+QueryLib and Raw Dapper times are nearly identical here (169.40 vs 168.76 ms)
because the materialization cost dominates and the `Build()` overhead is negligible.

**`GetByIdAsync` — consistent 2.11–3.07× advantage over EF**

A PK lookup returns one row. EF Core still pays a fixed per-query cost: it checks the change-tracker
for a cached entity, issues the query, and registers the result before returning. Dapper simply
opens a connection and issues the SQL. The ratio is stable between small and large datasets.

### Where EF Core wins (and why)

**`GetByCredentialAsync` large dataset (EF 0.58× vs Dapper — EF is 1.73× faster)**

The only scenario where EF Core leads. The difference is SQL generation:

- **EF Core** (`FirstOrDefaultAsync`) emits `SELECT ... WHERE Email=@e AND Password=@p LIMIT 1`.
  SQLite stops scanning at the first match.
- **Dapper + QueryLib** and **Raw Dapper** both emit `SELECT * FROM Users WHERE Email=@e AND Password=@p`
  — **no LIMIT clause**. SQLite scans the entire 100 000-row table, returns all matching rows, and
  Dapper picks the first one client-side.

The EF win here is entirely an artifact of the missing `LIMIT 1` in both Dapper paths. Adding
`.Take(1)` would eliminate this gap and restore Dapper's lead across the board.

### Dapper+QueryLib vs Raw Dapper: the abstraction cost

| Operation | Dapper+QueryLib ms/call | Raw Dapper ms/call | Overhead |
|---|---|---|---|
| `GetAllAsync` (small) | 0.112 | 0.087 | +0.025 ms (+29%) |
| `GetByIdAsync` (small) | 0.144 | 0.044 | +0.100 ms (+227%) |
| `GetByCredentialAsync` (small) | 0.142 | 0.028 | +0.114 ms (+407%) |
| `GetUserWithTenants` (small) | 0.164 | 0.032 | +0.132 ms (+413%) |
| `GetAllAsync` (large) | 169.40 | 168.76 | +0.64 ms (+0.4%) |
| `GetByIdAsync` (large) | 0.10 | 0.02 | +0.08 ms (+400%) |
| `GetByCredentialAsync` (large) | 4.03 | 3.67 | +0.36 ms (+10%) |
| `GetUserWithTenants` (large) | 0.12 | 0.03 | +0.09 ms (+300%) |

The QueryLib overhead is most visible on fast single-row operations (0.02–0.13 ms absolute), where
it represents a large percentage of an already-tiny total. For bulk loads the absolute overhead
disappears into materialization cost.

---

## Summary

| Characteristic | EF Core | Dapper + QueryLib | Raw Dapper |
|---|---|---|---|
| Raw throughput (full scan) | Slower (1.68×) | Fast — direct mapping | Fast — near-identical to QueryLib |
| PK / indexed lookups | Slower (2.11–3.07×) | Fast — no change-tracker | Fastest — no abstraction overhead |
| Complex joins | Slower (2.66–3.43×) | Fast — multi-map splitOn | Fastest |
| Unindexed single-row lookup | **Faster (0.58×)** | Slower — missing LIMIT 1 | Slower — missing LIMIT 1 |
| Code verbosity | Low — LINQ, auto-schema | Medium — QueryLib DSL | High — raw SQL strings |
| Query control | Implicit (LINQ→SQL) | Explicit — SQL visible | Explicit — full SQL visible |
| Abstraction cost | Framework overhead | Small but measurable | None |

**Raw Dapper wins all measured scenarios except the LIMIT 1 bug. Dapper+QueryLib beats EF Core in
7 of 8 scenarios. The single EF Core victory (unindexed credential lookup) is not a fundamental
advantage but a bug: neither Dapper path emits `LIMIT 1`, so both scan the full table. Fixing it
restores Dapper's lead across the board.**

EF Core's trade-off is developer productivity: no manual SQL, automatic migrations, first-class LINQ
support — at the cost of 1.7–17× slower per-call performance. Raw Dapper's trade-off is maximum
performance at the cost of hand-written SQL everywhere. Dapper+QueryLib sits in between: near-Dapper
speed for bulk operations, a small but measurable overhead (0.02–0.13 ms) for single-row operations,
with a parameterisation DSL instead of raw string concatenation.