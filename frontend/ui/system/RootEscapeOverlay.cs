using System;
using Godot;

namespace ArcadeFrontend;

public partial class RootEscapeOverlay : CanvasLayer
{
    private Control _panel = null!;
    private Button _exitButton = null!;
    private Button _configButton = null!;
    private bool _closed;

    [Signal] public delegate void ExitRequestedEventHandler();
    [Signal] public delegate void ConfigurationRequestedEventHandler();
    [Signal] public delegate void ClosedEventHandler();

    public override void _Ready()
    {
        Layer = 50;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(dim);

        _panel = new PanelContainer
        {
            AnchorLeft = 0.35f,
            AnchorTop = 0.3f,
            AnchorRight = 0.65f,
            AnchorBottom = 0.7f,
        };
        AddChild(_panel);

        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 16);
        _panel.AddChild(layout);

        var title = new Label
        {
            Text = "Pause Menu",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 32);
        layout.AddChild(title);

        _configButton = new Button { Text = "Configuration" };
        _configButton.Pressed += () => EmitSignal(SignalName.ConfigurationRequested);
        layout.AddChild(_configButton);

        _exitButton = new Button { Text = "Exit" };
        _exitButton.Pressed += () => EmitSignal(SignalName.ExitRequested);
        layout.AddChild(_exitButton);

        _configButton.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        EmitSignal(SignalName.Closed);
        QueueFree();
    }
}
