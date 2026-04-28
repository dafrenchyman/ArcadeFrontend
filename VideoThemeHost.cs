using Godot;

namespace ArcadeFrontend;

public partial class VideoThemeHost : Control
{
	private VideoStreamPlayer _videoPlayer;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		_videoPlayer = new VideoStreamPlayer();
		_videoPlayer.Name = "Video";
		_videoPlayer.SetAnchorsPreset(LayoutPreset.FullRect);
		_videoPlayer.Autoplay = true;
		_videoPlayer.Expand = true;
		_videoPlayer.Loop = true;
		_videoPlayer.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_videoPlayer);

		UpdateLayout();

		if (GetTree()?.Root != null)
		{
			GetTree().Root.SizeChanged += UpdateLayout;
		}
	}

	public override void _ExitTree()
	{
		if (GetTree()?.Root != null)
		{
			GetTree().Root.SizeChanged -= UpdateLayout;
		}
	}

	public void LoadTheme(string videoPath)
	{
		var stream = new VideoStreamTheora();
		stream.File = videoPath;
		_videoPlayer.Stream = stream;
		_videoPlayer.Play();
	}

	private void UpdateLayout()
	{
		Vector2 viewportSize = GetViewportRect().Size;
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
		{
			return;
		}

		Size = viewportSize;
		if (_videoPlayer != null)
		{
			_videoPlayer.Position = Vector2.Zero;
			_videoPlayer.Size = viewportSize;
		}
	}
}
