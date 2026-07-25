namespace KartDeliveryTrackingService.Application.Common;

/// <summary>
/// BRD S24.3 audit-actor obligation: every write on this service's path originates from a
/// well-known internal principal, never a human/API caller (requirement-spec.md S24.1.2/S24.3;
/// ddd-model.md's per-aggregate "Audit-actor invariant" sections). Unlike a service with an
/// end-user-facing write surface, there is no <c>ICurrentPrincipal</c>/HTTP-claim-derived
/// abstraction here to resolve - each write site supplies one of these constants directly.
/// </summary>
public static class SystemPrincipals
{
    public const string ShipmentDispatchedConsumer = "system:shipment-dispatched-consumer";

    public static string CarrierWebhook(string carrierId) => $"system:carrier-webhook:{carrierId}";

    public static string CarrierPoll(string carrierId) => $"system:carrier-poll:{carrierId}";

    public const string TerminalStatusSweep = "system:delivery-tracking-terminal-status-sweep";

    public const string UnmappedStatusEscalationSweep = "system:delivery-tracking-unmapped-status-escalation-sweep";
}
