using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Clients;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Services;

namespace Microsoft.CloudMine.SourceCode.Collectors.ToolingService
{
    public class ToolingService : ServiceBase
    {
        private readonly IRedisClient RedisClient;
        private static bool SimulatedQueue = false;

        public ToolingService(ITelemetryClient telemetryClient, IRedisClient redisClient) : base(telemetryClient)
        {
            this.RedisClient = redisClient;
        }

        public override async Task Run()
        {
            if (!SimulatedQueue)
            {
                await SimulateMessagePush();
                SimulatedQueue = true;
            }

            int failedCount = 0;
            List<string> failedRepositories = new List<string>();
            ServiceNotificationMessage? failureMessagePop;

            while ((failureMessagePop = await RedisClient.PopMessageAsync<ServiceNotificationMessage>(MessageQueueConstants.FailureHandlerQueue)) != null)
            {
                failedCount++;
                failedRepositories.Add(failureMessagePop.RepositoryState.RepositoryId);
            }

            Console.WriteLine($"Number of failures: {failedCount}");
            Console.WriteLine($"Failed Repositories: {JsonSerializer.Serialize(failedRepositories)}");
        }

        private async Task SimulateMessagePush()
        {
            // Simulate pushing messages with failure state to the SchedulerQueue
            var message1 = new ServiceNotificationMessage
            {
                RepositoryState = new RepositoryState
                {
                    Id = "test1",
                    RepositoryId = "12345",
                    OrganizationName = "meng",
                    RepositoryUrl = "test.com",
                    SessionId = Guid.NewGuid(),
                    RunState = RunState.FAILURE
                }
            };

            await RedisClient.PushMessageAsync(MessageQueueConstants.FailureHandlerQueue, message1).ConfigureAwait(false);

            var message2 = new ServiceNotificationMessage
            {
                RepositoryState = new RepositoryState
                {
                    Id = "test2",
                    RepositoryId = "123456",
                    OrganizationName = "meng2",
                    RepositoryUrl = "test.com",
                    SessionId = Guid.NewGuid(),
                    RunState = RunState.FAILURE
                }
            };

            await RedisClient.PushMessageAsync(MessageQueueConstants.FailureHandlerQueue, message2).ConfigureAwait(false);
        }
    }
}
