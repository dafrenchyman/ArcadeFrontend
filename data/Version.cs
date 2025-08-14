using System.Collections.Generic;

namespace ArcadeFrontend;

public class Version
{
    public bool Default { get; set; } = false;
    public List<string>? Regions { get; set; }
    public string? Revision { get; set; }
    public List<string>? Languages { get; set; }
    public string? LaunchCommand { get; set; }

}