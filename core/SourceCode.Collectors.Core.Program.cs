using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.Services
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Host host = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    IConfiguration configuration = Registrar.BuildConfig();
                    TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration
                    {
                        ConnectionString = configuration.GetConnectionString("AppInsightsConnectionString")
                    };

                    string sessionId = Guid.NewGuid().ToString();
                    string gitRevision = TelemetryClientHelpers.GetCommitSha();
                    ITelemetryClient telemetryClient = new AggregateTelemetryClient(
                        new ApplicationInsightsTelemetryClient(new TelemetryClient(telemetryConfiguration), sessionId, gitRevision),
                        new ConsoleTelemetryClient(sessionId, gitRevision)
                    );

                    Registrar.RegisterService(services, configuration, telemetryClient);
                    services.AddSingleton(telemetryClient);
                    services.ConfigureHostOptions(options =>
                    {
                        string shutdownTimeout = Environment.GetEnvironmentVariable("SHUTDOWN_TIMEOUT");
                        if (shutdownTimeout != null)
                        {
                            options.ShutdownTimeout = TimeSpan.FromSeconds(int.Parse(shutdownTimeout));
                        }
                    });
                })
                .UseConsoleLifetime()
                .Build();

            host.Run();
        }
    }
}
