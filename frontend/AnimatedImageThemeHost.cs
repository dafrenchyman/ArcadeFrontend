using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArcadeFrontend;

public partial class AnimatedImageThemeHost : Control
{
    private const float MinZoom = 1.00f;
    private const float MaxZoom = 1.16f;
    private const float MaxBiasFactor = 0.35f;
    private const double MinSegmentSeconds = 18.0;
    private const double MaxSegmentSeconds = 28.0;

    private static readonly Dictionary<string, string> LastImageBySet = new(StringComparer.OrdinalIgnoreCase);
    private readonly RandomNumberGenerator _rng = new();

    private TextureRect _imageRect = null!;
    private Texture2D? _texture;
    private MotionPose _fromPose = new(MinZoom, Vector2.Zero);
    private MotionPose _toPose = new(MinZoom, Vector2.Zero);
    private MotionPose _currentPose = new(MinZoom, Vector2.Zero);
    private bool _zoomedIn;
    private double _segmentDuration;
    private double _segmentElapsed;
    private bool _isAnimating;

    public override void _Ready()
    {
        ClipContents = true;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _rng.Randomize();
        _imageRect = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_imageRect);
        Resized += HandleResized;
    }

    public void LoadTheme(IReadOnlyList<string> imagePaths)
    {
        var usablePaths = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (usablePaths.Count == 0)
        {
            GD.PushError("Animated image theme has no image paths.");
            return;
        }

        var imagePath = SelectImagePath(usablePaths);
        _texture = Utils.LoadExternalImage(imagePath);
        if (_texture == null)
        {
            GD.PushError($"Failed to load animated background image: {imagePath}");
            return;
        }

        _imageRect.Texture = _texture;
        _currentPose = new MotionPose(MinZoom, Vector2.Zero);
        _fromPose = _currentPose;
        _toPose = _currentPose;
        ApplyPose(_currentPose);
        BeginNextSegment();
    }

    private void HandleResized()
    {
        if (_texture == null)
        {
            return;
        }

        ApplyPose(_currentPose);
    }

    public override void _Process(double delta)
    {
        if (!_isAnimating || _texture == null)
        {
            return;
        }

        _segmentElapsed += delta;
        var progress = _segmentDuration <= 0.0
            ? 1.0f
            : (float)Math.Clamp(_segmentElapsed / _segmentDuration, 0.0, 1.0);
        var easedProgress = 0.5f - (0.5f * MathF.Cos(progress * Mathf.Pi));

        ApplyInterpolatedPose(_fromPose, _toPose, easedProgress);

        if (progress >= 1.0f)
        {
            _currentPose = _toPose;
            BeginNextSegment();
        }
    }

    private void BeginNextSegment()
    {
        if (_texture == null)
        {
            _isAnimating = false;
            return;
        }

        _fromPose = _currentPose;
        _toPose = BuildRandomPose();
        _segmentDuration = _rng.RandfRange((float)MinSegmentSeconds, (float)MaxSegmentSeconds);
        _segmentElapsed = 0.0;
        _isAnimating = true;
    }

    private string SelectImagePath(IReadOnlyList<string> usablePaths)
    {
        if (usablePaths.Count == 1)
        {
            return usablePaths[0];
        }

        var setKey = BuildSetKey(usablePaths);
        LastImageBySet.TryGetValue(setKey, out var previousPath);

        var candidates = usablePaths
            .Where(path => !string.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = usablePaths.ToList();
        }

        var selectedPath = candidates[(int)_rng.RandiRange(0, candidates.Count - 1)];
        LastImageBySet[setKey] = selectedPath;
        return selectedPath;
    }

    private static string BuildSetKey(IReadOnlyList<string> usablePaths)
    {
        return string.Join("|", usablePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    private MotionPose BuildRandomPose()
    {
        var zoom = BuildNextZoom();
        var bias = new Vector2(
            _rng.RandfRange(-MaxBiasFactor, MaxBiasFactor),
            _rng.RandfRange(-MaxBiasFactor, MaxBiasFactor));
        return new MotionPose(zoom, bias);
    }

    private float BuildNextZoom()
    {
        if (_zoomedIn)
        {
            _zoomedIn = false;
            return MinZoom;
        }

        _zoomedIn = true;
        return _rng.RandfRange(1.12f, MaxZoom);
    }

    private void ApplyInterpolatedPose(MotionPose start, MotionPose target, float progress)
    {
        var pose = new MotionPose(
            Mathf.Lerp(start.Zoom, target.Zoom, progress),
            start.Bias.Lerp(target.Bias, progress));
        ApplyPose(pose);
    }

    private void ApplyPose(MotionPose pose)
    {
        if (_texture == null || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        _currentPose = pose;

        Vector2 viewportSize = Size;
        Vector2 imageSize = _texture.GetSize();
        if (imageSize.X <= 0 || imageSize.Y <= 0)
        {
            return;
        }

        var baseScale = MathF.Max(viewportSize.X / imageSize.X, viewportSize.Y / imageSize.Y);
        var scaledSize = imageSize * baseScale * pose.Zoom;
        var overflow = new Vector2(
            MathF.Max(0.0f, scaledSize.X - viewportSize.X),
            MathF.Max(0.0f, scaledSize.Y - viewportSize.Y));
        var offset = new Vector2(
            overflow.X * 0.5f * pose.Bias.X,
            overflow.Y * 0.5f * pose.Bias.Y);

        _imageRect.Size = scaledSize;
        _imageRect.Position = ((viewportSize - scaledSize) * 0.5f) - offset;
    }

    private readonly record struct MotionPose(float Zoom, Vector2 Bias);
}
