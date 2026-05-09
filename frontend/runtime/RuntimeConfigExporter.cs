using System.IO;
using System.Text.Json;

namespace ArcadeFrontend;

public sealed class RuntimeConfigExporter
{
    private readonly RuntimeLibraryBuilder _libraryBuilder;
    private readonly FrontendRuntimeStore _runtimeStore;

    public RuntimeConfigExporter(RuntimeLibraryBuilder libraryBuilder, FrontendRuntimeStore runtimeStore)
    {
        _libraryBuilder = libraryBuilder;
        _runtimeStore = runtimeStore;
    }

    public string Export(string systemId)
    {
        var menu = _libraryBuilder.BuildRootMenu(systemId);
        var exportPath = Path.Combine(FrontendPaths.GetProjectRoot(), "config.export.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
        };

        File.WriteAllText(exportPath, JsonSerializer.Serialize(menu, options));
        _runtimeStore.UpsertSetting("last_export_path", exportPath);
        return exportPath;
    }
}
