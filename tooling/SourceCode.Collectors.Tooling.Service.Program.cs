namespace Microsoft.CloudMine.SourceCode.Collectors.Tooling.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Configuration configuration = Registrar.BuildConfig();
            IServiceCollection services = new ServiceCollection();
            RegisterAllServices(services, configuration);
            services.AddSingleton<ToolingService>();
            var serviceProvider = services.BuildServiceProvider();
            ToolingService toolingService = serviceProvider.GetRequiredService<ToolingService>();
            toolingService.Run().Wait();
        }

        private static void RegisterAllServices(IServiceCollection services, Configuration configuration)
        {
            Registrar.RegisterCommonServices(services, configuration);
        }
    }
}
