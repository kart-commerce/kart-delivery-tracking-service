namespace KartDeliveryTrackingService.Application.Features.IngestCarrierWebhook;

/// <summary>
/// api-contract.yaml deliberately leaves a carrier's native webhook body unconstrained
/// (<c>additionalProperties: true</c> - shape is carrier-dependent and out of the contract's
/// scope). No real per-carrier integration exists yet to justify one bespoke parser per carrier,
/// so this is the one generic shape every configured carrier's payload is expected to already
/// match (trackingId/statusCode/eventTimestamp, case-insensitive, "status" accepted as a
/// statusCode alias) - a deliberate simplification, not a platform requirement. A carrier whose
/// real payload differs would get its own <c>ICarrierWebhookPayloadAdapter</c> implementation
/// registered by carrier id; none exists today because no real carrier does either.
/// </summary>
public sealed class GenericCarrierWebhookBody
{
    public string? TrackingId { get; set; }

    public string? StatusCode { get; set; }

    public string? Status { get; set; }

    public DateTimeOffset? EventTimestamp { get; set; }
}
