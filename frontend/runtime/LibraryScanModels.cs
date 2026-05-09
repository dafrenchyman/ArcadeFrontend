using System;

namespace ArcadeFrontend;

public enum LibraryScanPhase
{
    Discovery,
    Hashing,
    Matching,
    AssetFetch,
    Complete,
    Failed,
}

public sealed class LibraryScanProgress
{
    public string ScanId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public LibraryScanPhase Phase { get; init; }
    public int TotalCandidates { get; init; }
    public int ProcessedCandidates { get; init; }
    public int MatchedCandidates { get; init; }
    public int AssetCandidates { get; init; }
    public string? CurrentPath { get; init; }
    public string? Message { get; init; }
}

public sealed class LibraryScanResult
{
    public string ScanId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public int TotalCandidates { get; init; }
    public int MatchedCandidates { get; init; }
    public int UnmatchedCandidates { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}

public sealed class ScannedFileRecord
{
    public string FileId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string ScanId { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string? FileModifiedUtc { get; init; }
    public string? RelativePath { get; init; }
    public string? ArchivePath { get; init; }
    public string? ArchiveEntryName { get; init; }
    public string? Sha1 { get; init; }
    public string? Md5 { get; init; }
    public string MatchStatus { get; init; } = "pending";
    public string? MatchedReleaseKey { get; init; }
}

public sealed class OwnedReleaseRecord
{
    public string OwnedReleaseId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string GameKey { get; init; } = string.Empty;
    public string ReleaseKey { get; init; } = string.Empty;
    public string PrimaryFileId { get; init; } = string.Empty;
}

public sealed class MasterReleaseMatch
{
    public string ReleaseKey { get; init; } = string.Empty;
    public string GameKey { get; init; } = string.Empty;
}
