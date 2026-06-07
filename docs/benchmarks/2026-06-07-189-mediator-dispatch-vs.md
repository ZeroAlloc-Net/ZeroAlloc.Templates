```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3


```
| Method   | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------- |---------:|----------:|----------:|-------:|----------:|
| Dispatch | 5.996 μs | 0.2939 μs | 0.8046 μs | 0.0305 |   1.88 KB |
