# Changelog

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
