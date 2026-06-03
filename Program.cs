using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using BBR_Ban_Sync.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Hosting;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace BBR_Ban_Sync;

class Program
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    static async Task Main(string[] args)
    {
        try
        {
            LogManager.Setup().LoadConfigurationFromFile("NLog.config");
            logger.Info("Application starting... v{Version}", GetAppVersion());

            EnsureAppSettingsExists();

            var host = CreateHostBuilder(args).Build();

            await ValidateConfigurationAsync(host.Services);

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            LogManager.Shutdown();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory())
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddEnvironmentVariables()
                      .AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                services.AddOptions<BanSyncConfiguration>()
                    .Bind(configuration.GetSection(BanSyncConfiguration.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddOptions<DiscordConfiguration>()
                    .Bind(configuration.GetSection(DiscordConfiguration.SectionName))
                    .Validate(o => !o.Enabled || o.WebhookUrls.Any(), "Discord is enabled but no webhook URLs are configured")
                    .ValidateOnStart();

                services.AddOptions<GitHubConfiguration>()
                    .Bind(configuration.GetSection(GitHubConfiguration.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                RegisterServices(services, configuration);

                services.AddHostedService<BanSyncService>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            })
            .UseNLog()
            .UseConsoleLifetime();

    private static string GetAppVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            return "0.0.0";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    private static void EnsureAppSettingsExists()
    {
        const string appSettingsPath = "appsettings.json";
        const string templatePath = "appsettings.template.json";

        if (File.Exists(appSettingsPath))
            return;

        if (File.Exists(templatePath))
        {
            File.Copy(templatePath, appSettingsPath);
            logger.Warn("appsettings.json was missing. Created from template: {Template}. Please edit it with your real values.", templatePath);
        }
        else
        {
            File.WriteAllText(appSettingsPath, DefaultAppSettingsJson);
            logger.Warn("appsettings.json was missing. Created a default one. Please edit it with your real values.");
        }
    }

    private const string DefaultAppSettingsJson = @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft"": ""Warning"",
      ""Microsoft.Hosting.Lifetime"": ""Information""
    }
  },
  ""ConnectionStrings"": {
    ""DefaultConnection"": ""Server=YOUR_MYSQL_SERVER;Database=YOUR_SOURCEBANS_DATABASE;Uid=YOUR_USERNAME;Pwd=YOUR_PASSWORD;SslMode=Required;""
  },
  ""BanSync"": {
    ""OutputFile"": ""C:\\Path\\To\\Your\\Blacklist.txt"",
    ""SteamAPIKey"": ""YOUR_STEAM_API_KEY_HERE"",
    ""ServerID"": 1,
    ""DebugMode"": false,
    ""SyncIntervalMinutes"": 5,
    ""ReleaseCheckIntervalHours"": 24,
    ""FileWatcherEnabled"": true,
    ""MaxRetryAttempts"": 3,
    ""RetryDelaySeconds"": 5,
    ""CacheExpirationMinutes"": 30
  },
  ""Discord"": {
    ""Enabled"": false,
    ""WebhookUrls"": [
      ""https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN""
    ],
    ""EmbedColor"": 16711680,
    ""ReclamationUrl"": ""https://your-website.com/playerpanel/""
  },
  ""GitHub"": {
    ""Owner"": ""Royal-Multi-Gamers"",
    ""Repository"": ""Ban-Sync-Sourcebans""
  }
}
";

    private static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var banSyncConfig = configuration.GetSection(BanSyncConfiguration.SectionName).Get<BanSyncConfiguration>()
            ?? throw new InvalidOperationException("BanSync configuration is missing");

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string is missing");

        services.AddSingleton<IDatabaseService>(provider =>
        {
            var log = provider.GetRequiredService<ILogger<DatabaseService>>();
            return new DatabaseService(connectionString, log, banSyncConfig.MaxRetryAttempts, banSyncConfig.RetryDelaySeconds);
        });

        services.AddHttpClient("steam", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BBR-Ban-Sync");
        })
        .AddStandardResilienceHandler();

        services.AddHttpClient("discord", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddStandardResilienceHandler();

        services.AddHttpClient("github", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"BBR-Ban-Sync/{GetAppVersion()}");
        })
        .AddStandardResilienceHandler();

        services.AddSingleton<ISteamService>(provider =>
        {
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("steam");
            var log = provider.GetRequiredService<ILogger<SteamService>>();
            var opts = provider.GetRequiredService<IOptions<BanSyncConfiguration>>();
            return new SteamService(http, log, opts);
        });

        services.AddSingleton<IDiscordService>(provider =>
        {
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("discord");
            var log = provider.GetRequiredService<ILogger<DiscordService>>();
            var cfg = provider.GetRequiredService<IOptions<DiscordConfiguration>>().Value;
            return new DiscordService(http, log, cfg);
        });

        services.AddSingleton<IGitHubService>(provider =>
        {
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("github");
            var log = provider.GetRequiredService<ILogger<GitHubService>>();
            var opts = provider.GetRequiredService<IOptions<GitHubConfiguration>>();
            return new GitHubService(http, log, opts);
        });

        services.AddSingleton<IFileWatcherService>(provider =>
        {
            var log = provider.GetRequiredService<ILogger<FileWatcherService>>();
            return new FileWatcherService(banSyncConfig.OutputFile, log);
        });
    }

    private static async Task ValidateConfigurationAsync(IServiceProvider services)
    {
        logger.Info("Validating configuration...");

        var banSyncConfig = services.GetRequiredService<IOptions<BanSyncConfiguration>>().Value;

        if (string.IsNullOrWhiteSpace(banSyncConfig.OutputFile))
            throw new InvalidOperationException("OutputFile configuration is required");

        if (string.IsNullOrWhiteSpace(banSyncConfig.SteamAPIKey))
            throw new InvalidOperationException("SteamAPIKey configuration is required");

        if (!File.Exists(banSyncConfig.OutputFile))
        {
            logger.Warn("Output file does not exist, it will be created: {OutputFile}", banSyncConfig.OutputFile);

            var directory = Path.GetDirectoryName(banSyncConfig.OutputFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                logger.Info("Created directory: {Directory}", directory);
            }

            await File.WriteAllTextAsync(banSyncConfig.OutputFile, string.Empty);
            logger.Info("Created output file: {OutputFile}", banSyncConfig.OutputFile);
        }

        var databaseService = services.GetRequiredService<IDatabaseService>();
        if (!await databaseService.TestConnectionAsync())
            throw new InvalidOperationException("Database connection test failed");

        logger.Info("Configuration validation completed successfully");
    }
}
