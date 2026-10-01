using System.Collections.Generic;

namespace VoidUI.Models
{
    public class CrosshairState
    {
        public string Preset { get; set; } = "dot";
        public string Animation { get; set; } = "none";
        public bool SpinEnabled { get; set; }
        public bool OrbitEnabled { get; set; }
        public int Size { get; set; } = 16;
        public double Thickness { get; set; } = 3;
        public int Opacity { get; set; } = 100;
        public string Color { get; set; } = "#22c55e";
        public int Speed { get; set; } = 5;
        public int PosX { get; set; }
        public int PosY { get; set; }
        public string Symbol { get; set; } = "卐";
        public int Rotation { get; set; }
        public int OrbitRadius { get; set; } = 30;
        public bool RainbowEnabled { get; set; }
        public int RainbowSpeed { get; set; } = 5;
        public string RainbowTheme { get; set; } = "full";
        public List<string> RainbowPalette { get; set; } = new();
        public int SpinSpeed { get; set; } = 5;
        public string SpinDir { get; set; } = "normal";
        public int OrbitSpeed { get; set; } = 5;
        public string OrbitDir { get; set; } = "normal";
        public string BindKey { get; set; } = "F6";
    }
}
