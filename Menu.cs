using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ArcadeFrontend;

public partial class Menu : Control
{
	private MenuItemData _menu;
	private MenuData _menuData;
	private MenuPath _currentMenuLocation;
	public SingleWheel _currentMenu;

	private Dictionary<int, MenuItemData> _menuDepth = new Dictionary<int, MenuItemData>();
	private int _currDepth;

	public override void _Ready()
	{
		// Load Data
		var json = File.ReadAllText("config.json");
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			IncludeFields = true,
		};
		_menu = JsonSerializer.Deserialize<MenuItemData>(json, options);
		_currDepth = 0;
		_menuDepth.Add(_currDepth, _menu);
		_currentMenu = _LoadLayer(_menu);
		_currentMenuLocation = new MenuPath(new[] { 0 });	
	}

	private SingleWheel _LoadLayer(MenuItemData menuData)
	{
		// Create the layer control node
		SingleWheel layer = new SingleWheel(this, menuData);
		return layer;
	}
	
	private void RunCommandForSelectedItem()
	{
		var selectedItem = _menu.GetMenuItem(_currentMenuLocation);
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
		if (@event.IsActionPressed("ui_cancel"))
		{
			if (_currentMenuLocation.Length == 1)
			{
				GD.Print("Exit");
				GetTree().Quit();
			}
			else
			{
				_currentMenu.Remove();
				_currentMenuLocation.RemoveLast();
				_currentMenuLocation[^1] = 0;
				_currDepth--;
				_currentMenu = _LoadLayer(_menuDepth[_currDepth]);
			}
		}
		if (@event.IsActionPressed("ui_accept"))
		{
			var selectedItem = _menu.GetMenuItem(_currentMenuLocation);
			// If it's something with sub menus
			if (selectedItem.Items.Count > 0)
			{
				_currentMenu.Remove();
				_currentMenu = null;
				_currentMenu = _LoadLayer(_menu.GetMenuItem(_currentMenuLocation));
				var newPath = new MenuPath(_currentMenuLocation.Indices); // copy existing indices
				newPath.Indices.Add(0);
				_currentMenuLocation = newPath;
				_currDepth++;
			}
			// If it something we can run
			else if (!string.IsNullOrEmpty(selectedItem.LaunchCommand))
			{
				RunCommandForSelectedItem();	
			}
			
		}
		
		if (@event.IsActionPressed("ui_down"))
		{
			Console.Write("DOWN");
			_currentMenu.Down();
			// Change the location
			_currentMenuLocation[^1] += 1;
		}
		else if (@event.IsActionPressed("ui_up"))
		{
			Console.Write("UP");
			_currentMenu.Up();
			// Change the location
			_currentMenuLocation[^1] -= 1;
		}
	}
	
}
