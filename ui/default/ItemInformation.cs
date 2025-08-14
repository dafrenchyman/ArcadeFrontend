namespace ArcadeFrontend;
using Godot;

public partial class ItemInformation : Control
{
	private ColorRect _dimmer;
	private Control _content;

	public override void _Ready()
	{
		_dimmer = GetNode<ColorRect>(path:"./Overlay");
		_content = GetNode<Control>(path:"./../../Control"); // Panel or VBoxContainer
		//HideOverlay(immediate:false);
	}

	public void ShowOverlay()
	{
		Visible = true;
		var tw = CreateTween();
		tw.TweenProperty(_dimmer, property:"modulate:a", finalVal:0.5f, duration:0.2f); // fade background dim
		tw.TweenProperty(_content, property:"modulate:a", finalVal:1.0f, duration:0.2f);
		_content.MouseFilter = Control.MouseFilterEnum.Stop; // block clicks to background
	}

	public void FillFields(MenuItemData menuItem)
	{
		// Nodes we will add data to:
		Label gameDescriptionNode = this.GetNode<Label>("./Information/Description");
		TextureRect logeTextureNode = this.GetNode<TextureRect>("./Information/Logo");
		TextureRect posterTextureNode = this.GetNode<TextureRect>("./Information/Poster");
		
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
				logeTextureNode.Texture = texture;    
			}
			else
			{
				logeTextureNode.Texture = null;
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
			logeTextureNode.Texture = null;
			posterTextureNode.Texture = null;
		}
	}

	public void HideOverlay(bool immediate = false)
	{
		if (immediate)
		{
			_dimmer.Modulate = new Color(0,0,0,0);
			_content.Modulate = new Color(1,1,1,0);
			Visible = false;
			return;
		}

		var tw = CreateTween();
		tw.TweenProperty(_dimmer, "modulate:a", 0.0f, 0.2f);
		tw.TweenProperty(_content, "modulate:a", 0.0f, 0.2f)
		  .Finished += () => Visible = false;
		_content.MouseFilter = Control.MouseFilterEnum.Ignore;
	}
}
