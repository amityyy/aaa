using Azure.Core;
using Microsoft.CloudMine.SourceCode.Collectors.Clients;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Services;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.CloudMine.SourceCode.Collectors.Concierge.Service
{
    public class ConciergeStartup : ServiceStartupBase
    {
        public override void ConfigureServices(IServiceCollection serviceCollection, Configuration configuration)
        {
            // Setup CosmosDB settings and services
            CosmosDbSettings cosmosDbSettings = configuration.GetSection(nameof(CosmosDbSettings)).Get<CosmosDbSettings>();
            serviceCollection.AddSingleton<CosmosDbSettings>(cosmosDbSettings);
            serviceCollection.AddSingleton<ICosmosDbClient, CosmosDbClient>();

            // Setup Redis settings and services
            RedisSettings redisSettings = configuration.GetSection(nameof(RedisSettings)).Get<RedisSettings>();
            serviceCollection.AddSingleton<RedisSettings>(redisSettings);
            serviceCollection.AddSingleton<IRedisClient, RedisClient>();

            // Setup default Azure credentials
            serviceCollection.AddSingleton<TokenCredential>(new DefaultAzureCredential());

            // Setup Concierge settings
            ConciergeSettings conciergeSettings = configuration.GetSection(nameof(ConciergeSettings)).Get<ConciergeSettings>();
            serviceCollection.AddSingleton(conciergeSettings);

            // Setup Kusto settings and query provider
            KustoSettings kustoSettings = configuration.GetSection(nameof(KustoSettings)).Get<KustoSettings>();
            serviceCollection.AddSingleton(kustoSettings);

            KustoConnectionStringBuilder kustoStringBuilder = new KustoConnectionStringBuilder(kustoSettings.ClusterUrl, kustoSettings.DatabaseName).WithAadSystemManagedIdentity();
            ICslQueryProvider queryProvider = KustoClientFactory.CreateCslQueryProvider(kustoStringBuilder);
            serviceCollection.AddSingleton(queryProvider);
        }
    }
}
