using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Builds the OpenTelemetry TracerProvider and MeterProvider this lesson uses to view Agent
    /// Framework's built-in gen_ai.* spans and metrics directly on the console, so the lesson runs
    /// end to end with no external backend (Aspire Dashboard, Azure Monitor, etc.) required. A
    /// production system would swap the console exporters here for an OTLP or Azure Monitor
    /// exporter without touching any other file in this project.
    /// </summary>
    internal static class TelemetryFactory
    {
        /// <summary>
        /// Creates a TracerProvider subscribed to the given ActivitySource name, printing every
        /// completed span to the console.
        /// </summary>
        internal static TracerProvider BuildTracerProvider(string sourceName, string serviceName)
        {
            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                .AddSource(sourceName)
                .AddConsoleExporter()
                .Build();
        }

        /// <summary>
        /// Creates a MeterProvider subscribed to the given Meter name, printing every collected
        /// metric to the console. Note this calls AddMeter, not AddSource - traces and metrics are
        /// subscribed to by different builder methods in the OpenTelemetry .NET SDK even though
        /// Agent Framework emits both under the same sourceName string.
        /// </summary>
        internal static MeterProvider BuildMeterProvider(string sourceName, string serviceName)
        {
            return Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                .AddMeter(sourceName)
                .AddConsoleExporter()
                .Build();
        }
    }
}
