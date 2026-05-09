using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using ArcadeFrontend;

public partial class OverlayMenu : CanvasLayer
{
	
	private ColorRect _dimmer;
	private Control _content;
	private bool _closedEmitted = false;
	private MenuItemData _menuItem = null;
	
	
	[Export] private Button playButton { get; set; }
	[Export] private Button optionsButton { get; set; }
	[Export] private PopupMenu OptionsPopup { get; set; }
	[Export] private ItemInformation itemInformation { get; set; }
	
	[Signal] public delegate void ClosedEventHandler();
	[Signal] public delegate void OptionSelectedEventHandler(string option);
	
	public override void _Ready()
	{
		_dimmer = GetNode<ColorRect>(path:"./Overlay");
		_content = GetNode<Control>(path:"./../../Control"); // Panel or VBoxContainer
		//HideOverlay(immediate:false);
		
		// Set button pressed methods
		playButton.Pressed += OnPlayButtonPressed;
		optionsButton.Pressed += OnOptionsButtonPressed;
		
		// Set focus to the play button
		playButton.GrabFocus();
	}

	private void OnOptionsButtonPressed()
	{
		GD.Print("Options button pressed!");
		
	}
	private void OnPlayButtonPressed()
	{
		GD.Print("Play button pressed!");
		// Find the "default" menu item
		var versions = _menuItem.ItemInformation.Versions;
		var defaultVersion = versions.FirstOrDefault(v => v.Default);

		if (defaultVersion != null)
		{
			// Run the "default" item
			var psi = new ProcessStartInfo
			{
				FileName = "bash",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = false,
				RedirectStandardError = false,
			};
			psi.ArgumentList.Add("-c");
			psi.ArgumentList.Add(defaultVersion.LaunchCommand);

			// I don't think DISPLAY is needed anymore
			//psi.Environment["DISPLAY"] = ":10.0";
			psi.Environment["XAUTHORITY"] = System.Environment.GetEnvironmentVariable("XAUTHORITY");

			Process.Start(psi);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			this.Close();
		}

		if (@event.IsActionPressed("ui_accept"))
		{
			
		}
	}
	
	public void Close()
	{
		// Disable input on this class
		SetProcessUnhandledInput(enable: false);
		
		if (!_closedEmitted)
		{
			_closedEmitted = true;
			EmitSignal(SignalName.Closed);
		}
		
		// Remove 
		QueueFree();
	}

	public void Start(MenuItemData menuItem)
	{
		SetProcessUnhandledInput(enable: true);
		
		// Set the menuItem
		_menuItem = menuItem;
		
		// Fill out the data
		itemInformation.FillFields(_menuItem);
		
		// Fill out the menu
		foreach (var (item, index) in _menuItem.ItemInformation.Versions.Select((value, index) => (value, index)))
		{
			GD.Print($"Index: {index}, Value: {item}");
			OptionsPopup.AddItem(label:item.LaunchCommand, id:index);
		}
	}

	
	
	public void HideOverlay(bool immediate = false)
	{
		if (immediate)
		{
			_dimmer.Modulate = new Color(0,0,0,0);
			_content.Modulate = new Color(1,1,1,0);
			Visible = false;
			return;
		}

		var tw = CreateTween();
		tw.TweenProperty(_dimmer, "modulate:a", 0.0f, 0.2f);
		tw.TweenProperty(_content, "modulate:a", 0.0f, 0.2f)
			.Finished += () => Visible = false;
		_content.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	public void ShowOverlay()
	{
		Visible = true;
		var tw = CreateTween();
		tw.TweenProperty(_dimmer, property:"modulate:a", finalVal:0.5f, duration:0.2f); // fade background dim
		tw.TweenProperty(_content, property:"modulate:a", finalVal:1.0f, duration:0.2f);
		_content.MouseFilter = Control.MouseFilterEnum.Stop; // block clicks to background
	}
}
