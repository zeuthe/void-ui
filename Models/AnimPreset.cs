using System.Collections.Generic;

namespace VoidUI.Models
{
    public class AnimPreset
    {
        public string Name { get; set; } = "Custom";
        public string Description { get; set; } = "";
        public int Speed { get; set; } = 5;
        public List<string> Palette { get; set; } = new() { "#ff0000", "#00ff00", "#0000ff" };
        public string Type { get; set; } = "pulse";
        public AnimPresetParams Params { get; set; } = new();
    }

    public class AnimPresetParams
    {
        public double ScaleMin { get; set; } = 1.0;
        public double ScaleMax { get; set; } = 1.2;
        public int OpacityMin { get; set; } = 80;
        public int OpacityMax { get; set; } = 100;
        public bool ColorCycle { get; set; } = true;
        public double CycleSpeed { get; set; } = 3.0;
    }
}
