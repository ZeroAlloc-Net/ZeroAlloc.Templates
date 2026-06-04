# Changelog

## [0.13.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.13.2...ZeroAlloc.Templates-v0.13.3) (2026-06-04)


### Performance Improvements

* **authorization:** reduce ClaimsPrincipalSecurityContext per-request allocations across both templates ([#183](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/183)) ([23975d8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/23975d8df3ac4fc48a28583319867ede016bcec9))

## [0.13.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.13.1...ZeroAlloc.Templates-v0.13.2) (2026-06-03)


### Tests

* **za-clean:** MoneyConverter symmetry tests (5 cells from vs) ([#180](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/180)) ([1f6bc03](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/1f6bc034918930473d3b7727fe37d50c93b2f05d))

## [0.13.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.13.0...ZeroAlloc.Templates-v0.13.1) (2026-06-03)


### Code Refactoring

* **za-clean:** immutable Order aggregate — remove persistence leak (closes [#166](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/166)) ([#178](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/178)) ([aa8eb69](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/aa8eb693059d05ec38803e177a6629ca5d102366))

## [0.13.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.12.1...ZeroAlloc.Templates-v0.13.0) (2026-06-03)


### Features

* **za-clean:** read-path BDN allocation benchmark + narrow zero-alloc claim (closes [#164](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/164)) ([#176](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/176)) ([f7f5f89](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f7f5f894be5073ae95848d345738ff0a6cb45f78))

## [0.12.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.12.0...ZeroAlloc.Templates-v0.12.1) (2026-06-03)


### Bug Fixes

* **za-clean:** adopt ZA.ORM v1.5 transaction parameter - close non-atomic Order write (closes [#162](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/162)) ([#174](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/174)) ([cb056fe](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/cb056fe2bf950d447202bd0dd6642c7c958dde14))

## [0.12.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.11.0...ZeroAlloc.Templates-v0.12.0) (2026-06-03)


### Features

* **vs:** adopt Money VO + MoneyConverter to close provider divergence (closes [#163](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/163)) ([#168](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/168)) ([8a7edd9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/8a7edd96085a017823fd7239ca1c2d6f4f5710b6))

## [0.11.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.10.0...ZeroAlloc.Templates-v0.11.0) (2026-06-02)


### Features

* **za-vertical-slice:** enable AOT publish — hand-list handlers + endpoints (closes B5) ([#161](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/161)) ([e5fa262](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/e5fa262035327210fb75726559502a1665d7ac53))


### Documentation

* **backlog:** close B5 — AOT-ify za-vertical-slice shipped ([#161](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/161)) ([14d54e4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/14d54e4a88cc05faa74ddc012b175e0bbf5eb6a5))
* **za-clean:** correct stale 'EF Core + Infrastructure composition' Program.cs comment ([#159](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/159)) ([01130e4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/01130e40bc11c76d1e8b89e6a2352d6f6c3a9dd3))
* **za-clean:** re-measure AOT headline numbers post-swap (.NET 10.0.8) ([ed9277b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/ed9277b1b337cc2640c464b4908ec9f915f293f9))
* **za-vertical-slice:** update AOT headline numbers with measured values post-B5 ([c489829](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/c489829e1eb9e5bc28ea8b234a98e270e82a3b6a))

## [0.10.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.9.3...ZeroAlloc.Templates-v0.10.0) (2026-06-02)


### Features

* **templates:** adopt ZeroAlloc.ORM 1.2.0 + drop two workaround patches ([#155](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/155)) ([3c71f17](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/3c71f17ad03e29b38dc2719ad3755e9c2b82a5af))

## [0.9.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.9.2...ZeroAlloc.Templates-v0.9.3) (2026-06-02)


### Documentation

* **aggregator:** weekly refresh from upstream sources ([#151](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/151)) ([b7e9593](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/b7e959368e1df7d9dfbebf43b06ff162a3b61c85))

## [0.9.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.9.1...ZeroAlloc.Templates-v0.9.2) (2026-05-31)


### Documentation

* **design:** commit ZeroAlloc.ORM v1.0 design + working backlog ([#149](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/149)) ([9ba3ef3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/9ba3ef3790d4c13b2a3033653f60167183239ddf))

## [0.9.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.9.0...ZeroAlloc.Templates-v0.9.1) (2026-05-30)


### Performance Improvements

* **telemetry:** gate console exporter to Development, OTLP + sampler under load ([#145](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/145)) ([4a2cf22](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/4a2cf2210608e0d7ec372d2e2847ccfeee7ae850))


### Documentation

* **backlog:** add B5 — AOT-ify za-vertical-slice ([#146](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/146)) ([7b53b20](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/7b53b209518290d16c3f0718864b52127461a7d5))

## [0.9.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.8.1...ZeroAlloc.Templates-v0.9.0) (2026-05-29)


### Features

* **za-clean:** postgres mirror + cross-template SchemaStrategy sync ([#143](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/143)) ([c7dd442](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/c7dd4422db190c2606e8102b704fa615e90796ff))
* **za-vertical-slice/bench:** postgres bench profile (B2) ([#140](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/140)) ([34c82ad](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/34c82adc492ded299129cfa4643cb257022746e5))


### Documentation

* **za-clean:** fill in B3 Postgres numbers + refresh BDN table ([#144](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/144)) ([d05d238](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/d05d238239d044aff703f2b027c22ab887c2dd36))

## [0.8.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.8.0...ZeroAlloc.Templates-v0.8.1) (2026-05-28)


### Bug Fixes

* **za-vertical-slice/bench:** drop EnsureCreated, let MigrateAsync own schema ([#139](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/139)) ([883346c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/883346ca63e1d11c1b8e8ac23e782e63378a704c))


### Documentation

* refresh BDN numbers from CI + file backlog B1/B2 ([#133](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/133)) ([af51102](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/af511021387d1a501c70779fd63d216219b03f28))

## [0.8.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.7.1...ZeroAlloc.Templates-v0.8.0) (2026-05-28)


### Features

* migrate template request/DTO types to typed IDs (2.3.1 foundation) ([#128](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/128)) ([7569f09](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/7569f098ac863861f900885adb06454eb7848e4e))

## [0.7.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.7.0...ZeroAlloc.Templates-v0.7.1) (2026-05-27)


### Miscellaneous

* trigger 0.7.1 release for readonly record struct migration ([9c366a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/9c366a2e5806dd5fcd94c2a5d8864b451a8881c6))

## [0.7.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.6.1...ZeroAlloc.Templates-v0.7.0) (2026-05-27)


### Features

* za-vertical-slice template (Templates 0.4.0) ([#117](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/117)) ([6ec9867](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/6ec98676b63e12ea5951c2999c276bdb1107332f))

## [0.6.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.6.0...ZeroAlloc.Templates-v0.6.1) (2026-05-26)


### Documentation

* **aggregator:** weekly refresh from upstream sources ([#114](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/114)) ([2ef38ca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/2ef38cac7175069ff9d4f16fd928befe788207ce))

## [0.6.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.5.0...ZeroAlloc.Templates-v0.6.0) (2026-05-18)


### Features

* **ci:** add real-run smoke gate for jit /orders path ([#93](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/93)) ([7d6005e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/7d6005e3e1e72885eead36896936a04ae2b15b14))

## [0.5.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.4.1...ZeroAlloc.Templates-v0.5.0) (2026-05-18)


### Features

* **ci:** add weekly aggregator refresh workflow ([#89](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/89)) ([1900f6c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/1900f6c31a5ed888ef1bb9024d9fa2de9fd5d17d))


### Documentation

* **aggregator:** weekly refresh from upstream sources ([#90](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/90)) ([45a3771](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/45a37714f0a665d9cf890be6286853dcd08f7b0e))
* **template:** refresh validation section — generator nupkg already shipped ([#85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/85)) ([b21c63e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/b21c63e302899b23d6b9248de79dc4a411bcfebb))

## [0.4.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.4.0...ZeroAlloc.Templates-v0.4.1) (2026-05-14)


### Documentation

* **aggregator:** wire Scheduling, Outbox, EventSourcing comparison blocks ([#77](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/77)) ([fcd5cf1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/fcd5cf10a5b0b18cfaf69c15c5f2257bfb6e6597))

## [0.4.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.6...ZeroAlloc.Templates-v0.4.0) (2026-05-13)


### Features

* **template:** drop NotEmpty collection workaround now that ZA.Validation 1.3.0 supports it ([#74](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/74)) ([f523218](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f52321856edda811f5cc6af01f22bdb64954b965))
* **template:** enforce [Authorize] on mediator handlers via ZA.Mediator.Authorization ([#76](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/76)) ([d403e3a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/d403e3a4ffe63fdce8973aec4093bcce997c59f2))
* **template:** use [Validate] attributes instead of hand-rolled validator ([#70](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/70)) ([1aa4ce8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/1aa4ce8c0ddb6766a0a29e15dddfcf0997ae4a77))

## [0.3.6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.5...ZeroAlloc.Templates-v0.3.6) (2026-05-13)


### Documentation

* **comparisons:** add CACHE + TELEMETRY live sources — first tier-2 pair ([#66](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/66)) ([f0cb6c1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f0cb6c1f6eb7cb379a37a0e023ce90061a0d82ba))
* **comparisons:** add NOTIFY live source ([#68](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/68)) ([46e631c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/46e631cb9cefc03ad2ce9cf6551a10f53ebf1a09))

## [0.3.5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.4...ZeroAlloc.Templates-v0.3.5) (2026-05-13)


### Documentation

* **comparisons:** add REST + SERIALISATION live sources ([#60](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/60)) ([904575f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/904575fc8ae96b87320009786d770b521d93fa22))

## [0.3.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.3...ZeroAlloc.Templates-v0.3.4) (2026-05-13)


### Documentation

* **comparisons:** add STATEMACHINE + RESILIENCE live sources ([#57](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/57)) ([f1bf462](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/f1bf4626dcb5c7fbe5067047df269e55aa087b8e))
* **comparisons:** flip VALIDATION live + add SPECIFICATION source ([#53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/53)) ([8b61e1f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/8b61e1ffe0b5e0343a34c90fca5310325d98acc9))

## [0.3.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.2...ZeroAlloc.Templates-v0.3.3) (2026-05-13)


### Documentation

* **comparisons:** wire ZA.Results + ZA.ValueObjects live numbers ([#49](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/49)) ([3013062](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/301306204f4e47487ada3d86b048654f28873f5c))

## [0.3.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.1...ZeroAlloc.Templates-v0.3.2) (2026-05-12)


### Documentation

* **comparisons:** wire ZA.Inject live numbers (vs Jab + MS DI) into INJECT sentinel ([#37](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/37)) ([b32fc34](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/b32fc34f400286bcf2e6d3b449f39d279e2b923f))
* **comparisons:** wire ZA.Mediator live numbers into MEDIATOR sentinel ([#33](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/33)) ([a180a4a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/a180a4a96f644f9d0f658d72d317520a241e4164))

## [0.3.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/compare/ZeroAlloc.Templates-v0.3.0...ZeroAlloc.Templates-v0.3.1) (2026-05-12)


### Bug Fixes

* **template:** ship package icon so nuget.org doesn't show the generic die ([#31](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/31)) ([fa7cdc4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/commit/fa7cdc478865d8130518f0bb911e27c3ca0fc7b9))

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
