using System.Collections.Generic;

namespace VoidUI.Models
{
    public class SteamDetectionResult
    {
        public bool Success { get; set; }
        public string SteamRoot { get; set; } = "";
        public List<string> Libraries { get; set; } = new();
        public string RustPath { get; set; } = "";
        public string CfgPath { get; set; } = "";
        public string ClientCfg { get; set; } = "";
        public string KeysCfg { get; set; } = "";
        public string Status { get; set; } = "not_found";
    }
}
