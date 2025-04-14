using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using System.Timers;
using System.Data;
using Newtonsoft.Json.Linq;
using NLog;

class Program
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private const string currentVersion = "v0.0.4";
    private static string connectionString = string.Empty;
    private static string outputFile = string.Empty;
    private static string steamAPIKey = string.Empty;
    private static int serverID;
    private static bool debugMode;
    private static System.Timers.Timer? timer;
    private static FileSystemWatcher? watcher = new();
    private static List<string> lastLines = new();
    private static readonly HttpClient client = new();
    private static bool discordWebhookEnabled;
    private static List<string> discordWebhookUrls = new();
    private static bool isProcessing = false;

    static async Task Main()
    {
        ConfigureNLog();
        EnsureLogDirectoryExists();
        LoadConfiguration();

        logger.Info("Application started.");
        logger.Info($"Current software version: {currentVersion}");

        if (debugMode)
        {
            logger.Info("Debug mode is enabled.");
        }

        if (!File.Exists(outputFile))
        {
            logger.Error($"File not found: {outputFile}");
            return;
        }

        lastLines = new List<string>(File.ReadAllLines(outputFile));
        await SyncDatabaseToFile();

        var dummyEventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(outputFile) ?? string.Empty, Path.GetFileName(outputFile));
        await OnChanged(dummyEventArgs);

        timer = new System.Timers.Timer(60000);
        timer.Elapsed += async (sender, e) => await SyncDatabaseToFile();
        timer.Start();

        if (debugMode)
        {
            logger.Info("Timer started.");
        }

        SetupFileWatcher();
        await CheckForNewRelease();

        var releaseCheckTimer = new System.Timers.Timer(3600000);
        releaseCheckTimer.Elapsed += async (sender, e) => await CheckForNewRelease();
        releaseCheckTimer.Start();

        if (debugMode)
        {
            logger.Info("Release check timer started.");
        }

        await Task.Delay(-1);
    }

    private static async Task CheckForNewRelease()
    {
        string repoOwner = "Royal-Multi-Gamers";
        string repoName = "Ban-Sync-Sourcebans";

        client.DefaultRequestHeaders.Add("User-Agent", "C# App");

        var response = await client.GetStringAsync($"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest");
        var jsonResponse = JObject.Parse(response);
        var latestVersion = jsonResponse["tag_name"]?.ToString();

        if (!string.IsNullOrEmpty(latestVersion) && latestVersion != currentVersion)
        {
            logger.Info($"Nouvelle version disponible : {latestVersion}");
        }
        else
        {
            logger.Info("Aucune nouvelle version disponible.");
        }
    }

    private static void EnsureLogDirectoryExists()
    {
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }
    }

    private static void LoadConfiguration()
    {
        const string configFilePath = "config.json";

        if (!File.Exists(configFilePath))
        {
            CreateDefaultConfigFile(configFilePath);
        }

        var config = JObject.Parse(File.ReadAllText(configFilePath));

        connectionString = $"server={config["ConnectionString"]?["Server"]};uid={config["ConnectionString"]?["Uid"]};pwd={config["ConnectionString"]?["Pwd"]};database={config["ConnectionString"]?["Database"]}";
        outputFile = Environment.GetEnvironmentVariable(config["OutputFile"]?.ToString() ?? string.Empty) ?? config["OutputFile"]?.ToString() ?? string.Empty;
        steamAPIKey = config["SteamAPIKey"]?.ToString() ?? string.Empty;
        serverID = (int)(config["ServerID"] ?? 0);
        debugMode = (bool)(config["DebugMode"] ?? false);

        var discordWebhookConfig = config["DiscordWebhook"];
        if (discordWebhookConfig != null)
        {
            discordWebhookEnabled = (bool)(discordWebhookConfig["Enabled"] ?? false);
            discordWebhookUrls = discordWebhookConfig["Urls"]?.ToObject<List<string>>() ?? new List<string>();
        }
    }

    private static void CreateDefaultConfigFile(string configFilePath)
    {
        var defaultConfig = new JObject
        {
            ["ConnectionString"] = new JObject
            {
                ["Server"] = "localhost",
                ["Uid"] = "databaseuser",
                ["Pwd"] = "userpassword",
                ["Database"] = "databasename"
            },
            ["OutputFile"] = @"C:\testps\Blacklist.txt",
            ["SteamAPIKey"] = "steamapikey",
            ["ServerID"] = 5,
            ["DebugMode"] = true,
            ["DiscordWebhook"] = new JObject
            {
                ["Enabled"] = true,
                ["Urls"] = new JArray
                {
                    "https://discord.com/api/webhooks/your_webhook_id/your_webhook_token"
                }
            }
        };

        File.WriteAllText(configFilePath, defaultConfig.ToString());
    }

    private static void SetupFileWatcher()
    {
        watcher = new FileSystemWatcher(Path.GetDirectoryName(outputFile) ?? string.Empty)
        {
            Filter = Path.GetFileName(outputFile),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };

        watcher.Changed += async (sender, e) => await OnChanged(e);
        watcher.Created += async (sender, e) => await OnChanged(e);
        watcher.Renamed += async (sender, e) => await OnChanged(e);

        watcher.EnableRaisingEvents = true;

        if (debugMode)
        {
            logger.Info("File watcher started.");
        }
    }

    private static async Task OnChanged(FileSystemEventArgs e)
    {
        if (isProcessing)
        {
            if (debugMode)
            {
                logger.Info("Ignoring duplicate event.");
            }
            return;
        }

        isProcessing = true;

        if (debugMode)
        {
            logger.Info($"File changed: {e.FullPath}");
        }

        List<string> currentLines = new();

        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var stream = new FileStream(outputFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var currentLinesContent = await reader.ReadToEndAsync();
                currentLines = new List<string>(currentLinesContent.Split(new[] { Environment.NewLine }, StringSplitOptions.None));
                break;
            }
            catch (IOException ex)
            {
                if (debugMode)
                {
                    logger.Error($"IOException encountered: {ex.Message}");
                }
                await Task.Delay(100);
            }
        }

        if (currentLines.Count == 0)
        {
            logger.Error("Failed to read the file after multiple attempts.");
            isProcessing = false;
            return;
        }

        if (debugMode)
        {
            logger.Info("Comparing lines...");
            logger.Info($"Last lines count: {lastLines.Count}");
            logger.Info($"Current lines count: {currentLines.Count}");
        }

        var newLines = currentLines.Except(lastLines).ToList();
        var removedLines = lastLines.Except(currentLines).ToList();

        if (newLines.Count == 0 && removedLines.Count == 0)
        {
            if (debugMode)
            {
                logger.Info("No new lines detected.");
            }
            isProcessing = false;
            return;
        }

        foreach (var line in newLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                if (!debugMode)
                {
                    logger.Info($"New line detected: {line}");
                }
                await AddSteamIDToDatabase(line);
            }
        }

        lastLines = currentLines;

        if (debugMode)
        {
            logger.Info("Finished processing file changes.");
        }

        isProcessing = false;
    }

    private static async Task SendDiscordWebhook(string steamID64, string playerName)
    {
        if (!discordWebhookEnabled || discordWebhookUrls.Count == 0)
        {
            return;
        }

        using var client = new HttpClient();
        var embed = new
        {
            title = "Ban Notification",
            description = $"Le joueur **{playerName}** avec le SteamID64 : **{steamID64}** est banni du serveur BattleBit Remastered.\nPour toutes réclamations, aller sur : https://www.clan-rmg.com/playerpanel/",
            color = 16711680
        };

        var payload = new
        {
            embeds = new[] { embed }
        };

        var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");

        foreach (var url in discordWebhookUrls)
        {
            await client.PostAsync(url, content);
        }
    }

    private static async Task AddSteamIDToDatabase(string steamID64)
    {
        if (debugMode)
        {
            logger.Info($"Checking if SteamID {steamID64} needs to be added to the database...");
        }

        var steamID2 = ConvertSteamID64ToSteamID2(steamID64);
        if (steamID2 == null)
        {
            if (debugMode)
            {
                logger.Info($"SteamID2 is null for SteamID64 {steamID64}");
            }
            return;
        }

        if (await IsSteamIDInDatabase(steamID2))
        {
            if (debugMode)
            {
                logger.Info($"SteamID {steamID64} already exists in the database with RemoveType NULL.");
            }
            return;
        }

        var name = await GetSteamName(steamID64);
        if (name == null)
        {
            if (debugMode)
            {
                logger.Info($"Player name is null for SteamID64 {steamID64}");
            }
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var query = "INSERT INTO sb_bans (authid, name, created, ends, length, sid, ip) VALUES (@authid, @name, @created, @ends, @length, @sid, @ip)";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@authid", steamID2);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@created", timestamp);
            command.Parameters.AddWithValue("@ends", timestamp);
            command.Parameters.AddWithValue("@length", 0);
            command.Parameters.AddWithValue("@sid", serverID);
            command.Parameters.AddWithValue("@ip", "");

            if (debugMode)
            {
                logger.Info($"Executing query: {query}");
            }

            await command.ExecuteNonQueryAsync();

            if (debugMode)
            {
                logger.Info($"Query executed successfully: {query}");
            }
        }

        if (!debugMode)
        {
            logger.Info($"Added SteamID {steamID64} as {steamID2} to database.");
        }

        await SendDiscordWebhook(steamID64, name);
    }

    private static async Task<bool> IsSteamIDInDatabase(string steamID2)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var query = "SELECT COUNT(*) FROM sb_bans WHERE authid = @authid AND RemoveType IS NULL";
        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@authid", steamID2);

        if (debugMode)
        {
            logger.Info($"Executing query: {query}");
        }

        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);

        if (debugMode)
        {
            logger.Info($"Query executed successfully: {query}");
            logger.Info($"Count of SteamID2 {steamID2} in database: {count}");
        }

        return count > 0;
    }

    private static string? ConvertSteamID64ToSteamID2(string steamID64)
    {
        if (!long.TryParse(steamID64, out long steamID64Long))
        {
            throw new ArgumentException("Invalid SteamID64 format");
        }

        long z = (steamID64Long - 76561197960265728) / 2;
        int y = (steamID64Long - 76561197960265728) % 2 == 0 ? 0 : 1;

        return $"STEAM_0:{y}:{z}";
    }

    private static async Task<string?> GetSteamName(string steamID64)
    {
        try
        {
            var response = await client.GetStringAsync($"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={steamAPIKey}&steamids={steamID64}");
            var jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response);
            var players = jsonResponse?.response?.players as JArray;

            if (players == null || players.Count == 0)
            {
                logger.Error($"No player found for SteamID64: {steamID64}");
                return null;
            }

            var player = players[0];
            return player?["personaname"]?.ToString();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == (HttpStatusCode)429)
        {
            logger.Warn("API rate limit exceeded. Skipping this request.");
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred while fetching the Steam name.");
            throw;
        }
    }

    private static async Task SyncDatabaseToFile()
    {
        if (debugMode)
        {
            logger.Info("Synchronizing database to file...");
        }

        var currentSteamIDs = new HashSet<string>();
        if (File.Exists(outputFile))
        {
            currentSteamIDs = new HashSet<string>(await File.ReadAllLinesAsync(outputFile));
        }

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var query = "SELECT DISTINCT authid FROM sb_bans WHERE RemoveType IS NULL AND (sid = 0 OR sid = @sid)";
        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@sid", serverID);

        if (debugMode)
        {
            logger.Info($"Executing query: {query}");
        }

        await using var reader = await command.ExecuteReaderAsync();
        var newSteamIDs = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            var steamID2 = reader.GetString("authid");
            var steamID64 = await ConvertSteamID2ToSteamID64(steamID2);
            newSteamIDs.Add(steamID64);
        }

        var addedSteamIDs = newSteamIDs.Except(currentSteamIDs).ToList();

        if (debugMode)
        {
            logger.Info($"Writing {newSteamIDs.Count} unique SteamIDs to file.");
        }

        if (!currentSteamIDs.SetEquals(newSteamIDs))
        {
            await File.WriteAllLinesAsync(outputFile, newSteamIDs);

            foreach (var steamID64 in addedSteamIDs)
            {
                var playerName = await GetSteamName(steamID64);
                if (playerName != null)
                {
                    await SendDiscordWebhook(steamID64, playerName);
                }
            }

            if (debugMode)
            {
                logger.Info("Synchronized database to file.");
            }
        }
        else
        {
            if (debugMode)
            {
                logger.Info("No changes detected in the file. Skipping write operation.");
            }
        }
    }

    private static Task<string> ConvertSteamID2ToSteamID64(string steamID2)
    {
        var parts = steamID2.Split(':');
        if (parts.Length != 3 || !parts[0].Equals("STEAM_0"))
        {
            throw new ArgumentException("Invalid SteamID2 format");
        }

        var y = int.Parse(parts[1]);
        var z = long.Parse(parts[2]);

        var steamID64 = 76561197960265728 + z * 2 + y;
        return Task.FromResult(steamID64.ToString());
    }

    private static void ConfigureNLog()
    {
        LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration("NLog.config");
        LogManager.ConfigurationChanged += (sender, e) =>
        {
            if (e.ActivatedConfiguration != null)
            {
                logger.Info("NLog configuration reloaded successfully.");
            }
            else
            {
                logger.Error("NLog configuration reload failed.");
            }
        };
        LogManager.ThrowConfigExceptions = true;
        LogManager.ThrowExceptions = true;
    }
}
