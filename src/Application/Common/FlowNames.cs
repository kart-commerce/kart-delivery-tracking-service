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
    /// ingestion, TRK-4 status-transition application, TRK-5 polling fallback). kart-notification-
    /// service's own FlowNames.cs already coined this exact literal as a placeholder for
    /// shipping/tracking events (neither kart-shipping-service nor this service - the actual
    /// producers - were instrumented at the time it was written); reused here verbatim rather than
    /// diverged to a new name, since it's still the best fit for this flow's title.
    /// </summary>
    public const string ShippingWarehouseFulfillment = "ShippingWarehouseFulfillment";

    /// <summary>
    /// business-flows.md flow #14, "Customer Support &amp; Order Tracking". This service only has
    /// real code for the order-tracking/status-lookup step (TRK-2 GetTrackingStatus) - none of the
    /// support-ticket workflow (Open Support/Select Issue Type/Chatbot/Escalate to Agent/Agent
    /// Reviews Order History/Resolution/Ticket Closed/Feedback) exists anywhere in this service, so
    /// only that one step is tagged with this Flow. No other service's FlowNames.cs has coined a
    /// literal for this flow yet, so this one is newly derived directly from the flow's own title,
    /// same concatenation convention as ShippingWarehouseFulfillment/WishlistSavedItems.
    /// </summary>
    public const string CustomerSupportOrderTracking = "CustomerSupportOrderTracking";
}
