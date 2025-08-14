namespace ArcadeFrontend;
using Godot;

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
}