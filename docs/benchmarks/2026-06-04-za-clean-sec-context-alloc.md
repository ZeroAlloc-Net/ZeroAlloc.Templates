# ClaimsPrincipalSecurityContext allocation benchmark (#172)

Branch: `feat/sec-context-zero-alloc`. Measures `OrdersReadPolicy.HasScope` on top of the
rewritten `ClaimsPrincipalSecurityContext` (`content/za-clean/src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs`).

**Step 5 fallback APPLIED.** The initial Task-2 implementation used `principal.FindAll(key)` —
which returns an iterator and boxed the enumerator on `foreach` for every `TryGetValue` call.
Initial measurements showed 216 B / 288 B / 216 B per op. The fallback rewrite probes with
`principal.FindFirst(key)` first (zero-alloc early-out for missing-claim case) and then walks
`principal.Claims` directly only when at least one match exists, avoiding the `FindAll` iterator
allocation in the common path.

**Result:** no per-request *collection* allocation — what remains is the small enumerator
state-machine box from iterating `ClaimsPrincipal.Claims` (itself a BCL iterator). The early-out
"missing scope" path drops to 40 B (single enumerator alloc); the single-value path is 176 B
(enumerator + small Claim enumeration overhead). True 0 B/op would require a non-public
`ClaimsPrincipal._claims` walk; the goal here was "no per-request collection allocation",
which is met.

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3


```
| Method                    | Mean      | Error     | StdDev    | Median    | Gen0   | Allocated |
|-------------------------- |----------:|----------:|----------:|----------:|-------:|----------:|
| HasScope_SingleValue      | 288.60 ns | 22.881 ns |  67.46 ns | 277.69 ns | 0.0033 |     176 B |
| HasScope_MultiValueClaims | 380.56 ns | 34.244 ns | 100.43 ns | 353.80 ns | 0.0052 |     248 B |
| HasScope_Missing          |  46.34 ns |  5.048 ns |  14.88 ns |  44.17 ns | 0.0008 |      40 B |
