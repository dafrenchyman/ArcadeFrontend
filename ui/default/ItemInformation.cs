namespace ArcadeFrontend;
using Godot;

public partial class ItemInformation : Control
{

	public void FillFields(MenuItemData menuItem)
	{
		// Nodes we will add data to:
		Label gameDescriptionNode = this.GetNode<Label>("./Information/Description");
		TextureRect logoTextureNode = this.GetNode<TextureRect>("./Information/Logo");
		TextureRect posterTextureNode = this.GetNode<TextureRect>("./Information/Poster");
		
		if (ThemeManager.Instance.LoadThemePack(menuItem.ThemePck))
		{
			// logo
			string logoPath = menuItem.ThemeFile + "/gfx/logo.png";
			if (ResourceLoader.Exists(logoPath))
			{
				Texture2D texture = ResourceLoader.Load<Texture2D>(logoPath);
				logoTextureNode.Texture = texture;
			}
			
			// Poster
			string posterPath = menuItem.ThemeFile + "/gfx/poster.png";
			if (ResourceLoader.Exists(posterPath))
			{
				Texture2D texture = ResourceLoader.Load<Texture2D>(posterPath);
				posterTextureNode.Texture = texture;
			}
		}
		
		if (menuItem.ItemInformation != null)
		{
			// Description
			ItemInformationData info = menuItem.ItemInformation;
			if (info.Description != null)
			{
				
				gameDescriptionNode.Text = info.Description;    
			}
			else
			{
				gameDescriptionNode.Text = "";
			}
			
			// Add Item Logo
			if (info.LogoLocation != null)
			{
				var texture = Utils.LoadExternalImage(info.LogoLocation);
				logoTextureNode.Texture = texture;
			}
			else
			{
				logoTextureNode.Texture = null;
			}
			
			// Add Poster
			if (info.Poster != null)
			{
				var texture = Utils.LoadExternalImage(info.Poster);
				posterTextureNode.Texture = texture;    
			}
			else
			{
				posterTextureNode.Texture = null;
			}
		}
		else
		{
			gameDescriptionNode.Text = "";
			logoTextureNode.Texture = null;
			posterTextureNode.Texture = null;
		}
	}
}
