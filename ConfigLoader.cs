using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public class ConfigLoader
{
    public Dictionary<string, JsonElement> settings;
    public ConfigLoader()
    {
        settings = LoadSettings("config.json");
    }
    
    private Dictionary<string, JsonElement> LoadSettings(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, options);
    }

}