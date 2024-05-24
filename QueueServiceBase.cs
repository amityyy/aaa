using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Collectors.Clients;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;

namespace Microsoft.CloudMine.SourceCode.Collectors.Services
{
    public abstract class QueueServiceBase : ServiceBase
    {
        private readonly string QueueName;
        private readonly IRedisClient RedisClient;
        private readonly string ServiceSessionId;

        public QueueServiceBase(ITelemetryClient telemetryClient, IRedisClient redisClient, string queueName)
            : base(telemetryClient)
        {
            this.QueueName = queueName;
            this.RedisClient = redisClient;
            this.ServiceSessionId = telemetryClient.SessionId;
        }

        public override async Task Run()
        {
            ServiceNotificationMessage message = await this.RedisClient.PopMessageAsync<ServiceNotificationMessage>(this.QueueName).ConfigureAwait(false);
            if (message == null) return;

            bool success = true;
            this.TelemetryClient.TrackEvent("SessionStart", new Dictionary<string, string>
            {
                { "Message", JsonSerializer.Serialize(message) },
                { "QueueName", this.QueueName }
            });

            try
            {
                await this.RunQueueService(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.TelemetryClient.TrackException(ex);
                success = false;
                throw;
            }
            finally
            {
                this.TelemetryClient.TrackEvent("SessionEnd", new Dictionary<string, string>
                {
                    { "Message", JsonSerializer.Serialize(message) },
                    { "Success", success.ToString() }
                });
            }
        }

        protected abstract Task RunQueueService(ServiceNotificationMessage message);
    }
}
