using Azure.Core;
using Azure.Identity;
using Microsoft.CloudMine.SourceCode.Collectors.Clients;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.CloudMine.SourceCode.Collectors.Tooling.Service
{
    public class ToolingServiceStartup : ServiceStartupBase
    {
        public override void ConfigureServices(IServiceCollection serviceCollection, IConfiguration configuration)
        {
            // Configure Redis settings and services
            RedisSettings redisSettings = configuration.GetSection(nameof(RedisSettings)).Get<RedisSettings>();
            serviceCollection.AddSingleton<RedisSettings>(redisSettings);
            serviceCollection.AddSingleton<IRedisClient, RedisClient>();

            // Setup Azure CLI credentials
            serviceCollection.AddSingleton<TokenCredential>(new AzureCliCredential());

            // Add ToolingService to the services collection
            serviceCollection.AddSingleton<ToolingService>();
        }
    }
}
