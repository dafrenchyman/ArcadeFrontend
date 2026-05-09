using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ArcadeFrontend;

public sealed class RuntimeLibraryBuilder
{
    private readonly FrontendRuntimeStore _runtimeStore;
    private readonly string _masterDatabasePath;
    private readonly LaunchCommandResolver _launchCommandResolver;

    public RuntimeLibraryBuilder(FrontendRuntimeStore runtimeStore, string masterDatabasePath)
    {
        _runtimeStore = runtimeStore;
        _masterDatabasePath = masterDatabasePath;
        _launchCommandResolver = new LaunchCommandResolver(runtimeStore);
    }

    public bool HasRuntimeLibrary(string systemId)
    {
        return _runtimeStore.HasOwnedLibrary(systemId);
    }

    public MenuItemData BuildRootMenu(string systemId)
    {
        using var masterConnection = new SqliteConnection($"Data Source={_masterDatabasePath};Mode=ReadOnly");
        masterConnection.Open();
        AttachRuntimeDatabase(masterConnection);

        var systemName = GetSystemDisplayName(systemId);
        var items = new List<MenuItemData>
        {
            BuildSystemWheel(masterConnection, systemId, systemName)
        };

        var unidentified = BuildUnidentifiedWheel(systemId);
        if (unidentified != null)
        {
            items.Add(unidentified);
        }

        return new MenuItemData
        {
            Name = "System Selection",
            MenuType = "Wheel",
            Items = items,
        };
    }

    private MenuItemData BuildSystemWheel(SqliteConnection masterConnection, string systemId, string systemName)
    {
        var games = new List<MenuItemData>();
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            SELECT
                o.game_key,
                g.canonical_name,
                g.release_year,
                g.players_min,
                g.players_max,
                g.is_coop,
                g.primary_publisher_name,
                g.primary_developer_name,
                gd.text_value
            FROM frontend.owned_releases o
            INNER JOIN games g ON g.game_key = o.game_key
            LEFT JOIN game_descriptions gd ON gd.id = g.preferred_long_description_id
            WHERE o.system_id = $systemId
            GROUP BY
                o.game_key,
                g.canonical_name,
                g.release_year,
                g.players_min,
                g.players_max,
                g.is_coop,
                g.primary_publisher_name,
                g.primary_developer_name,
                gd.text_value
            ORDER BY LOWER(g.canonical_name);
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            games.Add(BuildCanonicalGameDetails(masterConnection, systemId, systemName, reader.GetString(0)));
        }

