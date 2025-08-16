using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using ArcadeFrontend;

public partial class Wheel : CanvasLayer
{
	private Node _parentNode;
	private float _arcRadians = 3.0f/4.0f *Single.Pi; //(1.0f / 4.0f) * Single.Pi ; // total spread of the arc

	private int _numItems = 8;
	private int _extraItems = 2;
	private int _totalItemsInDirection;
	private Vector2 _screenSize;
	private Timer _inactivityTimer;
	private const float StartingInactivitySeconds = 3.0f;
	private const float SubsequentInactivitySeconds = 3.0f;
	
	private int _currIndex = 0;
	private float _itemScaleRatio = 0.8f;
	private float _itemRotationRatio = 5.0f;
	private float _itemPerHeight = 10.0f;
	private float _rotationDuration = 0.2f;
	private float _fadeDuration = 0.5f;
	private Tween _spinningTween;
	private Tween _pulseTween;
	private Vector2 _sizePriorToPulse;
	private Node2D _pivot;
	private Dictionary<int, Node2D> _arcPoints = new Dictionary<int, Node2D>();
	private MenuItemData _menuData;
	private MenuPath _currentMenuLocation ;

	private bool _debug = false;

	private Dictionary<int, MenuItemData> _menuDepth = new Dictionary<int, MenuItemData>();
	private int _currDepth;
	private OverlayMenu _overlay;
	private bool _closedEmitted = false;
	//[Export] public PackedScene WheelScene { get; set; }
	
	[Export] public PackedScene OverlayMenuScene { get; set; }
	
	[Export] private Label _gameNameLabel { get; set; }
	[Export] private Background _background { get; set; }
	[Export] private Control _menuNode { get; set; }
	
	[Signal] public delegate void ClosedEventHandler();
	
	
	public void Start(Node parentNode, MenuItemData menuData)
	{
		this._parentNode = parentNode;
		this._menuData = menuData;
		
		// Set globals
		_totalItemsInDirection = this._numItems + this._extraItems;
		_screenSize = _menuNode.GetViewportRect().Size;
		
		// Create an inactivity timer
		_inactivityTimer = new Timer();
		_inactivityTimer.WaitTime = StartingInactivitySeconds; // Longer timeout on first start
		_inactivityTimer.OneShot = true;
		_inactivityTimer.Autostart = false;
		_menuNode.AddChild(_inactivityTimer);
		_inactivityTimer.Timeout += OnInactivityTimeout;
		_inactivityTimer.Start();
		
		// Find location of arc center
		var screenHeight = _menuNode.Size.Y;
		float radius = (screenHeight / 2.0f) / Convert.ToSingle(Math.Sin(_arcRadians / 2.0f));
		float xOffset = radius * Convert.ToSingle(Math.Cos(_arcRadians / 2.0f));
		
		// Create node at this position
		this._pivot = new Node2D();
		_menuNode.AddChild(_pivot);
		_pivot.Position = new Vector2(xOffset, screenHeight / 2.0f);
		_pivot.Rotation = Single.Pi; // Rotate to the middle of the screen
		
		// Rotate the node some more based off _currIndex
		//_pivot.Rotation += _RotateMenuItem(_currIndex, 0);
		
		// Create default items
		_currentMenuLocation = new MenuPath(new[] { 0 });
		MenuPath path = null;
		for (int index = -_totalItemsInDirection + 1; index < _totalItemsInDirection; index++)
		{
			this.AddMenuItem(index, 0);
		}
		
		StartPulse();
		
		// Call theme switch
		this.ThemeSwitch();
		
	}
	
	public void Remove()
	{
		_background.UnloadCurrentTheme();
		_background = null;
		_parentNode.RemoveChild(_menuNode);
		_menuNode.QueueFree();
		_pivot.QueueFree();
		_pivot = null;
		_menuNode = null;
		_menuData = null;
		//ResourceLoader.UnloadUnusedResources();
		GC.Collect();
		GC.WaitForPendingFinalizers();
	}
	
