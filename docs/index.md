---
id: index
title: ZeroAlloc.Templates
sidebar_label: Introduction
sidebar_position: 0
slug: /
---

# ZeroAlloc.Templates

`dotnet new` Web API templates for the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.
Both templates are source-generated and publish as Native AOT.

```bash
dotnet new install ZeroAlloc.Templates
```

| Short name | Architecture | Tour |
|---|---|---|
| `za-clean` | Clean Architecture: four layered projects with enforced boundaries | [za-clean](za-clean.md) |
| `za-vertical-slice` | Vertical Slice Architecture: one project, one folder per use case | [za-vertical-slice](za-vertical-slice.md) |

The two templates share the same domain, packages and test and benchmark fan-out. They differ in
how the code is arranged, so pick the one your team finds easier to work in.

[Benchmarks](benchmarks/README.md) holds the benchmark reports behind the performance figures, and
the runbooks for producing them.

The [README](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates#readme) lists which ZeroAlloc
package handles each concern in the generated solution.
