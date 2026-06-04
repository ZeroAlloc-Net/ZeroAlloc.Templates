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
