# Template benchmarks, re-measured 2026-10-21

Every BenchmarkDotNet class in `za-clean` and `za-vertical-slice`, re-measured after the ZA.Mapping
and ZA.Mediator releases that followed the 2026-10-11 allocation sweep.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Measured on an otherwise idle machine, out of process, in three launches pinned to two performance
cores. A ratio compares two rows of one class from the same run, with the lowest and highest of the
three launches in brackets. Postgres ran in a local Docker container. Allocations that changed from
the 2026-10-11 capture were confirmed per thread with `GC.GetAllocatedBytesForCurrentThread`.

## Finding: `ReadPipelineBench` measured only cache hits

`GET /orders/{id}` is wrapped in output caching, and the cache policy caches authenticated GETs.
The benchmark repeats one request for one id, so after the first iteration every request was
served from the cache, and the mediator, ZA.ORM read and JSON serialisation it describes never ran.
The allocations give it away: 24.7 KB per request, against 28.7 KB once the route's cache is off.

The benchmark now takes an `OutputCached` parameter and measures both paths. With it off, the
benchmark replaces the route's cache policy with one that does not cache.

This also moved the [#171](2026-08-07-171-jwt-validation-cost.md) denominator. The addendum in
that report re-measures JWT validation against both paths in one run: 6.2% of an uncached request
and 10.9% of a cache hit, against the 4% the report first gave.

## za-clean

### `GET /orders/{id}` — `ReadPipelineBench`

Measured together with `JwtValidationBench` in a second run the same evening.

| Output cache | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| off | 1.00 | | 28,738 B |
| hit | 0.57 | 0.54–0.58 | 24,723 B |

### `POST /orders` — `WritePipelineBench`

| Backend | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| Sqlite | 1.00 | | 36,920 B |
| Postgres | 37.1 | 34.1–38.1 | 38,551 B |

Time on Postgres is dominated by the round trip to the container, so the ratio describes this
setup, not the template.

### JWT validation — `JwtValidationBench`

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| `ValidatePerRequest` | 1.00 | | 4,400 B |
| `ValidateOnly_NoPrincipal` | 0.71 | 0.61–0.81 | 2,696 B |
| `CacheHit_HashKey` | 0.13 | 0.12–0.15 | 336 B |
| `CacheHit_TokenKey` | 0.01 | 0.01–0.02 | 0 B |

### Read hot path — `ReadHotPathBench`

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| `HandWrittenAdoNet` | 1.00 | | 2,128 B |
| `ZeroAlloc_ORM` | 1.08 | 1.07–1.09 | 2,992 B |

### Mediator dispatch and security context

| Benchmark | Allocated |
|---|---:|
| `MediatorDispatchBench.Dispatch` | 4,296 B |
| `SecurityContextBench.HasScope_SingleValue` | 0 B |
| `SecurityContextBench.HasScope_MultiValueClaims` | 72 B |
| `SecurityContextBench.HasScope_Missing` | 0 B |

`SecurityContextAllocationTests` asserts the security-context figures.

### Primitives — `PrimitivesBench`

Each row's time relative to `Mediator_Send`:

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| `Mapping_RequestToCommand` | 0.04 | 0.04–0.04 | 88 B |
| `Mediator_Send` | 1.00 | | 384 B |
| `Handler_Direct` | 0.30 | 0.29–0.31 | 208 B |
| `Validator_Generated` | 0.09 | 0.09–0.09 | 0 B |
| `ValueObject_TryCreate` | 0.01 | 0.01–0.01 | 0 B |
| `EndToEndPrimitives` | 1.15 | 1.10–1.16 | 472 B |

Mapping fell from 160 B to 88 B and the mediator send from 456 B to 384 B since 2026-10-11; what
each byte is, is broken down in [za-clean.md](../za-clean.md).

## za-vertical-slice

### `GET /orders/{id}` — `ReadPipelineBench`

| Output cache | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| off | 1.00 | | 26,343 B |
| hit | 0.71 | 0.70–0.73 | 24,636 B |

### `POST /orders` — `WritePipelineBench`

| Backend | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| Sqlite | 1.00 | | 30,785 B |
| Postgres | 56.0 | 53.9–60.2 | 34,537 B |

### Mediator dispatch and security context

| Benchmark | Allocated |
|---|---:|
| `MediatorDispatchBench.Dispatch` | 3,056 B |
| `SecurityContextBench.HasScope_SingleValue` | 0 B |
| `SecurityContextBench.HasScope_MultiValueClaims` | 72 B |
| `SecurityContextBench.HasScope_Missing` | 0 B |

### Primitives — `PrimitivesBench`

Each row's time relative to `Money_TryCreate_Success`:

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| `TypedId_Construct` | below resolution ¹ | | 0 B |
| `Money_TryCreate_Success` | 1.00 | | 0 B |
| `Money_TryCreate_Failure` | 0.94 | 0.92–0.97 | 0 B |
| `Result_Success` | 0.58 | 0.53–0.66 | 0 B |
| `Result_Failure` | below resolution ¹ | | 0 B |
| `Validator_Generated` | 0.39 | 0.37–0.43 | 0 B |

¹ Under a nanosecond, below what BenchmarkDotNet resolves. `tests/MyApp.UnitTests/AllocationTests.cs`
asserts the 0 B of every row; the two failure-path gates were added with this re-measure.

## Reproducing

Run from inside each benchmark project. Both templates have projects with the same names, so
BenchmarkDotNet's project search finds two matches from the template root.

```bash
cd content/za-clean/benchmarks/MyApp.Benchmarks
dotnet run -c Release -- --filter "*" --launchCount 3 --affinity 20
```

`WritePipelineBench` reads the Postgres connection from `POSTGRES_HOST`, `POSTGRES_PORT`,
`POSTGRES_DB`, `POSTGRES_USER` and `POSTGRES_PASSWORD`.
