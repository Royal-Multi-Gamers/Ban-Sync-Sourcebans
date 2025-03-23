﻿using System;
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
        Console.WriteLine("Application started.");

        if (debugMode)
        {
            logger.Info("Debug mode is enabled.");
            Console.WriteLine("Debug mode is enabled.");
        }

        if (!File.Exists(outputFile))
        {
            logger.Error($"File not found: {outputFile}");
            Console.WriteLine($"File not found: {outputFile}");
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
            Console.WriteLine("Timer started.");
        }

        SetupFileWatcher();

        await Task.Delay(-1); // Keep the application running
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
            ["DebugMode"] = true
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
            Console.WriteLine("File watcher started.");
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
                Console.WriteLine("Ignoring duplicate event.");
            }
            return;
        }

        isProcessing = true;

        if (debugMode)
        {
            logger.Info($"File changed: {e.FullPath}");
            Console.WriteLine($"File changed: {e.FullPath}");
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
                    Console.WriteLine($"IOException encountered: {ex.Message}");
                }
                await Task.Delay(100); // Wait 100ms before retrying
            }
        }

        if (currentLines.Count == 0)
        {
            logger.Error("Failed to read the file after multiple attempts.");
            Console.WriteLine("Failed to read the file after multiple attempts.");
            isProcessing = false;
            return;
        }

        if (debugMode)
        {
            logger.Info("Comparing lines...");
            Console.WriteLine("Comparing lines...");
            logger.Info($"Last lines count: {lastLines.Count}");
            Console.WriteLine($"Last lines count: {lastLines.Count}");
            logger.Info($"Current lines count: {currentLines.Count}");
            Console.WriteLine($"Current lines count: {currentLines.Count}");
        }

        foreach (var line in currentLines)
        {
            if (!string.IsNullOrWhiteSpace(line) && !lastLines.Contains(line))
            {
                if (debugMode)
                {
                    logger.Info($"New line detected: {line}");
                    Console.WriteLine($"New line detected: {line}");
                }
                await AddSteamIDToDatabase(line);
            }
        }

        // Update lastLines with the current content of the file
        lastLines = currentLines;

        if (debugMode)
        {
            logger.Info("Finished processing file changes.");
            Console.WriteLine("Finished processing file changes.");
        }

        isProcessing = false;
    }

    private static async Task AddSteamIDToDatabase(string steamID64)
    {
        if (debugMode)
        {
            logger.Info($"Adding SteamID {steamID64} to database...");
            Console.WriteLine($"Adding SteamID {steamID64} to database...");
        }

        var steamID2 = await ConvertSteamID64ToSteamID2(steamID64);

        if (debugMode)
        {
            logger.Info($"Converted SteamID64 {steamID64} to SteamID2 {steamID2}");
            Console.WriteLine($"Converted SteamID64 {steamID64} to SteamID2 {steamID2}");
        }

        if (steamID2 == null)
        {
            if (debugMode)
            {
                logger.Info($"SteamID2 is null for SteamID64 {steamID64}");
                Console.WriteLine($"SteamID2 is null for SteamID64 {steamID64}");
            }
            return;
        }

        if (await IsSteamIDInDatabase(steamID2))
        {
            if (debugMode)
            {
                logger.Info($"SteamID {steamID64} already exists in the database with RemoveType NULL.");
                Console.WriteLine($"SteamID {steamID64} already exists in the database with RemoveType NULL.");
            }
            return;
        }

        var name = await GetSteamName(steamID64);
        if (name == null)
        {
            if (debugMode)
            {
                logger.Info($"Player name is null for SteamID64 {steamID64}");
                Console.WriteLine($"Player name is null for SteamID64 {steamID64}");
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
                    Console.WriteLine($"Executing query: {query}");
                }

                await command.ExecuteNonQueryAsync();

                if (debugMode)
                {
                    logger.Info($"Query executed successfully: {query}");
                    Console.WriteLine($"Query executed successfully: {query}");
                }
            }
        }

        if (debugMode)
        {
            logger.Info($"Added SteamID {steamID64} as {steamID2} to database.");
            Console.WriteLine($"Added SteamID {steamID64} as {steamID2} to database.");
        }
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
                    Console.WriteLine($"Executing query: {query}");
                }

                var count = (long)(await command.ExecuteScalarAsync() ?? 0L);

                if (debugMode)
                {
                    logger.Info($"Query executed successfully: {query}");
                    Console.WriteLine($"Query executed successfully: {query}");
                    logger.Info($"Count of SteamID2 {steamID2} in database: {count}");
                    Console.WriteLine($"Count of SteamID2 {steamID2} in database: {count}");
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
                Console.WriteLine($"No player found for SteamID64: {steamID64}");
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
                Console.WriteLine($"No player found for SteamID64: {steamID64}");
                return null; // Or handle it as per your application's requirement
            }

            var player = players[0];
            string? playerName = player?["personaname"]?.ToString();

            if (string.IsNullOrEmpty(playerName))
            {
                logger.Error($"Player name not found for SteamID64: {steamID64}");
                Console.WriteLine($"Player name not found for SteamID64: {steamID64}");
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
            Console.WriteLine("Synchronizing database to file...");
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
                    Console.WriteLine($"Executing query: {query}");
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    var lines = new HashSet<string>();
                    while (await reader.ReadAsync())
                    {
                        var steamID2 = reader.GetString("authid");
                        var steamID64 = await ConvertSteamID2ToSteamID64(steamID2);
                        lines.Add(steamID64);
                    }

                    if (debugMode)
                    {
                        logger.Info($"Writing {lines.Count} unique SteamIDs to file.");
                        Console.WriteLine($"Writing {lines.Count} unique SteamIDs to file.");
                    }

                    await File.WriteAllLinesAsync(outputFile, lines);
                }

                if (debugMode)
                {
                    logger.Info($"Query executed successfully: {query}");
                    Console.WriteLine($"Query executed successfully: {query}");
                }
            }
        }

        if (debugMode)
        {
            logger.Info("Synchronized database to file.");
            Console.WriteLine("Synchronized database to file.");
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
                Console.WriteLine("NLog configuration reloaded successfully.");
            }
            else
            {
                Console.WriteLine("NLog configuration reload failed.");
            }
        };
        LogManager.ThrowConfigExceptions = true;
        LogManager.ThrowExceptions = true;
    }
}
