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
	private float _fadeDuration = 0.5f;
	private const float BaseRotationDurationSeconds = 0.2f;
	private const float HeldRotationDurationSeconds = 0.08f;
	private const float HoldRepeatDelaySeconds = 0.2f;
	private const float HoldRepeatStartIntervalSeconds = 0.08f;
	private const float HoldRepeatMinimumIntervalSeconds = 0.02f;
	private const float HoldRepeatAcceleration = 0.78f;
	private Tween _spinningTween;
	private Tween _pulseTween;
	private Vector2 _sizePriorToPulse;
	private bool _navigationInProgress = false;
	private Node2D _pivot;
	private Dictionary<int, Node2D> _arcPoints = new Dictionary<int, Node2D>();
	private MenuItemData _menuData;
	private MenuPath _currentMenuLocation ;

	private bool _debug = false;
	private int _heldDirection = 0;
	private double _holdDelayRemaining = 0.0;
	private double _holdRepeatRemaining = 0.0;
	private double _holdRepeatInterval = HoldRepeatStartIntervalSeconds;
	private bool _deferThemeLoading = false;
	private bool _themePending = false;

	private Dictionary<int, MenuItemData> _menuDepth = new Dictionary<int, MenuItemData>();
	private int _currDepth;
	private GameDetailsOverlay _overlay;
	private bool _closedEmitted = false;
	private TopLayer _rootHost;
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
		_rootHost = parentNode as TopLayer ?? (parentNode as Wheel)?._rootHost;
		
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
	
	private float _scaleMenuItem(int index, int direction, float inputWidth, float inputHeight)
	{
		// Scale to a uniform height while capping width for oversized clear logos.
		float desiredHeight = (_screenSize.Y / _itemPerHeight);
		float desiredMaxWidth = _screenSize.X * 0.22f;
		float scaleForHeight = desiredHeight / Math.Max(1.0f, inputHeight);
		float scaleForWidth = desiredMaxWidth / Math.Max(1.0f, inputWidth);
		float scaleRatio = Math.Min(scaleForHeight, scaleForWidth);
			
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
		if (!_arcPoints.TryGetValue(-_currIndex, out var node))
			return;
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
			scaleRatio = this._scaleMenuItem(index, direction, texture.GetSize().X, texture.GetSize().Y);
		}
		else // Add text instead
			{
				float desiredHeight = (_screenSize.Y / _itemPerHeight);
				float desiredWidth = Math.Min(_screenSize.X * 0.28f, desiredHeight * 5.4f);
				float desiredTextHeight = desiredHeight * 2.1f;

				var textContainer = new Control();
				textContainer.Name = "Logo";
				textContainer.Size = new Vector2(desiredWidth, desiredTextHeight);
				textContainer.Position = new Vector2(-desiredWidth / 2.0f, -desiredTextHeight / 2.0f);

				var label = new Label();
				label.AnchorRight = 1.0f;
				label.AnchorBottom = 1.0f;
				label.OffsetLeft = 0;
				label.OffsetTop = 0;
				label.OffsetRight = 0;
				label.OffsetBottom = 0;
				label.HorizontalAlignment = HorizontalAlignment.Center;
				label.VerticalAlignment = VerticalAlignment.Center;
				label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				label.ClipText = true;
				label.Text = menuItem.Name;
				label.AddThemeFontSizeOverride("font_size", Math.Max(18, (int)(desiredHeight * 0.39f)));

				textContainer.AddChild(label);
				textureNode.AddChild(textContainer);
				scaleRatio = this._scaleMenuItem(index, direction, desiredWidth, desiredTextHeight);
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
			if (!_arcPoints.TryGetValue(-_currIndex, out var node))
				return stopPulseTween;
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
			float rotationDuration = GetCurrentRotationDuration();
			Tween fadeInTween = this._menuNode.CreateTween();
			Color startAlpha = new Color(1.0f, 1.0f, 1.0f, this._menuNode.Modulate.A);
			Color endAlpha = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			fadeInTween.TweenMethod(
				Callable.From<Color>((value) => { this._menuNode.Modulate = value; }),
				startAlpha,
				endAlpha,
				rotationDuration
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			fadeInTween.Play();
		}
	}
	
	public void SpinWheel(int direction, Node2D pivot, int count)
	{
		float rotationDuration = GetCurrentRotationDuration();
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
				rotationDuration
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
					rotationDuration
				).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			
			// Scale to a uniform height on the Y-axis
			var logoNode = textureNode.GetNode<Node>("Logo");
			float inputWidth = 300.0f;
			float inputHeight = 100.0f;
			if (logoNode != null) {
				if (logoNode.GetClass() == "Sprite2D")
				{
					var size = textureNode.GetNode<Sprite2D>("Logo").Texture.GetSize();
					inputWidth = size.X;
					inputHeight = size.Y;
				}
				else if (logoNode is Control control)
				{
					inputWidth = control.Size.X;
					inputHeight = control.Size.Y;
				}

				float scaleRatio = this._scaleMenuItem(index, direction, inputWidth, inputHeight);
				var targetScale = new Vector2(scaleRatio, scaleRatio);

				var initialScale = textureNode.Scale;
					_spinningTween.Parallel().TweenMethod(
						Callable.From<Vector2>((value) => { textureNode.Scale = value; }),
						initialScale,
						targetScale,
						rotationDuration
					).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			}

			// Alpha channel the nodes
			var targetAlpha = _FadeMenuItem(nextIndex);
			var initialAlpha = textureNode.Modulate;
				_spinningTween.Parallel().TweenMethod(
					Callable.From<Color>((value) => { textureNode.Modulate = value; }),
					initialAlpha,
					targetAlpha,
					rotationDuration
				).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			
		}
		_spinningTween.Play();
		
		_inactivityTimer.WaitTime = SubsequentInactivitySeconds;
		_inactivityTimer.Start();
	}

	private MenuItemData GetSelectedMenuItem()
	{
		var path = new MenuPath(new[] { -_currIndex });
		return _menuData.GetMenuItem(path);
	}

	private void UpdateSelectedItemLabel(MenuItemData menuItem)
	{
		if (menuItem.Name != null)
		{
			_gameNameLabel.Text = menuItem.Name;    
		}
		else
		{
			_gameNameLabel.Text = "";
		}
	}

	public void ThemeSwitch()
	{
		MenuItemData menuItem = GetSelectedMenuItem();
		ThemeDefinition? selectedTheme = menuItem.GetResolvedTheme();
		ThemeDefinition? fallbackTheme = this._menuData.GetResolvedTheme();
				
		UpdateSelectedItemLabel(menuItem);
				
		// Call theme switch
		if (selectedTheme != null)
		{
			_background.ChangeTheme(selectedTheme);    
		}
		// Load a default if available
		else if (fallbackTheme != null) 
		{
			_background.ChangeTheme(fallbackTheme);
		}
		else
		{
			_background.ChangeTheme(null);
		}

		_themePending = false;
	}

	private void BeginDeferredThemeLoading(int direction)
	{
		BeginHoldNavigation(direction);
		if (_deferThemeLoading)
		{
			return;
		}

		_deferThemeLoading = true;
		_themePending = true;
		_background.ChangeTheme(null);
	}

	private void EndDeferredThemeLoading(int direction)
	{
		EndHoldNavigation(direction);
		_deferThemeLoading = false;

		if (_themePending && (_spinningTween == null || !_spinningTween.IsRunning()))
		{
			ThemeSwitch();
		}
	}
	
	public void AnimateWheel(int direction, Node2D pivot, int count)
	{
		if (_navigationInProgress || (_spinningTween != null && _spinningTween.IsRunning()))
			return;

		_navigationInProgress = true;
		var stopPulseTween = StopPulse();

		void BeginSpin()
		{
			SpinWheel(direction, pivot, count);
			if (_spinningTween == null)
			{
				_navigationInProgress = false;
				return;
			}
			_spinningTween.TweenCallback(Callable.From(() => {
				if (direction > 0)
				{
					_currIndex++;
				}
				else
				{	
					_currIndex--;
				}
				UpdateSelectedItemLabel(GetSelectedMenuItem());
				if (_deferThemeLoading)
				{
					_themePending = true;
				}
				else
				{
					ThemeSwitch();
				}
				StartPulse();
				_navigationInProgress = false;
				
				
			}));
		}

		if (stopPulseTween == null)
		{
			BeginSpin();
			return;
		}

		stopPulseTween.TweenCallback(Callable.From(BeginSpin));
		stopPulseTween.Play();
		
	}

	private bool StepSelection(int direction)
	{
		if (direction > 0)
		{
			return Down();
		}

		if (direction < 0)
		{
			return Up();
		}

		return false;
	}

	private void BeginHoldNavigation(int direction)
	{
		_heldDirection = direction;
		_holdDelayRemaining = HoldRepeatDelaySeconds;
		_holdRepeatRemaining = HoldRepeatStartIntervalSeconds;
		_holdRepeatInterval = HoldRepeatStartIntervalSeconds;
	}

	private void EndHoldNavigation(int direction)
	{
		if (_heldDirection == direction)
		{
			_heldDirection = 0;
			_holdDelayRemaining = 0.0;
			_holdRepeatRemaining = 0.0;
			_holdRepeatInterval = HoldRepeatStartIntervalSeconds;
		}
	}

	private float GetCurrentRotationDuration()
	{
		return _deferThemeLoading ? HeldRotationDurationSeconds : BaseRotationDurationSeconds;
	}

	public override void _Process(double delta)
	{
		if (_heldDirection == 0)
		{
			return;
		}

		if (_heldDirection > 0 && !Input.IsActionPressed("ui_down"))
		{
			EndHoldNavigation(_heldDirection);
			return;
		}

		if (_heldDirection < 0 && !Input.IsActionPressed("ui_up"))
		{
			EndHoldNavigation(_heldDirection);
			return;
		}

		if (_holdDelayRemaining > 0.0)
		{
			_holdDelayRemaining -= delta;
			return;
		}

		_holdRepeatRemaining -= delta;
		if (_holdRepeatRemaining > 0.0)
		{
			return;
		}

		if (StepSelection(_heldDirection))
		{
			_currentMenuLocation[^1] += _heldDirection;
			_holdRepeatInterval = Math.Max(HoldRepeatMinimumIntervalSeconds, _holdRepeatInterval * HoldRepeatAcceleration);
		}

		_holdRepeatRemaining = _holdRepeatInterval;
	}

	public bool Down()
	{
		if (_navigationInProgress || (_spinningTween != null && _spinningTween.IsRunning()))
			return false;
		this.AddMenuItem(_currIndex, 1);
			
		// Remove the last element on the other side
		int oppositeIndex = int.MaxValue;
		foreach (var key in _arcPoints.Keys)
		{
			if (key < oppositeIndex)
			{
				oppositeIndex = key;
			}
		}

		Node2D oppositeControl = _arcPoints[oppositeIndex];
		oppositeControl.QueueFree();
			
		// Remove item from dictionary
		_arcPoints.Remove(oppositeIndex);
	
		AnimateWheel(-1, this._pivot, _numItems);
		return true;
	}

	public bool Up()
	{
		if (_navigationInProgress || (_spinningTween != null && _spinningTween.IsRunning()))
			return false;
		this.AddMenuItem(_currIndex, -1);
			
		// Remove the last element on the other side
		int oppositeIndex = int.MinValue;
		foreach (var key in _arcPoints.Keys)
		{
			if (key > oppositeIndex)
			{
				oppositeIndex = key;
			}
		}

		Node2D oppositeControl = _arcPoints[oppositeIndex];
		oppositeControl.QueueFree();
			
		// Remove item from dictionary
		_arcPoints.Remove(oppositeIndex);

		AnimateWheel(1, this._pivot, _numItems);
		return true;
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
				if (_parentNode is TopLayer topLayer)
				{
					topLayer.OpenRootEscapeMenu();
				}
			}
			else if (selectedItem.ItemInformation != null && _overlay != null)
			{
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
				if (_rootHost == null)
				{
					return;
				}

				_overlay = new GameDetailsOverlay(
					_rootHost.RuntimeStore,
					new RuntimeLibraryBuilder(_rootHost.RuntimeStore, _rootHost.MasterDatabasePath),
					selectedItem.GameKey != null && selectedItem.SystemId != null
						? new RuntimeLibraryBuilder(_rootHost.RuntimeStore, _rootHost.MasterDatabasePath).BuildCanonicalGameDetails(selectedItem.SystemId, selectedItem.GameKey)
						: selectedItem);
				AddChild(_overlay);
				
				// Set the re-enable of the input after
				_overlay.Closed += () =>
				{
					SetProcessUnhandledInput(true);
					_overlay = null; // allow re-opening later
					FadeInWheel();
				};

				SetProcessUnhandledInput(enable: false);
			}
		}
		
		if (@event.IsActionPressed("ui_down"))
		{
			if (StepSelection(1))
			{
				_currentMenuLocation[^1] += 1;
			}
			BeginDeferredThemeLoading(1);
		}
		else if (@event.IsActionPressed("ui_up"))
		{
			if (StepSelection(-1))
			{
				_currentMenuLocation[^1] -= 1;
			}
			BeginDeferredThemeLoading(-1);
		}
		else if (@event.IsActionReleased("ui_down"))
		{
			EndDeferredThemeLoading(1);
		}
		else if (@event.IsActionReleased("ui_up"))
		{
			EndDeferredThemeLoading(-1);
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

	public void SetInteractionEnabled(bool enabled)
	{
		SetProcessUnhandledInput(enabled);
	}
}
