# kart-delivery-tracking-service

Real-time delivery tracking and ETA (`kart-requirements.md` §2.1 item 17) — a read-side service
fed entirely by two external triggers, `ShipmentDispatched` (aggregate creation) and per-carrier
webhooks/scheduled polling (state transitions). No PostgreSQL write side exists here at all: the
three MongoDB collections below are this service's sole durable source of truth (`architecture.md`
Boundary Rationale — a deliberate, approved exception to the platform's usual CQRS default).

Full design docs: `kart-platform/docs/services/kart-delivery-tracking-service/*.md`. This repo is
that design's implementation, covering all seven tickets (`tickets.md`, TRK-1..TRK-7).

## Architecture

.NET 8 / ASP.NET Core, Clean Architecture + Vertical Slice (CQRS via MediatR) — the same shape as
every other Kart service ([folder-structure.md](https://github.com/kakon-mehedi/agent-reusables)):

```
src/Api             Controllers, error handling, observability wiring
src/Application      MediatR commands/queries+handlers, one folder per ticket (TRK-1..TRK-7)
src/Domain           TrackingRecord/TrackingStatusHistory/WebhookDedupEntry, zero framework deps
src/Infrastructure   MongoDB.Driver persistence, RabbitMQ (manifest-driven), carrier adapters
```

`Domain` has no dependencies beyond `Kart.Shared.Domain`; `Application → Domain`;
`Infrastructure → Application, Domain`; `Api → Application, Infrastructure`.

Shared platform packages (`Kart.Shared.*`, vendored under `packages/` — see below) wire
observability, global exception handling, and auditing the same one-line way every Kart service
does (`kart-conventions.md`).

## Why no PostgreSQL

Per `architecture.md`/`ddd-model.md`/`database-design.md`: this service's busiest integration
surface is external (per-carrier webhooks/APIs), not a Kart peer, and it has no client-initiated
write command in the usual CQRS sense — `GET /v1/tracking/{trackingId}` is a pure read over a
MongoDB-materialized projection. Reliable outbound event publishing (`DeliveryStatusUpdated`,
`CarrierStatusIngested`, `UnmappedCarrierStatusFlagged`) is done via a Mongo-backed transactional
outbox (`tracking_outbox_events` + `Infrastructure/Messaging/OutboxRelayHostedService`) — the same
role an EF Core outbox plays in this org's other services, adapted to a datastore that has no
`DbContext`/`SaveChanges` interceptor to hook into.

## Message bus

`contracts/message-bus-manifest.json` is the single source of truth for this service's entire
RabbitMQ topology — exchanges, queues, bindings, dead-letter queues, retry ladders. Nothing
messaging-related is a hardcoded string literal in `Infrastructure/Messaging`; the manifest is
loaded once at startup and the topology is declared idempotently from it (same pattern as
`kart-identity-service`/`kart-category-service`/`kart-inventory-service`). See
`contracts/README.md` for the implementation addendum completing this service's own publishing
topology beyond the platform docs' consumer-only draft.

## Local development prerequisites

- .NET 8 SDK
- MongoDB 7
- RabbitMQ 3.x (management plugin optional)
- Docker (for the Testcontainers-backed integration tests)

### Connection configuration (env vars, double-underscore ASP.NET Core convention)

```
Mongo__ConnectionString=mongodb://localhost:27017
Mongo__Database=kart_delivery_tracking
RabbitMq__HostName=localhost
```

### Vendored shared packages

`Kart.Shared.*` has no published NuGet feed yet — `packages/*.nupkg` is a vendored local feed
(`nuget.config`), built via `dotnet pack` from the sibling `kart-shared` repo. To refresh:

```
dotnet pack ../kart-shared/Kart.Shared.sln --configuration Release -o packages
```

### Run the API

```
dotnet run --project src/Api
```

REST on `/v1/tracking/*` (public) and `/internal/v1/webhooks/carriers/*` (internal-only),
Prometheus scrape target at `/metrics`.

### Configuring carriers

`Carriers` in `appsettings.json` is this service's out-of-band, static reference data
(`ddd-model.md` Modeling Decision 5) — one entry per integrated carrier (shared webhook secret,
raw-status-code → canonical-status map, SLA fallback days, polling endpoint template). `demo-carrier`
ships as an illustrative example only; add a real carrier by adding configuration, not code.

## Tests

```
dotnet test
```

- `tests/UnitTests` — Domain invariants (non-regression guard, ordinal assignment) + every
  handler (TRK-1..TRK-7), including the dedup/ordinal-guard/unmapped-status branches
  `edge-cases.md` specifies.
- `tests/IntegrationTests` — Testcontainers-backed MongoDB and RabbitMQ; includes a genuine
  concurrency test proving the ordinal-guarded `findOneAndUpdate` actually serializes concurrent
  status updates for the same `trackingId`, a real-partial-index regression test (MongoDB rejects
  `$nin`/`$ne` in partial-index filter expressions), and a topology test that declares this
  service's actual committed `message-bus-manifest.json` against a live broker.
- `tests/ContractTests` — `WebApplicationFactory`-driven HTTP wire-shape assertions against
  `contracts/api-contract.yaml`, plus a YAML-level check that the contract and the domain's
  `CanonicalDeliveryStatus` enum haven't silently drifted apart.
