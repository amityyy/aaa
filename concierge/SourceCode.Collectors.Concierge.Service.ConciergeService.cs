using Kusto.Data.Common;
using Microsoft.CloudMine.Core.Collectors.Cache;
using Microsoft.CloudMine.Core.Collectors.Clients;
using Microsoft.CloudMine.Core.Collectors.Core;
using Microsoft.CloudMine.Core.Collectors.Model;
using Microsoft.CloudMine.Core.Collectors.Services;
using Microsoft.CloudMine.Core.Telemetry;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.CloudMine.SourceCode.Collectors.Concierge.Service
{
    public class ConciergeService : ServiceBase
    {
        private readonly ICosmosDbClient cosmosDbClient;
        private readonly KustoSettings kustoSettings;
        private readonly ConciergeSettings conciergeSettings;
        private readonly ICSQueryProvider queryProvider;
        private readonly TimeProvider timeProvider;

        public ConciergeService(ITelemetryClient telemetryClient, ICosmosDbClient cosmosDbClient, KustoSettings kustoSettings, ConciergeSettings conciergeSettings, ICSQueryProvider queryProvider, TimeProvider timeProvider = null) : base(telemetryClient)
        {
            if (queryProvider == null)
            {
                throw new ArgumentException("Cannot run Concierge service. Failed to establish a connection with Kusto to run the query");
            }

            this.cosmosDbClient = cosmosDbClient;
            this.kustoSettings = kustoSettings;
            this.conciergeSettings = conciergeSettings;
            this.queryProvider = queryProvider;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        public override async Task Run()
        {
            IEnumerable<RepositoryConfiguration> repositoryConfigurations = await GetRepositoriesFromKustoQueryAsync().ConfigureAwait(false);

            foreach (RepositoryConfiguration repositoryConfiguration in repositoryConfigurations)
            {
                await UpdateRepositoryConfigurationInCosmosDbAsync(repositoryConfiguration).ConfigureAwait(false);
            }
        }

        public async Task<List<RepositoryConfiguration>> GetRepositoriesFromKustoQueryAsync()
        {
            List<RepositoryConfiguration> repositoryConfigurations = new List<RepositoryConfiguration>();
            try
            {
                ClientRequestProperties clientRequestProperties = new() { ClientRequestId = Guid.NewGuid().ToString() };
                using (IDataReader dataReader = await queryProvider.ExecuteQueryAsync(KustoSettings.DatabaseName, conciergeSettings.KustoFunctionName, clientRequestProperties))
                {
                    while (dataReader.Read())
                    {
                        string organizationName = dataReader.GetString(dataReader.GetOrdinal("OrganizationName"));
                        string repositoryId = dataReader.GetString(dataReader.GetOrdinal("RepositoryId"));
                        string repositoryUrl = dataReader.GetString(dataReader.GetOrdinal("RepositoryUrl"));
                        string region = dataReader.GetString(dataReader.GetOrdinal("Region"));
                        bool isDisabled = dataReader.GetBoolean(dataReader.GetOrdinal("IsDisabled"));

                        RepositoryConfiguration repositoryConfiguration = new RepositoryConfiguration()
                        {
                            OrganizationName = organizationName,
                            RepositoryId = repositoryId,
                            OnboardedDateTime = DateTime.UtcNow,
                            Region = region,
                            RepositoryUrl = repositoryUrl,
                            State = isDisabled ? State.Inactive : State.Active,
                            RepositorySchedule = new Schedule() { Type = "Cron", Value = "0 * * * *" },
                            Type = RepositoryType.Git,
                            Id = Constants.GetRecordIdentifier(Constants.RepositoryConfigurationPrefix, organizationName, repositoryId),
                            StorageOptions = new List<StorageOption> { StorageOption.AdsGenTwo }
                        };

                        repositoryConfigurations.Add(repositoryConfiguration);
                    }
                }
            }
            catch (Exception ex)
            {
                telemetryClient.TrackException(ex);
                throw;
            }

            return repositoryConfigurations;
        }

    public async Task UpdateRepositoryConfigurationInCosmosDbAsync(IEnumerable<RepositoryConfiguration> repositoryConfigurations)
    {
        foreach (RepositoryConfiguration repositoryConfiguration in repositoryConfigurations)
        {
            if (repositoryConfiguration.State == State.Active)
            {
                // check if item exists in db
                RepositoryConfiguration? repositoryConfigurationInDb = await cosmosDbClient.GetFromContainerAsync(
                    CosmosDbSettings.RepositoryConfigContainerName, repositoryConfiguration.id);

                if (repositoryConfigurationInDb == null)
                {
                    // new item, so provide it with OnboardedTime
                    repositoryConfiguration.OnboardedTime = DateTime.UtcNow;
                    
                    bool addItemSuccess = await cosmosDbClient.AddItemToContainerAsync(
                        CosmosDbSettings.RepositoryConfigContainerName, repositoryConfiguration);

                    Dictionary<string, string> properties = new()
                    {
                        {"Id", repositoryConfiguration.id},
                        {"RepositoryId", repositoryConfiguration.RepositoryId},
                        {"RepositoryConfiguration", JsonSerializer.Serialize(repositoryConfiguration)}
                    };

                    if (addItemSuccess)
                    {
                        telemetryClient.TrackEvent("Created a new repository configuration item in CosmosDb", properties);
                    }
                    else
                    {
                        telemetryClient.TrackEvent("Failed to create a new repository configuration item in CosmosDb", properties);
                    }
                }
                else
                {
                    // delete the item if it exists and add it to inactive list
                    bool updateSuccess = await cosmosDbClient.UpsertItemInContainerAsync(
                        CosmosDbSettings.RepositoryConfigContainerName, repositoryConfigurationInDb.id, repositoryConfigurationInDb);

                    Dictionary<string, string> properties = new()
                    {
                        {"Id", repositoryConfiguration.id},
                        {"RepositoryId", repositoryConfiguration.RepositoryId},
                        {"RepositoryConfiguration", JsonSerializer.Serialize(repositoryConfiguration)}
                    };

                    if (updateSuccess)
                    {
                        properties.Add("OffboardedTime", repositoryConfigurationInDb.OffboardedTime.ToString());
                    }
                }

            }
        }

    }
}
