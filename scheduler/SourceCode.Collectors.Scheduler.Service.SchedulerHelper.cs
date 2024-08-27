using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Collectors.Clients;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.Core.Utility;
using Microsoft.CloudMine.SourceCode.Collectors.Cache;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.CloudMine.SourceCode.Collectors.Scheduler.Service
{
    public class SchedulerHelper
    {
        private readonly ITelemetryClient telemetryClient;
        private readonly IRedisClient redisClient;
        private readonly ICosmosDbClient cosmosDbClient;
        private readonly IGitCacheFactory gitCacheFactory;
        private readonly TimeProvider timeProvider;
        private readonly int maxReruns;

        public SchedulerHelper(ITelemetryClient telemetryClient, IRedisClient redisClient, ICosmosDbClient cosmosDbClient, IGitCacheFactory gitCacheFactory, SchedulerSettings schedulerSettings, TimeProvider timeProvider = null)
        {
            this.telemetryClient = telemetryClient;
            this.redisClient = redisClient;
            this.cosmosDbClient = cosmosDbClient;
            this.gitCacheFactory = gitCacheFactory;
            this.maxReruns = schedulerSettings.MaxReruns;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        internal async Task<RepositoryMetadata> HandleRepositoryMetadataAsync(string organizationName, string repositoryId, DateTimeOffset? lastCollectionStartTime = null, DateTimeOffset? lastCollectionEndTime = null)
        {
            string key = Constants.GetRecordIdentifier(Constants.RepositoryMetadataPrefix, organizationName, repositoryId);
            RepositoryMetadata repoMetadata = await cosmosDbClient.GetItemFromContainerAsync<RepositoryMetadata>(CosmosDbClient.CosmosDbSettings.RepositoryMetadataContainerName, key).ConfigureAwait(false);
            bool updateCache = false;

            if (repoMetadata == null)
            {
                updateCache = true;
                repoMetadata = new RepositoryMetadata()
                {
                    Id = key,
                    OrganizationName = organizationName,
                    RepositoryId = repositoryId
                };
            }

            if (lastCollectionStartTime.HasValue)
            {
                updateCache = true;
                repoMetadata.LastCollectionBeginDateTime = lastCollectionStartTime.Value;
            }

            if (lastCollectionEndTime.HasValue)
            {
                updateCache = true;
                repoMetadata.LastCollectionEndDateTime = lastCollectionEndTime.Value;
            }

            if (updateCache)
            {
                bool success = await redisClient.SetValueAsync(key, repoMetadata).ConfigureAwait(false);
                Dictionary<string, string> properties = new Dictionary<string, string>
                {
                    { "RepositoryId", repositoryId },
                    { "RepositoryMetadata", JsonSerializer.Serialize(repoMetadata) },
                    { "Success", success.ToString() }
                };
                telemetryClient.TrackEvent("Updating repository metadata.", properties);
            }

            return repoMetadata;
        }

        internal async Task AddWorkerMessageAsync(RepositoryState state)
        {
            try
            {
                ServiceNotificationMessage message = new ServiceNotificationMessage
                {
                    SessionId = Guid.NewGuid(),
                    RepositoryState = state
                };

                string jobKey = Constants.GetRecordIdentifier(Constants.RepositoryJobPrefix, state);
                await redisClient.SetHashValueAsync(jobKey, message.SessionId.ToString(), new SourceCodeJob { Message = message, UpdateTime = timeProvider.GetUtcNow() }).ConfigureAwait(false);
                await redisClient.PushMessageAsync(RepositoryQueueConstants.WorkerQueue, message).ConfigureAwait(false);

                Dictionary<string, string> properties = new Dictionary<string, string>
                {
                    { "RepositoryId", state.RepositoryId },
                    { "RepositoryState", JsonSerializer.Serialize(state) }
                };
                telemetryClient.TrackEvent("Scheduling repository", properties);

                await HandleRepositoryMetadataAsync(state.OrganizationName, state.RepositoryId, lastCollectionStartTime: timeProvider.GetUtcNow()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                telemetryClient.TrackException(e, "Error in AddWorkerMessageAsync");
                throw;
            }
        }

        internal async Task HandleSuccessfulJobAsync(ServiceNotificationMessage message)
        {
            try
            {
                string repoStateKey = Constants.GetRecordIdentifier(Constants.RepositoryStatePrefix, message.RepositoryState);
                string jobKey = Constants.GetRecordIdentifier(Constants.RepositoryJobPrefix, message.RepositoryState);
                Dictionary<string, string> properties = new Dictionary<string, string>
                {
                    { "RepositoryId", message.RepositoryState.RepositoryId },
                    { "RepositoryState", JsonSerializer.Serialize(message.RepositoryState) }
                };

                telemetryClient.TrackEvent("Job succeeded, updating repository state.", properties);
                await UpdateRepositoryStateInCosmosDbAsync(repoStateKey, message.RepositoryState);
                var metadata = await HandleRepositoryMetadataAsync(message.RepositoryState.OrganizationName, message.RepositoryState.RepositoryId, LastCollectionEndTime: timeProvider.GetUtcNow());
                await UpdateRepositoryMetadataInCosmosDbAsync(metadata);

                string failedJobKey = Constants.GetRecordIdentifier(Constants.FailedJobPrefix, message.RepositoryState);
                if (await redisClient.KeyExistsAsync(failedJobKey))
                {
                    await redisClient.DeleteValueAsync(failedJobKey);
                    await redisClient.DeleteHashValueAsync(jobKey, message.SessionId.ToString());
                }

                IGitCache sessionCache = this.gitCacheFactory.CreateSessionCache(message);
                IGitCache globalCache = this.gitCacheFactory.CreateGlobalCache(message);
                await sessionCache.ClearAndMergeIntoGlobalCacheAsync(globalCache);
            }
            catch (Exception e)
            {
                telemetryClient.TrackException(e, "Error in HandleSuccessfulJobAsync");
                throw;
            }
        }

        internal async Task HandleFailedJobAsync(ServiceNotificationMessage message)
        {
            try
            {
                string jobKey = Constants.GetRecordIdentifier(Constants.RepositoryJobPrefix, message.RepositoryState);
                string failedJobKey = Constants.GetRecordIdentifier(Constants.FailedJobPrefix, message.RepositoryState);
                Dictionary<string, string> properties = new Dictionary<string, string>
                {
                    { "RepositoryId", message.RepositoryState.RepositoryId },
                    { "RepositoryState", JsonSerializer.Serialize(message.RepositoryState) }
                };

                telemetryClient.TrackEvent("Handling failed collection", properties);
                await HandleRepositoryMetadataAsync(message.RepositoryState.OrganizationName, message.RepositoryState.RepositoryId, LastCollectionEndTime: timeProvider.GetUtcNow());

                IGitCache sessionCache = this.gitCacheFactory.CreateSessionCache(message);
                await sessionCache.ClearAsync();

                int rerunNumber = await redisClient.GetValueAsync<int>(failedJobKey);
                if (rerunNumber < maxReruns)
                {
                    rerunNumber++;
                    telemetryClient.TrackEvent("Re-running failed job.", properties);
                    await redisClient.SetValueAsync(failedJobKey, rerunNumber);
                    await AddWorkerMessageAsync(message.RepositoryState);
                }
                else
                {
                    telemetryClient.TrackEvent("Reruns exceeded. Not rescheduling.", properties);
                    await redisClient.PushMessageAsync(MessageQueueConstants.FailureHandlerQueue, message);
                    await redisClient.DeleteHashValueAsync(jobKey, message.SessionId.ToString());
                }
            }
            catch (Exception e)
            {
                telemetryClient.TrackException(e, "Error in HandleFailedJobAsync");
                throw;
            }
        }

        internal async Task UpdateRepositoryStateInCosmosDbAsync(string key, RepositoryState repoState)
        {
            Dictionary<string, string> properties = new Dictionary<string, string>
            {
                { "RepositoryId", repoState.RepositoryId },
                { "RepositoryState", JsonSerializer.Serialize(repoState) }
            };

            bool upsertItemSuccess = await cosmosDbClient.UpsertItemInContainerAsync(
            CosmosDbClient.CosmosDbSettings.RepositoryStateContainerName, 
            key, repoState).ConfigureAwait(false);

            properties.Add("UpsertItemSuccess", upsertItemSuccess.ToString());
            telemetryClient.TrackEvent("CosmosDbStateRecord", properties);
        }

        internal async Task UpdateRepositoryMetadataInCosmosDbAsync(RepositoryMetadata metadata)
        {
            Dictionary<string, string> properties = new Dictionary<string, string>
            {
                { "RepositoryId", metadata.RepositoryId },
                { "RepositoryState", JsonSerializer.Serialize(metadata) }
            };

            bool upsertItemSuccess = await cosmosDbClient.UpsertItemInContainerAsync(
            CosmosDbClient.CosmosDbSettings.RepositoryMetadataContainerName, metadata.Id, metadata).ConfigureAwait(false);

            properties.Add("UpdateItemSuccess", upsertItemSuccess.ToString());
            telemetryClient.TrackEvent("CosmosDbMetadataRecord", properties);
        }
    }    
}
