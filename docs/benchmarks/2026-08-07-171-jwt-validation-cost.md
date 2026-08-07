# JwtValidationBench — is a validated-token cache worth it? (#171)

Issue #171 asks whether JWT bearer validation is a large enough share of
per-request CPU to justify caching validated tokens, and says explicitly
that a measurement should drive the call rather than an intuition. This
is that measurement.

## What was measured

`JwtValidationBench` in `content/za-clean/benchmarks/MyApp.Benchmarks`,
against `JsonWebTokenHandler` with the exact `TokenValidationParameters`
from `src/MyApp.Api/Program.cs`.

`JsonWebTokenHandler` is the handler under test because it is the one the
template actually runs: `AddJwtBearer` does not override the handler, and
it is the framework default on .NET 8 and later. Benchmarking the legacy
`JwtSecurityTokenHandler` would have measured a path no request takes.

## Result

| Method                   | Mean      | Ratio | Allocated |
|--------------------------|-----------|-------|-----------|
| ValidatePerRequest       | 11,826 ns | 1.00  | 4,400 B   |
| ValidateOnly_NoPrincipal |  8,273 ns | 0.71  | 2,696 B   |
| CacheHit_HashKey         |  1,051 ns | 0.09  |   336 B   |
| CacheHit_TokenKey        |     70 ns | 0.006 |     0 B   |

12th Gen Intel Core i9-12900HK, .NET 10.0.10, X64 RyuJIT.

## Reading it

**JWT validation costs ~11.8 µs and 4.4 KB per authenticated request.**
Against the ~26 µs read hot path from `ReadHotPathBench`, that puts JWT
at roughly **31% of per-request CPU** on `GET /orders/{id}` — and about
64% of the allocations, which is the more striking number for a template
that advertises a low-allocation hot path.

**There is no cheaper targeted fix.** Splitting the cost shows 8.3 µs in
signature verification and parsing, and ~3.5 µs in `ClaimsPrincipal`
construction. Neither dominates enough to fix on its own: eliminating
claims materialization entirely would recover under a third of the cost,
and the cryptographic work cannot be made cheaper without skipping it.
Caching is the only lever that moves the 8.3 µs.

**A cache would recover almost all of it.** A hit costs 1.05 µs with the
hash-keyed design the issue proposes — a 91% saving, and 336 B against
4,400 B.

**Hashing dominates the hit path.** Keyed by the token directly, a hit is
70 ns and allocation-free; the SHA-256 hash is therefore ~93% of the
cache-hit cost. That does not change the verdict — 1.05 µs against
11.8 µs is still overwhelming — but it means the hash is a deliberate
security trade (not holding raw bearer tokens as dictionary keys) bought
at 15× the lookup cost, not a free choice.

## The case is stronger than these numbers alone

Issue #171 notes this matters most **combined with** output caching on
the read path. That is what the split implies: once reads are served
from an output cache, the ~26 µs disappears on a hit and JWT validation
becomes the dominant remaining per-request cost, not a third of it.

## Verdict on the first acceptance criterion

Material. ~31% of per-request CPU and ~64% of allocations today, and the
per-request floor once reads are cached.

## What this measurement does not settle

Whether a template *should* ship a validated-token cache is a separate
question from whether it would be faster. A cache that skips signature
verification has to cache only successfully validated tokens, bound
entries by the token's own `exp`, never serve a principal for an expired
token, and stay bounded in size so that a flood of distinct tokens cannot
grow it without limit. A template is copied by people who may not read
the invalidation rules, and getting them wrong fails open.

That trade — real throughput against security surface in code meant as a
starting point — is a judgement call for the maintainer, which is why the
issue offers "or document why not" as an acceptable outcome. This
document is the measurement half; the decision is recorded separately.

## Reproducing

```bash
cd content/za-clean
dotnet run --project benchmarks/MyApp.Benchmarks -c Release -- --filter "*JwtValidationBench*"
```
