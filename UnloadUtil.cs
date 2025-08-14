using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class UnloadUtil
{
    // Track your own signal connections via this helper so you can disconnect later
    public static readonly List<(Object Emitter, string Signal, Callable Handler)> Connections = new();

    public static void ConnectTracked(Object emitter, string signal, Callable handler, int flags = 0)
    {
        ((Node)emitter).Connect(signal, handler, (uint)flags);
        Connections.Add((emitter, signal, handler));
    }

    public static void DisconnectAllTracked()
    {
        foreach (var (em, sig, cb) in Connections)
        {
            // if (GodotObject.IsInstanceValid(em))
            // {
            //     try { ((Node)em).Disconnect(sig, cb); } catch { /* already gone */ }
            // }
        }
        Connections.Clear();
    }

    // Clear common resource references in a subtree so RefCounted assets can be released
    public static void ClearResourceRefs(Node root)
    {
        if (!GodotObject.IsInstanceValid(root)) return;

        root.TreeTraverse(node =>
        {
            switch (node)
            {
                case TextureRect tr: tr.Texture = null; break;
                case Sprite2D s2d: s2d.Texture = null; break;
                case AnimatedSprite2D as2d: as2d.SpriteFrames = null; break;
                case VideoStreamPlayer vsp: vsp.Stream = null; break;
                case AudioStreamPlayer asp: asp.Stream = null; break;
                case AudioStreamPlayer2D asp2: asp2.Stream = null; break;
                //case GPUParticles2D p2d: p2d.ProcessMaterial = null; break;
            }

            if (node is CanvasItem ci)
            {
                // Materials/textures assigned directly to the CanvasItem
                ci.Material = null;
                ci.SelfModulate = Colors.White; // optional reset
            }
        });
    }

    // Utility to traverse
    private static void TreeTraverse(this Node n, Action<Node> action)
    {
        action(n);
        foreach (Node c in n.GetChildren())
            TreeTraverse(c, action);
    }

    // Call this after you’ve stopped activity + cleared refs
    public static async Task FinishMemoryReleaseAsync()
    {
        // Let queued frees happen at end-of-frame
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); // one more frame for safety

        // Ask Godot to drop any now-unreferenced cached resources
        //ResourceLoader.UnloadUnusedResources();

        // Optional: nudge .NET GC (don’t spam this; do it after big unloads)
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
