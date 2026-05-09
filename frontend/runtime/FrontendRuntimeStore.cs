using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ArcadeFrontend;

public sealed class FrontendRuntimeStore : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteConnection _connection;

    public FrontendRuntimeStore(string databasePath)
    {
        _databasePath = databasePath;
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        EnsureSchema();
    }

    public string DatabasePath => _databasePath;

    public SqliteConnection Connection => _connection;

    public void Dispose()
    {
        _connection.Dispose();
    }

    public void UpsertSetting(string key, string? value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_settings (setting_key, setting_value, updated_at)
            VALUES ($key, $value, CURRENT_TIMESTAMP)
            ON CONFLICT(setting_key) DO UPDATE SET
                setting_value = excluded.setting_value,
                updated_at = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void EnsureSystem(
        string systemId,
        string displayName,
        bool isEnabled = false,
        string? preferredRegion = null,
        string? preferredLanguage = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO systems (
                system_id,
                display_name,
                is_enabled,
                preferred_region_code,
                preferred_language_code,
                updated_at
            )
            VALUES (
                $systemId,
                $displayName,
                $isEnabled,
                $preferredRegion,
                $preferredLanguage,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(system_id) DO UPDATE SET
                display_name = excluded.display_name,
                preferred_region_code = COALESCE(systems.preferred_region_code, excluded.preferred_region_code),
                preferred_language_code = COALESCE(systems.preferred_language_code, excluded.preferred_language_code),
                updated_at = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$isEnabled", isEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$preferredRegion", (object?)preferredRegion ?? DBNull.Value);
        command.Parameters.AddWithValue("$preferredLanguage", (object?)preferredLanguage ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public RuntimeSettingsSnapshot LoadSettings()
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT setting_key, setting_value FROM app_settings;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                settings[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        var systems = new List<SystemSettingsSnapshot>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT system_id, display_name, is_enabled, rom_root_path, default_emulator_command,
                       preferred_region_code, preferred_language_code, last_scanned_at
                FROM systems
                ORDER BY display_name;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                systems.Add(new SystemSettingsSnapshot
                {
                    SystemId = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    IsEnabled = reader.GetInt32(2) != 0,
                    RomRootPath = reader.IsDBNull(3) ? null : reader.GetString(3),
                    DefaultEmulatorCommand = reader.IsDBNull(4) ? null : reader.GetString(4),
                    PreferredRegionCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                    PreferredLanguageCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                    LastScannedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
                });
            }
        }

        return new RuntimeSettingsSnapshot(settings, systems);
    }

    public string StartScanSession(string systemId)
    {
        var scanId = Guid.NewGuid().ToString("N");
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO scan_sessions (
                scan_id,
                system_id,
                status,
                total_candidates,
                hashed_candidates,
                matched_candidates,
                asset_candidates,
                started_at
            )
            VALUES (
                $scanId,
                $systemId,
                'running',
                0,
                0,
                0,
                0,
                CURRENT_TIMESTAMP
            );
            """;
        command.Parameters.AddWithValue("$scanId", scanId);
        command.Parameters.AddWithValue("$systemId", systemId);
        command.ExecuteNonQuery();
        return scanId;
    }

    public void ResetSystemLibrary(string systemId)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var sql in new[]
                 {
                     "DELETE FROM owned_releases WHERE system_id = $systemId;",
                     "UPDATE discovered_files SET scan_id = NULL, updated_at = CURRENT_TIMESTAMP WHERE system_id = $systemId;",
                     "DELETE FROM scan_sessions WHERE system_id = $systemId;"
                 })
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$systemId", systemId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public ScannedFileRecord? GetReusableDiscoveredFile(string systemId, string absolutePath, long fileSizeBytes, string fileModifiedUtc)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                file_id,
                system_id,
                scan_id,
                absolute_path,
                file_name,
                file_extension,
                file_size_bytes,
                file_modified_utc,
                relative_path,
                archive_path,
                archive_entry_name,
                sha1,
                md5,
                match_status,
                matched_release_key
            FROM discovered_files
            WHERE system_id = $systemId
              AND absolute_path = $absolutePath
              AND file_size_bytes = $fileSizeBytes
              AND COALESCE(file_modified_utc, '') = $fileModifiedUtc
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$absolutePath", absolutePath);
        command.Parameters.AddWithValue("$fileSizeBytes", fileSizeBytes);
        command.Parameters.AddWithValue("$fileModifiedUtc", fileModifiedUtc);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ScannedFileRecord
        {
            FileId = reader.GetString(0),
            SystemId = reader.GetString(1),
            ScanId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            AbsolutePath = reader.GetString(3),
            FileName = reader.GetString(4),
            FileExtension = reader.GetString(5),
            FileSizeBytes = reader.GetInt64(6),
            FileModifiedUtc = reader.IsDBNull(7) ? null : reader.GetString(7),
            RelativePath = reader.IsDBNull(8) ? null : reader.GetString(8),
            ArchivePath = reader.IsDBNull(9) ? null : reader.GetString(9),
            ArchiveEntryName = reader.IsDBNull(10) ? null : reader.GetString(10),
            Sha1 = reader.IsDBNull(11) ? null : reader.GetString(11),
            Md5 = reader.IsDBNull(12) ? null : reader.GetString(12),
            MatchStatus = reader.GetString(13),
            MatchedReleaseKey = reader.IsDBNull(14) ? null : reader.GetString(14),
        };
    }

    public string? GetDiscoveredFileId(string systemId, string absolutePath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT file_id
            FROM discovered_files
            WHERE system_id = $systemId
              AND absolute_path = $absolutePath
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$absolutePath", absolutePath);
        return command.ExecuteScalar() as string;
    }

    public void UpdateScanProgress(LibraryScanProgress progress)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            UPDATE scan_sessions
            SET status = $status,
                total_candidates = $totalCandidates,
                hashed_candidates = $hashedCandidates,
                matched_candidates = $matchedCandidates,
                asset_candidates = $assetCandidates
            WHERE scan_id = $scanId;
            """;
        command.Parameters.AddWithValue("$status", progress.Phase.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$totalCandidates", progress.TotalCandidates);
        command.Parameters.AddWithValue("$hashedCandidates", progress.ProcessedCandidates);
        command.Parameters.AddWithValue("$matchedCandidates", progress.MatchedCandidates);
        command.Parameters.AddWithValue("$assetCandidates", progress.AssetCandidates);
        command.Parameters.AddWithValue("$scanId", progress.ScanId);
        command.ExecuteNonQuery();
    }

    public void CompleteScanSession(LibraryScanResult result)
    {
        using var transaction = _connection.BeginTransaction();
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE scan_sessions
                SET status = 'complete',
                    total_candidates = $totalCandidates,
                    hashed_candidates = $totalCandidates,
                    matched_candidates = $matchedCandidates,
                    asset_candidates = $matchedCandidates,
                    completed_at = CURRENT_TIMESTAMP
                WHERE scan_id = $scanId;
                """;
            command.Parameters.AddWithValue("$totalCandidates", result.TotalCandidates);
            command.Parameters.AddWithValue("$matchedCandidates", result.MatchedCandidates);
            command.Parameters.AddWithValue("$scanId", result.ScanId);
            command.ExecuteNonQuery();
        }

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE systems
                SET last_scanned_at = CURRENT_TIMESTAMP,
                    updated_at = CURRENT_TIMESTAMP
                WHERE system_id = $systemId;
                """;
            command.Parameters.AddWithValue("$systemId", result.SystemId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void FailScanSession(string scanId, string errorMessage)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            UPDATE scan_sessions
            SET status = 'failed',
                error_message = $errorMessage,
                completed_at = CURRENT_TIMESTAMP
            WHERE scan_id = $scanId;
            """;
        command.Parameters.AddWithValue("$scanId", scanId);
        command.Parameters.AddWithValue("$errorMessage", errorMessage);
        command.ExecuteNonQuery();
    }

    public void UpsertDiscoveredFile(ScannedFileRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO discovered_files (
                file_id,
                system_id,
                scan_id,
                absolute_path,
                file_name,
                file_extension,
                file_size_bytes,
                file_modified_utc,
                relative_path,
                archive_path,
                archive_entry_name,
                sha1,
                md5,
                match_status,
                matched_release_key,
                discovered_at,
                updated_at
            )
            VALUES (
                $fileId,
                $systemId,
                $scanId,
                $absolutePath,
                $fileName,
                $fileExtension,
                $fileSizeBytes,
                $fileModifiedUtc,
                $relativePath,
                $archivePath,
                $archiveEntryName,
                $sha1,
                $md5,
                $matchStatus,
                $matchedReleaseKey,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(absolute_path) DO UPDATE SET
                scan_id = excluded.scan_id,
                file_name = excluded.file_name,
                file_extension = excluded.file_extension,
                file_size_bytes = excluded.file_size_bytes,
                file_modified_utc = excluded.file_modified_utc,
                relative_path = excluded.relative_path,
                archive_path = excluded.archive_path,
                archive_entry_name = excluded.archive_entry_name,
                sha1 = excluded.sha1,
                md5 = excluded.md5,
                match_status = excluded.match_status,
                matched_release_key = excluded.matched_release_key,
                updated_at = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$fileId", record.FileId);
        command.Parameters.AddWithValue("$systemId", record.SystemId);
        command.Parameters.AddWithValue("$scanId", record.ScanId);
        command.Parameters.AddWithValue("$absolutePath", record.AbsolutePath);
        command.Parameters.AddWithValue("$fileName", record.FileName);
        command.Parameters.AddWithValue("$fileExtension", record.FileExtension);
        command.Parameters.AddWithValue("$fileSizeBytes", record.FileSizeBytes);
        command.Parameters.AddWithValue("$fileModifiedUtc", (object?)record.FileModifiedUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("$relativePath", (object?)record.RelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$archivePath", (object?)record.ArchivePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$archiveEntryName", (object?)record.ArchiveEntryName ?? DBNull.Value);
        command.Parameters.AddWithValue("$sha1", (object?)record.Sha1 ?? DBNull.Value);
        command.Parameters.AddWithValue("$md5", (object?)record.Md5 ?? DBNull.Value);
        command.Parameters.AddWithValue("$matchStatus", record.MatchStatus);
        command.Parameters.AddWithValue("$matchedReleaseKey", (object?)record.MatchedReleaseKey ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void DeleteDiscoveredFilesMissingFromScan(string systemId, IReadOnlyCollection<string> scannedPaths)
    {
        using var transaction = _connection.BeginTransaction();

        var keep = new HashSet<string>(scannedPaths, StringComparer.OrdinalIgnoreCase);
        var stalePaths = new List<string>();

        using (var select = _connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                """
                SELECT absolute_path
                FROM discovered_files
                WHERE system_id = $systemId;
                """;
            select.Parameters.AddWithValue("$systemId", systemId);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!keep.Contains(path))
                {
                    stalePaths.Add(path);
                }
            }
        }

        foreach (var stalePath in stalePaths)
        {
            using var delete = _connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText =
                """
                DELETE FROM discovered_files
                WHERE system_id = $systemId
                  AND absolute_path = $absolutePath;
                """;
            delete.Parameters.AddWithValue("$systemId", systemId);
            delete.Parameters.AddWithValue("$absolutePath", stalePath);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void UpsertOwnedRelease(OwnedReleaseRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO owned_releases (
                owned_release_id,
                system_id,
                game_key,
                release_key,
                primary_file_id,
                selected_by_default,
                discovered_at,
                updated_at
            )
            VALUES (
                $ownedReleaseId,
                $systemId,
                $gameKey,
                $releaseKey,
                $primaryFileId,
                0,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(system_id, release_key, primary_file_id) DO UPDATE SET
                game_key = excluded.game_key,
                updated_at = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$ownedReleaseId", record.OwnedReleaseId);
        command.Parameters.AddWithValue("$systemId", record.SystemId);
        command.Parameters.AddWithValue("$gameKey", record.GameKey);
        command.Parameters.AddWithValue("$releaseKey", record.ReleaseKey);
        command.Parameters.AddWithValue("$primaryFileId", record.PrimaryFileId);
        command.ExecuteNonQuery();
    }

    public void UpdateSystemRomRoot(string systemId, string romRootPath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            UPDATE systems
            SET rom_root_path = $romRootPath,
                updated_at = CURRENT_TIMESTAMP
            WHERE system_id = $systemId;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$romRootPath", romRootPath);
        command.ExecuteNonQuery();
    }

    public void UpdateSystemConfiguration(
        string systemId,
        string? romRootPath,
        string? defaultEmulatorCommand,
        string? preferredRegionCode,
        string? preferredLanguageCode,
        bool isEnabled)
    {
        using var transaction = _connection.BeginTransaction();

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE systems
                SET rom_root_path = $romRootPath,
                    default_emulator_command = $defaultEmulatorCommand,
                    preferred_region_code = $preferredRegionCode,
                    preferred_language_code = $preferredLanguageCode,
                    is_enabled = $isEnabled,
                    updated_at = CURRENT_TIMESTAMP
                WHERE system_id = $systemId;
                """;
            command.Parameters.AddWithValue("$systemId", systemId);
            command.Parameters.AddWithValue("$romRootPath", (object?)romRootPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$defaultEmulatorCommand", (object?)defaultEmulatorCommand ?? DBNull.Value);
            command.Parameters.AddWithValue("$preferredRegionCode", (object?)preferredRegionCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$preferredLanguageCode", (object?)preferredLanguageCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$isEnabled", isEnabled ? 1 : 0);
            command.ExecuteNonQuery();
        }

        transaction.Commit();

        UpsertSetting("preferred_region_code", preferredRegionCode);
        UpsertSetting("preferred_language_code", preferredLanguageCode);
    }

    public string? GetSystemRomRoot(string systemId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT rom_root_path FROM systems WHERE system_id = $systemId LIMIT 1;";
        command.Parameters.AddWithValue("$systemId", systemId);
        return command.ExecuteScalar() as string;
    }

    public IReadOnlyList<string> GetUnidentifiedFileNames(string systemId)
    {
        var results = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT file_name
            FROM discovered_files
            WHERE system_id = $systemId AND match_status = 'unidentified'
            ORDER BY LOWER(file_name);
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public bool HasOwnedLibrary(string systemId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM owned_releases WHERE system_id = $systemId LIMIT 1);";
        command.Parameters.AddWithValue("$systemId", systemId);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    public void ReplaceCachedAssets(string systemId, string gameKey, IReadOnlyList<CachedAssetRecord> assets)
    {
        using var transaction = _connection.BeginTransaction();

        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                """
                DELETE FROM cached_assets
                WHERE system_id = $systemId AND game_key = $gameKey;
                """;
            delete.Parameters.AddWithValue("$systemId", systemId);
            delete.Parameters.AddWithValue("$gameKey", gameKey);
            delete.ExecuteNonQuery();
        }

        foreach (var asset in assets)
        {
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO cached_assets (
                    cached_asset_id,
                    system_id,
                    game_key,
                    release_key,
                    asset_role,
                    source_system,
                    source_reference,
                    region_code,
                    language_code,
                    cache_path,
                    sort_order,
                    selected_by_default,
                    cached_at,
                    updated_at
                )
                VALUES (
                    $cachedAssetId,
                    $systemId,
                    $gameKey,
                    $releaseKey,
                    $assetRole,
                    $sourceSystem,
                    $sourceReference,
                    $regionCode,
                    $languageCode,
                    $cachePath,
                    $sortOrder,
                    $selectedByDefault,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                );
                """;
            insert.Parameters.AddWithValue("$cachedAssetId", asset.CachedAssetId);
            insert.Parameters.AddWithValue("$systemId", asset.SystemId);
            insert.Parameters.AddWithValue("$gameKey", asset.GameKey);
            insert.Parameters.AddWithValue("$releaseKey", (object?)asset.ReleaseKey ?? DBNull.Value);
            insert.Parameters.AddWithValue("$assetRole", asset.AssetRole);
            insert.Parameters.AddWithValue("$sourceSystem", (object?)asset.SourceSystem ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sourceReference", (object?)asset.SourceReference ?? DBNull.Value);
            insert.Parameters.AddWithValue("$regionCode", (object?)asset.RegionCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$languageCode", (object?)asset.LanguageCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$cachePath", asset.CachePath);
            insert.Parameters.AddWithValue("$sortOrder", asset.SortOrder);
            insert.Parameters.AddWithValue("$selectedByDefault", asset.SelectedByDefault ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public string? GetSelectedAssetPath(string systemId, string gameKey, string assetRole)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT cache_path
            FROM cached_assets
            WHERE system_id = $systemId
              AND game_key = $gameKey
              AND asset_role = $assetRole
            ORDER BY selected_by_default DESC, sort_order ASC, cached_at ASC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        command.Parameters.AddWithValue("$assetRole", assetRole);
        var result = command.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(result) || !File.Exists(result) ? null : result;
    }

    public IReadOnlyList<string> GetAssetPaths(string systemId, string gameKey, string assetRole)
    {
        var results = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT cache_path
            FROM cached_assets
            WHERE system_id = $systemId
              AND game_key = $gameKey
              AND asset_role = $assetRole
            ORDER BY sort_order ASC, cached_at ASC;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        command.Parameters.AddWithValue("$assetRole", assetRole);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (File.Exists(path))
            {
                results.Add(path);
            }
        }

        return results;
    }

    public string? GetSystemDefaultCommand(string systemId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT default_emulator_command FROM systems WHERE system_id = $systemId LIMIT 1;";
        command.Parameters.AddWithValue("$systemId", systemId);
        return command.ExecuteScalar() as string;
    }

    public string? GetGamePreferredReleaseKey(string systemId, string gameKey)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT preferred_release_key
            FROM game_preferences
            WHERE system_id = $systemId AND game_key = $gameKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        return command.ExecuteScalar() as string;
    }

    public void SetGamePreferredReleaseKey(string systemId, string gameKey, string releaseKey)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO game_preferences (
                system_id,
                game_key,
                preferred_release_key,
                is_favorite,
                updated_at
            )
            VALUES (
                $systemId,
                $gameKey,
                $releaseKey,
                COALESCE((SELECT is_favorite FROM game_preferences WHERE system_id = $systemId AND game_key = $gameKey), 0),
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(system_id, game_key) DO UPDATE SET
                preferred_release_key = excluded.preferred_release_key,
                updated_at = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        command.Parameters.AddWithValue("$releaseKey", releaseKey);
        command.ExecuteNonQuery();
    }

    public bool IsGameFavorite(string systemId, string gameKey)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT is_favorite
            FROM game_preferences
            WHERE system_id = $systemId AND game_key = $gameKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) != 0;
    }

    public void SetGameFavorite(string systemId, string gameKey, bool isFavorite)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO game_preferences (
                system_id,
                game_key,
                preferred_release_key,
                is_favorite,
                favorite_marked_at,
                updated_at
            )
            VALUES (
                $systemId,
                $gameKey,
                (SELECT preferred_release_key FROM game_preferences WHERE system_id = $systemId AND game_key = $gameKey),
                $isFavorite,
                CASE WHEN $isFavorite = 1 THEN CURRENT_TIMESTAMP ELSE NULL END,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(system_id, game_key) DO UPDATE SET
                is_favorite = excluded.is_favorite,
                favorite_marked_at = CASE WHEN excluded.is_favorite = 1 THEN CURRENT_TIMESTAMP ELSE NULL END,
                updated_at = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        command.Parameters.AddWithValue("$isFavorite", isFavorite ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public string MakeRelativeToSystemRoot(string systemId, string absolutePath)
    {
        var root = GetSystemRomRoot(systemId);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Path.GetFileName(absolutePath);
        }

        return Path.GetRelativePath(root, absolutePath);
    }

    private void EnsureSchema()
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var statement in FrontendRuntimeSchema.Statements)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        EnsureColumnExists(transaction, "discovered_files", "file_modified_utc", "TEXT");

        using (var versionCommand = _connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText =
                """
                INSERT INTO schema_info (id, schema_version, updated_at)
                VALUES (1, $schemaVersion, CURRENT_TIMESTAMP)
                ON CONFLICT(id) DO UPDATE SET
                    schema_version = $schemaVersion,
                    updated_at = CURRENT_TIMESTAMP;
                """;
            versionCommand.Parameters.AddWithValue("$schemaVersion", FrontendRuntimeSchema.CurrentSchemaVersion);
            versionCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void EnsureColumnExists(SqliteTransaction transaction, string tableName, string columnName, string columnDefinition)
    {
        using var check = _connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = _connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
}
