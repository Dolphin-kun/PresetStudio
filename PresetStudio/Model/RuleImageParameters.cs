namespace PresetStudio.Model
{
    internal enum RuleImageShape
    {
        Linear,
        Radial,
        Rectangle,
        Diamond,
        Clock,
        Blinds,
        Checker,
        Rings,
        Spiral,
        Random,
    }

    internal sealed class RuleImageParameters
    {
        public RuleImageShape Shape { get; set; } = RuleImageShape.Linear;

        public double Angle { get; set; }

        public int Count { get; set; } = 8;

        public double Noise { get; set; }

        public int Seed { get; set; } = 1;

        public bool IsInverted { get; set; }

        public RuleImageParameters Clone() => (RuleImageParameters)MemberwiseClone();
    }

    internal sealed class RuleImageRecord
    {
        public string Path { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public RuleImageParameters Parameters { get; set; } = new();
    }
}
