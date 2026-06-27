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
    private static string _telemetryPepper = string.Empty;

    /// <summary>
    /// Gets or sets the pepper used to cryptographically salt caller ID hashes in telemetry.
    /// Must be configured in appsettings.json/environment.
    /// Changing this value will alter generated hashes, which will make historical metrics correlation problematic.
    /// </summary>
    public static string TelemetryPepper
    {
        get => _telemetryPepper;
        set => _telemetryPepper = value ?? string.Empty;
    }

    /// <summary>
    /// Anonymizes a caller identifier (email) by hashing it with HMAC-SHA-256 and a secret pepper to protect PII in telemetry.
    /// </summary>
    /// <param name="callerId">The raw caller identifier email.</param>
    /// <returns>The hexadecimal representation of the keyed hash.</returns>
    public static string AnonymizeCallerId(string callerId)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return string.Empty;
        var normalized = callerId.Trim().ToLowerInvariant();
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(_telemetryPepper);
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(normalized);
        using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(valueBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Sets standardized telemetry tags on the current activity for effective group and budget source tracking (§10.2).
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    /// <param name="callerId">The caller identifier (will be anonymized to protect PII).</param>
    /// <param name="effectiveGroup">The effective group ID or "None".</param>
    /// <param name="budgetSource">The budget source (e.g. Group, UserOverride, None).</param>
    public static void SetTelemetryTags(this Activity? activity, string callerId, string effectiveGroup, string budgetSource)
    {
        if (activity is not null)
        {
            activity.SetTag("effective_group", effectiveGroup);
            activity.SetTag("budget_source", budgetSource);
            activity.SetTag("caller_id", AnonymizeCallerId(callerId));
        }
    }
    /// <summary>
    /// Configures Serilog for logging, console sink, and Otlp logs exporter if configured.
    /// </summary>
    /// <param name="hostBuilder">The host builder to configure.</param>
    /// <param name="serviceName">The name of the service.</param>
    /// <returns>The configured host builder.</returns>
    public static IHostBuilder UseObservability(this IHostBuilder hostBuilder, string serviceName)
    {
        return hostBuilder.UseSerilog((context, services, loggerConfig) =>
        {
            var cfg = context.Configuration.GetSection("Observability");
            var minLevel = cfg.GetValue("MinimumLevel", LogEventLevel.Information);
            var serviceVersion = cfg.GetValue("ServiceVersion", "1.0.0")!;
            var environment = context.HostingEnvironment.EnvironmentName;

            var pepper = cfg.GetValue<string>("TelemetryPepper");
            if (string.IsNullOrWhiteSpace(pepper))
            {
                throw new InvalidOperationException("TelemetryPepper configuration is missing! For cryptographic security, telemetry caller IDs must be hashed using HMAC-SHA-256 and a secret pepper. Configure a unique 'TelemetryPepper' under the 'Observability' section in appsettings.json. Note: changing the pepper in the future will alter generated hashes, which will make historical metrics correlation problematic.");
            }
            TelemetryPepper = pepper;

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

    /// <summary>
    /// Configures OpenTelemetry tracing and metrics with appropriate instrumentation and Otlp exporter.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="serviceName">The name of the service.</param>
    /// <returns>The configured builder.</returns>
    public static IHostApplicationBuilder ConfigureObservability(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        var cfg = builder.Configuration.GetSection("Observability");
        var serviceVersion = cfg.GetValue("ServiceVersion", "1.0.0")!;
        var environment = builder.Environment.EnvironmentName;
        var otlpEndpoint = cfg.GetValue<string>("Otlp:Endpoint");

        var pepper = cfg.GetValue<string>("TelemetryPepper");
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException("TelemetryPepper configuration is missing! For cryptographic security, telemetry caller IDs must be hashed using HMAC-SHA-256 and a secret pepper. Configure a unique 'TelemetryPepper' under the 'Observability' section in appsettings.json. Note: changing the pepper in the future will alter generated hashes, which will make historical metrics correlation problematic.");
        }
        TelemetryPepper = pepper;

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
