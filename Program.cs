using System;
using System.IO;
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
    private static string connectionString = string.Empty;
    private static string outputFile = string.Empty;
    private static string steamAPIKey = string.Empty;
    private static int serverID;
    private static bool debugMode;
    private static System.Timers.Timer? timer;
    private static FileSystemWatcher? watcher;
    private static List<string> lastLines = new List<string>();

    static async Task Main(string[] args)
    {
        ConfigureNLog();
        EnsureLogDirectoryExists();
        LoadConfiguration();

        logger.Info("Application started.");

        if (debugMode)
        {
            logger.Info("Debug mode is enabled.");
        }

        if (!File.Exists(outputFile))
        {
            logger.Error($"File not found: {outputFile}");
            return;
        }

        // Initialize lastLines with the current content of the file
        lastLines = new List<string>(File.ReadAllLines(outputFile));

        // Execute database extraction at startup
        await SyncDatabaseToFile();

        // Create a dummy FileSystemEventArgs to call OnChanged at startup
        var dummyEventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(outputFile) ?? string.Empty, Path.GetFileName(outputFile));
        await OnChanged(dummyEventArgs);

        timer = new System.Timers.Timer(60000); // 1 minute
        timer.Elapsed += async (sender, e) => await SyncDatabaseToFile();
        timer.Start();

        if (debugMode)
        {
            logger.Info("Timer started.");
        }

        SetupFileWatcher();

        // Ajouter un timer pour vérifier les nouvelles versions toutes les heures
        var releaseCheckTimer = new System.Timers.Timer(3600000); // 1 heure
        releaseCheckTimer.Elapsed += async (sender, e) => await CheckForNewRelease();
        releaseCheckTimer.Start();

        if (debugMode)
        {
            logger.Info("Release check timer started.");
        }

        await Task.Delay(-1); // Keep the application running
    }
    private static async Task CheckForNewRelease()
    {
        string repoOwner = "Royal-Multi-Gamers";
        string repoName = "Ban-Sync-Sourcebans";
        string currentVersion = "v0.0.1"; // Par exemple, "v1.0.0"

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("User-Agent", "C# App");

            var response = await client.GetStringAsync($"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest");
            var jsonResponse = JObject.Parse(response);
            var latestVersion = jsonResponse["tag_name"]?.ToString();

            if (!string.IsNullOrEmpty(latestVersion) && latestVersion != currentVersion)
            {
                logger.Info($"Nouvelle version disponible : {latestVersion}");
                // Vous pouvez ajouter ici des actions supplémentaires, comme notifier l'utilisateur ou télécharger la nouvelle version.
            }
            else
            {
                logger.Info("Aucune nouvelle version disponible.");
            }
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

    private static bool discordWebhookEnabled;
    private static List<string> discordWebhookUrls = new List<string>();

    private static void LoadConfiguration()
    {
        const string configFilePath = "config.json";

        if (!File.Exists(configFilePath))
        {
            CreateDefaultConfigFile(configFilePath);
        }

        var config = JObject.Parse(File.ReadAllText(configFilePath));

        var connectionStringConfig = config["ConnectionString"];
        if (connectionStringConfig != null)
        {
            connectionString = $"server={connectionStringConfig["Server"]};uid={connectionStringConfig["Uid"]};pwd={connectionStringConfig["Pwd"]};database={connectionStringConfig["Database"]}";
        }

        var outputFilePath = config["OutputFile"]?.ToString() ?? string.Empty;
        outputFile = Environment.GetEnvironmentVariable(outputFilePath) ?? outputFilePath;
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

    private static bool isProcessing = false;

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

        List<string> currentLines = new List<string>();

        for (int i = 0; i < 5; i++) // Retry up to 5 times
        {
            try
            {
                using (var stream = new FileStream(outputFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    var currentLinesContent = await reader.ReadToEndAsync();
                    currentLines = new List<string>(currentLinesContent.Split(new[] { Environment.NewLine }, StringSplitOptions.None));
                }
                break; // Exit loop if successful
            }
            catch (IOException ex)
            {
                if (debugMode)
                {
                    logger.Error($"IOException encountered: {ex.Message}");
                }
                await Task.Delay(100); // Wait 100ms before retrying
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

        foreach (var line in currentLines)
        {
            if (!string.IsNullOrWhiteSpace(line) && !lastLines.Contains(line))
            {
                if (!debugMode)
                {
                    logger.Info($"New line detected: {line}");
                }
                await AddSteamIDToDatabase(line);
            }
        }

        // Update lastLines with the current content of the file
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

        using (var client = new HttpClient())
        {
            var embed = new
            {
                title = "Ban Notification",
                description = $"Le joueur **{playerName}** avec le SteamID64 : **{steamID64}** est banni du serveur BattleBit Remastered.\nPour toutes réclamations, aller sur : https://www.clan-rmg.com/playerpanel/",
                color = 16711680 // Rouge
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
    }


    private static async Task AddSteamIDToDatabase(string steamID64)
    {
        if (debugMode)
        {
            logger.Info($"Adding SteamID {steamID64} to database...");
        }

        var steamID2 = await ConvertSteamID64ToSteamID2(steamID64);

        if (debugMode)
        {
            logger.Info($"Converted SteamID64 {steamID64} to SteamID2 {steamID2}");
        }

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
            using (var command = new MySqlCommand(query, connection))
            {
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
        }

        if (!debugMode)
        {
            logger.Info($"Added SteamID {steamID64} as {steamID2} to database.");
        }

        // Send Discord webhook notification
        await SendDiscordWebhook(steamID64, name);
    }

    private static async Task<bool> IsSteamIDInDatabase(string steamID2)
    {
        using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT COUNT(*) FROM sb_bans WHERE authid = @authid AND RemoveType IS NULL";
            using (var command = new MySqlCommand(query, connection))
            {
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
        }
    }

    private static async Task<string?> ConvertSteamID64ToSteamID2(string steamID64)
    {
        using (var client = new HttpClient())
        {
            var response = await client.GetStringAsync($"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={steamAPIKey}&steamids={steamID64}");
            var jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response);
            var players = jsonResponse?.response?.players as JArray;

            if (players == null || players.Count == 0)
            {
                logger.Error($"No player found for SteamID64: {steamID64}");
                return null; // Or handle it as per your application's requirement
            }

            var player = players[0];

            if (!long.TryParse(steamID64, out long steamID64Long))
            {
                throw new ArgumentException("Invalid SteamID64 format");
            }

            long z = (steamID64Long - 76561197960265728) / 2;
            int y = (steamID64Long - 76561197960265728) % 2 == 0 ? 0 : 1;

            string steamID2 = $"STEAM_0:{y}:{z}";
            return steamID2;
        }
    }

    private static async Task<string?> GetSteamName(string steamID64)
    {
        using (var client = new HttpClient())
        {
            var response = await client.GetStringAsync($"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={steamAPIKey}&steamids={steamID64}");
            var jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response);
            var players = jsonResponse?.response?.players as JArray;

            if (players == null || players.Count == 0)
            {
                logger.Error($"No player found for SteamID64: {steamID64}");
                return null; // Or handle it as per your application's requirement
            }

            var player = players[0];
            string? playerName = player?["personaname"]?.ToString();

            if (string.IsNullOrEmpty(playerName))
            {
                logger.Error($"Player name not found for SteamID64: {steamID64}");
                return null; // Or handle it as per your application's requirement
            }

            return playerName;
        }
    }

    private static async Task SyncDatabaseToFile()
    {
        if (debugMode)
        {
            logger.Info("Synchronizing database to file...");
        }

        // Lire les SteamID64 actuels du fichier
        var currentSteamIDs = new HashSet<string>();
        if (File.Exists(outputFile))
        {
            currentSteamIDs = new HashSet<string>(await File.ReadAllLinesAsync(outputFile));
        }

        using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT DISTINCT authid FROM sb_bans WHERE RemoveType IS NULL AND (sid = 0 OR sid = @sid)";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@sid", serverID);

                if (debugMode)
                {
                    logger.Info($"Executing query: {query}");
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    var newSteamIDs = new HashSet<string>();
                    while (await reader.ReadAsync())
                    {
                        var steamID2 = reader.GetString("authid");
                        var steamID64 = await ConvertSteamID2ToSteamID64(steamID2);
                        newSteamIDs.Add(steamID64);
                    }

                    // Comparer les SteamID64 actuels avec ceux de la base de données
                    var addedSteamIDs = newSteamIDs.Except(currentSteamIDs).ToList();

                    if (debugMode)
                    {
                        logger.Info($"Writing {newSteamIDs.Count} unique SteamIDs to file.");
                    }

                    await File.WriteAllLinesAsync(outputFile, newSteamIDs);

                    // Envoyer des notifications pour les nouveaux SteamID64
                    foreach (var steamID64 in addedSteamIDs)
                    {
                        var playerName = await GetSteamName(steamID64);
                        if (playerName != null)
                        {
                            await SendDiscordWebhook(steamID64, playerName);
                        }
                    }
                }

                if (debugMode)
                {
                    logger.Info($"Query executed successfully: {query}");
                }
            }
        }

        if (debugMode)
        {
            logger.Info("Synchronized database to file.");
        }
    }

    private static Task<string> ConvertSteamID2ToSteamID64(string steamID2)
    {
        // Example SteamID2: STEAM_0:1:12345678
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
