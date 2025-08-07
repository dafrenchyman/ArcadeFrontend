using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace ArcadeFrontend;

public class MenuItems
{
    private List<MenuItemData> _menuItems;

    private string _getElementFromJson(JsonElement item, string itemName)
    {
        string value = null;
        if (item.TryGetProperty(itemName, out JsonElement element) && element.ValueKind != JsonValueKind.Null)
        {
            value = element.ToString();
        }
        return value;
    }
    public MenuItems()
    {
        _menuItems = new List<MenuItemData>();
        
        // Load from config
        ConfigLoader configLoader = new ConfigLoader();
        var settings = configLoader.settings;

        // Access the array
        JsonElement gameList = settings["Items"];
        _menuItems = _processMenuItem(gameList);
        GD.Print("Menu Loaded");
    }

    private List<MenuItemData> _processMenuItem(JsonElement gameList)
    {
        List<MenuItemData> menuItems = new List<MenuItemData>();
        foreach (JsonElement item in gameList.EnumerateArray())
        {
            string name = item.GetProperty("Name").ToString();
            string logoLocation = this._getElementFromJson(item, "LogoLocation");
            string theme_pck = this._getElementFromJson(item, "ThemePck");
            string theme_file = this._getElementFromJson(item, "ThemeFile");
            string launch_command = this._getElementFromJson(item, "LaunchCommand");
            
            MenuItemData menuItem = new MenuItemData();
            menuItem.Name = name;
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

    public MenuItemData getMenuItem(int index)
    {
        int numItems = _menuItems.Count;
        
        if (numItems == 0)
            throw new InvalidOperationException("Menu is empty.");

        // Properly wrap index for positive and negative values
        int wrappedIndex = ((index % numItems) + numItems) % numItems;

        return _menuItems[wrappedIndex];
    }
}