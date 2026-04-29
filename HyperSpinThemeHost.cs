using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArcadeFrontend;

public partial class HyperSpinThemeHost : Control
{
	private sealed class ParticleEmitterRuntime
	{
		public Node2D Container { get; init; }
		public HyperSpinParticleDefinition Definition { get; init; }
		public double SpawnAccumulatorMs { get; set; }
		public List<ParticleInstanceRuntime> Particles { get; } = new();
	}

	private sealed class ParticleInstanceRuntime
	{
		public Sprite2D Sprite { get; init; }
		public Vector2 Velocity { get; set; }
		public float GravityPerSecond { get; init; }
		public float LifetimeMs { get; init; }
		public float FadeMs { get; init; }
		public double AgeMs { get; set; }
	}

private const string IntroEffectShaderCode = @"
shader_type canvas_item;

uniform float blur_strength : hint_range(0.0, 24.0) = 0.0;
uniform float pixel_size : hint_range(1.0, 32.0) = 1.0;
uniform float scanline_strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec2 uv = UV;
	if (pixel_size > 1.0) {
		vec2 texel = TEXTURE_PIXEL_SIZE * pixel_size;
		uv = floor(uv / texel) * texel + texel * 0.5;
	}

	vec4 color;
	if (blur_strength > 0.0) {
		vec2 texel = TEXTURE_PIXEL_SIZE * blur_strength;
		vec4 sum =
			texture(TEXTURE, uv + texel * vec2(-2.0, -2.0)) * 1.0 +
			texture(TEXTURE, uv + texel * vec2(-1.0, -2.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(0.0, -2.0)) * 7.0 +
			texture(TEXTURE, uv + texel * vec2(1.0, -2.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(2.0, -2.0)) * 1.0 +
			texture(TEXTURE, uv + texel * vec2(-2.0, -1.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(-1.0, -1.0)) * 16.0 +
			texture(TEXTURE, uv + texel * vec2(0.0, -1.0)) * 26.0 +
			texture(TEXTURE, uv + texel * vec2(1.0, -1.0)) * 16.0 +
			texture(TEXTURE, uv + texel * vec2(2.0, -1.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(-2.0, 0.0)) * 7.0 +
			texture(TEXTURE, uv + texel * vec2(-1.0, 0.0)) * 26.0 +
			texture(TEXTURE, uv) * 41.0 +
			texture(TEXTURE, uv + texel * vec2(1.0, 0.0)) * 26.0 +
			texture(TEXTURE, uv + texel * vec2(2.0, 0.0)) * 7.0 +
			texture(TEXTURE, uv + texel * vec2(-2.0, 1.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(-1.0, 1.0)) * 16.0 +
			texture(TEXTURE, uv + texel * vec2(0.0, 1.0)) * 26.0 +
			texture(TEXTURE, uv + texel * vec2(1.0, 1.0)) * 16.0 +
			texture(TEXTURE, uv + texel * vec2(2.0, 1.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(-2.0, 2.0)) * 1.0 +
			texture(TEXTURE, uv + texel * vec2(-1.0, 2.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(0.0, 2.0)) * 7.0 +
			texture(TEXTURE, uv + texel * vec2(1.0, 2.0)) * 4.0 +
			texture(TEXTURE, uv + texel * vec2(2.0, 2.0)) * 1.0;
		color = sum / 273.0;
	} else {
		color = texture(TEXTURE, uv);
	}

	if (scanline_strength > 0.0) {
		float line = 0.5 + 0.5 * sin(uv.y / TEXTURE_PIXEL_SIZE.y * 3.14159265);
		float scanline = mix(1.0 - scanline_strength, 1.0, line);
		color.rgb *= scanline;
		color.rgb *= vec3(1.02, 1.0, 0.96);
	}

	COLOR = color;
}";

	private Control _canvas;
	private HyperSpinThemeDefinition _theme;
	private readonly List<ParticleEmitterRuntime> _particleEmitters = new();

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);

		_canvas = new Control();
		_canvas.SetAnchorsPreset(LayoutPreset.TopLeft);
		_canvas.Position = Vector2.Zero;
		_canvas.Size = new Vector2(HyperSpinThemeDefinition.BaseWidth, HyperSpinThemeDefinition.BaseHeight);
		AddChild(_canvas);

		if (GetTree()?.Root != null)
		{
			GetTree().Root.SizeChanged += UpdateCanvasTransform;
		}
	}

	public override void _ExitTree()
	{
		if (GetTree()?.Root != null)
		{
			GetTree().Root.SizeChanged -= UpdateCanvasTransform;
		}
	}

	public override void _Process(double delta)
	{
		if (_particleEmitters.Count == 0)
		{
			return;
		}

		foreach (ParticleEmitterRuntime emitter in _particleEmitters)
		{
			UpdateParticleEmitter(emitter, delta);
		}
	}

	public void LoadTheme(HyperSpinThemeDefinition theme)
	{
		_theme = theme;
		if (_canvas == null)
		{
			_canvas = new Control();
			_canvas.SetAnchorsPreset(LayoutPreset.TopLeft);
			_canvas.Position = Vector2.Zero;
			_canvas.Size = new Vector2(HyperSpinThemeDefinition.BaseWidth, HyperSpinThemeDefinition.BaseHeight);
			AddChild(_canvas);
		}
		UpdateCanvasTransform();
		RenderTheme();
	}

	private void RenderTheme()
	{
		_particleEmitters.Clear();

		foreach (Node child in _canvas.GetChildren())
		{
			child.QueueFree();
		}

		if (_theme == null)
		{
			return;
		}

		if (_theme.Warnings.Count > 0)
		{
			foreach (string warning in _theme.Warnings)
			{
				GD.Print($"HyperSpin theme warning: {warning}");
			}
		}

		var renderQueue = new List<(string key, Action render)>();
		if (_theme.BackgroundTexture != null)
		{
			renderQueue.Add(("background", AddBackgroundNode));
		}

		HyperSpinThemeElement? backdropArtwork = _theme.Artworks
			.FirstOrDefault(artwork => string.Equals(artwork.Name, "artwork1", StringComparison.OrdinalIgnoreCase));
		List<HyperSpinThemeElement> foregroundArtwork = _theme.Artworks
			.Where(artwork => !string.Equals(artwork.Name, "artwork1", StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (_theme.Video != null && string.Equals(_theme.Video.Below, "yes", StringComparison.OrdinalIgnoreCase))
		{
			renderQueue.Add(("video", () => AddVideoNode(_theme.Video)));
		}

		if (backdropArtwork != null)
		{
			renderQueue.Add(("artwork1", () => AddArtworkNode(backdropArtwork)));
		}

		if (_theme.Video != null && !string.Equals(_theme.Video.Below, "yes", StringComparison.OrdinalIgnoreCase))
		{
			renderQueue.Add(("video", () => AddVideoNode(_theme.Video)));
		}

		foreach (HyperSpinThemeElement artwork in foregroundArtwork)
		{
			string key = artwork.Name.ToLowerInvariant();
			renderQueue.Add((key, () => AddArtworkNode(artwork)));
		}

		InsertParticleRender(renderQueue, _theme.Particle);

		foreach ((string _, Action render) in renderQueue)
		{
			render();
		}
	}

	private void UpdateCanvasTransform()
	{
		Vector2 viewportSize = GetViewportRect().Size;
		if (_canvas == null || viewportSize.X <= 0 || viewportSize.Y <= 0)
		{
			return;
		}

		_canvas.Scale = new Vector2(
			viewportSize.X / HyperSpinThemeDefinition.BaseWidth,
			viewportSize.Y / HyperSpinThemeDefinition.BaseHeight
		);
	}

	private Control? CreateArtworkNode(HyperSpinThemeElement element)
	{
		if (element.Texture == null)
		{
			return null;
		}

		var node = new TextureRect();
		node.Name = element.Name;
		node.Texture = element.Texture;
		node.StretchMode = TextureRect.StretchModeEnum.Scale;
		node.Size = element.Texture.GetSize();
		ApplyLayout(node, element);
		return node;
	}

	private void AddBackgroundNode()
	{
		var background = new TextureRect();
		background.Name = "Background";
		background.Texture = _theme.BackgroundTexture;
		background.Position = Vector2.Zero;
		background.Size = new Vector2(HyperSpinThemeDefinition.BaseWidth, HyperSpinThemeDefinition.BaseHeight);
		background.StretchMode = TextureRect.StretchModeEnum.Scale;
		_canvas.AddChild(background);
	}

	private Control? CreateVideoNode(HyperSpinThemeElement element)
	{
		if (string.IsNullOrWhiteSpace(element.AssetPath))
		{
			return null;
		}

		float maxOffset = element.BorderLayers.Count > 0 ? element.BorderLayers.Max(layer => layer.Offset) : 0.0f;
		Vector2 baseVideoPosition = new Vector2(maxOffset, maxOffset);
		Vector2 overlaySize = element.OverlayTexture?.GetSize() ?? Vector2.Zero;
		Vector2 baseOverlayPosition = baseVideoPosition;
		if (element.OverlayTexture != null)
		{
			// HyperSpin's Video.png behaves like a frame centered around the video slot,
			// with overlayoffsetx/y nudging that frame relative to the slot afterward.
			baseOverlayPosition += ((element.Size - overlaySize) / 2.0f) + element.OverlayOffset;
		}
		Vector2 minBounds = Vector2.Zero;
		Vector2 maxBounds = baseVideoPosition + element.Size;
		if (element.OverlayTexture != null)
		{
			Vector2 overlayMax = baseOverlayPosition + overlaySize;
			minBounds = new Vector2(
				Mathf.Min(minBounds.X, baseOverlayPosition.X),
				Mathf.Min(minBounds.Y, baseOverlayPosition.Y));
			maxBounds = new Vector2(
				Mathf.Max(maxBounds.X, overlayMax.X),
				Mathf.Max(maxBounds.Y, overlayMax.Y));
		}
		else
		{
			minBounds = new Vector2(
				Mathf.Min(minBounds.X, baseVideoPosition.X),
				Mathf.Min(minBounds.Y, baseVideoPosition.Y));
		}

		Vector2 shift = -minBounds;
		Vector2 videoPosition = baseVideoPosition + shift;
		Vector2 overlayPositionWithShift = baseOverlayPosition + shift;
		Vector2 localVideoCenter = videoPosition + (element.Size / 2.0f);
		Vector2 containerSize = maxBounds - minBounds;
		var container = new Control();
		container.Name = element.Name;
		container.Size = containerSize;
		container.MouseFilter = MouseFilterEnum.Ignore;

		foreach (Panel borderPanel in CreateBorderBands(element, maxOffset, shift))
		{
			container.AddChild(borderPanel);
		}

		if (element.OverlayBelow)
		{
			AddVideoOverlay(container, element, overlayPositionWithShift);
		}

		var stream = new VideoStreamTheora();
		stream.File = element.AssetPath;

		var video = new VideoStreamPlayer();
		video.Name = "Video";
		video.Stream = stream;
		video.Autoplay = true;
		video.Expand = true;
		video.Loop = true;
		video.MouseFilter = MouseFilterEnum.Ignore;
		video.Size = element.Size;
		video.Position = videoPosition;
		container.AddChild(video);

		if (!element.OverlayBelow)
		{
			AddVideoOverlay(container, element, overlayPositionWithShift);
		}

		ApplyVideoLayout(container, element, containerSize, localVideoCenter);
		return container;
	}

	private void ApplyLayout(Control node, HyperSpinThemeElement element, Vector2? explicitSize = null)
	{
		Vector2 size = explicitSize ?? element.Size;
		node.Size = size;
		node.Position = element.Center - (size / 2.0f);
		node.PivotOffset = size / 2.0f;
		node.Rotation = Mathf.DegToRad(element.RotationDegrees);
	}

	private void ApplyVideoLayout(Control node, HyperSpinThemeElement element, Vector2 containerSize, Vector2 localAnchorCenter)
	{
		node.Size = containerSize;
		node.Position = element.Center - localAnchorCenter;
		node.PivotOffset = localAnchorCenter;
		node.Rotation = Mathf.DegToRad(element.RotationDegrees);
	}

	private void PlayIntro(Control node, HyperSpinThemeElement element)
	{
		Vector2 finalPosition = node.Position;
		Vector2 finalScale = Vector2.One;
		Color finalModulate = node.Modulate;
		Node visualTarget = GetPrimaryVisualTarget(node);
		ShaderMaterial? shaderMaterial = AttachIntroShader(visualTarget, element);
		float startingBlur = element.HasEffect("blur") ? 40.0f : 0.0f;
		float startingPixelSize = element.HasEffect("pixelate") ? 50.0f : 1.0f;
		float scanlineStrength = element.HasEffect("tv") ? 0.18f : 0.0f;
		Vector2 startPosition = GetStartPosition(element, finalPosition);

		node.Position = startPosition;
		node.Scale = GetStartingScale(element, finalScale);
		node.Modulate = element.HasEffect("fade")
			? new Color(finalModulate.R, finalModulate.G, finalModulate.B, 0.0f)
			: finalModulate;

		if (shaderMaterial != null)
		{
			shaderMaterial.SetShaderParameter("blur_strength", startingBlur);
			shaderMaterial.SetShaderParameter("pixel_size", startingPixelSize);
			shaderMaterial.SetShaderParameter("scanline_strength", scanlineStrength);
		}

		float duration = element.TimeSeconds <= 0.0f ? 0.01f : element.TimeSeconds;
		var tween = CreateTween();
		if (element.DelaySeconds > 0.0f)
		{
			tween.TweenInterval(element.DelaySeconds);
		}

		Tween.TransitionType transition = GetTransition(element);
		Tween.EaseType ease = GetEase(element);

		tween.SetParallel(true);
		if (element.HasEffect("rain"))
		{
			tween.TweenMethod(
				Callable.From<float>(progress => node.Position = GetRainFloatPosition(element, startPosition, finalPosition, progress)),
				0.0f,
				1.0f,
				duration
			).SetEase(ease).SetTrans(transition);
		}
		else
		{
			tween.TweenProperty(node, "position", finalPosition, duration)
				.SetEase(ease)
				.SetTrans(transition);
		}
		tween.TweenProperty(node, "scale", finalScale, duration)
			.SetEase(ease)
			.SetTrans(transition);
		tween.TweenProperty(node, "modulate", finalModulate, duration)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(transition);

		if (shaderMaterial != null)
		{
			if (element.HasEffect("blur"))
			{
				tween.TweenMethod(
					Callable.From<float>(value => shaderMaterial.SetShaderParameter("blur_strength", value)),
					startingBlur,
					0.0f,
					duration
				).SetEase(ease).SetTrans(transition);
			}

			if (element.HasEffect("pixelate"))
			{
				tween.TweenMethod(
					Callable.From<float>(value => shaderMaterial.SetShaderParameter("pixel_size", value)),
					startingPixelSize,
					1.0f,
					duration
				).SetEase(ease).SetTrans(transition);
			}
		}
	}

	private Vector2 GetStartPosition(HyperSpinThemeElement element, Vector2 finalPosition)
	{
		return element.Start switch
		{
			"left" => new Vector2(-element.Size.X, finalPosition.Y),
			"right" => new Vector2(HyperSpinThemeDefinition.BaseWidth + element.Size.X, finalPosition.Y),
			"top" => new Vector2(finalPosition.X, -element.Size.Y),
			"bottom" => new Vector2(finalPosition.X, HyperSpinThemeDefinition.BaseHeight + element.Size.Y),
			_ => finalPosition,
		};
	}

	private Vector2 GetRainFloatPosition(HyperSpinThemeElement element, Vector2 startPosition, Vector2 finalPosition, float progress)
	{
		float clampedProgress = Mathf.Clamp(progress, 0.0f, 1.0f);
		float remaining = 1.0f - clampedProgress;
		float fallDistance = Mathf.Clamp(element.Size.Y * 0.22f, 36.0f, 96.0f);
		float swayDistance = Mathf.Clamp(element.Size.X * 0.08f, 12.0f, 36.0f);
		float floatDistance = element.HasEffect("float")
			? Mathf.Clamp(element.Size.Y * 0.04f, 8.0f, 20.0f)
			: 0.0f;
		Vector2 rainStart = startPosition;

		if (string.Equals(element.Start, "none", StringComparison.OrdinalIgnoreCase))
		{
			rainStart = finalPosition + new Vector2(-swayDistance * 0.5f, -fallDistance);
		}

		Vector2 position = rainStart.Lerp(finalPosition, clampedProgress);
		float sway = Mathf.Sin(clampedProgress * Mathf.Pi * 2.5f) * swayDistance * remaining;
		float bob = Mathf.Sin(clampedProgress * Mathf.Pi * 4.0f) * floatDistance * remaining;
		position += new Vector2(sway, bob);
		return position;
	}

	private void AddArtworkNode(HyperSpinThemeElement artwork)
	{
		Control? artworkNode = CreateArtworkNode(artwork);
		if (artworkNode == null)
		{
			return;
		}

		_canvas.AddChild(artworkNode);
		PlayIntro(artworkNode, artwork);
	}

	private void AddVideoNode(HyperSpinThemeElement element)
	{
		Control? videoNode = CreateVideoNode(element);
		if (videoNode == null)
		{
			return;
		}

		_canvas.AddChild(videoNode);
		PlayIntro(videoNode, element);
	}

	private void AddParticleNode(HyperSpinParticleDefinition definition)
	{
		if (definition.Texture == null)
		{
			return;
		}

		var container = new Node2D();
		container.Name = "Particles";
		_canvas.AddChild(container);
		_particleEmitters.Add(new ParticleEmitterRuntime
		{
			Container = container,
			Definition = definition,
		});
	}

	private Vector2 GetStartingScale(HyperSpinThemeElement element, Vector2 finalScale)
	{
		if (element.HasEffect("bounce"))
		{
			return finalScale * 0.65f;
		}

		if (element.HasEffect("grow"))
		{
			return finalScale * 0.3f;
		}

		return finalScale;
	}

	private Tween.TransitionType GetTransition(HyperSpinThemeElement element)
	{
		if (element.HasEffect("bounce"))
		{
			return Tween.TransitionType.Bounce;
		}

		if (element.HasEffect("ease"))
		{
			return Tween.TransitionType.Cubic;
		}

		if (element.HasEffect("grow"))
		{
			return Tween.TransitionType.Back;
		}

		return Tween.TransitionType.Sine;
	}

	private Tween.EaseType GetEase(HyperSpinThemeElement element)
	{
		if (element.HasEffect("bounce"))
		{
			return Tween.EaseType.Out;
		}

		if (element.HasEffect("ease"))
		{
			return Tween.EaseType.InOut;
		}

		if (element.HasEffect("grow"))
		{
			return Tween.EaseType.Out;
		}

		return Tween.EaseType.Out;
	}

	private Node GetPrimaryVisualTarget(Control node)
	{
		if (node is TextureRect || node is VideoStreamPlayer)
		{
			return node;
		}

		Node childVideo = node.GetNodeOrNull<Node>("Video");
		if (childVideo != null)
		{
			return childVideo;
		}

		return node;
	}

	private ShaderMaterial? AttachIntroShader(Node visualTarget, HyperSpinThemeElement element)
	{
		if (!element.HasEffect("blur") && !element.HasEffect("pixelate") && !element.HasEffect("tv"))
		{
			return null;
		}

		if (visualTarget is not CanvasItem canvasItem)
		{
			return null;
		}

		var material = new ShaderMaterial();
		material.Shader = new Shader { Code = IntroEffectShaderCode };
		canvasItem.Material = material;
		return material;
	}

	private void InsertParticleRender(List<(string key, Action render)> renderQueue, HyperSpinParticleDefinition? particle)
	{
		if (particle == null || particle.Texture == null)
		{
			return;
		}

		int targetIndex = renderQueue.FindIndex(entry => string.Equals(entry.key, particle.Depth, StringComparison.OrdinalIgnoreCase));
		if (targetIndex < 0)
		{
			targetIndex = 0;
			while (targetIndex < renderQueue.Count && string.Equals(renderQueue[targetIndex].key, "background", StringComparison.OrdinalIgnoreCase))
			{
				targetIndex++;
			}
		}
		else if (particle.ParticlesOnTop)
		{
			targetIndex++;
		}

		renderQueue.Insert(Mathf.Clamp(targetIndex, 0, renderQueue.Count), ("particle", () => AddParticleNode(particle)));
	}

	private void UpdateParticleEmitter(ParticleEmitterRuntime emitter, double delta)
	{
		double deltaMs = delta * 1000.0;
		emitter.SpawnAccumulatorMs += deltaMs;

		while (emitter.SpawnAccumulatorMs >= emitter.Definition.SpawnIntervalMs)
		{
			emitter.SpawnAccumulatorMs -= emitter.Definition.SpawnIntervalMs;
			SpawnParticle(emitter);
		}

		for (int index = emitter.Particles.Count - 1; index >= 0; index--)
		{
			ParticleInstanceRuntime particle = emitter.Particles[index];
			particle.AgeMs += deltaMs;
			if (particle.AgeMs >= particle.LifetimeMs)
			{
				particle.Sprite.QueueFree();
				emitter.Particles.RemoveAt(index);
				continue;
			}

			float deltaSeconds = (float)delta;
			particle.Velocity += new Vector2(0.0f, particle.GravityPerSecond * deltaSeconds);
			particle.Sprite.Position += particle.Velocity * deltaSeconds;

			float alpha = 1.0f;
			if (particle.FadeMs > 0.0f)
			{
				alpha = Mathf.Clamp((float)((particle.LifetimeMs - particle.AgeMs) / particle.FadeMs), 0.0f, 1.0f);
			}

			Color modulate = particle.Sprite.Modulate;
			modulate.A = alpha;
			particle.Sprite.Modulate = modulate;
		}
	}

	private void SpawnParticle(ParticleEmitterRuntime emitter)
	{
		HyperSpinParticleDefinition definition = emitter.Definition;
		if (definition.Texture == null)
		{
			return;
		}

		var sprite = new Sprite2D();
		sprite.Texture = definition.Texture;
		sprite.Centered = true;
		sprite.Position = GetRandomEmitterPoint(definition);

		float startScale = (float)GD.RandRange(definition.StartScaleRange.X, definition.StartScaleRange.Y);
		sprite.Scale = new Vector2(startScale, startScale);

		if (definition.MovementEnabled)
		{
			float speed = (float)GD.RandRange(definition.SpeedRange.X, definition.SpeedRange.Y);
			float angleDegrees = (float)GD.RandRange(definition.AngleRange.X, definition.AngleRange.Y);
			float angleRadians = Mathf.DegToRad(angleDegrees);
			Vector2 velocity = new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * speed;
			sprite.Rotation = angleRadians;
			emitter.Particles.Add(new ParticleInstanceRuntime
			{
				Sprite = sprite,
				Velocity = velocity,
				GravityPerSecond = definition.Gravity * 120.0f,
				LifetimeMs = definition.LifespanMs,
				FadeMs = definition.FadeMs,
			});
		}
		else
		{
			emitter.Particles.Add(new ParticleInstanceRuntime
			{
				Sprite = sprite,
				Velocity = Vector2.Zero,
				GravityPerSecond = 0.0f,
				LifetimeMs = definition.LifespanMs,
				FadeMs = definition.FadeMs,
			});
		}

		emitter.Container.AddChild(sprite);
	}

	private Vector2 GetRandomEmitterPoint(HyperSpinParticleDefinition definition)
	{
		float randomX = definition.EmitterSize.X <= 1.0f
			? 0.0f
			: (float)GD.RandRange(0.0f, definition.EmitterSize.X);
		float randomY = definition.EmitterSize.Y <= 1.0f
			? 0.0f
			: (float)GD.RandRange(0.0f, definition.EmitterSize.Y);
		return definition.EmitterPosition + new Vector2(randomX, randomY);
	}

	private void AddVideoOverlay(Control container, HyperSpinThemeElement element, Vector2 overlayPosition)
	{
		if (element.OverlayTexture == null)
		{
			return;
		}

		var overlay = new TextureRect();
		overlay.Name = "VideoOverlay";
		overlay.Texture = element.OverlayTexture;
		overlay.StretchMode = TextureRect.StretchModeEnum.Scale;
		overlay.MouseFilter = MouseFilterEnum.Ignore;
		overlay.Position = overlayPosition;
		overlay.Size = element.OverlayTexture.GetSize();
		container.AddChild(overlay);
	}

	private IEnumerable<Panel> CreateBorderBands(HyperSpinThemeElement element, float maxOffset, Vector2 shift)
	{
		for (int index = 0; index < element.BorderLayers.Count; index++)
		{
			HyperSpinBorderLayer currentLayer = element.BorderLayers[index];
			float nextOffset = index == element.BorderLayers.Count - 1 ? 0.0f : element.BorderLayers[index + 1].Offset;
			int borderWidth = Mathf.RoundToInt(currentLayer.Offset - nextOffset);
			if (borderWidth <= 0)
			{
				continue;
			}

			var panel = new Panel();
			panel.Name = $"Border{index + 1}";
			panel.MouseFilter = MouseFilterEnum.Ignore;
			panel.Position = shift + new Vector2(maxOffset - currentLayer.Offset, maxOffset - currentLayer.Offset);
			panel.Size = element.Size + new Vector2(2.0f * currentLayer.Offset, 2.0f * currentLayer.Offset);
			panel.AddThemeStyleboxOverride("panel", CreateBorderStyleBox(currentLayer.ColorValue, borderWidth));
			yield return panel;
		}
	}

	private StyleBoxFlat CreateBorderStyleBox(long colorValue, int borderWidth)
	{
		var styleBox = new StyleBoxFlat();
		styleBox.DrawCenter = false;
		styleBox.BorderColor = DecodeHyperSpinColor(colorValue);
		styleBox.BorderWidthLeft = borderWidth;
		styleBox.BorderWidthTop = borderWidth;
		styleBox.BorderWidthRight = borderWidth;
		styleBox.BorderWidthBottom = borderWidth;
		styleBox.BgColor = new Color(0, 0, 0, 0);
		return styleBox;
	}

	private Color DecodeHyperSpinColor(long colorValue)
	{
		byte red = (byte)((colorValue >> 16) & 0xFF);
		byte green = (byte)((colorValue >> 8) & 0xFF);
		byte blue = (byte)(colorValue & 0xFF);
		return Color.Color8(red, green, blue);
	}
}
