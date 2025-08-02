using Godot;
using System;

public partial class TopLayer : Node2D
{
	
	private const int WM_FOCUS_OUT = 1005;
	private const int WM_FOCUS_IN = 1004;
	public TopLayer()
	{
		Console.Write("Hello");
		Console.Write(" ");
		Console.Write("World!");
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
}
