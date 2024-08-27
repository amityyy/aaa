using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.Extensions.Hosting;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.Services
{
    /// <summary>
    /// This is the base class for a service. Every service can extend this to implement a run method responsible for execution.
    /// </summary>
    public abstract class ServiceBase : BackgroundService
    {
        protected readonly ITelemetryClient TelemetryClient;

        public ServiceBase(ITelemetryClient telemetryClient)
        {
            TelemetryClient = telemetryClient;
        }

        /// <summary>
        /// The entrypoint for all services.
        /// </summary>
        public abstract Task Run();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Dictionary<string, string> properties = new Dictionary<string, string>
            {
                { "SessionId", TelemetryClient.SessionId },
                { "GitRevision", TelemetryClient.GitRevision },
                { "ServiceType", GetType().Name }
            };

            TelemetryClient.TrackEvent("ServiceStart", properties);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Run().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TelemetryClient.TrackException(ex, properties);
                throw;
            }
            finally
            {
                TelemetryClient.TrackEvent("ServiceStop", properties);
                TelemetryClient.Flush();
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
