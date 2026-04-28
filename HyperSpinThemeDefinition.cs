using Godot;
using System.Collections.Generic;

namespace ArcadeFrontend;

public class HyperSpinThemeDefinition
{
	public const float BaseWidth = 1024.0f;
	public const float BaseHeight = 768.0f;

	public string ThemeXmlPath { get; set; }
	public string AssetRoot { get; set; }
	public string? BackgroundPath { get; set; }
	public HyperSpinThemeElement? Video { get; set; }
	public List<HyperSpinThemeElement> Artworks { get; set; } = new();
	public List<string> Warnings { get; set; } = new();
}

public class HyperSpinThemeElement
{
	public string Name { get; set; }
	public string? AssetPath { get; set; }
	public Vector2 Center { get; set; }
	public Vector2 Size { get; set; }
	public float RotationDegrees { get; set; }
	public float TimeSeconds { get; set; }
	public float DelaySeconds { get; set; }
	public string Start { get; set; } = "none";
	public string Rest { get; set; } = "none";
	public string Below { get; set; } = "yes";
	public HashSet<string> Effects { get; set; } = new();
	public List<HyperSpinBorderLayer> BorderLayers { get; set; } = new();
	public bool IsVideo { get; set; }

	public bool HasEffect(string effect)
	{
		return Effects.Contains(effect);
	}
}

public class HyperSpinBorderLayer
{
	public float Offset { get; set; }
	public long ColorValue { get; set; }
}
