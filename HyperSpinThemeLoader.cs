using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

		if (string.Equals(Path.GetExtension(absoluteThemePath), ".zip", StringComparison.OrdinalIgnoreCase))
		{
			return LoadFromZip(absoluteThemePath, videoOverridePath);
		}

		return LoadFromDirectory(absoluteThemePath, videoOverridePath);
	}

	private static HyperSpinThemeDefinition LoadFromDirectory(string absoluteThemePath, string? videoOverridePath)
	{
		string assetRoot = Path.GetDirectoryName(absoluteThemePath) ?? throw new InvalidOperationException("Theme XML must have a parent directory.");
		var document = XDocument.Load(absoluteThemePath);
		return BuildTheme(
			document,
			absoluteThemePath,
			assetRoot,
			relativePath => Utils.LoadExternalImage(Path.Combine(assetRoot, relativePath)),
			() => ResolveVideoPath(assetRoot, videoOverridePath, allowSiblingVideo: true));
	}

	private static HyperSpinThemeDefinition LoadFromZip(string zipPath, string? videoOverridePath)
	{
		using var archive = ZipFile.OpenRead(zipPath);
		ZipArchiveEntry themeXmlEntry = FindThemeXmlEntry(archive)
			?? throw new FileNotFoundException($"Theme.xml not found inside HyperSpin zip: {zipPath}");

		using var themeStream = themeXmlEntry.Open();
		var document = XDocument.Load(themeStream);
		string xmlDirectory = GetEntryDirectory(themeXmlEntry.FullName);
		string assetRoot = $"{zipPath}!/{xmlDirectory}".TrimEnd('/');

		return BuildTheme(
			document,
			$"{zipPath}!/{NormalizeZipPath(themeXmlEntry.FullName)}",
			assetRoot,
			relativePath => LoadZipImage(archive, CombineZipPath(xmlDirectory, relativePath)),
			() => ResolveVideoPath(zipPath, videoOverridePath, allowSiblingVideo: false));
	}

	private static HyperSpinThemeDefinition BuildTheme(
		XDocument document,
		string themeXmlPath,
		string assetRoot,
		Func<string, Texture2D?> loadImage,
		Func<string?> resolveVideoPath)
	{
		var root = document.Root ?? throw new InvalidOperationException("Theme XML is missing a root element.");
		var theme = new HyperSpinThemeDefinition
		{
			ThemeXmlPath = themeXmlPath,
			AssetRoot = assetRoot,
		};

		theme.BackgroundTexture = loadImage("Background.png");
		if (theme.BackgroundTexture == null)
		{
			theme.Warnings.Add("Background.png not found; theme will render without a background.");
		}

		foreach (XElement element in root.Elements())
		{
			string name = element.Name.LocalName;
			if (string.Equals(name, "video", StringComparison.OrdinalIgnoreCase))
			{
				theme.Video = ParseVideo(element, resolveVideoPath(), loadImage("Video.png"), theme.Warnings);
				continue;
			}

			if (name.StartsWith("artwork", StringComparison.OrdinalIgnoreCase))
			{
				HyperSpinThemeElement? artwork = ParseArtwork(name, element, theme.Warnings, loadImage);
				if (artwork != null)
				{
					theme.Artworks.Add(artwork);
				}

				continue;
			}

			if (string.Equals(name, "particle", StringComparison.OrdinalIgnoreCase))
			{
				theme.Particle = ParseParticle(element, loadImage("Particle.png"), theme.Warnings);
				continue;
			}

			theme.Warnings.Add($"Unsupported theme element '{name}' ignored.");
		}

		theme.Artworks = theme.Artworks
			.OrderBy(artwork => artwork.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return theme;
	}

	private static HyperSpinThemeElement ParseVideo(XElement element, string? resolvedVideoPath, Texture2D? overlayTexture, List<string> warnings)
	{
		if (string.IsNullOrWhiteSpace(resolvedVideoPath))
		{
			warnings.Add("No HyperSpin video asset found; theme video region will be skipped.");
		}

		var video = new HyperSpinThemeElement
		{
			Name = "video",
			AssetPath = resolvedVideoPath,
			OverlayTexture = overlayTexture,
			OverlayOffset = new Vector2(ParseFloat(element, "overlayoffsetx", 0.0f), ParseFloat(element, "overlayoffsety", 0.0f)),
			OverlayBelow = ParseBool(element, "overlaybelow", false),
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

	private static string? ResolveVideoPath(string assetRoot, string? videoOverridePath, bool allowSiblingVideo)
	{
		if (!string.IsNullOrWhiteSpace(videoOverridePath))
		{
			string resolvedOverride = Utils.ResolvePath(videoOverridePath);
			if (File.Exists(resolvedOverride))
			{
				return resolvedOverride;
			}
		}

		if (!allowSiblingVideo)
		{
			return null;
		}

		string siblingVideoPath = Path.Combine(assetRoot, "video.ogv");
		return File.Exists(siblingVideoPath) ? siblingVideoPath : null;
	}

	private static HyperSpinParticleDefinition? ParseParticle(XElement element, Texture2D? particleTexture, List<string> warnings)
	{
		if (!ParseBool(element, "onoff", true))
		{
			return null;
		}

		if (particleTexture == null)
		{
			warnings.Add("Particle.png not found; particle emitter will be skipped.");
			return null;
		}

		foreach (XAttribute attribute in element.Attributes())
		{
			if (!SupportedParticleAttributes.Contains(attribute.Name.LocalName))
			{
				warnings.Add($"Unsupported particle attribute '{attribute.Name.LocalName}' ignored.");
			}
		}

		return new HyperSpinParticleDefinition
		{
			Texture = particleTexture,
			Depth = ParseString(element, "depth", "background"),
			ParticlesOnTop = ParseBool(element, "particlesontop", true),
			SpawnIntervalMs = Mathf.Max(ParseFloat(element, "ppm", 250.0f), 1.0f),
			EmitterPosition = new Vector2(ParseFloat(element, "x", 0.0f), ParseFloat(element, "y", 0.0f)),
			EmitterSize = new Vector2(ParseFloat(element, "width", 1.0f), ParseFloat(element, "height", 1.0f)),
			MovementEnabled = ParseBool(element, "movement", true),
			SpeedRange = ParseRange(element, "speed", 0.0f, 0.0f),
			AngleRange = ParseRange(element, "angle", 0.0f, 0.0f),
			StartScaleRange = ParseRange(element, "startScale", 1.0f, 1.0f),
			Gravity = ParseFloat(element, "gravity", 0.0f),
			FadeMs = Mathf.Max(ParseFloat(element, "fade", 0.0f), 0.0f),
			LifespanMs = Mathf.Max(ParseFloat(element, "lifespan", 4000.0f), 1.0f),
		};
	}

	private static HyperSpinThemeElement? ParseArtwork(string name, XElement element, List<string> warnings, Func<string, Texture2D?> loadImage)
	{
		string artworkNumber = name["artwork".Length..];
		string assetName = $"Artwork{artworkNumber}.png";
		Texture2D? texture = loadImage(assetName);
		if (texture == null)
		{
			warnings.Add($"{assetName} not found; {name} will be skipped.");
			return null;
		}

		Vector2 size = texture.GetSize();

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
			Texture = texture,
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

	private static Texture2D? LoadZipImage(ZipArchive archive, string entryPath)
	{
		ZipArchiveEntry? entry = FindEntryCaseInsensitive(archive, entryPath);
		if (entry == null)
		{
			return null;
		}

		using var stream = entry.Open();
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return Utils.LoadImageFromBuffer(memory.ToArray(), entry.Name);
	}

	private static ZipArchiveEntry? FindThemeXmlEntry(ZipArchive archive)
	{
		return archive.Entries
			.Where(entry => string.Equals(Path.GetFileName(entry.FullName), "Theme.xml", StringComparison.OrdinalIgnoreCase))
			.OrderBy(entry => NormalizeZipPath(entry.FullName).Count(character => character == '/'))
			.FirstOrDefault();
	}

	private static ZipArchiveEntry? FindEntryCaseInsensitive(ZipArchive archive, string entryPath)
	{
		string normalizedPath = NormalizeZipPath(entryPath);
		return archive.Entries.FirstOrDefault(entry =>
			string.Equals(NormalizeZipPath(entry.FullName), normalizedPath, StringComparison.OrdinalIgnoreCase));
	}

	private static string GetEntryDirectory(string entryPath)
	{
		string normalizedPath = NormalizeZipPath(entryPath);
		int separatorIndex = normalizedPath.LastIndexOf('/');
		return separatorIndex >= 0 ? normalizedPath[..separatorIndex] : string.Empty;
	}

	private static string CombineZipPath(string directory, string relativePath)
	{
		string normalizedDirectory = NormalizeZipPath(directory);
		string normalizedRelativePath = NormalizeZipPath(relativePath);
		return string.IsNullOrWhiteSpace(normalizedDirectory)
			? normalizedRelativePath
			: $"{normalizedDirectory}/{normalizedRelativePath}";
	}

	private static string NormalizeZipPath(string path)
	{
		return path.Replace('\\', '/').TrimStart('/');
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

	private static bool ParseBool(XElement element, string attributeName, bool defaultValue)
	{
		string normalized = ParseString(element, attributeName, defaultValue ? "true" : "false");
		return normalized switch
		{
			"true" => true,
			"yes" => true,
			"1" => true,
			"false" => false,
			"no" => false,
			"0" => false,
			_ => defaultValue,
		};
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

	private static Vector2 ParseRange(XElement element, string attributeName, float defaultMin, float defaultMax)
	{
		string? value = element.Attribute(attributeName)?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			return new Vector2(defaultMin, defaultMax);
		}

		string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float single))
		{
			return new Vector2(single, single);
		}

		if (parts.Length >= 2
			&& float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float min)
			&& float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float max))
		{
			return new Vector2(min, max);
		}

		return new Vector2(defaultMin, defaultMax);
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

	private static readonly HashSet<string> SupportedParticleAttributes = new(StringComparer.OrdinalIgnoreCase)
	{
		"onoff",
		"depth",
		"ppm",
		"x",
		"y",
		"width",
		"height",
		"movement",
		"speed",
		"angle",
		"randomframe",
		"startscale",
		"limit",
		"gravity",
		"accel",
		"fade",
		"bound",
		"pointswarm",
		"xoscillate",
		"rotate",
		"scale",
		"lifespan",
		"particlesontop",
		"blendmode",
		"rotatetoangle",
	};
}
