using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CloudMine.Core.Telemetry;
using Microsoft.CloudMine.SourceCode.Collectors.Clients;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;
using Moq;
using Xunit;

namespace Microsoft.CloudMine.SourceCode.Collectors.Tooling.Service.Tests
{
    public class ToolingServiceTests
    {
        private readonly ToolingService toolingService;
        private readonly Mock<ITelemetryClient> mockTelemetryClient;
        private readonly Mock<IRedisClient> mockRedisClient;

        public ToolingServiceTests()
        {
            mockTelemetryClient = new Mock<ITelemetryClient>();
            mockRedisClient = new Mock<IRedisClient>();
            toolingService = new ToolingService(mockTelemetryClient.Object, mockRedisClient.Object);
        }

        public async Task RunProcessAndReport()
        {
            var failedRepositories = new List<string> { "repo1", "repo2" }; // Corrected "repol" to "repo1"
            var mockTelemetryClient = new Mock<ITelemetryClient>();
            var mockRedisClient = new Mock<IRedisClient>();
            var toolingServiceMock = new Mock<ToolingService>(mockTelemetryClient.Object, mockRedisClient.Object) { CallBase = true };

            toolingServiceMock.Setup(x => x.ProcessFailureMessages()).ReturnsAsync(failedRepositories);
            toolingServiceMock.Setup(x => x.ReportStatistics(It.Is<List<string>>(y => y.SequenceEqual(failedRepositories))));

            await toolingServiceMock.Object.Run();

            toolingServiceMock.Verify(x => x.ProcessFailureMessages(), Times.Once());
            toolingServiceMock.Verify(x => x.ReportStatistics(It.Is<List<string>>(y => y.SequenceEqual(failedRepositories))), Times.Once());
        }

        [Fact]
        public async Task ProcessFailureMessagesAndRetrievesMessages()
        {
            var fakeMessage1 = new ServiceNotificationMessage
            {
                RepositoryState = new RepositoryState
                {
                    Id = "test1",
                    RepositoryId = "repo1",
                    OrganizationName = "meng",
                    RepositoryUrl = "test.com",
                    SessionId = Guid.NewGuid(),
                    RunState = RunState.FAILURE
                }
            };

            var fakeMessage2 = new ServiceNotificationMessage
            {
                RepositoryState = new RepositoryState
                {
                    Id = "test2",
                    RepositoryId = "repo2",
                    OrganizationName = "meng?",
                    RepositoryUrl = "test2.com",
                    SessionId = Guid.NewGuid(),
                    RunState = RunState.FAILURE
                }
            };

            var queue = new Queue<ServiceNotificationMessage?>(new[] { fakeMessage1, fakeMessage2, null });
            mockRedisClient.Setup(x => x.PopMessageAsync<ServiceNotificationMessage>(It.IsAny<string>())).ReturnsAsync(queue.Dequeue);

            await toolingService.Run();

            mockRedisClient.Verify(x => x.PopMessageAsync<ServiceNotificationMessage>(MessageQueueConstants.FailureHandlerQueue), Times.Exactly(3));
            Assert.Empty(queue);
        }

        [Fact]
        public async Task HandlesEmptyQueue()
        {
            mockRedisClient.Setup(x => x.PopMessageAsync<ServiceNotificationMessage>(It.IsAny<string>())).ReturnsAsync((ServiceNotificationMessage?)null);

            var result = await toolingService.ProcessFailureMessages();

            Assert.Empty(result);
            mockTelemetryClient.Verify(x => x.TrackEvent("FailureMessagesProcessed", It.IsAny<Dictionary<string, string>>(), Times.Never()));
        }

        [Fact]
        public void ReportStatisticsAndLogsCorrectly()
        {
            var failedRepositories = new List<string> { "12345", "123456" };
            toolingService.ReportStatistics(failedRepositories);

            mockTelemetryClient.Verify(x => x.TrackEvent(
                "RepositoryFailures",
                It.Is<Dictionary<string, string>>(stats =>
                    stats.ContainsKey("Number of Failures") &&
                    stats["Number of Failures"] == "2" &&
                    stats.ContainsKey("Failed Repositories") &&
                    DeserializeAndCompare(stats["Failed Repositories"], failedRepositories)
                ),
                Times.Once()));
        }

        private bool DeserializeAndCompare(string json, List<string> expectedList)
        {
            try
            {
                var deserializedList = JsonSerializer.Deserialize<List<string>>(json);
                return deserializedList != null && deserializedList.SequenceEqual(expectedList);
            }
            catch
            {
                return false;
            }
        }
    }
}
