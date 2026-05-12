# Changelog

## [0.3.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.2.0...ZeroAlloc.Templates-v0.3.0) (2026-05-12)


### Features

* **api:** AOT-publish-ready (compiled model + JsonContext) ([a00fd7f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/a00fd7fa53ae6b66bdee9bf7281483faf8efe09f))
* **bench:** import-comparisons.ps1 + sentinels for per-package numbers ([5256d50](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/5256d50fa69dfe95f643f6b1600beb3f936d8e74))
* **bench:** primitives-only benchmark harness ([104ea07](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/104ea079f2a5e6309631d13e3c7c3fb6071f9af8))
* **template:** NuGet package readme + Shipping:UseStub flag + NBomber numbers ([88c78ff](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/88c78ff06a389abd60a1e2f50b7691cddced8b1b))
* **template:** NuGet package readme + Shipping:UseStub flag + real NBomber numbers ([b9a5d7b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/b9a5d7b4b71afd8e86b06d25a0dd3dfc8483c7bb))
* v0.3.0 — AOT publish + primitives benchmark + comparison plumbing ([d3efc53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/d3efc5387cb317535c0267d12a5756cc6def8fa4))


### Bug Fixes

* drop redundant ZA.Rest.Generator package reference (unblocks Renovate) ([e6e7ced](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/e6e7ced8a9af98cb902a79c24abceecef7e6f182))
* **infrastructure:** drop redundant ZA.Rest.Generator package reference ([2421e9c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/2421e9ce86f5ffd1e41da397c6f0001c8f7f5408))
* **infrastructure:** GET /orders/{id} via raw SQL + document EF Core AOT limitations ([a8e4e6b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/a8e4e6be963dbc505f85e42fd1a5226a99296c4c))


### Documentation

* **template:** refresh primitives numbers from BDN full run ([3b45e28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/3b45e2825bcc94c56234abf421fd5b0edc217fbf))
* **template:** v0.3.0 AOT-led headline + Primitives subsection ([883c135](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/883c1352c6a1381b2145294cf4982c74a812cb8b))

## [0.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.1.0...ZeroAlloc.Templates-v0.2.0) (2026-05-11)


### Features

* **api:** Orders endpoints + ZA.Mapping at the boundary ([48e5ed8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/48e5ed8a9bde307a13daec5cc0cd7f619a0f237e))
* **api:** Program.cs composition root with full ZA stack ([8e7344c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/8e7344c1a40a24e38d03d1a57fb08188ed9cf363))
* **application:** CreateOrder command + handler + validator ([f1a989f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f1a989fb4d0f6f5f30dd07b04c6654181e8155bd))
* **application:** GetOrderById query + handler ([4128da1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/4128da11dc910e9cef5dd0179debf660ef9d576b))
* **bench:** BDN write-pipeline scenario ([fa8dae7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/fa8dae7dfb87a6e6f5e7326418a97e955c039105))
* **bench:** NBomber read-RPS scenario ([f8b21eb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f8b21eb2847922e770f195c6ff047a57928892ee))
* **domain:** Order entity with line-aggregation ([9c78c0d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/9c78c0da3da603686fd6e14a4b0fc2377ccac735))
* **domain:** value objects (OrderId, CustomerId, Money) ([b21db93](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/b21db93adef35e4ccccbd9e3cfbc94fdd813f57a))
* **infrastructure:** AppDbContext + Order EF mapping + initial migration ([d4c984a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/d4c984a8e69abf8300adde7cab1698dae0238b8c))
* **infrastructure:** IShippingQuoteClient via ZA.Rest + Resilience pipeline ([5aeaa6a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/5aeaa6a3b41d2640fdc6c4c78257e3896309a229))
* **template:** NuGet template package definition ([5dfadd1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/5dfadd11533f55fa7135ced1ed51fc3fd5a6fd68))
* ZA-clean Web API template — scaffolds Clean Architecture with 10 ZA.* packages ([8882644](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/888264402187efb0a16456a5bcf6778e2569d004))


### Bug Fixes

* **api:** tighten auth policies to require scope claims ([07fff56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/07fff56ae96e2e8416af4f909551b9f1c324b69d))
* **application:** hand-roll validator until ZA.Validation generator ships ([189de1f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/189de1ff188cb9c3a844a5f140e9ca48414e9967))
* **infrastructure:** register IRestSerializer + add scaffolded README with real BDN numbers ([b392011](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/b3920110d6970a8a8d297c188894715e9110a0e0))


### Code Refactoring

* **application:** use UnitResult&lt;E&gt; from ZA.Results; drop unused ZA.Validation reference ([f4d27be](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f4d27bef52c03bd2ae295dadbf1186a8cf2bb063))


### Documentation

* AGENTS.md + CLAUDE.md + copilot-instructions for template-repo maintainers ([d678e80](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/d678e80cb916bbbb3809c4f8c52a6e94671218b5))
* **template:** AGENTS.md + CLAUDE.md + copilot-instructions for scaffolded app ([8a31b15](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/8a31b15bf1e0456508c56938a13dc9ac2d75d75e))
* za-clean tour ([2ac8cc5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/2ac8cc53e9806d7ef3b94166f33fff8636c895d8))


### Tests

* **arch:** Clean Architecture boundary rules via NetArchTest ([d305ca3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/d305ca3de528357fbf8e0d026e240c10fd9be2ec))
* **integration:** POST /orders happy-path roundtrip ([3261b0c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/3261b0cf2aeff3e8f8a8750e3e994f3c5ea37e49))
* **smoke:** template scaffold + build + test gate ([aa8971e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/aa8971ed4228796b21f70690e54272fef603435b))

## Changelog

All notable changes will be documented in this file. Format follows release-please conventions.
