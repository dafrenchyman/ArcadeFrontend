using Godot;
using System;
using System.IO;
using System.Text.Json;
using ArcadeFrontend;


public partial class TopLayer : Control
{
	// Constants
	private const int WM_FOCUS_OUT = 1005;
	private const int WM_FOCUS_IN = 1004;
	
	private MenuItemData _menu;
	private Wheel _wheel;
	
	// Exports
	[Export] public PackedScene WheelScene { get; set; }

	public TopLayer()
	{
		Console.Write("Hello");
		Console.Write(" ");
		Console.Write("World!");
	}
	
	public override void _Ready()
	{
		GetTree().Root.SizeChanged += OnRootViewportSizeChanged;
		
		// Create the action at runtime if it doesn't exist
		if (!InputMap.HasAction("toggle_fullscreen"))
		{
			InputMap.AddAction("toggle_fullscreen");
			InputMap.ActionAddEvent("toggle_fullscreen",
				new InputEventKey { PhysicalKeycode = Key.F });
		}
		
		// Load Menu Data
		var json = File.ReadAllText("config.json");
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			IncludeFields = true,
		};
		_menu = JsonSerializer.Deserialize<MenuItemData>(json, options);
		
		// Load the first wheel
		_wheel = WheelScene.Instantiate<Wheel>();
		AddChild(_wheel);
		
		_wheel.Start(this, _menu);

	}

	private void OnRootViewportSizeChanged()
	{
		GD.Print("Window size changed!");
		//var wheelArc = GetNode<Menu>("CanvasLayer/Control/Menu");
		//wheelArc._currentMenu.WindowResized();
		GD.Print($"New size: {GetTree().Root.Size}");
	}
	
	public override void _Notification(int what)
	{
		if (what == WM_FOCUS_OUT)
		{
			GD.Print("Lost focus — pausing");
			GetTree().Paused = true;
		}
		else if (what == WM_FOCUS_IN)
		{
			GD.Print("Gained focus — resuming");
			GetTree().Paused = false;
		}
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_fullscreen"))
			ToggleFullscreen();
	}

	private void ToggleFullscreen()
	{
		var win = GetWindow();
		if (win.Mode == Window.ModeEnum.Fullscreen || win.Mode == Window.ModeEnum.ExclusiveFullscreen)
			win.Mode = Window.ModeEnum.Windowed;
		else
			win.Mode = Window.ModeEnum.Fullscreen;
	}
}
