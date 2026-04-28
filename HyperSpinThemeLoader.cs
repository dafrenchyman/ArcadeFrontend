using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ArcadeFrontend;

public static class HyperSpinThemeLoader
{
	public static HyperSpinThemeDefinition Load(string themeXmlPath, string? videoOverridePath = null)
	{
		string absoluteThemePath = Utils.ResolvePath(themeXmlPath);
		if (!File.Exists(absoluteThemePath))
		{
			throw new FileNotFoundException($"HyperSpin theme XML not found: {absoluteThemePath}");
		}

		string assetRoot = Path.GetDirectoryName(absoluteThemePath) ?? throw new InvalidOperationException("Theme XML must have a parent directory.");
		var document = XDocument.Load(absoluteThemePath);
		var root = document.Root ?? throw new InvalidOperationException("Theme XML is missing a root element.");

		var theme = new HyperSpinThemeDefinition
		{
			ThemeXmlPath = absoluteThemePath,
			AssetRoot = assetRoot,
		};

		string backgroundPath = Path.Combine(assetRoot, "Background.png");
		if (File.Exists(backgroundPath))
		{
			theme.BackgroundPath = backgroundPath;
		}
		else
		{
			theme.Warnings.Add("Background.png not found; theme will render without a background.");
		}

		foreach (XElement element in root.Elements())
		{
				string name = element.Name.LocalName;
				if (string.Equals(name, "video", StringComparison.OrdinalIgnoreCase))
				{
					theme.Video = ParseVideo(assetRoot, element, videoOverridePath, theme.Warnings);
					continue;
				}

			if (name.StartsWith("artwork", StringComparison.OrdinalIgnoreCase))
			{
				HyperSpinThemeElement? artwork = ParseArtwork(assetRoot, name, element, theme.Warnings);
				if (artwork != null)
				{
					theme.Artworks.Add(artwork);
				}

				continue;
			}

			theme.Warnings.Add($"Unsupported theme element '{name}' ignored.");
		}

		theme.Artworks = theme.Artworks
			.OrderBy(artwork => artwork.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return theme;
	}

	private static HyperSpinThemeElement ParseVideo(string assetRoot, XElement element, string? videoOverridePath, List<string> warnings)
	{
		string? resolvedVideoPath = ResolveVideoPath(assetRoot, videoOverridePath);
		if (string.IsNullOrWhiteSpace(resolvedVideoPath))
		{
			warnings.Add("No HyperSpin video asset found; theme video region will be skipped.");
		}

		var video = new HyperSpinThemeElement
		{
			Name = "video",
			AssetPath = resolvedVideoPath,
			Center = new Vector2(ParseFloat(element, "x", 0.0f), ParseFloat(element, "y", 0.0f)),
			Size = new Vector2(ParseFloat(element, "w", 0.0f), ParseFloat(element, "h", 0.0f)),
			RotationDegrees = NormalizeRotation(ParseFloat(element, "r", 0.0f)),
			TimeSeconds = ParseFloat(element, "time", 0.0f),
			DelaySeconds = ParseFloat(element, "delay", 0.0f),
			Start = ParseString(element, "start", "none"),
			Rest = ParseString(element, "rest", "none"),
			Below = ParseString(element, "below", "yes"),
			Effects = ParseEffects(element),
			BorderLayers = ParseBorderLayers(element),
			IsVideo = true,
		};

		foreach (XAttribute attribute in element.Attributes())
		{
			if (!SupportedVideoAttributes.Contains(attribute.Name.LocalName))
			{
				warnings.Add($"Unsupported video attribute '{attribute.Name.LocalName}' ignored.");
			}
		}

		return video;
	}

	private static string? ResolveVideoPath(string assetRoot, string? videoOverridePath)
	{
		if (!string.IsNullOrWhiteSpace(videoOverridePath))
		{
			string resolvedOverride = Utils.ResolvePath(videoOverridePath);
			if (File.Exists(resolvedOverride))
			{
				return resolvedOverride;
			}
		}

		string siblingVideoPath = Path.Combine(assetRoot, "video.ogv");
		return File.Exists(siblingVideoPath) ? siblingVideoPath : null;
	}

	private static HyperSpinThemeElement? ParseArtwork(string assetRoot, string name, XElement element, List<string> warnings)
	{
		string artworkNumber = name["artwork".Length..];
		string assetPath = Path.Combine(assetRoot, $"Artwork{artworkNumber}.png");
		if (!File.Exists(assetPath))
		{
			warnings.Add($"{Path.GetFileName(assetPath)} not found; {name} will be skipped.");
			return null;
		}

		Vector2 size = Vector2.Zero;
		var texture = Utils.LoadExternalImage(assetPath);
		if (texture != null)
		{
			size = texture.GetSize();
		}
		else
		{
			warnings.Add($"{Path.GetFileName(assetPath)} could not be loaded; {name} will be skipped.");
			return null;
		}

		foreach (XAttribute attribute in element.Attributes())
		{
			if (!SupportedArtworkAttributes.Contains(attribute.Name.LocalName))
			{
				warnings.Add($"Unsupported {name} attribute '{attribute.Name.LocalName}' ignored.");
			}
		}

		return new HyperSpinThemeElement
		{
			Name = name,
			AssetPath = assetPath,
			Center = new Vector2(ParseFloat(element, "x", 0.0f), ParseFloat(element, "y", 0.0f)),
			Size = size,
			RotationDegrees = NormalizeRotation(ParseFloat(element, "r", 0.0f)),
			TimeSeconds = ParseFloat(element, "time", 0.0f),
			DelaySeconds = ParseFloat(element, "delay", 0.0f),
			Start = ParseString(element, "start", "none"),
			Rest = ParseString(element, "rest", "none"),
			Effects = ParseEffects(element),
			IsVideo = false,
		};
	}

	private static HashSet<string> ParseEffects(XElement element)
	{
		string type = ParseString(element, "type", "none");
		return type
			.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(effect => effect.ToLowerInvariant())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	private static string ParseString(XElement element, string attributeName, string defaultValue)
	{
		string? value = element.Attribute(attributeName)?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			return defaultValue;
		}

		return value.Trim().ToLowerInvariant();
	}

	private static float ParseFloat(XElement element, string attributeName, float defaultValue)
	{
		string? value = element.Attribute(attributeName)?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			return defaultValue;
		}

		if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
		{
			return parsed;
		}

		return defaultValue;
	}

