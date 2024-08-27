using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Microsoft.CloudMine.SourceCode.Collectors.Concierge.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            IConfiguration configuration = Registrar.BuildConfig();
            IServiceCollection services = new ServiceCollection();
            RegisterAllServices(services, configuration);
            services.AddSingleton<ConciergeService>();
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ConciergeService conciergeService = serviceProvider.GetRequiredService<ConciergeService>();
            conciergeService.Run().Wait();
        }

        private static void RegisterAllServices(IServiceCollection services, IConfiguration configuration)
        {
            string connectionString = configuration.GetConnectionString("AppInsightsConnectionString");
            TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration
            {
                ConnectionString = connectionString
            };

            string sessionId = Guid.NewGuid().ToString();
            string gitRevision = TelemetryClientHelpers.GetCommitSha();
            ITelemetryClient telemetryClient = new AggregateTelemetryClient(
                new ApplicationInsightsTelemetryClient(new TelemetryClient(telemetryConfiguration), sessionId, gitRevision),
                new ConsoleTelemetryClient(sessionId, gitRevision)
            );

            Registrar.RegisterService(services, configuration, telemetryClient);
            services.AddSingleton(telemetryClient);
        }
    }
}
