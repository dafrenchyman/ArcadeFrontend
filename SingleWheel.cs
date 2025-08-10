namespace ArcadeFrontend;
using Godot;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using ArcadeFrontend;
using Environment = System.Environment;


public class SingleWheel
{
	private Control _parentNode;
	private Control _menuNode;
    private float _arcRadians = 3.0f/4.0f *Single.Pi; //(1.0f / 4.0f) * Single.Pi ; // total spread of the arc

    private int _numItems = 8;
    private int _extraItems = 2;
    private int _totalItemsInDirection;
    private Vector2 _screenSize;
    private Background _background;
    private Timer _inactivityTimer;
	
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

    private bool _debug = false;
    
    public SingleWheel(Control parentNode, MenuItemData menuData)
    {
	    this._parentNode = parentNode;
	    this._menuData = menuData;
	    //this._currIndex = currIndex;
	    
	    // Create control node that takes up the left of the screen
	    _menuNode = new Control();
	    this._parentNode.AddChild(_menuNode);
	    _menuNode.SetOffsetsPreset(Control.LayoutPreset.RightWide);     
	    
		// Set globals
		_totalItemsInDirection = this._numItems + this._extraItems;
		_screenSize = _parentNode.GetViewportRect().Size;
		
		// Create an inactivity timer
		_inactivityTimer = new Timer();
		_inactivityTimer.WaitTime = 10.0f; // Longer timeout on first start
		_inactivityTimer.OneShot = true;
		_inactivityTimer.Autostart = false;
		_menuNode.AddChild(_inactivityTimer);
		_inactivityTimer.Timeout += OnInactivityTimeout;
		_inactivityTimer.Start();
		
		// Get a reference to Background node
		_background = _menuNode.GetNode<Background>("../../Background");
		
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
		MenuPath path = null;
		for (int index = -_totalItemsInDirection + 1; index < _totalItemsInDirection; index++)
		{
			path = new MenuPath(new[] { index });
			MenuItemData menuItem = _menuData.GetMenuItem(path);
		
			Vector2 offset = this._GenerateOffset(index);

			Node2D node = new Node2D();

			_pivot.AddChild(node);
			node.Position = offset;
			node.GlobalRotation = 0;
			
			// Calculate the zindex
			node.ZIndex = this._ZIndexMenuItem(index);
			
			_arcPoints[index] = node;
			
			// Add a temporary box with the name
			var label = new Label();
			if (_debug)
			{
				label.Text = menuItem.Name;
				label.ZIndex = node.ZIndex + 10;
			}

			// Texture
			var texture = LoadExternalImage(menuItem.LogoLocation);

			Node2D textureNode = new Node2D();
			Sprite2D logo = new Sprite2D();
			logo.Texture = texture;
			logo.Name = "Logo";
			textureNode.AddChild(logo);
			
			// Scale to a uniform height on the Y-axis
			float scaleRatio = this._scaleMenuItem(index, 0, texture.GetSize().Y);
			textureNode.Scale = new Vector2(scaleRatio, scaleRatio);
			
			// Rotation
			textureNode.Rotation = _RotateMenuItem(index, 0);
			textureNode.Name = "TextureNode";
			
			// Alpha
			textureNode.Modulate = _FadeMenuItem(index);
			
			node.AddChild(textureNode);
			textureNode.AddChild(label);
			
			// Draw a debug dot at the node's origin
			if (_debug)
			{
				var debug = new DebugDot();
				textureNode.AddChild(debug);
			}
		}
		
		// Call theme switch
		path = new MenuPath(new[] { 0 });
		MenuItemData currMenuItem = _menuData.GetMenuItem(path);
		_background.ChangeTheme(currMenuItem.ThemePck, currMenuItem.ThemeFile);
		
		StartPulse();
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
	    var texture = LoadExternalImage(menuItem.LogoLocation);
	    Node2D textureNode = new Node2D();
	    Sprite2D logo = new Sprite2D();
	    logo.Texture = texture;
	    logo.Name = "Logo";
	    textureNode.AddChild(logo);
		
	    // Scale to a uniform height on the Y-axis
	    float scaleRatio = this._scaleMenuItem(index, direction, texture.GetSize().Y);
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
		Color startAlpha = new Color(1.0f, 1.0f,1.0f, this._menuNode.Modulate.A);
		Color endAlpha = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		if (this._menuNode.Modulate.A < 1.0f)
			_spinningTween.Parallel().TweenMethod(
				Callable.From<Color>((value) => { this._menuNode.Modulate = value; }),
				startAlpha,
				endAlpha,
				_rotationDuration
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
		
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
			Vector2 textureSize = textureNode.GetNode<Sprite2D>("Logo").Texture.GetSize(); // Original texture dimensions
			float scaleRatio = this._scaleMenuItem(index, direction, textureSize.Y);
			var targetScale = new Vector2(scaleRatio, scaleRatio);
				
			var initialScale = textureNode.Scale;
			_spinningTween.Parallel().TweenMethod(
				Callable.From<Vector2>((value) => { textureNode.Scale = value; }),
				initialScale,
				targetScale,
				_rotationDuration
			).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
			
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
		
		_inactivityTimer.WaitTime = 3.0f;
		_inactivityTimer.Start();
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
			    StartPulse();

			    // Call theme switch
			    var path = new MenuPath(new[] { -_currIndex });
			    MenuItemData menuItem = _menuData.GetMenuItem(path);
			    if (menuItem.ThemeFile != null)
			    {
				    _background.ChangeTheme(menuItem.ThemePck, menuItem.ThemeFile);    
			    }
			    // Load a default if available
			    else if (this._menuData.ThemeFile != null) 
			    {
				    _background.ChangeTheme(this._menuData.ThemePck, this._menuData.ThemeFile);
			    }
			    
				
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
    
}