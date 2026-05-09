using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ArcadeFrontend;



public class MenuData
{
    private MenuItemData _menu;

    private string _getElementFromJson(JsonElement item, string itemName)
    {
        string value = null;
        if (item.TryGetProperty(itemName, out JsonElement element) && element.ValueKind != JsonValueKind.Null)
        {
            value = element.ToString();
        }
        return value;
    }
    public MenuData()
    {
        _menu = new MenuItemData();
        
        var json = File.ReadAllText("config.json");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
        };
        _menu = JsonSerializer.Deserialize<MenuItemData>(json, options);
        GD.Print("Menu Loaded");
    }


    private List<MenuItemData> _processMenuItem(JsonElement gameList)
    {
        List<MenuItemData> menuItems = new List<MenuItemData>();
        foreach (var (item, index) in gameList.EnumerateArray().Select((item, index) => (item, index)))
        {
            string name = item.GetProperty("Name").ToString();
            string menu_type = this._getElementFromJson(item, "MenuType");
            string logoLocation = this._getElementFromJson(item, "LogoLocation");
            string theme_pck = this._getElementFromJson(item, "ThemePck");
            string theme_file = this._getElementFromJson(item, "ThemeFile");
            string launch_command = this._getElementFromJson(item, "LaunchCommand");
            
            MenuItemData menuItem = new MenuItemData();
            menuItem.index = index;
            menuItem.Name = name;
            menuItem.MenuType = menu_type;
            menuItem.LogoLocation = logoLocation;
            menuItem.ThemePck = theme_pck;
            menuItem.ThemeFile = theme_file;
            menuItem.LaunchCommand = launch_command;

            if (item.TryGetProperty("Items", out JsonElement nestedItems))
            {
                menuItem.Items = _processMenuItem(nestedItems);
            }
            menuItems.Add(menuItem);
        }

        return menuItems;
    }
    
    public string getMenuType()
    {
        return "s";
    }
    
    private static int Wrap(int index, int length) => ((index % length) + length) % length;
    
    public MenuItemData GetMenuItem(MenuPath path)
    {
        if (_menu.Items.Count == 0)
            throw new InvalidOperationException("Menu is empty.");
        if (path == null || path.Length == 0)
            throw new ArgumentException("Path must contain at least one index.", nameof(path));

        IList<MenuItemData> current = _menu.Items;
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