using Azure.Core;
using Azure.Identity;
using Microsoft.CloudMine.SourceCode.Collectors.Cache;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.CloudMine.SourceCode.Collectors.Scheduler.Service
{
    public class SchedulerServiceStartup : ServiceStartupBase
    {
        public override void ConfigureServices(IServiceCollection serviceCollection, IConfiguration configuration)
        {
            // Configure Redis settings and services
            RedisSettings redisSettings = configuration.GetSection(nameof(RedisSettings)).Get<RedisSettings>();
            serviceCollection.AddSingleton(redisSettings);
            serviceCollection.AddSingleton<IRedisClient, RedisClient>();
            serviceCollection.AddSingleton<IGitCacheFactory, RedisGitCacheFactory>();

            // Configure CosmosDB settings and services
            CosmosDbSettings cosmosDbSettings = configuration.GetSection(nameof(CosmosDbSettings)).Get<CosmosDbSettings>();
            serviceCollection.AddSingleton(cosmosDbSettings);
            serviceCollection.AddSingleton<ICosmosDbClient, CosmosDbClient>();
            serviceCollection.AddSingleton<ITokenCredential>(new DefaultAzureCredential());

            // Configure Scheduler settings
            SchedulerSettings schedulerSettings = configuration.GetSection(nameof(SchedulerSettings)).Get<SchedulerSettings>();
            serviceCollection.AddSingleton(schedulerSettings);
            serviceCollection.AddSingleton<ISchedulerHelper, SchedulerHelper>();
        }
    }
}
