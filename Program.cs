using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.CloudMine.Core.Collectors.Scheduler.Service;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.Services
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            IConfiguration configuration = Registrar.BuildConfig();
            string schedulerRunType = Environment.GetEnvironmentVariable("SchedulerRunType") ?? "Event";

            if (schedulerRunType == "Timer")
            {
                IServiceCollection services = new ServiceCollection();
                RegisterAllServices(services, configuration);
                services.AddSingleton<SchedulerJob>();
                ServiceProvider serviceProvider = services.BuildServiceProvider();
                SchedulerJob scheduler = serviceProvider.GetRequiredService<SchedulerJob>();
                scheduler.RunScheduler().Wait();
            }
            else
            {
                Host host = new HostBuilder()
                    .ConfigureServices((hostContext, services) =>
                    {
                        RegisterAllServices(services, configuration);
                    })
                    .UseConsoleLifetime()
                    .Build();

                host.Run();
            }
        }

        private static void RegisterAllServices(IServiceCollection services, IConfiguration configuration)
        {
            string connectionString = configuration.GetConnectionStringOrSetting("AppInsightsConnectionString");
            TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration
            {
                ConnectionString = connectionString
            };

            string sessionId = Guid.NewGuid().ToString();
            string gitRevision = TelemetryClientHelpers.GetCommitSha();

            ITelemetryClient telemetryClient = new AggregatedTelemetryClient(
                new ApplicationInsightsTelemetryClient(telemetryConfiguration, sessionId, gitRevision),
                new ConsoleTelemetryClient(sessionId, gitRevision)
            );

            Registrar.RegisterService(services, configuration, telemetryClient);
            services.AddSingleton(telemetryClient);
        }
    }
}
