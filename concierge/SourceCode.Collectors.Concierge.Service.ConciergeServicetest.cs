using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Moq;
using Microsoft.CloudMine.Core.Collectors.Tests.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Clients;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Settings;

namespace Microsoft.CloudMine.SourceCode.Collectors.Concierge.Service.Tests
{
    public class ConciergeServiceTests
    {
        private readonly ConciergeService conciergeService;
        private readonly CosmosDbSettings cosmosDbSettings;
        private readonly RepositoryConfiguration repoConfig1;
        private readonly RepositoryConfiguration repoConfig2;
        private readonly Mock<ICosmosDbClient> mockCosmosDbClient;
        private readonly Mock<ICslQueryProvider> mockKustoQueryProvider;

        public ConciergeServiceTests()
        {
            NoopTelemetryClient telemetryClient = new NoopTelemetryClient();
            cosmosDbSettings = new CosmosDbSettings
            {
                DatabaseName = "testCosmosdb",
                RepositoryConfigContainerName = "testCosmosContainer",
                RepositoryMetadataContainerName = "test",
                RepositoryStateContainerName = "test",
                EndpointUri = "https://cosmosdb.test"
            };

            mockCosmosDbClient = new Mock<ICosmosDbClient>();
            mockCosmosDbClient.Setup(x => x.CosmosDbSettings).Returns(cosmosDbSettings);

            ConciergeSettings conciergeSettings = new ConciergeSettings
            {
                KustoFunctionName = "testFunction"
            };

            KustoSettings kustoSettings = new KustoSettings
            {
                ClusterUrl = "https://test.kusto.windows.net",
                DatabaseName = "testDb"
            };

            mockKustoQueryProvider = new Mock<ICslQueryProvider>();
            conciergeService = new ConciergeService(telemetryClient, mockCosmosDbClient.Object, kustoSettings, conciergeSettings, mockKustoQueryProvider.Object);

            repoConfig1 = new RepositoryConfiguration
            {
                Id = $"{Constants.RepositoryConfigurationPrefix}:TestOrgName+TestRepoId1",
                RepositoryId = "TestRepoId1",
                OrganizationName = "TestOrgName",
                OnboardedDateTime = DateTime.UtcNow,
                Region = "TestRegion",
                RepositoryUrl = "TestRepoUrl1",
                State = State.Active,
                RepositorySchedule = new RepositorySchedule
                {
                    Type = "testType",
                    Value = "testValue",
                    Type = RepositoryType.Git,
                    StorageOptions = new List<StorageOption> { StorageOption.AdlsGenTwo }
                }
            };

            repoConfig2 = new RepositoryConfiguration
            {
                Id = $"{Constants.RepositoryConfigurationPrefix}:TestOrgName+TestRepoId2",
                RepositoryId = "TestRepoId2",
                OrganizationName = "TestOrgName",
                OnboardedDateTime = DateTime.UtcNow,
                Region = "TestRegion",
                RepositoryUrl = "TestRepoUrl2",
                State = State.Inactive,
                RepositorySchedule = new RepositorySchedule
                {
                    Type = "testType",
                    Value = "testValue",
                    Type = RepositoryType.Git,
                    StorageOptions = new List<StorageOption> { StorageOption.AdlsGenTwo }
                }
            };
        }

        [Fact]
        public async Task GetRepositoriesFromKustoQueryAsync()
        {
            var expected = repoConfig1;
            DataTable dataTable = new DataTable
            {
                Columns =
                {
                    new DataColumn("OrganizationName", typeof(string)),
                    new DataColumn("RepositoryId", typeof(string)),
                    new DataColumn("Region", typeof(string)),
                    new DataColumn("RepositoryUrl", typeof(string)),
                    new DataColumn("IsDisabled", typeof(bool))
                },
                Rows =
                {
                    { expected.OrganizationName, expected.RepositoryId, expected.Region, expected.RepositoryUrl, expected.State == State.Inactive }
                }
            };

            mockKustoQueryProvider.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ClientRequestProperties>(), default))
                .ReturnsAsync(dataTable.CreateDataReader());

            List<RepositoryConfiguration> repositoryConfigurations = await conciergeService.GetRepositoriesFromKustoQueryAsync();
            Assert.Single(repositoryConfigurations);

            var actual = repositoryConfigurations[0];
            Assert.Equal(expected.OrganizationName, actual.OrganizationName);
            Assert.Equal(expected.RepositoryId, actual.RepositoryId);
            Assert.Equal(expected.Region, actual.Region);
            Assert.Equal(expected.RepositoryUrl, actual.RepositoryUrl);
            Assert.Equal(expected.State, actual.State);
            Assert.NotNull(actual.RepositorySchedule);
            Assert.NotNull(actual.OnboardedDateTime);
        }

        [Fact]
        public async Task UpdateRepositoryConfigurationInCosmosDbAsync()
        {
            string containerName = cosmosDbSettings.RepositoryConfigContainerName;

            // repoConfig1 item does not exist
            mockCosmosDbClient.Setup(x => x.GetItemFromContainerAsync<RepositoryConfiguration>(containerName, repoConfig1.Id))
                .ReturnsAsync((RepositoryConfiguration?)null);

            // repoConfig1 item is created
            mockCosmosDbClient.Setup(x => x.AddItemToContainerAsync(containerName, repoConfig1.Id, repoConfig1))
                .ReturnsAsync(true);

            // repoConfig2 item exists
            mockCosmosDbClient.Setup(x => x.GetItemFromContainerAsync<RepositoryConfiguration>(containerName, repoConfig2.Id))
                .ReturnsAsync(repoConfig2);

            // repoConfig2 item is offboarded
            mockCosmosDbClient.Setup(x => x.UpsertItemInContainerAsync(containerName, repoConfig2.Id, repoConfig2))
                .ReturnsAsync(true);

            await conciergeService.UpdateRepositoryConfigurationInCosmosDbAsync(new List<RepositoryConfiguration> { repoConfig1, repoConfig2 });

            Assert.NotNull(repoConfig2.OffboardedDateTime);
            mockCosmosDbClient.VerifyAll();
            mockCosmosDbClient.VerifyNoOtherCalls();
        }
    }
}
