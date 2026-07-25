using Kart.Shared.Auditing;
using Kart.Shared.ErrorHandling;
using Kart.Shared.Observability;
using KartDeliveryTrackingService.Application;
using KartDeliveryTrackingService.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// kart-conventions.md Observability section: Serilog + OpenTelemetry SDK behind one DI call,
// never reimplemented per service. Standard (not 100%) trace-sampling tier - this service is not
// an Order Saga participant.
builder.AddKartObservability("kart-delivery-tracking-service");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// kart-conventions.md Error Handling section: the single global exception handler + ProblemDetails
// factory, wired once via the shared package - no local try/catch for translation anywhere in this
// service's handler/controller/domain code.
builder.Services.AddKartErrorHandling();

// No dedicated audit sink exists for this service beyond the createdBy/updatedBy columns already
// stamped inline at each MongoDB write site (ddd-model.md's per-aggregate audit-actor invariants) -
// registers the safe NullAuditLogWriter default so the shared package is wired the same one-line
// way every Kart service wires it, ready to swap in a real sink if a future requirement asks for one.
builder.Services.AddKartAuditing();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Per-HTTP-request Information log (method/path/status/elapsed) - registered outermost, wrapping
// UseKartErrorHandling below, so this always logs the *final* status code a client actually
// received.
app.UseSerilogRequestLogging();

// The single global error handler - every unhandled exception is translated to the platform's
// ProblemDetails envelope and logged here, so no controller/handler needs its own try/catch.
app.UseKartErrorHandling();

app.UseHttpsRedirection();

// Prometheus scrape target (observability-standards.md's mandatory /metrics).
app.MapPrometheusScrapingEndpoint();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in IntegrationTests/ContractTests.
public partial class Program
{
}
