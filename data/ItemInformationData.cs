using System.Collections.Generic;

namespace ArcadeFrontend;

public class ItemInformationData
{
    public string? Description { get; set; }
    public string? Poster { get; set; }
    public string? Platform { get; set; }
    public string? LogoLocation { get; set; }
    public string? ReleaseData { get; set; }
    public int? Players { get; set; }
    public bool? Coop { get; set; }
    public List<string>? Developers { get; set; }
    public List<string>? Publishers { get; set; } 
    public List<ItemInformationData> Versions { get; set; } = new();
}