	private void OnInactivityTimeout()
	{
		// Fade out
		var fadeTween = _menuNode.CreateTween();
		Color current = _menuNode.Modulate;
		Color target = new Color(current.R, current.G, current.B, 0.0f);

		fadeTween.TweenProperty(_menuNode, "modulate", target, _fadeDuration)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Sine);
	}
	
	private int _ZIndexMenuItem(int index)
	{
		int newZIndex = -Math.Abs(index + _currIndex);
		return newZIndex;
	}
	
	private Vector2 _GenerateOffset(int index)
	{
		float screenHeight = _menuNode.Size.Y;
		float radius = (screenHeight / 2.0f) / Convert.ToSingle(Math.Sin(_arcRadians / 2.0f));

		// Create node at index location along the arc
		float t = (float)index / (_numItems - 1); // 0 to 1 (for numItems)
		float angleRad = t * _arcRadians / 2.0f;

		Vector2 offset = new Vector2(
			radius * Mathf.Cos(angleRad),
			radius * Mathf.Sin(angleRad)
		);

		return offset;
	}
	
	public static ImageTexture LoadExternalImage(string absolutePath)
	{
		// Load image from file
		var image = new Image();
		var err = image.Load(absolutePath);  // ← Absolute path with NO file:// prefix

		if (err != Error.Ok)
		{
			GD.PrintErr($"Failed to load image: {absolutePath}, Error: {err}");
			return null;
		}

		// Convert to a texture
		var texture = ImageTexture.CreateFromImage(image);
		return texture;
	}
	
	private float _RotateMenuItem(int index, int direction)
	{
		int indexRelativeToCenter = (index + _currIndex);
		float rotation = ((float) indexRelativeToCenter / (_itemRotationRatio*_totalItemsInDirection))*Single.Pi;
		return rotation;
	}
	
	private Color _FadeMenuItem(int index)
	{
		int indexRelativeToCenter = Math.Abs(index + _currIndex);
		float colorValue = ((float)indexRelativeToCenter / (2.0f * _totalItemsInDirection));
		Color color = new Color(1, 1, 1, 1.0f - colorValue);  // RGBA
		return color;
	} 
	
	private float _scaleMenuItem(int index, int direction, float inputHeight)
	{
		// Scale to a uniform height on the Y-axis
		float desiredHeight = (_screenSize.Y / _itemPerHeight);
		float scaleRatio = desiredHeight / inputHeight;
			
		// Scale based on vertical position to fake 3D depth
		float scaler = 1.0f - Math.Abs((float) (index + _currIndex + direction) / (_totalItemsInDirection + 1.0f)) * _itemScaleRatio;
		scaleRatio = scaleRatio * scaler;
		return scaleRatio;
	}
	
	private void StartPulse()
	{
		if (_pulseTween != null && _pulseTween.IsRunning())
			return;
		
		// Get the "center" node
		var node = this._arcPoints[-_currIndex];
		Node2D textureNode = node.GetNode<Node2D>("TextureNode");
		_sizePriorToPulse = new Vector2(textureNode.Scale.X, textureNode.Scale.Y);
		var newScale = textureNode.Scale * 1.25f;
		
		_pulseTween = _menuNode.CreateTween();
		_pulseTween.SetLoops(); // loops forever
		_pulseTween.TweenProperty(textureNode, "scale", newScale, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_pulseTween.TweenProperty(textureNode, "scale", _sizePriorToPulse, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		
	}
	
	private void AddMenuItem(int currIndex, int direction)
	{
		if (_spinningTween != null && _spinningTween.IsRunning())
			return;

		// Add new item at the top/bottom
		int index = direction * (_numItems + _extraItems) - currIndex;
		var path = new MenuPath(new[] { index });
		MenuItemData menuItem = _menuData.GetMenuItem(path);

		// Create node at each point along the arc
		Vector2 offset = this._GenerateOffset(index);

		Node2D node = new Node2D();

		_pivot.AddChild(node);
		node.Position = offset;
		node.GlobalRotation = 0;
		
		// Calculate the zindex
		node.ZIndex = this._ZIndexMenuItem(index);

		_arcPoints[index] = node;
		
		//label.Rotation = -pivot.Transform.Rotation; // - angleRad;

		// Texture
		//Texture2D texture = GD.Load<Texture2D>(menuItem.LogoLocation);
		var texture = Utils.LoadExternalImage((menuItem.LogoLocation));
		Node2D textureNode = new Node2D();

		float scaleRatio = 1.0f;
		
		// If we have a logo, add it
		if (texture != null)
		{
			Sprite2D logo = new Sprite2D();
			logo.Texture = texture;	
			logo.Name = "Logo";
			textureNode.AddChild(logo);
			scaleRatio = this._scaleMenuItem(index, direction, texture.GetSize().Y);
		}
		else // Add text instead
		{
			var label = new Label();
			float desiredHeight = (_screenSize.Y / _itemPerHeight);
			float desiredWidth = desiredHeight * 4;
			label.Size = new Vector2(desiredWidth, desiredHeight);
			label.SetAnchorsPreset(Control.LayoutPreset.Center);
			label.Scale = new Vector2(3, 3);
			label.Text = menuItem.Name;
			//label.Theme.SetFontSize("","", (int) desiredHeight);
			label.Name = "Logo";
			textureNode.AddChild(label);
			scaleRatio = this._scaleMenuItem(index, direction, label.GetSize().Y);
		}
		
		// Scale to a uniform height on the Y-axis
		textureNode.Scale = new Vector2(scaleRatio, scaleRatio);
		
		// Rotation
		textureNode.Rotation = _RotateMenuItem(index, direction);
		textureNode.Name = "TextureNode";
		
		// Alpha
		textureNode.Modulate = _FadeMenuItem(currIndex);
		node.AddChild(textureNode);
		
		// Draw a debug dot at the node's origin
		if (_debug)
		{
			// Add a temporary box with the name
			var label = new Label();
			label.Text = menuItem.Name;
			textureNode.AddChild(label);
			
			var debug = new DebugDot();
			textureNode.AddChild(debug);
		}
		
	}
	
	private Tween StopPulse()
	{
		if (_pulseTween != null)
		{
			var stopPulseTween = _menuNode.CreateTween();
			_pulseTween.Kill();
			_pulseTween = null;
			
			// Get the "center" node
			var node = this._arcPoints[-_currIndex];
			Node2D textureNode = node.GetNode<Node2D>("TextureNode");
			
			stopPulseTween.TweenMethod(
				Callable.From<Vector2>((value) => { textureNode.Scale = value; }),
				textureNode.Scale,
				_sizePriorToPulse,
				0.05f
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			
			return stopPulseTween;
		}
		return null;
	}

	public void FadeInWheel()
	{
		if (this._menuNode.Modulate.A < 1.0f)
		{
			Tween fadeInTween = this._menuNode.CreateTween();
			Color startAlpha = new Color(1.0f, 1.0f, 1.0f, this._menuNode.Modulate.A);
			Color endAlpha = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			fadeInTween.TweenMethod(
				Callable.From<Color>((value) => { this._menuNode.Modulate = value; }),
				startAlpha,
				endAlpha,
				_rotationDuration
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			fadeInTween.Play();
		}
	}
	
	public void SpinWheel(int direction, Node2D pivot, int count)
	{
		float t = 1.0f / (count - 1.0f);
		float stepAngle = t * _arcRadians / 2.0f;
		float startRotation = pivot.Rotation;
		float endRotation = startRotation + direction * stepAngle;

		_spinningTween = this._menuNode.CreateTween();
		
		// Reset timer on interaction
		_inactivityTimer.Stop();
		
		// Fade in if not already visible
		FadeInWheel();
		
		// Rotate the "wheel"
		_spinningTween.Parallel().TweenMethod(
			Callable.From<float>((value) => { pivot.GlobalRotation = value; }),
			startRotation,
			endRotation,
			_rotationDuration
		).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
		
		// Process each of the nodes in the wheel
		foreach (KeyValuePair<int, Node2D> entry in this._arcPoints)
		{
			Node2D node = entry.Value;
			int index = entry.Key;
			
			Node2D textureNode = node.GetNode<Node2D>("TextureNode");
			
			// Get the size of the next item
			int nextIndex = direction * 1 + index;
			
			// Set the new z-index
			int currZindex = node.ZIndex;
			int newZIndex = this._ZIndexMenuItem(nextIndex);
			_spinningTween.Parallel().TweenMethod(
				Callable.From<int>((value) => { node.ZIndex = value; }),
				currZindex,
				newZIndex,
				0.0f
			);
			
			// Rotation
			var targetRotation = _RotateMenuItem(nextIndex, direction);
			var initialRotation = textureNode.GlobalRotation;
				
			// Make sure we keep them all at the same rotation
			_spinningTween.Parallel().TweenMethod(
				Callable.From<float>((value) => { textureNode.GlobalRotation = value; }),
				initialRotation,
				targetRotation,
				_rotationDuration
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			
			// Scale to a uniform height on the Y-axis
			var logoNode = textureNode.GetNode<Node>("Logo");
			float inputHeight = 100.0f;
			if (logoNode != null) {
				if (logoNode.GetClass() == "Sprite2D")
				{
					inputHeight = textureNode.GetNode<Sprite2D>("Logo").Texture.GetSize().Y;
				}
				else if (logoNode.GetClass() == "Label")
				{
					inputHeight = textureNode.GetNode<Label>("Logo").GetSize().Y;
				}

				float scaleRatio = this._scaleMenuItem(index, direction, inputHeight);
				var targetScale = new Vector2(scaleRatio, scaleRatio);

				var initialScale = textureNode.Scale;
				_spinningTween.Parallel().TweenMethod(
					Callable.From<Vector2>((value) => { textureNode.Scale = value; }),
					initialScale,
					targetScale,
					_rotationDuration
				).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			}

			// Alpha channel the nodes
			var targetAlpha = _FadeMenuItem(nextIndex);
			var initialAlpha = textureNode.Modulate;
			_spinningTween.Parallel().TweenMethod(
				Callable.From<Color>((value) => { textureNode.Modulate = value; }),
				initialAlpha,
				targetAlpha,
				_rotationDuration
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			
		}
		_spinningTween.Play();
		
		_inactivityTimer.WaitTime = SubsequentInactivitySeconds;
		_inactivityTimer.Start();
	}

	public void ThemeSwitch()
	{
		var path = new MenuPath(new[] { -_currIndex });
		MenuItemData menuItem = _menuData.GetMenuItem(path);
				
		// Add item name to screen
		if (menuItem.Name != null)
		{
			_gameNameLabel.Text = menuItem.Name;    
		}
		else
		{
			_gameNameLabel.Text = "";
		}
				
		// Call theme switch
		if (menuItem.ThemeFile != null)
		{
			_background.ChangeTheme(menuItem.ThemePck, menuItem.ThemeFile);    
		}
		// Load a default if available
		else if (this._menuData.ThemeFile != null) 
		{
			_background.ChangeTheme(this._menuData.ThemePck, this._menuData.ThemeFile);
		}
	}
	
	public void AnimateWheel(int direction, Node2D pivot, int count)
	{
		if (_spinningTween != null && _spinningTween.IsRunning())
			return;

		var stopPulseTween = StopPulse();
		
		stopPulseTween.TweenCallback(Callable.From(() => {
			SpinWheel(direction, pivot, count);
			_spinningTween.TweenCallback(Callable.From(() => {
				if (direction > 0)
				{
					_currIndex++;
				}
				else
				{	
					_currIndex--;
				}
				this.ThemeSwitch();
				StartPulse();
				
				
			}));
		}));
		stopPulseTween.Play();
		
	}

	public void Down()
	{
		if (_spinningTween != null && _spinningTween.IsRunning())
			return;
		this.AddMenuItem(_currIndex, 1);
			
		// Remove the last element on the other side
		int oppositeIndex = -(_numItems + _extraItems + _currIndex -1);
		Node2D oppositeControl = _arcPoints[oppositeIndex];
		oppositeControl.QueueFree();
			
		// Remove item from dictionary
		_arcPoints.Remove(oppositeIndex);
	
		AnimateWheel(-1, this._pivot, _numItems);
	}

	public void Up()
	{
		if (_spinningTween != null && _spinningTween.IsRunning())
			return;
		this.AddMenuItem(_currIndex, -1);
			
		// Remove the last element on the other side
		int oppositeIndex = (_numItems + _extraItems - _currIndex -1);
		Node2D oppositeControl = _arcPoints[oppositeIndex];
		oppositeControl.QueueFree();
			
		// Remove item from dictionary
		_arcPoints.Remove(oppositeIndex);

		AnimateWheel(1, this._pivot, _numItems);
	}
	
	public void WindowResized()
	{
		// Call your layout update or repositioning code here
		// Re-Set globals
		_totalItemsInDirection = this._numItems + this._extraItems;
		_screenSize = _menuNode.GetViewportRect().Size;
			
		// Calculate the new center for the center of the wheel
		var screenHeight = _menuNode.Size.Y;
		float radius = (screenHeight / 2.0f) / Convert.ToSingle(Math.Sin(_arcRadians / 2.0f));
		float xOffset = radius * Convert.ToSingle(Math.Cos(_arcRadians / 2.0f));
		_pivot.Position = new Vector2(xOffset, screenHeight / 2.0f);
		
		// Set the elements at the correct distance
		foreach (KeyValuePair<int, Node2D> entry in this._arcPoints)
		{
			Vector2 offset = this._GenerateOffset(entry.Key);
			entry.Value.Position = offset;
		}
		
		SpinWheel(0, this._pivot, _numItems);
		
		//var background = GetNode<Background>("../Background");
		//background.RestartTheme();
	}
	
	private void RunCommandForSelectedItem()
	{
		MenuItemData selectedItem = _menuData.GetMenuItem(_currentMenuLocation);
		if (!string.IsNullOrEmpty(selectedItem.LaunchCommand))
		{
			GD.Print($"Running: {selectedItem.LaunchCommand}");
			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = "bash",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = false,
					RedirectStandardError = false,
				};
				psi.ArgumentList.Add("-c");
				psi.ArgumentList.Add(selectedItem.LaunchCommand);

				// I don't think DISPLAY is needed anymore
				//psi.Environment["DISPLAY"] = ":10.0";
				psi.Environment["XAUTHORITY"] = System.Environment.GetEnvironmentVariable("XAUTHORITY");

				Process.Start(psi);
				
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to run command: {ex.Message}");
			}
		}
	}
	
	public override void _UnhandledInput(InputEvent @event)
	{
		var selectedItem = _menuData.GetMenuItem(_currentMenuLocation);
		if (@event.IsActionPressed("ui_cancel"))
		{
			if (_parentNode.GetType().Name == "Wheel")
			{
				this.Close();
			}
			else if (_currentMenuLocation.Length == 1)
			{
				GD.Print("Exit");
				GetTree().Quit();
			}
			else if (selectedItem.ItemInformation != null && _overlay != null)
			{
				this.RemoveChild(_overlay);
				_overlay.QueueFree();
				_overlay = null;
			}
			else
			{
				this.Remove();
				_currentMenuLocation.RemoveLast();
				_currentMenuLocation[^1] = 0;
				_currDepth--;
				//_currentMenu = _LoadLayer(_menuDepth[_currDepth]);
			}
		}
		if (@event.IsActionPressed("ui_accept"))
		{
			
			// If it's something with sub menus
			if (selectedItem.Items.Count > 0)
			{
				var wheelScene = GD.Load<PackedScene>("res://ui/default/Wheel/Wheel.tscn");
				Wheel wheel = wheelScene.Instantiate<Wheel>();
				AddChild(wheel);
				_gameNameLabel.Visible = false;
				
				// Set the re-enable of the input after
				wheel.Closed += () =>
				{
					SetProcessUnhandledInput(true);
					wheel = null; // allow re-opening later
					_background.RestartTheme();
					FadeInWheel();
					_menuNode.Visible = true;
					_gameNameLabel.Visible = true;
				};

				_background.UnloadCurrentTheme();
				_menuNode.Visible = false;
				wheel.Start(this, selectedItem);
				SetProcessUnhandledInput(enable: false);
			}
			// If it's something we can run
			else if (!string.IsNullOrEmpty(selectedItem.LaunchCommand))
			{
				RunCommandForSelectedItem();	
			}
			// If it's an overlay
			else if (selectedItem.ItemInformation != null && _overlay == null)
			{
				this.Visible = false;
				_overlay = OverlayMenuScene.Instantiate<OverlayMenu>();
				AddChild(_overlay);
				
				// Set the re-enable of the input after
				_overlay.Closed += () =>
				{
					SetProcessUnhandledInput(true);
					_overlay = null; // allow re-opening later
					FadeInWheel();
					this.Visible = true;
				};
				
				_overlay.Start(selectedItem);
				
				SetProcessUnhandledInput(enable: false);
			}
		}
		
		if (@event.IsActionPressed("ui_down"))
		{
			Console.Write("DOWN");
			this.Down();
			// Change the location
			_currentMenuLocation[^1] += 1;
		}
		else if (@event.IsActionPressed("ui_up"))
		{
			Console.Write("UP");
			this.Up();
			// Change the location
			_currentMenuLocation[^1] -= 1;
		}
	}
	
	public void Close()
	{
		// Disable input on this class
		SetProcessUnhandledInput(enable: false);
		_menuNode.QueueFree();
		_menuNode = null;
		
		if (!_closedEmitted)
		{
			_closedEmitted = true;
			EmitSignal(SignalName.Closed);
		}
		
		// Remove 
		QueueFree();
	}
}
