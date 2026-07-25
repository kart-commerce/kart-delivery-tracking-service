# contracts/

Vendored, read-only copies of this service's approved design artifacts from
`kart-platform/docs/services/kart-delivery-tracking-service/`:

- `api-contract.yaml` — the public + internal REST contract (unchanged copy of the approved
  upstream artifact).
- `message-bus-manifest.json` — the **single source of truth** for this service's entire RabbitMQ
  topology. `Infrastructure/Messaging/RabbitMqTopologyProvisioner` declares every exchange, queue,
  binding, dead-letter queue, and retry-tier queue directly from this file at startup — nothing
  messaging-related is a hardcoded string literal in C#. This mirrors `kart-inventory-service`,
  `kart-category-service`, and `kart-identity-service` exactly.

**Implementation addendum** (this repo's own, not upstream): the upstream draft manifest in
`kart-platform` only sketched this service's consumer binding for `ShipmentDispatched`; it does
not lay out `kart-delivery-tracking-service`'s own publishing topology (`event-contract.md`
resolves those events, but the platform docs never produced a code-ready manifest for them). This
copy completes that topology — `tracking.exchange`'s three published events
(`DeliveryStatusUpdated`, `CarrierStatusIngested`, `UnmappedCarrierStatusFlagged`) and this
service's own internal consumer queue for `CarrierStatusIngested` — following the same shape
`kart-inventory-service/contracts/message-bus-manifest.json` already established as this org's
code-level convention.

Update either file only by re-copying the upstream artifact (or, for the manifest addendum, by
updating the upstream draft itself and re-copying) — never by hand-editing the copy here out of
sync with its source of truth.
