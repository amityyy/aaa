using System;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Collectors.Clients;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Services;

namespace Microsoft.CloudMine.SourceCode.Collectors.Scheduler.Service
{
    public class SchedulerService : QueueServiceBase
    {
        private readonly IRedisClient redisClient;
        private readonly SchedulerHelper schedulerHelper;
        private readonly TimeProvider timeProvider;

        public SchedulerService(ITelemetryClient telemetryClient, IRedisClient redisClient, SchedulerHelper schedulerHelper, TimeProvider timeProvider = null)
            : base(telemetryClient, redisClient, MessageQueueConstants.SchedulerQueue)
        {
            this.redisClient = redisClient;
            this.schedulerHelper = schedulerHelper;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override async Task RunQueueService(ServiceNotificationMessage message)
        {
            switch (message.RunState)
            {
                case RunState.RUNNING:
                    string jobKey = Constants.GetRecordIdentifier(Constants.RepositoryJobPrefix, message.RepositoryState);
                    await redisClient.SetHashValueAsync(jobKey, message.SessionId.ToString(), new SourceCodeJob
                    {
                        Message = message,
                        UpdateTime = timeProvider.GetUtcNow()
                    }).ConfigureAwait(false);
                    break;

                case RunState.SUCCESS:
                    await schedulerHelper.HandleSuccessfulJobAsync(message).ConfigureAwait(false);
                    break;

                case RunState.FAILURE:
                    await schedulerHelper.HandleFailedJobAsync(message).ConfigureAwait(false);
                    break;
            }
        }
    }
}