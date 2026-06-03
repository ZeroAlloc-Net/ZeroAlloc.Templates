# `za-vertical-slice` Money VO + Converter — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.Templates `content/za-vertical-slice/`, structural
**Closes:** #163
**Branch:** `chore/vs-money-converter` off `main` at `11dea8d`

## Background

The EF Core → ZA.ORM swap (PR #152) left `za-vertical-slice` binding raw `decimal Total` directly to ADO.NET parameters. The two provider migrations took different column types:

| Provider | vs `Total` column | za-clean `Total` column |
|---|---|---|
| Sqlite | `TEXT` | `TEXT` |
| Postgres | `numeric` | `TEXT` |

vs's single `decimal` code path therefore binds against two storage contracts — `TEXT` on Sqlite (driver does implicit `decimal.ToString()`), `numeric` on Postgres (Npgsql native bind). za-clean by contrast routes every money value through `MoneyConverter.ToStorage/FromStorage` producing `"amount|currency"` strings, then stores `TEXT` on both providers.

vs has no Postgres integration coverage today, so the divergent Postgres path is unverified.

## Decision

Adopt za-clean's `Money` value object + `MoneyConverter` in vs. The two templates become structurally equivalent on money handling. vs already adopted typed IDs (`CustomerId`, `OrderId`) at its API surface; `Money` is the same idiom applied to the next obvious primitive.

## What changes

**Files added (2):**

- `content/za-vertical-slice/src/MyApp/Common/Money.cs` — `[ValueObject]` struct with `Amount` (decimal) + `Currency` (string), `TryCreate` factory rejecting negative amount + empty currency. Ported from za-clean's `Domain/ValueObjects/Money.cs`, namespace adjusted to `MyApp.Common` (matching vs's existing `CustomerId` / `OrderId` location).
- `content/za-vertical-slice/src/MyApp/Persistence/MoneyConverter.cs` — `ToStorage(Money) → string` (`"<amount>|<currency>"`) and `FromStorage(string) → Money` (tolerates empty/malformed by returning `Money(0, "USD")`). Ported from za-clean's `Infrastructure/Persistence/MoneyConverter.cs`.

**Files modified (~7):**

- `content/za-vertical-slice/src/MyApp/Persistence/Migrations/Postgres/001_initial_schema.sql` — `Total numeric` → `Total text` (uniform with Sqlite).
- `Features/Orders/PlaceOrder/PlaceOrder.cs`:
  - `PlaceOrderCommand`: `decimal Total` → `Money Total`. The `[GreaterThan(0)]` validator is removed; `Money.TryCreate` already rejects negative amount (matching za-clean's semantics, which permit zero).
  - `Order` entity: `decimal Total` → `Money Total`.
  - `InsertOrderAsync` partial method signature: `decimal total` → `string total` (the storage shape after `MoneyConverter.ToStorage`).
  - Handler body: call `MoneyConverter.ToStorage(cmd.Total)` at bind site.
- `Features/Orders/GetOrder/GetOrder.cs`:
  - `OrderDto`: `decimal Total` → `Money Total`.
  - `OrderRow`: `decimal Total` → `string Total` (the on-the-wire shape ZA.ORM reads).
  - Handler: project `row.Total` through `MoneyConverter.FromStorage` into the DTO.
- `Features/Orders/ListOrders/ListOrders.cs`:
  - `OrderListItem`: `decimal Total` → `Money Total`.
  - `OrderListRow`: `decimal Total` → `string Total`.
  - Handler: convert via `MoneyConverter.FromStorage` when projecting each row.
- `Program.cs`: register `Money` in `JsonContext` (the AOT-friendly JSON resolver) if it doesn't get picked up transitively. The `[ValueObject]`-generated converter is registered automatically by the existing `AddZeroAllocValueObjectConverters()` call.
- Any seed / smoke / integration test source that constructs an order with a literal `99.99m` — updated to `Money.TryCreate(99.99m, "EUR").Value` (matching za-clean's literal style).

**Tests:**

- `content/za-vertical-slice/tests/MyApp.UnitTests/Common/MoneyTests.cs` — port za-clean's existing Money unit tests.
- **`content/za-vertical-slice/tests/MyApp.UnitTests/Persistence/MoneyConverterTests.cs`** — new tests covering `MoneyConverter.ToStorage/FromStorage` round-trip (5 cells: simple decimal, fractional pennies, currency code preservation, empty-string defensive fallback, malformed-string defensive fallback). za-clean has no equivalent tests today; we'll add the same file there in a separate small PR for symmetry.
- Existing vs `IntegrationTests` continue to exercise the Sqlite roundtrip via Money — no test changes needed beyond updating literal `decimal Total` values in test payloads to the new JSON shape `{"Amount":99.99,"Currency":"EUR"}` (or whatever the JSON converter emits — see "Open question" below).

## What's intentionally NOT in this PR

- **Postgres integration tests for vs.** Neither template has Testcontainers-based Postgres coverage today. The Postgres path is "structurally guaranteed" after this PR because the SQL contract is identical to Sqlite (TEXT in both schemas, MoneyConverter is the only path through the storage boundary, and the converter has its own unit tests). Adding a Postgres test fixture is a meaningful infrastructure addition that benefits both templates and should be a separate issue.
- **MoneyConverter unit tests for za-clean.** The new tests in vs are obvious additions to za-clean too, but rolling that into this PR widens scope. Separate small PR.
- **`Money.TryCreate` semantics change.** za-clean's converter currently allows zero; if vs's `[GreaterThan(0)]` invariant turns out to be load-bearing for the marquee place-order flow, that's a separate decision affecting both templates.

## Open question (decide during planning)

The `Money` VO's JSON wire shape is whatever the `[ValueObject]` source-generated converter emits. If za-clean's existing JSON shape is `{"Amount":99.99,"Currency":"EUR"}` (object), then `PlaceOrderCommand`'s payload changes from `{"customerId":42, "total":99.99}` to `{"customerId":42, "total":{"amount":99.99,"currency":"EUR"}}`. Confirm during plan-writing by reading za-clean's existing endpoint tests; the design assumes object-shape and adjusts test payloads accordingly.

## Acceptance criteria (from #163)

- [x] vs money storage strategy matches za-clean — Money VO + MoneyConverter + TEXT on both providers.
- [~] Decimal round-trip verified on both Sqlite and Postgres — Sqlite via existing integration tests; Postgres via structural guarantee (uniform SQL contract) + MoneyConverter unit-test coverage. End-to-end Postgres integration coverage deferred to a separate infrastructure PR.

## Commit shape

Single commit, conventional `feat:`-titled so release-please picks it up. The vs template is at v0.11.0 → this would land as v0.12.0 (minor — new public API surface in template content). Squash title must start with `feat:` (the recurring release-please gotcha — `chore:` squashes get skipped).
