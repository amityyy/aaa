using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.Services
{
    /// <summary>
    /// This class is used to help with the startup and appConfig connection for all microservices.
    /// It will also register everything that is needed across services.
    /// </summary>
    public abstract class ServiceStartupBase
    {
        /// <summary>
        /// This method needs to be overwritten by all services.
        /// It should be used to add its implementation as well as additional services to the serviceCollection.
        /// </summary>
        public abstract void ConfigureServices(IServiceCollection serviceCollection, IConfiguration configuration);
    }
}
