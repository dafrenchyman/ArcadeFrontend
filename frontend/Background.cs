using Godot;
using System;
using ArcadeFrontend;

public partial class Background : Control
{
	private PackedScene _scene;
	private Control _currentThemeInstance;
	private ColorRect _fadeRect;
	
	public override void _Ready()
	{
		// Create a fullscreen black rectangle for fade effect
		_fadeRect = new ColorRect();
		_fadeRect.Color = new Color(0, 0, 0, 1); // Black
		_fadeRect.Visible = false;
		_fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		//_fadeRect.SetAnchorsAndMarginsPreset(LayoutPreset.FullRect);
		AddChild(_fadeRect);
		MoveChild(_fadeRect, GetChildCount() - 1); // Move on top
	}

	public void RestartTheme()
	{
		UnloadCurrentTheme();
		if (_scene == null)
		{
			return;
		}
		_currentThemeInstance = _scene.Instantiate<Control>();
		_currentThemeInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		//_currentThemeInstance.Size = GetViewport().GetVisibleRect().Size;
		AddChild(_currentThemeInstance);
		//MoveChild(_currentThemeInstance, 0); // Put it behind everything else
	}


	public void UnloadCurrentTheme()
	{
		if (_currentThemeInstance != null)
		{
			UnloadUtil.ClearResourceRefs(_currentThemeInstance);
			RemoveChild(_currentThemeInstance);
			_currentThemeInstance.QueueFree();
			_currentThemeInstance = null;
		}
	}
	
	public async void ChangeTheme(ThemeDefinition? theme)
	{
		if (theme == null)
		{
			UnloadCurrentTheme();
			return;
		}

		GD.Print($"Switching to theme: {theme.NormalizedType()} → {theme.Path}");

		// Fade to black
		//await Fade(true);

		// Remove current theme
		UnloadCurrentTheme();

		string? themeType = theme.NormalizedType();
		if (string.Equals(themeType, ThemeType.HyperSpin, StringComparison.OrdinalIgnoreCase))
		{
			_scene = null;
			LoadHyperSpinTheme(theme.Path, theme.Video);
			return;
		}

		if (string.Equals(themeType, ThemeType.Video, StringComparison.OrdinalIgnoreCase))
		{
			_scene = null;
			LoadVideoTheme(theme.Path);
			return;
		}

		if (string.Equals(themeType, ThemeType.AnimatedImage, StringComparison.OrdinalIgnoreCase))
		{
			_scene = null;
			LoadAnimatedImageTheme(theme.Variants);
			return;
		}

			LoadGodotTheme(theme.Pck, theme.Path);

		// Fade in
		//await Fade(false);
	}

	private void LoadHyperSpinTheme(string? themePath, string? videoPath)
	{
		if (string.IsNullOrWhiteSpace(themePath))
		{
			GD.PushError("HyperSpin theme path is missing.");
			return;
		}

		try
		{
			var themeDefinition = HyperSpinThemeLoader.Load(themePath, videoPath);
			var host = new HyperSpinThemeHost();
			host.ZIndex = -4_000;
			AddChild(host);
			host.LoadTheme(themeDefinition);
			_currentThemeInstance = host;
		}
		catch (Exception exception)
		{
			GD.PushError($"Failed to load HyperSpin theme '{themePath}': {exception.Message}");
		}
	}

	private void LoadVideoTheme(string? videoPath)
	{
		if (string.IsNullOrWhiteSpace(videoPath))
		{
			GD.PushError("Video theme path is missing.");
			return;
		}

		string resolvedVideoPath = Utils.ResolvePath(videoPath);
		if (!System.IO.File.Exists(resolvedVideoPath))
		{
			GD.PushError($"Video theme file not found: {resolvedVideoPath}");
			return;
		}

		var host = new VideoThemeHost();
		host.ZIndex = -4_000;
		AddChild(host);
		host.LoadTheme(resolvedVideoPath);
		_currentThemeInstance = host;
	}

	private void LoadGodotTheme(string? pckPath, string? tscnPath)
	{
		if (string.IsNullOrWhiteSpace(tscnPath))
		{
			GD.PushError("Godot theme path is missing.");
			return;
		}

		if (!string.IsNullOrWhiteSpace(pckPath) && !ThemeManager.Instance.LoadThemePack(pckPath))
		{
			GD.PushError($"Failed to load PCK: {pckPath}");
			return;
		}

		string fullTscnPath = tscnPath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)
			? tscnPath
			: tscnPath + "/theme.tscn";
		var scene = GD.Load<PackedScene>(fullTscnPath);
		if (scene == null)
		{
			GD.PushError($"Failed to load scene: {fullTscnPath}");
			return;
		}

		_scene = scene;
		
		_currentThemeInstance = _scene.Instantiate<Control>();
		_currentThemeInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_currentThemeInstance.ZIndex = -4_000;
		//_currentThemeInstance.Size = GetViewport().GetVisibleRect().Size;
		//_currentThemeInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_currentThemeInstance);
		//MoveChild(_currentThemeInstance, -1_000); // Put it behind everything else
	}

	private void LoadAnimatedImageTheme(System.Collections.Generic.IReadOnlyList<string> imagePaths)
	{
		var host = new AnimatedImageThemeHost();
		host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		host.ZIndex = -4_000;
		AddChild(host);
		host.LoadTheme(imagePaths);
		_currentThemeInstance = host;
	}
	

}
