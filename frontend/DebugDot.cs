namespace ArcadeFrontend;

using Godot;
using System;
using System.Collections.Generic;

public partial class DebugDot : Node2D
{
    [Export] public float Radius = 5f;
    [Export] public Color Color = new Color(1, 0, 0); // red

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, Color);
    }
}