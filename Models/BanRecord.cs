namespace BBR_Ban_Sync.Models;

public class BanRecord
{
    public string AuthId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Created { get; set; }
    public long Ends { get; set; }
    public int Length { get; set; }
    public int ServerId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string? RemoveType { get; set; }
    public string Reason { get; set; } = "Banned by Ban Sync System";
}

public class SteamPlayer
{
    public string SteamId64 { get; set; } = string.Empty;
    public string SteamId2 { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class SteamApiResponse
{
    public SteamResponse? Response { get; set; }
}

public class SteamResponse
{
    public List<SteamPlayerSummary>? Players { get; set; }
}

public class SteamPlayerSummary
{
    public string? SteamId { get; set; }
    public string? PersonaName { get; set; }
}
