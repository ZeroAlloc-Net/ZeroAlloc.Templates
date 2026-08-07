# JwtValidationBench — is a validated-token cache worth it? (#171)

Issue #171 asks whether JWT bearer validation is a large enough share of
per-request CPU to justify caching validated tokens, and says explicitly
that a measurement should drive the call rather than an intuition.

**Answer: no. JWT validation is ~4% of per-request CPU. The cache is not
worth its security surface in a template.**

## What was measured

`JwtValidationBench` in `content/za-clean/benchmarks/MyApp.Benchmarks`,
against `JsonWebTokenHandler` with the exact `TokenValidationParameters`
from `src/MyApp.Api/Program.cs`.

`JsonWebTokenHandler` is the handler under test because it is the one the
template actually runs: `AddJwtBearer` does not override the handler, and
it is the framework default on .NET 8 and later. Benchmarking the legacy
`JwtSecurityTokenHandler` would have measured a path no request takes.

### JWT cost in isolation

| Method                   | Mean      | Ratio | Allocated |
|--------------------------|-----------|-------|-----------|
| ValidatePerRequest       | 11,826 ns | 1.00  | 4,400 B   |
| ValidateOnly_NoPrincipal |  8,273 ns | 0.71  | 2,696 B   |
| CacheHit_HashKey         |  1,051 ns | 0.09  |   336 B   |
| CacheHit_TokenKey        |     70 ns | 0.006 |     0 B   |

### The denominator

The share only means something against a whole request.
`ReadPipelineBench` is that measurement — `GET /orders/{id}` end-to-end
through ASP.NET middleware, JWT auth, mediator dispatch, the ZA.ORM
repository read, and JSON serialisation. It already contains the JWT
validation being weighed. Both templates re-measured on this machine, in
the same session as the JWT numbers above:

| Template          | Full request | Allocated | JWT share (CPU) | JWT share (alloc) |
|-------------------|--------------|-----------|-----------------|-------------------|
| za-clean          | 278.3 µs     | 24.69 KB  | **4.3%**        | 17.4%             |
| za-vertical-slice | 293.6 µs     | 25.39 KB  | **4.0%**        | 16.9%             |

## Verdict

**Not material on CPU.** At 4% of a request, a cache that eliminated JWT
validation entirely would return about 10.8 µs of 278 µs — under 4% of
request time. That does not buy a cache which skips signature
verification.

**Allocations are the more interesting number, and still do not justify
it.** 4.4 KB of a ~25 KB request is ~17%. Worth knowing for a template
that advertises a low-allocation hot path, but the request allocates
25 KB regardless; JWT is not what drives it.

**No cheaper targeted fix exists either.** Splitting the cost shows
8.3 µs in signature verification and parsing, ~3.5 µs in
`ClaimsPrincipal` construction. Neither dominates, and the cryptographic
work cannot be made cheaper without skipping it. There is nothing to
tune here — which is fine, because at 4% there is nothing worth tuning.

## Both templates, one measurement

`za-clean` and `za-vertical-slice` have byte-identical
`TokenValidationParameters` and both use the framework-default handler,
so JWT validation costs the same 11.8 µs in each. Their per-request
denominators differ by less than the run-to-run noise. A second copy of
this benchmark in `za-vertical-slice` would re-measure the same number
against the same denominator, so the finding is recorded once and
applies to both. `za-cqrs-es` has no JWT bearer auth at all.

## Secondary finding: hashing dominates the hit path

Recorded because it would matter if this decision is ever revisited. The
hash-keyed cache design in the issue serves a hit in 1.05 µs; keyed by
the token directly a hit is 70 ns and allocation-free. SHA-256 hashing is
therefore ~93% of the cache-hit cost, 15× the dictionary lookup. Anyone
reopening this should know the hash is a deliberate security trade — not
holding raw bearer tokens as dictionary keys — bought at a real multiple,
rather than a free choice.

## What would change the answer

The 4% share is what makes this a no. Two things could move it:

- **Output caching on reads.** #171 notes this matters most combined
  with output caching. That is directionally right — remove the database
  and serialisation work and JWT's share rises. But the ASP.NET
  middleware and Kestrel costs remain, so JWT does not become a
  per-request *floor*; it becomes a larger slice of a smaller request.
  Re-measure before acting on it.
- **Asymmetric key signing.** These numbers are HMAC-SHA256 with a
  symmetric key. RSA or ECDSA signature verification is substantially
  more expensive, and an app validating RS256 tokens could land somewhere
  the trade looks different.

## Reproducing

```bash
cd content/za-clean
dotnet run --project benchmarks/MyApp.Benchmarks -c Release -- --filter "*JwtValidationBench*"
dotnet run --project benchmarks/MyApp.Benchmarks -c Release -- --filter "*ReadPipelineBench*"
```

12th Gen Intel Core i9-12900HK, .NET 10.0.10, X64 RyuJIT.
