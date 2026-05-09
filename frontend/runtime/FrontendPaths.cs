using System;
using System.IO;
using Godot;

namespace ArcadeFrontend;

public static class FrontendPaths
{
    public const string MasterDatabaseRelativePath = "database/database/unified_snes.db";
    public const string CompatibilityConfigPath = "config.json";
    public const string RuntimeDatabaseFileName = "frontend_runtime.db";
    public const string AssetCacheDirectoryName = "asset-cache";

    public static string GetProjectRoot()
    {
        return Directory.GetParent(ProjectSettings.GlobalizePath("res://"))!.FullName;
    }

    public static string GetMasterDatabasePath()
    {
        return Path.GetFullPath(Path.Combine(GetProjectRoot(), "..", MasterDatabaseRelativePath));
    }

    public static string GetRuntimeDataDirectory()
    {
        var path = ProjectSettings.GlobalizePath("user://");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetRuntimeDatabasePath()
    {
        return Path.Combine(GetRuntimeDataDirectory(), RuntimeDatabaseFileName);
    }

    public static string GetAssetCacheDirectory()
    {
        var path = Path.Combine(GetRuntimeDataDirectory(), AssetCacheDirectoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetCompatibilityConfigPath()
    {
        return Path.Combine(GetProjectRoot(), CompatibilityConfigPath);
    }
}
