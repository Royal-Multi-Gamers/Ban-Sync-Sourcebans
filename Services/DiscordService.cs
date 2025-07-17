using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace BBR_Ban_Sync.Services;

public class DiscordService : IDiscordService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordService> _logger;
    private readonly DiscordConfiguration _config;

    public DiscordService(HttpClient httpClient, ILogger<DiscordService> logger, DiscordConfiguration config)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task SendBanNotificationAsync(string steamId64, string playerName, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled || !_config.WebhookUrls.Any())
        {
            _logger.LogDebug("Discord notifications are disabled or no webhook URLs configured");
            return;
        }

        if (string.IsNullOrWhiteSpace(steamId64) || string.IsNullOrWhiteSpace(playerName))
        {
            _logger.LogWarning("SteamID64 or player name is null or empty");
            return;
        }

        var embed = CreateBanEmbed(steamId64, playerName);
        var payload = new { embeds = new[] { embed } };

        await SendWebhookAsync(payload, cancellationToken);
    }

    public async Task SendBulkBanNotificationAsync(IEnumerable<(string steamId64, string playerName)> bans, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled || !_config.WebhookUrls.Any())
        {
            _logger.LogDebug("Discord notifications are disabled or no webhook URLs configured");
            return;
        }

        var banList = bans.ToList();
        if (!banList.Any())
        {
            _logger.LogDebug("No bans to send in bulk notification");
            return;
        }

        // Discord has a limit of 10 embeds per message
        const int maxEmbedsPerMessage = 10;
        var chunks = banList.Chunk(maxEmbedsPerMessage);

        foreach (var chunk in chunks)
        {
            var embeds = chunk.Select(ban => CreateBanEmbed(ban.steamId64, ban.playerName)).ToArray();
            var payload = new { embeds };

            await SendWebhookAsync(payload, cancellationToken);

            // Add a small delay between bulk messages to avoid rate limiting
            if (chunks.Count() > 1)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public async Task<bool> TestWebhookAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled || !_config.WebhookUrls.Any())
        {
            _logger.LogWarning("Discord notifications are disabled or no webhook URLs configured");
            return false;
        }

        var testEmbed = new
        {
            title = "Test Notification",
            description = "This is a test message from BBR-Ban-Sync to verify webhook connectivity.",
            color = _config.EmbedColor,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        var payload = new { embeds = new[] { testEmbed } };

        try
        {
            await SendWebhookAsync(payload, cancellationToken);
            _logger.LogInformation("Discord webhook test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord webhook test failed");
            return false;
        }
    }

    private object CreateBanEmbed(string steamId64, string playerName)
    {
        var description = $"Le joueur **{playerName}** avec le SteamID64 : **{steamId64}** est banni du serveur BattleBit Remastered.";
        
        if (!string.IsNullOrWhiteSpace(_config.ReclamationUrl))
        {
            description += $"\nPour toutes réclamations, aller sur : {_config.ReclamationUrl}";
        }

        return new
        {
            title = "Ban Notification",
            description,
            color = _config.EmbedColor,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            fields = new[]
            {
                new { name = "SteamID64", value = steamId64, inline = true },
                new { name = "Nom du joueur", value = playerName, inline = true }
            }
        };
    }

    private async Task SendWebhookAsync(object payload, CancellationToken cancellationToken = default)
    {
        var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var tasks = _config.WebhookUrls.Select(async url =>
        {
            try
            {
                _logger.LogDebug("Sending Discord webhook to: {Url}", MaskWebhookUrl(url));

                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Successfully sent Discord webhook to: {Url}", MaskWebhookUrl(url));
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Failed to send Discord webhook to {Url}. Status: {StatusCode}, Response: {Response}",
                        MaskWebhookUrl(url), response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Discord webhook to: {Url}", MaskWebhookUrl(url));
            }
        });

        await Task.WhenAll(tasks);
    }

    private static string MaskWebhookUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "***";

        try
        {
            var uri = new Uri(url);
            var pathSegments = uri.AbsolutePath.Split('/');
            
            if (pathSegments.Length >= 3)
            {
                // Mask the webhook token (last segment)
                pathSegments[^1] = "***";
                var maskedPath = string.Join("/", pathSegments);
                return $"{uri.Scheme}://{uri.Host}{maskedPath}";
            }
        }
        catch
        {
            // If URL parsing fails, return a generic mask
        }

        return "***";
    }
}
