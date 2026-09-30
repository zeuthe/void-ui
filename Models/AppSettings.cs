using System.Collections.Generic;

namespace VoidUI.Models
{
    public class AppSettings
    {
        public CrosshairState Crosshair { get; set; } = new();
        public CombatSettings Combat { get; set; } = new();
        public string Theme { get; set; } = "black";
    }

    public class CombatSettings
    {
        public List<TrackedSite> CustomSites { get; set; } = new();
    }

    public class TrackedSite
    {
        public string Name { get; set; } = "";
        public string UrlTemplate { get; set; } = "";
    }
}