using System;
using System.IO;
using System.Text.Json;

namespace ArcadeFrontend;

public sealed class RuntimeBootstrap : IDisposable
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    public RuntimeBootstrap()
    {
        RuntimeStore = new FrontendRuntimeStore(FrontendPaths.GetRuntimeDatabasePath());
        CompatibilityConfigPath = FrontendPaths.GetCompatibilityConfigPath();
        MasterDatabasePath = FrontendPaths.GetMasterDatabasePath();

        InitializeDefaults();
        RuntimeSettings = RuntimeStore.LoadSettings();
    }

    public FrontendRuntimeStore RuntimeStore { get; }

    public RuntimeSettingsSnapshot RuntimeSettings { get; }

    public string CompatibilityConfigPath { get; }

    public string MasterDatabasePath { get; }

    public bool MasterDatabaseExists => File.Exists(MasterDatabasePath);

    public MenuItemData LoadInitialMenu()
    {
        var runtimeLibraryBuilder = new RuntimeLibraryBuilder(RuntimeStore, MasterDatabasePath);
        if (MasterDatabaseExists && runtimeLibraryBuilder.HasRuntimeLibrary("snes"))
        {
            RuntimeStore.UpsertSetting("runtime_menu_source", "frontend-runtime-db");
            return runtimeLibraryBuilder.BuildRootMenu("snes");
        }

        if (!File.Exists(CompatibilityConfigPath))
        {
            throw new FileNotFoundException("Compatibility config.json was not found.", CompatibilityConfigPath);
        }

        var json = File.ReadAllText(CompatibilityConfigPath);
        var menu = JsonSerializer.Deserialize<MenuItemData>(json, _jsonOptions);
        if (menu == null)
        {
            throw new InvalidOperationException("Failed to deserialize compatibility config.json.");
        }

        RuntimeStore.UpsertSetting("compatibility_config_path", CompatibilityConfigPath);
        RuntimeStore.UpsertSetting("master_database_path", MasterDatabasePath);
        RuntimeStore.UpsertSetting("runtime_menu_source", "config-json");

        return menu;
    }

    public void Dispose()
    {
        RuntimeStore.Dispose();
    }

    private void InitializeDefaults()
    {
        RuntimeStore.UpsertSetting("master_database_path", MasterDatabasePath);
        RuntimeStore.UpsertSetting("compatibility_config_path", CompatibilityConfigPath);
        RuntimeStore.UpsertSetting("preferred_region_code", RuntimeStore.GetSetting("preferred_region_code") ?? "USA");
        RuntimeStore.UpsertSetting("preferred_language_code", RuntimeStore.GetSetting("preferred_language_code") ?? "EN");
        RuntimeStore.UpsertSetting("runtime_schema_version", FrontendRuntimeSchema.CurrentSchemaVersion.ToString());

        RuntimeStore.EnsureSystem(
            systemId: "snes",
            displayName: "Super Nintendo Entertainment System",
            preferredRegion: "USA",
            preferredLanguage: "EN");
    }
}
