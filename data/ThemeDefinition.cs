using System;

namespace ArcadeFrontend;

public static class ThemeType
{
	public const string Godot = "Godot";
	public const string HyperSpin = "HyperSpin";
	public const string Video = "Video";
}

public class ThemeDefinition
{
	public string? Type { get; set; }
	public string? Path { get; set; }
	public string? Pck { get; set; }
	public string? Video { get; set; }

	public bool IsValid()
	{
		return !string.IsNullOrWhiteSpace(NormalizedType());
	}

	public string? NormalizedType()
	{
			if (string.IsNullOrWhiteSpace(Type))
			{
				if (!string.IsNullOrWhiteSpace(Path) && string.Equals(System.IO.Path.GetExtension(Path), ".xml", StringComparison.OrdinalIgnoreCase))
				{
					return ThemeType.HyperSpin;
				}

				if (!string.IsNullOrWhiteSpace(Path) && string.Equals(System.IO.Path.GetExtension(Path), ".ogv", StringComparison.OrdinalIgnoreCase))
				{
					return ThemeType.Video;
				}

				if (!string.IsNullOrWhiteSpace(Pck) || !string.IsNullOrWhiteSpace(Path))
				{
					return ThemeType.Godot;
			}

			return null;
		}

		if (string.Equals(Type, ThemeType.HyperSpin, StringComparison.OrdinalIgnoreCase))
		{
			return ThemeType.HyperSpin;
		}

		if (string.Equals(Type, ThemeType.Godot, StringComparison.OrdinalIgnoreCase))
		{
			return ThemeType.Godot;
		}

		if (string.Equals(Type, ThemeType.Video, StringComparison.OrdinalIgnoreCase))
		{
			return ThemeType.Video;
		}

		return Type;
	}

	public ThemeDefinition Normalize()
	{
		return new ThemeDefinition
		{
			Type = NormalizedType(),
			Path = Path,
			Pck = Pck,
			Video = Video,
		};
	}

	public string? GetGodotResourceRoot()
	{
		if (string.IsNullOrWhiteSpace(Path))
		{
			return null;
		}

		if (Path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
		{
			return System.IO.Path.GetDirectoryName(Path)?.Replace("\\", "/");
		}

		return Path;
	}
}