        return new MenuItemData
        {
            Name = systemName,
            MenuType = "Wheel",
            Items = games,
        };
    }

    private List<Version> LoadOwnedVersions(SqliteConnection masterConnection, string systemId, string gameKey)
    {
        var versions = new List<Version>();
        var preferredReleaseKey = _runtimeStore.GetGamePreferredReleaseKey(systemId, gameKey);
        var firstReleaseKey = string.Empty;
        var releaseKeys = new List<string>();

        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            SELECT
                o.release_key,
                gr.revision_label,
                gr.primary_region_code,
                df.absolute_path,
                GROUP_CONCAT(DISTINCT rl.language_code)
            FROM frontend.owned_releases o
            INNER JOIN game_releases gr ON gr.release_key = o.release_key
            INNER JOIN frontend.discovered_files df ON df.file_id = o.primary_file_id
            LEFT JOIN release_languages rl ON rl.release_key = o.release_key
            WHERE o.system_id = $systemId AND o.game_key = $gameKey
            GROUP BY o.release_key, gr.revision_label, gr.primary_region_code, df.absolute_path
            ORDER BY LOWER(gr.release_title), LOWER(COALESCE(gr.revision_label, ''));
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var releaseKey = reader.GetString(0);
            if (string.IsNullOrEmpty(firstReleaseKey))
            {
                firstReleaseKey = releaseKey;
            }
            releaseKeys.Add(releaseKey);

            var languages = reader.IsDBNull(4)
                ? null
                : new List<string>(reader.GetString(4).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var region = reader.IsDBNull(2) ? null : reader.GetString(2);
            var romPath = reader.GetString(3);
            var launchCommand = _launchCommandResolver.Resolve(systemId, gameKey, releaseKey, romPath);

            versions.Add(new Version
            {
                ReleaseKey = releaseKey,
                Default = false,
                Regions = region == null ? null : [region],
                Revision = reader.IsDBNull(1) ? "Original" : reader.GetString(1),
                Languages = languages,
                LaunchCommand = launchCommand,
            });
        }

        var effectivePreferredKey = preferredReleaseKey ?? firstReleaseKey;
        if (!string.IsNullOrEmpty(effectivePreferredKey))
        {
            for (var index = 0; index < releaseKeys.Count; index++)
            {
                if (releaseKeys[index] == effectivePreferredKey && index < versions.Count)
                {
                    versions[index].Default = true;
                    break;
                }
            }

            if (versions.Count > 0 && !versions.Exists(version => version.Default))
            {
                versions[0].Default = true;
            }
        }

        return versions;
    }

    public List<MenuItemData> LoadRelatedGames(string systemId, string gameKey, string relation)
    {
        using var masterConnection = new SqliteConnection($"Data Source={_masterDatabasePath};Mode=ReadOnly");
        masterConnection.Open();
        AttachRuntimeDatabase(masterConnection);

        return relation switch
        {
            "series" => LoadSeriesGames(masterConnection, systemId, gameKey),
            "publisher" => LoadCompanyGames(masterConnection, systemId, gameKey, "publisher"),
            "developer" => LoadCompanyGames(masterConnection, systemId, gameKey, "developer"),
            _ => [],
        };
    }

    public MenuItemData BuildCanonicalGameDetails(string systemId, string gameKey)
    {
        using var masterConnection = new SqliteConnection($"Data Source={_masterDatabasePath};Mode=ReadOnly");
        masterConnection.Open();
        AttachRuntimeDatabase(masterConnection);
        return BuildCanonicalGameDetails(masterConnection, systemId, GetSystemDisplayName(systemId), gameKey);
    }

    private MenuItemData? BuildUnidentifiedWheel(string systemId)
    {
        var filenames = _runtimeStore.GetUnidentifiedFileNames(systemId);
        if (filenames.Count == 0)
        {
            return null;
        }

        var items = new List<MenuItemData>();
        foreach (var filename in filenames)
        {
            items.Add(new MenuItemData
            {
                Name = filename,
                MenuType = "Wheel",
            });
        }

        return new MenuItemData
        {
            Name = "Unidentified SNES",
            MenuType = "Wheel",
            Items = items,
        };
    }

    private List<MenuItemData> LoadSeriesGames(SqliteConnection masterConnection, string systemId, string gameKey)
    {
        var results = new List<MenuItemData>();
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            WITH source_series AS (
                SELECT series_key
                FROM platform_series_games
                WHERE game_key = $gameKey
            )
            SELECT DISTINCT g.game_key, g.canonical_name, g.release_year
            FROM platform_series_games psg
            INNER JOIN source_series ss ON ss.series_key = psg.series_key
            INNER JOIN games g ON g.game_key = psg.game_key
            INNER JOIN frontend.owned_releases o ON o.game_key = g.game_key AND o.system_id = $systemId
            WHERE g.game_key <> $gameKey
            ORDER BY psg.sort_order, LOWER(g.canonical_name);
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(BuildLightweightGameReference(
                systemId,
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }

        return results;
    }

    private List<MenuItemData> LoadCompanyGames(SqliteConnection masterConnection, string systemId, string gameKey, string role)
    {
        var results = new List<MenuItemData>();
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            WITH source_company AS (
                SELECT company_name
                FROM game_companies
                WHERE game_key = $gameKey AND role = $role
                ORDER BY is_primary DESC, company_name
                LIMIT 1
            )
            SELECT DISTINCT g.game_key, g.canonical_name, g.release_year
            FROM game_companies gc
            INNER JOIN source_company sc ON sc.company_name = gc.company_name
            INNER JOIN games g ON g.game_key = gc.game_key
            INNER JOIN frontend.owned_releases o ON o.game_key = g.game_key AND o.system_id = $systemId
            LEFT JOIN frontend.game_preferences gp ON gp.system_id = $systemId AND gp.game_key = g.game_key
            WHERE gc.role = $role AND g.game_key <> $gameKey
            ORDER BY COALESCE(gp.is_favorite, 0) DESC, COALESCE(g.release_year, 0) DESC, LOWER(g.canonical_name);
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        command.Parameters.AddWithValue("$role", role);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(BuildLightweightGameReference(
                systemId,
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }

        return results;
    }

    private MenuItemData BuildLightweightGameReference(string systemId, string gameKey, string name, int? releaseYear)
    {
        var posterPath = _runtimeStore.GetSelectedAssetPath(systemId, gameKey, "poster");
        return new MenuItemData
        {
            Name = name,
            MenuType = "Wheel",
            SystemId = systemId,
            GameKey = gameKey,
            Poster = posterPath,
            ItemInformation = new ItemInformationData
            {
                Poster = posterPath,
                ReleaseData = releaseYear?.ToString(),
            },
        };
    }

    private MenuItemData BuildCanonicalGameDetails(
        SqliteConnection masterConnection,
        string systemId,
        string systemName,
        string gameKey)
    {
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            """
            SELECT
                g.game_key,
                g.canonical_name,
                g.release_year,
                g.players_min,
                g.players_max,
                g.is_coop,
                g.primary_publisher_name,
                g.primary_developer_name,
                gd.text_value
            FROM games g
            LEFT JOIN game_descriptions gd ON gd.id = g.preferred_long_description_id
            WHERE g.game_key = $gameKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$gameKey", gameKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return BuildLightweightGameReference(systemId, gameKey, gameKey, null);
        }

        var logoPath = _runtimeStore.GetSelectedAssetPath(systemId, gameKey, "clear_logo");
        var posterPath = _runtimeStore.GetSelectedAssetPath(systemId, gameKey, "poster");
        var screenshots = _runtimeStore.GetAssetPaths(systemId, gameKey, "screenshot");
        var hyperspinThemePath = _runtimeStore.GetSelectedAssetPath(systemId, gameKey, "hyperspin_theme");
        var fanartPaths = _runtimeStore.GetAssetPaths(systemId, gameKey, "fanart");

        return new MenuItemData
        {
            Name = reader.GetString(1),
            MenuType = "Wheel",
            SystemId = systemId,
            GameKey = gameKey,
            LogoLocation = logoPath,
            Poster = posterPath,
            Theme = string.IsNullOrWhiteSpace(hyperspinThemePath)
                ? BuildAnimatedImageTheme(fanartPaths)
                : new ThemeDefinition
                {
                    Type = ThemeType.HyperSpin,
                    Path = hyperspinThemePath,
                },
            ItemInformation = new ItemInformationData
            {
                Description = reader.IsDBNull(8) ? null : reader.GetString(8),
                Poster = posterPath,
                LogoLocation = logoPath,
                Screenshots = screenshots.ToList(),
                Platform = systemName,
                ReleaseData = reader.IsDBNull(2) ? null : reader.GetInt32(2).ToString(),
                Players = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Coop = reader.IsDBNull(5) ? null : reader.GetInt32(5) != 0,
                Publishers = reader.IsDBNull(6) ? null : [reader.GetString(6)],
                Developers = reader.IsDBNull(7) ? null : [reader.GetString(7)],
                Versions = LoadOwnedVersions(masterConnection, systemId, gameKey),
            },
        };
    }

    private string GetSystemDisplayName(string systemId)
    {
        foreach (var system in _runtimeStore.LoadSettings().Systems)
        {
            if (string.Equals(system.SystemId, systemId, StringComparison.OrdinalIgnoreCase))
            {
                return system.DisplayName;
            }
        }

        return systemId;
    }

    private static ThemeDefinition? BuildAnimatedImageTheme(IReadOnlyList<string> fanartPaths)
    {
        var usablePaths = fanartPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (usablePaths.Count == 0)
        {
            return null;
        }

        return new ThemeDefinition
        {
            Type = ThemeType.AnimatedImage,
            Variants = usablePaths,
        };
    }

    private void AttachRuntimeDatabase(SqliteConnection masterConnection)
    {
        using (var check = masterConnection.CreateCommand())
        {
            check.CommandText = "PRAGMA database_list;";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(1) && string.Equals(reader.GetString(1), "frontend", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var command = masterConnection.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS frontend;";
        command.Parameters.AddWithValue("$path", _runtimeStore.DatabasePath);
        command.ExecuteNonQuery();
    }
}
