using Godot;
using System;

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
	
	public void _OldReady() {
		// Load scene
		string pckPath = "//home/mrsharky/dev/dafrenchyman/arcadeFrontendThemes/donkey_kong_theme.pck";
		if (ProjectSettings.LoadResourcePack(pckPath))
		{
			GD.Print("Theme loaded!");

			// Load a scene *inside* the .pck — path must match exactly.
			var scene = (PackedScene)ResourceLoader.Load("res://donkey_kong_country_2.tscn");
			if (scene != null)
			{
				var instance = scene.Instantiate();
				
				this.AddChild(instance);
			}
			else
			{
				GD.PrintErr("Could not load theme scene from pck.");
			}
		}
		else
		{
			GD.PrintErr("Failed to load .pck file.");
		}
	}

	public void RestartTheme()
	{
		UnloadCurrentTheme();
		_currentThemeInstance = _scene.Instantiate<Control>();
		_currentThemeInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		//_currentThemeInstance.Size = GetViewport().GetVisibleRect().Size;
		AddChild(_currentThemeInstance);
		MoveChild(_currentThemeInstance, 0); // Put it behind everything else
	}


	public void UnloadCurrentTheme()
	{
		if (_currentThemeInstance != null)
		{
			RemoveChild(_currentThemeInstance);
			_currentThemeInstance.QueueFree();
			_currentThemeInstance = null;
		}
	}
	
	public async void ChangeTheme(string pckPath, string tscnPath)
	{
		GD.Print($"Switching to theme: {pckPath} → {tscnPath}");

		// Fade to black
		//await Fade(true);

		// Remove current theme
		UnloadCurrentTheme();

		// Load .pck file
		if (!ProjectSettings.LoadResourcePack(pckPath))
		{
			GD.PushError($"Failed to load PCK: {pckPath}");
			return;
		}

		// Load .tscn from the pck
		var scene = GD.Load<PackedScene>(tscnPath);
		if (scene == null)
		{
			GD.PushError($"Failed to load scene: {tscnPath}");
			return;
		}

		_scene = scene;
		
		_currentThemeInstance = _scene.Instantiate<Control>();
		_currentThemeInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		//_currentThemeInstance.Size = GetViewport().GetVisibleRect().Size;
		//_currentThemeInstance.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_currentThemeInstance);
		MoveChild(_currentThemeInstance, 0); // Put it behind everything else

		// Fade in
		//await Fade(false);
	}
	

}
