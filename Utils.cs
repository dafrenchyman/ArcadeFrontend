namespace ArcadeFrontend;
using Godot;
using System;
using System.IO;

public class Utils
{
    public static ImageTexture LoadExternalImage(string absolutePath)
    {
        // Load image from file
        var image = new Image();
        var err = image.Load(absolutePath);  // ← Absolute path with NO file:// prefix

        if (err != Error.Ok)
        {
            GD.PrintErr($"Failed to load image: {absolutePath}, Error: {err}");
            return null;
        }

        // Convert to a texture
        var texture = ImageTexture.CreateFromImage(image);
        return texture;
    }

    public static ImageTexture? LoadImageFromBuffer(byte[] bytes, string assetName)
    {
        if (bytes == null || bytes.Length == 0)
        {
            GD.PrintErr($"Failed to load image from buffer: {assetName}, buffer was empty.");
            return null;
        }

        string extension = Path.GetExtension(assetName).ToLowerInvariant();
        var image = new Image();
        Error err = extension switch
        {
            ".png" => image.LoadPngFromBuffer(bytes),
            ".jpg" or ".jpeg" => image.LoadJpgFromBuffer(bytes),
            ".webp" => image.LoadWebpFromBuffer(bytes),
            _ => image.LoadPngFromBuffer(bytes),
        };

        if (err != Error.Ok)
        {
            GD.PrintErr($"Failed to load image buffer: {assetName}, Error: {err}");
            return null;
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string projectRoot = ProjectSettings.GlobalizePath("res://");
        return Path.GetFullPath(path, projectRoot);
    }
}
