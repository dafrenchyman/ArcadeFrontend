using Godot;
using System;
using ArcadeFrontend;


public partial class TopLayer : Control
{
	// Constants
	private const int WM_FOCUS_OUT = 1005;
	private const int WM_FOCUS_IN = 1004;
	
	private MenuItemData _menu;
	private Wheel _wheel;
	private RuntimeBootstrap _runtimeBootstrap;
	private RootEscapeOverlay _rootEscapeOverlay;
	private ConfigurationOverlay _configurationOverlay;
	
	// Exports
	[Export] public PackedScene WheelScene { get; set; }

	public FrontendRuntimeStore RuntimeStore => _runtimeBootstrap.RuntimeStore;

	public string MasterDatabasePath => _runtimeBootstrap.MasterDatabasePath;

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
		
		_runtimeBootstrap = new RuntimeBootstrap();
		_menu = _runtimeBootstrap.LoadInitialMenu();
		
		// Load the first wheel
		_wheel = WheelScene.Instantiate<Wheel>();
		AddChild(_wheel);
		
		_wheel.Start(this, _menu);
	}

	public void OpenRootEscapeMenu()
	{
		if (_rootEscapeOverlay != null)
		{
			return;
		}

		_wheel?.SetInteractionEnabled(false);
		_rootEscapeOverlay = new RootEscapeOverlay();
		AddChild(_rootEscapeOverlay);
		_rootEscapeOverlay.ExitRequested += () => GetTree().Quit();
		_rootEscapeOverlay.ConfigurationRequested += OpenConfigurationOverlay;
		_rootEscapeOverlay.Closed += () =>
		{
			_rootEscapeOverlay = null;
			if (_configurationOverlay == null)
			{
				_wheel?.SetInteractionEnabled(true);
			}
		};
	}

	private void OpenConfigurationOverlay()
	{
		if (_configurationOverlay != null)
		{
			return;
		}

		_rootEscapeOverlay?.Close();

		_configurationOverlay = new ConfigurationOverlay(
			_runtimeBootstrap.RuntimeStore,
			new RuntimeLibraryBuilder(_runtimeBootstrap.RuntimeStore, _runtimeBootstrap.MasterDatabasePath),
			_runtimeBootstrap.MasterDatabasePath);
		AddChild(_configurationOverlay);
		_configurationOverlay.LibraryChanged += ReloadLibraryMenu;
		_configurationOverlay.Closed += () =>
		{
			_configurationOverlay = null;
			if (_rootEscapeOverlay == null)
			{
				_wheel?.SetInteractionEnabled(true);
			}
		};
	}

	private void ReloadLibraryMenu()
	{
		_menu = _runtimeBootstrap.LoadInitialMenu();
		if (_wheel != null)
		{
			RemoveChild(_wheel);
			_wheel.QueueFree();
		}

		_wheel = WheelScene.Instantiate<Wheel>();
		AddChild(_wheel);
		_wheel.Start(this, _menu);
		_wheel.SetInteractionEnabled(_rootEscapeOverlay == null && _configurationOverlay == null);
	}

	public override void _ExitTree()
	{
		_runtimeBootstrap?.Dispose();
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
