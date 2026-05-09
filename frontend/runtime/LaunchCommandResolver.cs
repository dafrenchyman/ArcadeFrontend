using System.IO;

namespace ArcadeFrontend;

public sealed class LaunchCommandResolver
{
    private readonly FrontendRuntimeStore _runtimeStore;

    public LaunchCommandResolver(FrontendRuntimeStore runtimeStore)
    {
        _runtimeStore = runtimeStore;
    }

    public string? Resolve(string systemId, string gameKey, string releaseKey, string romPath)
    {
        var commandTemplate = ResolveTemplate(systemId, gameKey, releaseKey);
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            return null;
        }

        var quotedRomPath = Quote(romPath);
        return commandTemplate
            .Replace("\"{romPath}\"", quotedRomPath)
            .Replace("'{romPath}'", quotedRomPath)
            .Replace("{romPath}", quotedRomPath)
            .Replace("\"{gameKey}\"", QuoteLiteral(gameKey))
            .Replace("'{gameKey}'", QuoteLiteral(gameKey))
            .Replace("{gameKey}", gameKey)
            .Replace("\"{releaseKey}\"", QuoteLiteral(releaseKey))
            .Replace("'{releaseKey}'", QuoteLiteral(releaseKey))
            .Replace("{releaseKey}", releaseKey)
            .Replace("\"{systemId}\"", QuoteLiteral(systemId))
            .Replace("'{systemId}'", QuoteLiteral(systemId))
            .Replace("{systemId}", systemId);
    }

    private string? ResolveTemplate(string systemId, string gameKey, string releaseKey)
    {
        using var command = _runtimeStore.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT command_template
            FROM launch_overrides
            WHERE system_id = $systemId
              AND (
                    (scope_type = 'release' AND release_key = $releaseKey)
                 OR (scope_type = 'game' AND game_key = $gameKey)
                 OR (scope_type = 'system')
              )
            ORDER BY
              CASE scope_type
                WHEN 'release' THEN 1
                WHEN 'game' THEN 2
                WHEN 'system' THEN 3
                ELSE 4
              END
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$gameKey", gameKey);
        command.Parameters.AddWithValue("$releaseKey", releaseKey);
        return command.ExecuteScalar() as string ?? _runtimeStore.GetSystemDefaultCommand(systemId);
    }

    private static string Quote(string path)
    {
        return $"\"{Path.GetFullPath(path)}\"";
    }

    private static string QuoteLiteral(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
