namespace KartDeliveryTrackingService.Application.Common;

/// <summary>
/// Business-flow tags for KartFlowContext.Push, per kart-conventions.md's per-flow tracing/logging
/// standard (checkpoint-logging-standard.md).
/// </summary>
public static class FlowNames
{
    /// <summary>
    /// business-flows.md flow #8, "Shipping, Warehouse &amp; Fulfillment" - this service's
    /// tracking-status-update steps (TRK-1 ShipmentDispatched intake, TRK-3 carrier webhook
    /// ingestion, TRK-4 status-transition application, TRK-5 polling fallback).
    /// </summary>
    public const string ShippingWarehouseFulfillment = "ShippingWarehouseFulfillment";

    /// <summary>
    /// business-flows.md flow #14, "Customer Support &amp; Order Tracking". This service only has
    /// real code for the order-tracking/status-lookup step (TRK-2 GetTrackingStatus) - none of the
    /// support-ticket workflow (Open Support/Select Issue Type/Chatbot/Escalate to Agent/Agent
    /// Reviews Order History/Resolution/Ticket Closed/Feedback) exists anywhere in this service, so
    /// only that one step is tagged with this Flow.
    /// </summary>
    public const string CustomerSupportOrderTracking = "CustomerSupportOrderTracking";
}
