using System.Collections.Generic;

namespace ArcadeFrontend;

public sealed record RuntimeSettingsSnapshot(
    IReadOnlyDictionary<string, string?> AppSettings,
    IReadOnlyList<SystemSettingsSnapshot> Systems);

public sealed class SystemSettingsSnapshot
{
    public string SystemId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public string? RomRootPath { get; init; }
    public string? DefaultEmulatorCommand { get; init; }
    public string? PreferredRegionCode { get; init; }
    public string? PreferredLanguageCode { get; init; }
    public string? LastScannedAt { get; init; }
}
