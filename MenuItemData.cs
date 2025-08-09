using System;
using System.Collections.Generic;

namespace ArcadeFrontend;

public class MenuItemData
{
	public int index { get; set; }
	public string? Name { get; set; }
	public string? LogoLocation { get; set; }
	public string? ThemePck { get; set; }
	public string? ThemeFile { get; set; }
	public string? LaunchCommand { get; set; }
	public string? MenuType { get; set; }
	public List<MenuItemData> Items { get; set; } = new();
	
	private static int Wrap(int index, int length) => ((index % length) + length) % length;
    
	public MenuItemData GetMenuItem(MenuPath path)
	{
		if (this.Items.Count == 0)
			throw new InvalidOperationException("Menu is empty.");
		if (path == null || path.Length == 0)
			throw new ArgumentException("Path must contain at least one index.", nameof(path));

		IList<MenuItemData> current = this.Items;
		MenuItemData node = null;

		for (int depth = 0; depth < path.Length; depth++)
		{
			if (current.Count == 0)
				throw new InvalidOperationException($"Menu at depth {depth} is empty.");

			int wrapped = Wrap(path[depth], current.Count);
			node = current[wrapped];

			if (depth < path.Length - 1)
			{
				if (node.Items == null)
					throw new InvalidOperationException($"Node at depth {depth} has no children.");
				current = node.Items;
			}
		}

		return node;
	}
}