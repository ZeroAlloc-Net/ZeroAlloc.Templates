```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3


```
| Method                    | Mean      | Error    | StdDev    | Median    | Gen0   | Allocated |
|-------------------------- |----------:|---------:|----------:|----------:|-------:|----------:|
| HasScope_SingleValue      | 148.25 ns | 5.866 ns | 17.019 ns | 143.04 ns | 0.0036 |     176 B |
| HasScope_MultiValueClaims | 197.09 ns | 8.005 ns | 23.225 ns | 193.68 ns | 0.0052 |     248 B |
| HasScope_Missing          |  35.82 ns | 2.851 ns |  8.225 ns |  33.97 ns | 0.0008 |      40 B |

## Follow-up (#474, 2026-10-11)

The remaining enumerator cost is gone. `ClaimsPrincipalSecurityContext` now indexes
`ClaimsPrincipal.Identities` and each `ClaimsIdentity.Claims` list directly instead of calling
`FindFirst` and `Claims`, whose boxed enumerators and iterator objects were the 176 B / 40 B
above. Re-measured on the same i9-12900HK, .NET 10.0.12, `--inProcess`:

| Method                    | Before    | After     |
|-------------------------- |----------:|----------:|
| HasScope_SingleValue      |     176 B |       0 B |
| HasScope_MultiValueClaims |     248 B |      72 B |
| HasScope_Missing          |      40 B |       0 B |

The 72 B multi-value row is the joined `"orders.read orders.write"` string, which RFC 6749
§3.3 semantics require. `HttpSecurityContextAccessor` also caches the context per request,
so the 24 B context object is paid once per request instead of once per mediator dispatch.
