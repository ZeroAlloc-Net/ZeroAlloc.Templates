# ReadHotPathBench — Post-#173 Flat Read Model

Measures the allocation footprint of the `GET /orders/{id}` read path on
the za-clean template after #173 (replacing domain-aggregate
reconstruction with a flat `OrderReadModel` projection). N=2 lines per
the bench's seed (head + lines multi-result-set).

## Post-change result (this branch, `feat/flat-read-model`)

Both benchmark rows now return `Task<OrderReadModel?>` (apples-to-apples
on the new flat read shape). `HandWrittenAdoNet` was updated in this
task to swap its trailing `Order.Materialize(...)` call for an
`OrderReadModel` construction using `MoneyConverter.AmountFromStorage`
for per-line price and `MoneyConverter.FromStorage` for the head total
(mirrors `OrderRepository.GetByIdAsync`).

| Method            | Allocated | Alloc Ratio |
|-------------------|-----------|-------------|
| HandWrittenAdoNet | 1.57 KB   | 1.00        |
| ZeroAlloc_ORM     | 1.71 KB   | 1.09        |

The 1.09× framework-vs-hand-written ratio matches ZA.ORM's documented
1.13× MultiResultSetBench parity; the +0.14 KB delta is the
`AdoCommandBatch.Async` wrapper overhead inherent to ZA.ORM.

## Comparison against pre-#173 numbers

Comparing absolute bytes to the pre-#173 numbers in `content/za-clean/
README.md` (commit `8afe430`, "ZA.ORM ~27 µs / 1.71 KB vs hand-written
ADO.NET ~26 µs / 1.57 KB") is **not apples-to-apples**: the pre-#173
bench returned `Task<Order?>` from both methods (full domain aggregate
reconstruction); the post-#173 bench returns `Task<OrderReadModel?>`
from both. The numbers happen to land at the same absolute total
(1.71 KB / 1.57 KB), but the composition shifted:

- **Pre-#173 ZA.ORM:** `Order` + `List<OrderLine>` + N × `OrderLine`
  + N+1 × `Money` + `Enum.Parse<OrderStatus>` per request.
- **Post-#173 ZA.ORM:** `OrderReadModel` + `OrderLineReadModel[]`
  + N × `OrderLineReadModel`; no `Money`, no `Enum.Parse`, no domain
  invariant rerun.

The aggregate byte count is similar because the record + array shape
the flat read model uses is comparable in size to the
`Order` + `List<>` + lines shape. The structural win shows downstream:
the endpoint no longer needs the second Api-DTO array translation
(handled by `ReadModelToResponse`), which is where the user-visible
saving lands across the full GET pipeline — not exercised by this
micro-bench.

If you need an end-to-end "before vs after" allocation delta covering
the full pipeline, run the NBomber `read-rps` scenario against both
branches.

## Raw BDN report

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3


```
| Method            | Mean     | Error     | StdDev    | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------ |---------:|----------:|----------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWrittenAdoNet | 6.455 us | 0.1502 us | 0.4261 us | 6.345 us |  1.00 |    0.09 | 0.0305 |   1.57 KB |        1.00 |
| ZeroAlloc_ORM     | 6.569 us | 0.1311 us | 0.3696 us | 6.442 us |  1.02 |    0.09 | 0.0305 |   1.71 KB |        1.09 |
