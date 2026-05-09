using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ArcadeFrontend;

public sealed class FrontendAssetCacheManager
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private const string MrSharkyBaseUrl = "https://mrsharky.com";

    private static readonly string[] ClearLogoRegionOrder =
    [
        "USA",
        "NORTH_AMERICA",
        "WORLD",
        "EUROPE",
        "OCEANIA",
        "JAPAN",
    ];

    private readonly FrontendRuntimeStore _runtimeStore;
    private readonly string _masterDatabasePath;

    public FrontendAssetCacheManager(FrontendRuntimeStore runtimeStore, string masterDatabasePath)
    {
        _runtimeStore = runtimeStore;
        _masterDatabasePath = masterDatabasePath;
    }

    public int SyncAssets(string systemId, IReadOnlyCollection<string> gameKeys, Action<int, int, string>? onProgress = null)
    {
        if (gameKeys.Count == 0)
        {
            return 0;
        }

        using var masterConnection = new SqliteConnection($"Data Source={_masterDatabasePath};Mode=ReadOnly");
        masterConnection.Open();

        var normalizedKeys = gameKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var cachedGames = 0;
        for (var index = 0; index < normalizedKeys.Count; index++)
        {
            var gameKey = normalizedKeys[index];
            onProgress?.Invoke(index + 1, normalizedKeys.Count, gameKey);
            SyncGameAssets(masterConnection, systemId, gameKey);
            cachedGames++;
        }

        return cachedGames;
    }

    private void SyncGameAssets(SqliteConnection masterConnection, string systemId, string gameKey)
    {
        var candidates = LoadCandidates(masterConnection, gameKey);
        var websiteTheme = LoadWebsiteThemeCandidate(masterConnection, gameKey);
        var selections = SelectAssets(systemId, candidates, websiteTheme);
        _runtimeStore.ReplaceCachedAssets(systemId, gameKey, selections);
    }

    private List<CachedAssetRecord> SelectAssets(string systemId, IReadOnlyList<AssetCandidate> candidates, WebsiteThemeCandidate? websiteTheme)
    {
        var selected = new List<CachedAssetRecord>();

        var clearLogo = SelectClearLogo(candidates);
        if (clearLogo != null)
        {
            var record = BuildCachedAssetRecord(systemId, clearLogo, "clear_logo", 0, true);
            if (record != null)
            {
                selected.Add(record);
            }
        }

        var poster = SelectPoster(candidates);
        if (poster != null)
        {
            var record = BuildCachedAssetRecord(systemId, poster, "poster", 0, true);
            if (record != null)
            {
                selected.Add(record);
            }
        }

        var screenshots = SelectScreenshots(candidates);
        for (var index = 0; index < screenshots.Count; index++)
        {
            var record = BuildCachedAssetRecord(systemId, screenshots[index], "screenshot", index, index == 0);
            if (record != null)
            {
                selected.Add(record);
            }
        }

        var fanarts = SelectFanarts(candidates);
        for (var index = 0; index < fanarts.Count; index++)
        {
            var record = BuildCachedAssetRecord(systemId, fanarts[index], "fanart", index, index == 0);
            if (record != null)
            {
                selected.Add(record);
            }
        }

        if (websiteTheme != null)
        {
            var themeRecord = BuildWebsiteThemeRecord(systemId, websiteTheme);
            if (themeRecord != null)
            {
                selected.Add(themeRecord);
            }
        }

        return selected;
    }

    private CachedAssetRecord? BuildWebsiteThemeRecord(string systemId, WebsiteThemeCandidate candidate)
    {
        var cachePath = EnsureDownloadedBinaryAsset(
            systemId,
            candidate.GameSlug,
            "hyperspin",
            candidate.DownloadUrl,
            ".zip",
            requireExistsCheck: true);
        if (cachePath == null)
        {
            return null;
        }

        return new CachedAssetRecord
        {
            CachedAssetId = Guid.NewGuid().ToString("N"),
            SystemId = systemId,
            GameKey = candidate.GameKey,
            ReleaseKey = null,
            AssetRole = "hyperspin_theme",
            SourceSystem = "mrsharky",
            SourceReference = candidate.DownloadUrl,
            RegionCode = null,
            LanguageCode = null,
            CachePath = cachePath,
            SortOrder = 0,
            SelectedByDefault = true,
        };
    }

    private CachedAssetRecord? BuildCachedAssetRecord(string systemId, AssetCandidate candidate, string assetRole, int sortOrder, bool selectedByDefault)
    {
        var cachePath = EnsureDownloadedAsset(systemId, candidate.GameKey, assetRole, sortOrder, candidate);
        if (cachePath == null)
        {
            return null;
        }

        return new CachedAssetRecord
        {
            CachedAssetId = Guid.NewGuid().ToString("N"),
            SystemId = systemId,
            GameKey = candidate.GameKey,
            ReleaseKey = candidate.ReleaseKey,
            AssetRole = assetRole,
            SourceSystem = candidate.SourceSystem,
            SourceReference = candidate.PathOrUrl,
            RegionCode = candidate.RegionCode,
            LanguageCode = candidate.LanguageCode,
            CachePath = cachePath,
            SortOrder = sortOrder,
            SelectedByDefault = selectedByDefault,
        };
    }

    private string? EnsureDownloadedAsset(string systemId, string gameKey, string assetRole, int sortOrder, AssetCandidate candidate)
    {
        var sourceReference = candidate.PathOrUrl;
        var roleName = assetRole == "screenshot" ? $"screenshot-{sortOrder + 1:00}" : assetRole.Replace('_', '-');

        foreach (var sourceUrl in ResolveSourceUrls(candidate))
        {
            try
            {
                return EnsureDownloadedBinaryAsset(
                    systemId,
                    GetCacheSlug(gameKey),
                    roleName,
                    sourceUrl,
                    DetectAssetExtension(sourceReference),
                    requireExistsCheck: false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.Error.WriteLine($"Asset not found, skipping URL: {sourceUrl}");
            }
        }

        Console.Error.WriteLine($"All asset URLs failed for {candidate.SourceSystem}:{candidate.AssetType}:{candidate.PathOrUrl}");
        return null;
    }

    private static string? EnsureDownloadedBinaryAsset(
        string systemId,
        string gameSlug,
        string roleName,
        string sourceUrl,
        string? defaultExtension,
        bool requireExistsCheck)
    {
        var cacheDirectory = Path.Combine(FrontendPaths.GetAssetCacheDirectory(), systemId, gameSlug);
        Directory.CreateDirectory(cacheDirectory);

        var fingerprint = ComputeShortHash(sourceUrl);
        var existingPath = FindExistingCachedAsset(cacheDirectory, roleName, fingerprint);
        if (existingPath != null)
        {
            return existingPath;
        }

        if (requireExistsCheck && !RemoteAssetExists(sourceUrl))
        {
            return null;
        }

        var bytes = HttpClient.GetByteArrayAsync(sourceUrl).GetAwaiter().GetResult();
        var extension = DetectImageExtension(bytes) ?? NormalizeExtension(defaultExtension) ?? ".bin";
        var sourceFileName = $"{roleName}__{fingerprint}{extension}";
        var cachePath = Path.Combine(cacheDirectory, sourceFileName);
        File.WriteAllBytes(cachePath, bytes);
        return cachePath;
    }

    private static string? FindExistingCachedAsset(string cacheDirectory, string roleName, string fingerprint)
    {
        var pattern = $"{roleName}__{fingerprint}.*";
        return Directory
            .EnumerateFiles(cacheDirectory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static AssetCandidate? SelectClearLogo(IReadOnlyList<AssetCandidate> candidates)
    {
        return candidates
            .Where(candidate => IsClearLogo(candidate))
            .OrderBy(candidate => GetSourceRank(candidate.SourceSystem))
            .ThenBy(candidate => GetClearLogoRegionRank(candidate.RegionCode))
            .ThenBy(candidate => candidate.PriorityRank)
            .ThenBy(candidate => candidate.PathOrUrl, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static AssetCandidate? SelectPoster(IReadOnlyList<AssetCandidate> candidates)
    {
        return candidates
            .Where(candidate => IsPoster(candidate))
            .OrderBy(candidate => GetSourceRank(candidate.SourceSystem))
            .ThenBy(candidate => GetPosterTypeRank(candidate))
            .ThenBy(candidate => GetClearLogoRegionRank(candidate.RegionCode))
            .ThenBy(candidate => candidate.PriorityRank)
            .ThenBy(candidate => candidate.PathOrUrl, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static List<AssetCandidate> SelectScreenshots(IReadOnlyList<AssetCandidate> candidates)
    {
        return candidates
            .Where(candidate => IsScreenshot(candidate))
            .DistinctBy(candidate => $"{candidate.SourceSystem}|{candidate.PathOrUrl}")
            .OrderBy(candidate => GetSourceRank(candidate.SourceSystem))
            .ThenBy(candidate => GetScreenshotTypeRank(candidate))
            .ThenBy(candidate => GetClearLogoRegionRank(candidate.RegionCode))
            .ThenBy(candidate => candidate.PriorityRank)
            .ThenBy(candidate => candidate.PathOrUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsClearLogo(AssetCandidate candidate)
    {
        return string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(candidate.AssetType, "Clear Logo", StringComparison.OrdinalIgnoreCase)
            : string.Equals(candidate.AssetType, "clearlogo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPoster(AssetCandidate candidate)
    {
        if (string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(candidate.AssetType, "Fanart - Box - Front", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.AssetType, "Box - Front", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(candidate.AssetType, "boxart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.AssetType, "fanart", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScreenshot(AssetCandidate candidate)
    {
        if (string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.AssetType.StartsWith("Screenshot -", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(candidate.AssetType, "screenshot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFanart(AssetCandidate candidate)
    {
        if (string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(candidate.AssetType, "Fanart - Background", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(candidate.AssetType, "fanart", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSourceRank(string? sourceSystem)
    {
        return string.Equals(sourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static int GetClearLogoRegionRank(string? regionCode)
    {
        if (string.IsNullOrWhiteSpace(regionCode))
        {
            return ClearLogoRegionOrder.Length + 50;
        }

        for (var index = 0; index < ClearLogoRegionOrder.Length; index++)
        {
            if (string.Equals(ClearLogoRegionOrder[index], regionCode, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return ClearLogoRegionOrder.Length + 10;
    }

    private static int GetPosterTypeRank(AssetCandidate candidate)
    {
        if (string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(candidate.AssetType, "Fanart - Box - Front", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        return candidate.PathOrUrl.StartsWith("boxart/front/", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static int GetScreenshotTypeRank(AssetCandidate candidate)
    {
        if (string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.AssetType switch
            {
                "Screenshot - Gameplay" => 0,
                "Screenshot - Game Title" => 1,
                "Screenshot - Game Select" => 2,
                "Screenshot - Game Over" => 3,
                _ => 4,
            };
        }

        return 10;
    }

    private static List<AssetCandidate> SelectFanarts(IReadOnlyList<AssetCandidate> candidates)
    {
        return candidates
            .Where(candidate => IsFanart(candidate))
            .DistinctBy(candidate => $"{candidate.SourceSystem}|{candidate.PathOrUrl}")
            .OrderBy(candidate => GetSourceRank(candidate.SourceSystem))
            .ThenBy(candidate => GetClearLogoRegionRank(candidate.RegionCode))
            .ThenBy(candidate => candidate.PriorityRank)
            .ThenBy(candidate => candidate.PathOrUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ResolveSourceUrls(AssetCandidate candidate)
    {
        if (string.Equals(candidate.SourceSystem, "launchbox", StringComparison.OrdinalIgnoreCase))
        {
            if (candidate.PathOrUrl.StartsWith("r2_", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"https://gamesdb-images.launchbox.gg/{candidate.PathOrUrl}";
                yield return $"https://images.launchbox-app.com/{candidate.PathOrUrl}";
                yield break;
            }

            yield return $"https://images.launchbox-app.com/{candidate.PathOrUrl}";
            yield return $"https://gamesdb-images.launchbox.gg/{candidate.PathOrUrl}";
            yield break;
        }

        yield return $"https://cdn.thegamesdb.net/images/original/{candidate.PathOrUrl}";
    }

    private static string ComputeShortHash(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
    }

    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A)
        {
            return ".png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46 &&
            bytes[3] == 0x46 &&
            bytes[8] == 0x57 &&
            bytes[9] == 0x45 &&
            bytes[10] == 0x42 &&
            bytes[11] == 0x50)
        {
            return ".webp";
        }

        return null;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        extension = extension.ToLowerInvariant();
        return extension switch
        {
            ".png" => ".png",
            ".jpg" => ".jpg",
            ".jpeg" => ".jpg",
            ".webp" => ".webp",
            _ => extension,
        };
    }

    private static string? DetectAssetExtension(string? sourceReference)
    {
        return string.IsNullOrWhiteSpace(sourceReference) ? null : Path.GetExtension(sourceReference);
    }

    private static string GetCacheSlug(string gameKey)
    {
        var segments = gameKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length >= 3)
        {
            return Slugify(segments[2]);
        }

        return Slugify(gameKey);
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool RemoteAssetExists(string sourceUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, sourceUrl);

        try
        {
            using var response = HttpClient.Send(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
            {
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArcadeFrontend/1.0");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static List<AssetCandidate> LoadCandidates(SqliteConnection masterConnection, string gameKey)
    {
        var candidates = new List<AssetCandidate>();
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            SELECT
                game_key,
                release_key,
                asset_type,
                source_system,
                region_code,
                language_code,
                path_or_url,
                priority_rank
            FROM asset_candidates
            WHERE game_key = $gameKey
            ORDER BY source_system, asset_type, region_code, priority_rank, path_or_url;
            """;
        command.Parameters.AddWithValue("$gameKey", gameKey);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            candidates.Add(new AssetCandidate
            {
                GameKey = reader.GetString(0),
                ReleaseKey = reader.IsDBNull(1) ? null : reader.GetString(1),
                AssetType = reader.GetString(2),
                SourceSystem = reader.GetString(3),
                RegionCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                LanguageCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                PathOrUrl = reader.GetString(6),
                PriorityRank = reader.IsDBNull(7) ? 999 : reader.GetInt32(7),
            });
        }

        return candidates;
    }

    private static WebsiteThemeCandidate? LoadWebsiteThemeCandidate(SqliteConnection masterConnection, string gameKey)
    {
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            SELECT
                g.game_key,
                g.slug,
                p.library_slug,
                p.platform_slug
            FROM games g
            INNER JOIN platforms p ON p.platform_key = g.platform_key
            WHERE g.game_key = $gameKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$gameKey", gameKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var gameSlug = reader.GetString(1);
        var librarySlug = reader.GetString(2);
        var platformSlug = reader.GetString(3);
        return new WebsiteThemeCandidate
        {
            GameKey = reader.GetString(0),
            GameSlug = gameSlug,
            DownloadUrl = $"{MrSharkyBaseUrl}/{librarySlug}/{platformSlug}/{gameSlug}/hyperspin.zip",
        };
    }

    private sealed class AssetCandidate
    {
        public string GameKey { get; init; } = string.Empty;
        public string? ReleaseKey { get; init; }
        public string AssetType { get; init; } = string.Empty;
        public string SourceSystem { get; init; } = string.Empty;
        public string? RegionCode { get; init; }
        public string? LanguageCode { get; init; }
        public string PathOrUrl { get; init; } = string.Empty;
        public int PriorityRank { get; init; }
    }

    private sealed class WebsiteThemeCandidate
    {
        public string GameKey { get; init; } = string.Empty;
        public string GameSlug { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;
    }
}

public sealed class CachedAssetRecord
{
    public string CachedAssetId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string GameKey { get; init; } = string.Empty;
    public string? ReleaseKey { get; init; }
    public string AssetRole { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? SourceReference { get; init; }
    public string? RegionCode { get; init; }
    public string? LanguageCode { get; init; }
    public string CachePath { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool SelectedByDefault { get; init; }
}
