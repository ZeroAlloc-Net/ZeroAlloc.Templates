```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3


```
| Method       | Backend | Mean     | Error    | StdDev   | Allocated |
|------------- |-------- |---------:|---------:|---------:|----------:|
| ReadPipeline | Sqlite  | 323.5 μs | 30.34 μs | 89.47 μs |  27.11 KB |
