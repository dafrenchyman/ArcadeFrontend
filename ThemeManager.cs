using Godot;
using System.Collections.Generic;

public partial class ThemeManager : Node
{
	public static ThemeManager Instance { get; private set; }

	private HashSet<string> _loadedPacks = new HashSet<string>();

	public override void _Ready()
	{
		Instance = this; // Store a global reference
	}

	public bool LoadThemePack(string pckPath)
	{
		string normalizedPath = ProjectSettings.GlobalizePath(pckPath);

		if (_loadedPacks.Contains(normalizedPath))
		{
			GD.Print($"Theme pack already loaded: {pckPath}");
			return true;
		}

		bool success = ProjectSettings.LoadResourcePack(pckPath);
		if (success)
		{
			_loadedPacks.Add(normalizedPath);
			GD.Print($"Theme pack loaded: {pckPath}");
		}
		else
		{
			GD.PrintErr($"Failed to load theme pack: {pckPath}");
		}

		return success;
	}
}
