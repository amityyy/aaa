using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CloudMine.Core.Telemetry;

namespace Microsoft.CloudMine.SourceCode.Collectors.Core.Services
{
    private static Type DiscoverServiceType(Type baseType, bool throwException = true)
    {
        Assembly executingAssembly = Assembly.GetExecutingAssembly();
        string location = Path.GetDirectoryName(executingAssembly.Location) ?? System.Environment.CurrentDirectory;
        IEnumerable<string> assemblyFiles = Directory.GetFiles(location, "*.dll").Where(fileName => fileName.EndsWith("Microsoft.CloudMine.Collectors.Core.dll"));

        foreach (string assemblyFile in assemblyFiles)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(assemblyFile);
                Type serviceType = assembly.GetTypes().FirstOrDefault(type => type.IsSubclassOf(baseType) && !type.IsAbstract);
                return serviceType;
            }
            catch (Exception)
            {
                // continue looking for correct type
            }
        }

        if (throwException)
        {
            throw new ApplicationException("Could not load service");
        }

        return null;
    }

    public static void RegisterService(IServiceCollection serviceCollection, IConfiguration configuration, ITelemetryClient telemetryClient)
    {
        try
        {
            Type serviceType = DiscoverServiceType(typeof(ServiceBase));
            serviceCollection.AddSingleton(typeof(IHostedService), serviceType);

        // discover and call custom dependency injection method if it exists.
        // We do not throw an exception here since this is not required.
            Type? serviceStartupType = DiscoverServiceType(typeof(ServiceStartupBase), false);
            if (serviceStartupType != null)
            {
                InvokeConfigureServices(serviceStartupType, serviceCollection, configuration);
            }
        }
        catch (Exception e)
        {
            telemetryClient.TrackException(e);
            throw;
        }
    }

    internal static void InvokeConfigureServices(Type serviceStartupType, IServiceCollection serviceCollection, IConfiguration configuration)
    {
        const string configureServicesMethodName = nameof(ServiceStartupBase.ConfigureServices);
        MethodInfo[] methods = serviceStartupType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        MethodInfo configureServicesMethod = methods.FirstOrDefault(method => method.Name.Equals(configureServicesMethodName, StringComparison.OrdinalIgnoreCase));

        object? serviceStartup = Activator.CreateInstance(serviceStartupType);
        configureServicesMethod.Invoke(serviceStartup, new object[] { serviceCollection, configuration });
    }

    public static IConfiguration BuildConfig()
    {
        string dirName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException("Missing execution path");
        string basePath = new DirectoryInfo(dirName).ToString();
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        if (Environment.GetEnvironmentVariable(EnvironmentEV) == "Testing")
        {
            builder.AddUserSecrets<Startup>();
        }

        string endpoint = Environment.GetEnvironmentVariable(AppConfigENV);
        if (string.IsNullOrEmpty(endpoint))
        {
            throw new Exception("App configuration endpoint not set.");
        }

        DefaultAzureCredential credential = new DefaultAzureCredential();
        builder.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(endpoint), credential)
                   .ConfigureKeyVault(kv => kv.SetCredential(credential));
        });

        return builder.Build();
    }
    
}
