using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks,
// and OpenTelemetry. Services built on this default share one telemetry and
// endpoint configuration sourced from the Aspire host environment.
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static void AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default: retries, timeouts, and rate limits.
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default: HttpClient instances created
            // by IHttpClientFactory resolve `https+http://serviceName` URIs.
            http.AddServiceDiscovery();
        });
    }

    public static void ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing => tracing.RecordException = true)
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
    }

    private static void AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
        );

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    public static void AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddHealthChecks()
            // Adds a default liveness check to ensure the app is responsive.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development
        // environments has security implications. See
        // https://aka.ms/dotnet/aspire/healthchecks for details.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to
            // accept traffic after the `/health` endpoint is mapped.
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app
            // to be considered alive.
            app.MapHealthChecks(
                AlivenessEndpointPath,
                new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") }
            );
        }

        return app;
    }
}
