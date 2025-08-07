using System.Collections.Generic;

namespace ArcadeFrontend;

public class MenuItemData
{
	public string Name;
	public string LogoLocation;
	public string ThemePck;
	public string ThemeFile;
	public string LaunchCommand;
	public List<MenuItemData> Items;
}