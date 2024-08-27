using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CloudMine.SourceCode.Collectors.Clients;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Telemetry;

namespace Microsoft.CloudMine.SourceCode.Collectors.Tooling.Service
{
    public class ToolingService : ServiceBase
    {
        private readonly IRedisClient RedisClient;

        public ToolingService(ITelemetryClient telemetryClient, IRedisClient redisClient) : base(telemetryClient)
        {
            this.RedisClient = redisClient;
        }

        public override async Task Run()
        {
            IEnumerable<string> failedRepositories = await ProcessFailureMessages();
            ReportStatistics(failedRepositories);
        }

        public async virtual Task<List<string>> ProcessFailureMessages()
        {
            List<string> failedRepositories = new List<string>();
            var messages = await RedisClient.ListRangeAsync<ServiceNotificationMessage>(
                MessageQueueConstants.FailureHandlerQueue, 0, -1);

            foreach (var message in messages)
            {
                if (message?.RepositoryState?.RepositoryId != null)
                {
                    failedRepositories.Add(message.RepositoryState.RepositoryId);
                }
            }

            return failedRepositories;
        }


        public void ReportStatistics(List<string> failedRepositories)
        {
            List<string> repositoryList = new(failedRepositories); // Convert to list if necessary for operations like Count

            TelemetryClient.TrackEvent("RepositoryFailures", new Dictionary<string, string>
            {
                { "Number of Failures", repositoryList.Count.ToString() },
                { "Failed Repositories", JsonSerializer.Serialize(repositoryList) }
            });
        }
    }
}
