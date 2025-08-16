using Godot;
using System;
using ArcadeFrontend;

public partial class OverlayMenu : CanvasLayer
{
	
	private ColorRect _dimmer;
	private Control _content;
	private bool _closedEmitted = false;
	
	
	[Export] private Button playButton { get; set; }
	[Export] private ItemInformation itemInformation { get; set; }
	
	[Signal] public delegate void ClosedEventHandler();
	[Signal] public delegate void OptionSelectedEventHandler(string option);
	
	public override void _Ready()
	{
		_dimmer = GetNode<ColorRect>(path:"./Overlay");
		_content = GetNode<Control>(path:"./../../Control"); // Panel or VBoxContainer
		//HideOverlay(immediate:false);
		
		// Set focus to the play button
		playButton.GrabFocus();
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
		
		// Fill out the data
		itemInformation.FillFields(menuItem);
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
