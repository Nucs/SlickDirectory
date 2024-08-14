using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace SlickDirectory;

static class Program
{
    static ServiceProvider Startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console() // Log to the console
            .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day) // Log to a file with daily rolling
            .CreateLogger();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddAutoMapper(typeof(Program).Assembly);
        serviceCollection.AddSingleton<IConfiguration>(configuration);
        serviceCollection.AddLogging(configure => configure.AddSerilog());
        serviceCollection.AddScoped<PersistenceLayer>();
        serviceCollection.AddScoped<ClipboardHandler>();
        serviceCollection.AddScoped<BusinessLayer>();
        serviceCollection.AddScoped<Main>();

        return serviceCollection.BuildServiceProvider();
    }

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var serviceProvider = Startup();
        var logger = serviceProvider.GetRequiredService<ILogger<BusinessLayer>>();
        using var scope = serviceProvider.CreateScope();
        logger.LogInformation("Application starting");

        Application.Run(scope.ServiceProvider.GetRequiredService<Main>());
        Log.CloseAndFlush();
    }
}