	private static long ParseLong(XElement element, string attributeName, long defaultValue)
	{
		string? value = element.Attribute(attributeName)?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			return defaultValue;
		}

		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
		{
			return parsed;
		}

		return defaultValue;
	}

	private static List<HyperSpinBorderLayer> ParseBorderLayers(XElement element)
	{
		var layers = new List<HyperSpinBorderLayer>();

		AddBorderLayer(layers, ParseFloat(element, "bsize", 0.0f), ParseLong(element, "bcolor", 0L));
		AddBorderLayer(layers, ParseFloat(element, "bsize2", 0.0f), ParseLong(element, "bcolor2", 0L));
		AddBorderLayer(layers, ParseFloat(element, "bsize3", 0.0f), ParseLong(element, "bcolor3", 0L));

		return layers
			.Where(layer => layer.Offset > 0.0f)
			.OrderByDescending(layer => layer.Offset)
			.ToList();
	}

	private static void AddBorderLayer(List<HyperSpinBorderLayer> layers, float offset, long colorValue)
	{
		if (offset <= 0.0f)
		{
			return;
		}

		layers.Add(new HyperSpinBorderLayer
		{
			Offset = offset,
			ColorValue = colorValue,
		});
	}

	private static float NormalizeRotation(float rotationDegrees)
	{
		float normalized = rotationDegrees % 360.0f;
		return normalized < 0.0f ? normalized + 360.0f : normalized;
	}

	private static readonly HashSet<string> SupportedArtworkAttributes = new(StringComparer.OrdinalIgnoreCase)
	{
		"x",
		"y",
		"r",
		"time",
		"delay",
		"type",
		"start",
		"rest",
	};

	private static readonly HashSet<string> SupportedVideoAttributes = new(StringComparer.OrdinalIgnoreCase)
	{
		"w",
		"h",
		"x",
		"y",
		"r",
		"time",
		"delay",
		"type",
		"start",
		"rest",
		"below",
		"forceaspect",
		"overlaybelow",
		"overlayoffsetx",
		"overlayoffsety",
		"rx",
		"ry",
		"bsize",
		"bsize2",
		"bsize3",
		"bcolor",
		"bcolor2",
		"bcolor3",
		"bshape",
	};
}
