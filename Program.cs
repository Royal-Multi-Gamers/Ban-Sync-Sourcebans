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

namespace BBR_Ban_Sync;

class Program
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    static async Task Main(string[] args)
    {
        try
        {
            // Configure NLog
            LogManager.Setup().LoadConfigurationFromFile("NLog.config");

            logger.Info("Application starting...");

            // Create host builder
            var host = CreateHostBuilder(args).Build();

            // Validate configuration
            await ValidateConfigurationAsync(host.Services);

            // Run the application
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

                // Configure options
                services.Configure<BanSyncConfiguration>(configuration.GetSection(BanSyncConfiguration.SectionName));
                services.Configure<DiscordConfiguration>(configuration.GetSection(DiscordConfiguration.SectionName));
                // Removed GitHubConfiguration binding and validation as per user request

                // Validate configurations
                services.AddSingleton<IValidateOptions<BanSyncConfiguration>, BanSyncConfigurationValidator>();
                services.AddSingleton<IValidateOptions<DiscordConfiguration>, DiscordConfigurationValidator>();
                // Removed GitHubConfigurationValidator registration as per user request

                // Configure HttpClient
                services.AddHttpClient();

                // Register services
                RegisterServices(services, configuration);

                // Register hosted service
                services.AddHostedService<BanSyncService>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            })
            .UseNLog()
            .UseConsoleLifetime();

    private static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Get configurations for service registration
        var banSyncConfig = configuration.GetSection(BanSyncConfiguration.SectionName).Get<BanSyncConfiguration>()
            ?? throw new InvalidOperationException("BanSync configuration is missing");

        var discordConfig = configuration.GetSection(DiscordConfiguration.SectionName).Get<DiscordConfiguration>()
            ?? new DiscordConfiguration();

        // Removed GitHub configuration retrieval and exception as per user request

        // Database service
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string is missing");

        services.AddSingleton<IDatabaseService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<DatabaseService>>();
            return new DatabaseService(connectionString, logger, banSyncConfig.MaxRetryAttempts, banSyncConfig.RetryDelaySeconds);
        });

        // Steam service
        services.AddSingleton<ISteamService>(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
            var logger = provider.GetRequiredService<ILogger<SteamService>>();
            var cacheExpiration = TimeSpan.FromMinutes(banSyncConfig.CacheExpirationMinutes);
            return new SteamService(httpClient, logger, banSyncConfig.SteamAPIKey, cacheExpiration);
        });

        // Discord service
        services.AddSingleton<IDiscordService>(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
            var logger = provider.GetRequiredService<ILogger<DiscordService>>();
            return new DiscordService(httpClient, logger, discordConfig);
        });

        // File watcher service
        services.AddSingleton<IFileWatcherService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<FileWatcherService>>();
            return new FileWatcherService(banSyncConfig.OutputFile, logger);
        });

        // GitHub service
        services.AddSingleton<IGitHubService>(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
            var logger = provider.GetRequiredService<ILogger<GitHubService>>();
            return new GitHubService(httpClient, logger);
        });
    }

    private static async Task ValidateConfigurationAsync(IServiceProvider services)
    {
        logger.Info("Validating configuration...");

        // Validate BanSync configuration
        var banSyncOptions = services.GetRequiredService<IOptions<BanSyncConfiguration>>();
        var banSyncConfig = banSyncOptions.Value;

        if (string.IsNullOrWhiteSpace(banSyncConfig.OutputFile))
        {
            throw new InvalidOperationException("OutputFile configuration is required");
        }

        if (string.IsNullOrWhiteSpace(banSyncConfig.SteamAPIKey))
        {
            throw new InvalidOperationException("SteamAPIKey configuration is required");
        }

        if (!File.Exists(banSyncConfig.OutputFile))
        {
            logger.Warn("Output file does not exist, it will be created: {OutputFile}", banSyncConfig.OutputFile);
            
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(banSyncConfig.OutputFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                logger.Info("Created directory: {Directory}", directory);
            }

            // Create empty file
            await File.WriteAllTextAsync(banSyncConfig.OutputFile, string.Empty);
            logger.Info("Created output file: {OutputFile}", banSyncConfig.OutputFile);
        }

        // Test database connection
        var databaseService = services.GetRequiredService<IDatabaseService>();
        if (!await databaseService.TestConnectionAsync())
        {
            throw new InvalidOperationException("Database connection test failed");
        }

        logger.Info("Configuration validation completed successfully");
    }
}

// Configuration validators
public class BanSyncConfigurationValidator : IValidateOptions<BanSyncConfiguration>
{
    public ValidateOptionsResult Validate(string? name, BanSyncConfiguration options)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(options);

        if (!Validator.TryValidateObject(options, context, validationResults, true))
        {
            var errors = validationResults.Select(r => r.ErrorMessage).Where(e => e != null);
            return ValidateOptionsResult.Fail(errors!);
        }

        return ValidateOptionsResult.Success;
    }
}

public class DiscordConfigurationValidator : IValidateOptions<DiscordConfiguration>
{
    public ValidateOptionsResult Validate(string? name, DiscordConfiguration options)
    {
        if (options.Enabled && !options.WebhookUrls.Any())
        {
            return ValidateOptionsResult.Fail("Discord is enabled but no webhook URLs are configured");
        }

        return ValidateOptionsResult.Success;
    }
}

public class GitHubConfigurationValidator : IValidateOptions<GitHubConfiguration>
{
    public ValidateOptionsResult Validate(string? name, GitHubConfiguration options)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(options);

        if (!Validator.TryValidateObject(options, context, validationResults, true))
        {
            var errors = validationResults.Select(r => r.ErrorMessage).Where(e => e != null);
            return ValidateOptionsResult.Fail(errors!);
        }

        return ValidateOptionsResult.Success;
    }
}
