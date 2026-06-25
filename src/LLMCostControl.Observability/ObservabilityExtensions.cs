using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace LLMCostControl.Observability;

/// <summary>
/// Provides extension methods to configure and enrich observability structures.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Sets standardized telemetry tags on the current activity for effective group and budget source tracking (§10.2).
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    /// <param name="callerId">The caller identifier.</param>
    /// <param name="effectiveGroup">The effective group ID or "None".</param>
    /// <param name="budgetSource">The budget source (e.g. Group, UserOverride, None).</param>
    public static void SetTelemetryTags(this Activity? activity, string callerId, string effectiveGroup, string budgetSource)
    {
        if (activity is not null)
        {
            activity.SetTag("effective_group", effectiveGroup);
            activity.SetTag("budget_source", budgetSource);
            activity.SetTag("caller_id", callerId);
        }
    }
    public static IHostBuilder UseObservability(this IHostBuilder hostBuilder, string serviceName)
    {
        return hostBuilder.UseSerilog((context, services, loggerConfig) =>
        {
            var cfg = context.Configuration.GetSection("Observability");
            var minLevel = cfg.GetValue("MinimumLevel", LogEventLevel.Information);
            var serviceVersion = cfg.GetValue("ServiceVersion", "1.0.0")!;
            var environment = context.HostingEnvironment.EnvironmentName;

            loggerConfig
                .MinimumLevel.Is(minLevel)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service.name", serviceName)
                .Enrich.WithProperty("service.version", serviceVersion)
                .Enrich.WithProperty("deployment.environment", environment);

            loggerConfig.WriteTo.Console(
                theme: Serilog.Sinks.SystemConsole.Themes.SystemConsoleTheme.Literate,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

            var seqUrl = cfg.GetValue<string>("Seq:Url");
            if (!string.IsNullOrWhiteSpace(seqUrl))
            {
                loggerConfig.WriteTo.Seq(seqUrl);
            }

            var otlpLogsUrl = cfg.GetValue<string>("Otlp:LogsUrl");
            if (!string.IsNullOrWhiteSpace(otlpLogsUrl))
            {
                loggerConfig.WriteTo.OpenTelemetry(o =>
                {
                    o.Endpoint = otlpLogsUrl;
                    o.Protocol = OtlpProtocol.Grpc;
                    o.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                        ["service.version"] = serviceVersion,
                        ["deployment.environment"] = environment,
                    };
                });
            }
        });
    }

    public static IHostApplicationBuilder ConfigureObservability(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        var cfg = builder.Configuration.GetSection("Observability");
        var serviceVersion = cfg.GetValue("ServiceVersion", "1.0.0")!;
        var environment = builder.Environment.EnvironmentName;
        var otlpEndpoint = cfg.GetValue<string>("Otlp:Endpoint");

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", environment),
                }))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddSource(serviceName);
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation()
                 .AddMeter(serviceName);
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }
}
