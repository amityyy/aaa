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
            bool messageSimulated = false;
            ServiceNotificationMessage? message = null;
            if (!messageSimulated)
            {
                for (int i = 0; i < 2; i++) // Simulate pushing messages for the first repository to the queue
                {
                    message = new ServiceNotificationMessage
                    {       
                        RepositoryState = new RepositoryState
                        {
                            Id = this.ServiceSessionId,
                            RepositoryId = "12345",
                            OrganizationName = "meng",
                            RepositoryUrl = "test.com"
                        },
                        SessionId = Guid.NewGuid(),
                        RunState = RunState.FAILURE
                    };

                    await RedisClient.PushMessageAsync(MessageQueueConstants.SchedulerQueue, message).ConfigureAwait(false);
                }
                for (int i = 0; i < 2; i++) // Simulate pushing messages for the first repository to the queue
                {
                    message = new ServiceNotificationMessage
                    {       
                        RepositoryState = new RepositoryState
                        {
                            Id = this.ServiceSessionId,
                            RepositoryId = "12345",
                            OrganizationName = "meng",
                            RepositoryUrl = "test.com"
                        },
                        SessionId = Guid.NewGuid(),
                        RunState = RunState.FAILURE
                    };

                    await RedisClient.PushMessageAsync(MessageQueueConstants.SchedulerQueue, message).ConfigureAwait(false);
                }
                messageSimulated = true;
            }
            int failedCount = 0;
            List<String> failedRepositories = new List<String>;
            ServiceNotificationMessage? failureMessagePop;
            while ((failureMessagePop = await RedisClient.PopMessageAsync<ServiceNotificationMessage>
                (MessageQueueConstants .FailureHandlerQueue)) != null) {
                failedCount++;
                failedRepositories.Add(failureMessagePop.RepositoryState.RepositoryId);
            }
            Console.WriteLine($"Number: {failedCount}");
            Console. WriteLine($"Repositories: {JsonSerializer.Serialize(failedRepositories)}");

            //ServiceNotificationMessage message = await this.RedisClient.PopMessageAsync<ServiceNotificationMessage>(this.QueueName).ConfigureAwait(false);
            if (message != null)
            {
                bool success = true;
                this.TelemetryClient.SessionId = message.SessionId.ToString();
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
                    this.TelemetryClient.SessionId = this.ServiceSessionId;
                }
            }
        }

        protected abstract Task RunQueueService(ServiceNotificationMessage message);
    }
}
