using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace ArcadeFrontend;

public sealed class UnifiedSnesLibraryScanner
{
    private static readonly string[] SupportedExtensions = [".sfc", ".smc"];

    private readonly FrontendRuntimeStore _runtimeStore;
    private readonly string _masterDatabasePath;
    private readonly FrontendAssetCacheManager _assetCacheManager;

    public UnifiedSnesLibraryScanner(FrontendRuntimeStore runtimeStore, string masterDatabasePath)
    {
        _runtimeStore = runtimeStore;
        _masterDatabasePath = masterDatabasePath;
        _assetCacheManager = new FrontendAssetCacheManager(runtimeStore, masterDatabasePath);
    }

    public static IReadOnlyList<string> ScanExtensions => SupportedExtensions;

    public LibraryScanResult Scan(string systemId, string romRootPath, Action<LibraryScanProgress>? onProgress = null)
    {
        if (!Directory.Exists(romRootPath))
        {
            throw new DirectoryNotFoundException($"ROM root path does not exist: {romRootPath}");
        }

        if (!File.Exists(_masterDatabasePath))
        {
            throw new FileNotFoundException("Unified SNES database was not found.", _masterDatabasePath);
        }

        _runtimeStore.UpdateSystemRomRoot(systemId, romRootPath);
        _runtimeStore.ResetSystemLibrary(systemId);
        var scanId = _runtimeStore.StartScanSession(systemId);

        try
        {
            var candidates = DiscoverCandidates(romRootPath);
            ReportProgress(onProgress, new LibraryScanProgress
            {
                ScanId = scanId,
                SystemId = systemId,
                Phase = LibraryScanPhase.Discovery,
                TotalCandidates = candidates.Count,
                Message = $"Discovered {candidates.Count} candidate ROM files.",
            });

            _runtimeStore.UpdateScanProgress(new LibraryScanProgress
            {
                ScanId = scanId,
                SystemId = systemId,
                Phase = LibraryScanPhase.Discovery,
                TotalCandidates = candidates.Count,
            });

            using (var masterConnection = new SqliteConnection($"Data Source={_masterDatabasePath};Mode=ReadOnly"))
            {
                masterConnection.Open();

                var matchedCount = 0;
                var matchedGameKeys = new HashSet<string>(StringComparer.Ordinal);
                var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < candidates.Count; index++)
                {
                    var candidate = candidates[index];
                    var fileInfo = new FileInfo(candidate);
                    var relativePath = Path.GetRelativePath(romRootPath, candidate);
                    var fileModifiedUtc = fileInfo.LastWriteTimeUtc.ToString("O");
                    scannedPaths.Add(candidate);

                    var reusable = _runtimeStore.GetReusableDiscoveredFile(systemId, candidate, fileInfo.Length, fileModifiedUtc);
                    var existingFileId = _runtimeStore.GetDiscoveredFileId(systemId, candidate);
                    var hashes = reusable?.Sha1 != null && reusable.Md5 != null
                        ? (sha1: reusable.Sha1, md5: reusable.Md5)
                        : ComputeHashes(candidate);
                    var match = ResolveMatch(masterConnection, reusable, hashes.sha1, hashes.md5);
                    var fileId = existingFileId ?? reusable?.FileId ?? Guid.NewGuid().ToString("N");

                    _runtimeStore.UpsertDiscoveredFile(new ScannedFileRecord
                    {
                        FileId = fileId,
                        SystemId = systemId,
                        ScanId = scanId,
                        AbsolutePath = candidate,
                        FileName = fileInfo.Name,
                        FileExtension = fileInfo.Extension.ToLowerInvariant(),
                        FileSizeBytes = fileInfo.Length,
                        FileModifiedUtc = fileModifiedUtc,
                        RelativePath = relativePath,
                        ArchivePath = null,
                        ArchiveEntryName = null,
                        Sha1 = hashes.sha1,
                        Md5 = hashes.md5,
                        MatchStatus = match == null ? "unidentified" : "matched",
                        MatchedReleaseKey = match?.ReleaseKey,
                    });

                    if (match != null)
                    {
                        matchedCount++;
                        matchedGameKeys.Add(match.GameKey);
                        _runtimeStore.UpsertOwnedRelease(new OwnedReleaseRecord
                        {
                            OwnedReleaseId = Guid.NewGuid().ToString("N"),
                            SystemId = systemId,
                            GameKey = match.GameKey,
                            ReleaseKey = match.ReleaseKey,
                            PrimaryFileId = fileId,
                        });
                    }

                    var progress = new LibraryScanProgress
                    {
                        ScanId = scanId,
                        SystemId = systemId,
                        Phase = LibraryScanPhase.Matching,
                        TotalCandidates = candidates.Count,
                        ProcessedCandidates = index + 1,
                        MatchedCandidates = matchedCount,
                        CurrentPath = candidate,
                        Message = $"Processed {index + 1} of {candidates.Count} files.",
                    };
                    _runtimeStore.UpdateScanProgress(progress);
                    ReportProgress(onProgress, progress);
                }

                _runtimeStore.DeleteDiscoveredFilesMissingFromScan(systemId, scannedPaths);

                var assetTotal = matchedGameKeys.Count;
                var assetProcessed = 0;
                if (assetTotal > 0)
                {
                    _assetCacheManager.SyncAssets(systemId, matchedGameKeys.ToList(), (processed, total, gameKey) =>
                    {
                        assetProcessed = processed;
                        var progress = new LibraryScanProgress
                        {
                            ScanId = scanId,
                            SystemId = systemId,
                            Phase = LibraryScanPhase.AssetFetch,
                            TotalCandidates = total,
                            ProcessedCandidates = processed,
                            MatchedCandidates = matchedCount,
                            AssetCandidates = total,
                            CurrentPath = gameKey,
                            Message = $"Cached assets for {processed} of {total} matched games.",
                        };
                        _runtimeStore.UpdateScanProgress(progress);
                        ReportProgress(onProgress, progress);
                    });
                }

                var assetProgress = new LibraryScanProgress
                {
                    ScanId = scanId,
                    SystemId = systemId,
                    Phase = LibraryScanPhase.AssetFetch,
                    TotalCandidates = assetTotal,
                    ProcessedCandidates = assetProcessed,
                    MatchedCandidates = matchedCount,
                    AssetCandidates = assetTotal,
                    Message = assetTotal == 0
                        ? "No matched games required asset downloads."
                        : $"Cached assets for {assetProcessed} matched games.",
                };
                _runtimeStore.UpdateScanProgress(assetProgress);
                ReportProgress(onProgress, assetProgress);

                var result = new LibraryScanResult
                {
                    ScanId = scanId,
                    SystemId = systemId,
                    TotalCandidates = candidates.Count,
                    MatchedCandidates = matchedCount,
                    UnmatchedCandidates = candidates.Count - matchedCount,
                    CompletedAt = DateTimeOffset.UtcNow,
                };
                _runtimeStore.CompleteScanSession(result);
                ReportProgress(onProgress, new LibraryScanProgress
                {
                    ScanId = scanId,
                    SystemId = systemId,
                    Phase = LibraryScanPhase.Complete,
                    TotalCandidates = result.TotalCandidates,
                    ProcessedCandidates = result.TotalCandidates,
                    MatchedCandidates = result.MatchedCandidates,
                    AssetCandidates = result.MatchedCandidates,
                    Message = "Scan complete.",
                });

                return result;
            }
        }
        catch (Exception ex)
        {
            _runtimeStore.FailScanSession(scanId, ex.Message);
            ReportProgress(onProgress, new LibraryScanProgress
            {
                ScanId = scanId,
                SystemId = systemId,
                Phase = LibraryScanPhase.Failed,
                Message = ex.Message,
            });
            throw;
        }
    }

    private List<string> DiscoverCandidates(string romRootPath)
    {
        return Directory.EnumerateFiles(romRootPath, "*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (string sha1, string md5) ComputeHashes(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha1 = SHA1.Create();
        using var md5 = MD5.Create();

        var sha1Hash = Convert.ToHexString(sha1.ComputeHash(stream)).ToLowerInvariant();
        stream.Position = 0;
        var md5Hash = Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();

        return (sha1Hash, md5Hash);
    }

    private MasterReleaseMatch? FindReleaseMatch(SqliteConnection connection, string sha1, string md5)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT rr.release_key, gr.game_key
            FROM release_roms rr
            INNER JOIN game_releases gr ON gr.release_key = rr.release_key
            WHERE rr.sha1 = $sha1
               OR (rr.md5 = $md5 AND NOT EXISTS (
                    SELECT 1
                    FROM release_roms exact_sha1
                    WHERE exact_sha1.sha1 = $sha1
               ))
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sha1", sha1);
        command.Parameters.AddWithValue("$md5", md5);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new MasterReleaseMatch
        {
            ReleaseKey = reader.GetString(0),
            GameKey = reader.GetString(1),
        };
    }

    private MasterReleaseMatch? ResolveMatch(SqliteConnection connection, ScannedFileRecord? reusable, string sha1, string md5)
    {
        if (reusable != null &&
            string.Equals(reusable.MatchStatus, "matched", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(reusable.MatchedReleaseKey))
        {
            var gameKey = FindGameKeyForRelease(connection, reusable.MatchedReleaseKey);
            if (!string.IsNullOrWhiteSpace(gameKey))
            {
                return new MasterReleaseMatch
                {
                    ReleaseKey = reusable.MatchedReleaseKey,
                    GameKey = gameKey,
                };
            }
        }

        if (reusable != null && string.Equals(reusable.MatchStatus, "unidentified", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FindReleaseMatch(connection, sha1, md5);
    }

    private static string? FindGameKeyForRelease(SqliteConnection connection, string releaseKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT game_key
            FROM game_releases
            WHERE release_key = $releaseKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$releaseKey", releaseKey);
        return command.ExecuteScalar() as string;
    }

    private static void ReportProgress(Action<LibraryScanProgress>? onProgress, LibraryScanProgress progress)
    {
        onProgress?.Invoke(progress);
    }
}
