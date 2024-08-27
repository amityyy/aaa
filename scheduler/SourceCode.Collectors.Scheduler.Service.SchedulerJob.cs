using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Collectors.Clients;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Cache;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.CloudMine.SourceCode.Collectors.Scheduler.Service
{
    public class SchedulerJob
    {
        private readonly TimeSpan TimeoutPeriod;
        private readonly ITelemetryClient telemetryClient;
        private readonly IRedisClient redisClient;
        private readonly ICosmosDbClient cosmosDbClient;
        private readonly SchedulerSettings schedulerSettings;
        private readonly SchedulerHelper schedulerHelper;
        private readonly TimeProvider timeProvider;

        public SchedulerJob(ITelemetryClient telemetryClient, IRedisClient redisClient, ICosmosDbClient cosmosDbClient, SchedulerSettings schedulerSettings, SchedulerHelper schedulerHelper, TimeProvider timeProvider = null)
        {
            this.telemetryClient = telemetryClient;
            this.redisClient = redisClient;
            this.cosmosDbClient = cosmosDbClient;
            this.schedulerSettings = schedulerSettings;
            this.TimeoutPeriod = schedulerSettings.MaxTimeout;
            this.schedulerHelper = schedulerHelper;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        internal async Task RunScheduler()
        {
            telemetryClient.TrackEvent("Running scheduler", new Dictionary<string, string>());
            try
            {
                IEnumerable<RepositoryConfiguration> activeRepoConfigs = await cosmosDbClient.GetItemsFromContainerAsync<RepositoryConfiguration>(cosmosDbClient.CosmosDbSettings.RepositoryConfigContainerName, schedulerSettings.ConfigurationQuery).ConfigureAwait(false);
                Dictionary<string, string> activeRepoProperties = new Dictionary<string, string>
                {
                    { "ActiveRepositoryCount", activeRepoConfigs.Count().ToString() }
                };
                telemetryClient.TrackEvent("Scheduling repo collection", activeRepoProperties);
                foreach (RepositoryConfiguration activeRepo in activeRepoConfigs)
                {
                    await ScheduleRepositoryAsync(activeRepo).ConfigureAwait(false);
                }
                telemetryClient.TrackEvent("Repo collection scheduled", activeRepoProperties);
            }
            catch (Exception e)
            {
                telemetryClient.TrackException(e, "Error in RunScheduler");
                throw;
            }
        }

        internal async Task ScheduleRepositoryAsync(RepositoryConfiguration repoConfig)
        {
            try
            {
                string jobKey = Constants.GetRecordIdentifier(Constants.RepositoryJobPrefix, repoConfig.OrganizationName, repoConfig.RepositoryId);
                IEnumerable<SourceCodeJob> jobs = await redisClient.GetHashValuesAsync<SourceCodeJob>(jobKey).ConfigureAwait(false);

                if (jobs.Any(job => job.Message.RunState == RunState.RUNNING))
                {
                    if (jobs.Any(job => timeProvider.GetUtcNow() > job.UpdateTime + TimeoutPeriod))
                    {
                        foreach (SourceCodeJob runningJob in jobs)
                        {
                            Dictionary<string, string> properties = new Dictionary<string, string>
                            {
                                { "RepositoryId", repoConfig.RepositoryId },
                                { "SessionId", runningJob.Message.SessionId.ToString() }
                            };
                            telemetryClient.TrackEvent("JobTimeout", properties);
                            await schedulerHelper.HandleFailedJobAsync(runningJob.Message).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        telemetryClient.TrackEvent("Previous job is still running, do not schedule new job.", new Dictionary<string, string> { { "RepositoryId", repoConfig.RepositoryId } });
                    }
                    return;
                }

                bool shouldSchedule = await ShouldScheduleAsync(repoConfig).ConfigureAwait(false);
                if (shouldSchedule)
                {
                    RepositoryState repoState = await cosmosDbClient.GetItemFromContainerAsync<RepositoryState>(CosmosDbClient.CosmosDbSettings.RepositoryStateContainerName, Constants.GetRecordIdentifier(Constants.RepositoryStatePrefix, repoConfig.OrganizationName, repoConfig.RepositoryId)).ConfigureAwait(false) ?? new RepositoryState
                    {
                        RepositoryId = repoConfig.RepositoryId,
                        Id = Constants.GetRecordIdentifier(Constants.RepositoryStatePrefix, repoConfig.OrganizationName, repoConfig.RepositoryId),
                        OrganizationName = repoConfig.OrganizationName,
                        RepositoryUrl = repoConfig.RepositoryUrl
                    };

                    await schedulerHelper.AddWorkerMessageAsync(repoState).ConfigureAwait(false);
                }
                else
                {
                    telemetryClient.TrackEvent("Not scheduling repository", new Dictionary<string, string> { { "RepositoryId", repoConfig.RepositoryId } });
                }
            }
            catch (Exception e)
            {
                telemetryClient.TrackException(e, "Error in ScheduleRepositoryAsync");
                throw;
            }
        }

        internal async Task<bool> ShouldScheduleAsync(RepositoryConfiguration repoConfig)
        {
            try
            {
            RepositoryMetadata repoMetadata = await schedulerHelper.HandleRepositoryMetadataAsync(
                repoConfig.OrganizationName, repoConfig.RepositoryId).ConfigureAwait(false);
            DateTimeOffset? lastCollectionEndDate = repoMetadata.LastCollectionEndDate;

        // Immediately schedule the repository if it has never been scheduled before
            if (!lastCollectionEndDate.HasValue)
            {
                return true;
            }

            if (repoConfig.RepositorySchedule.Type == "Cron")
            {
                string cronSchedule = repoConfig.RepositorySchedule.Value;
                CrontabSchedule schedule;
                try
                {
                    schedule = CrontabSchedule.Parse(cronSchedule);
                }
                catch (CrontabException e)
                {
                    telemetryClient.TrackException(e, "Cannot parse cron schedule.");
                    return false;
                }

                DateTime nextScheduledTime = schedule.GetNextOccurrence(lastCollectionEndDate.Value.UtcDateTime);
            // Only schedule repository if the current time is greater than or equal to the next scheduled time
                return timeProvider.GetUtcNow() >= nextScheduledTime;
            }
            else
            {
            // Currently not handling other types of schedules
                return false;
            }
        }
        catch (Exception e)
        {
            telemetryClient.TrackException(e, "Error in ShouldScheduleAsync");
            throw;
        }
    }
}
