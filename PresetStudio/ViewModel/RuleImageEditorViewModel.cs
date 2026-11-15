using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PresetStudio.Model;
using PresetStudio.Preview;
using PresetStudio.View;
using YukkuriMovieMaker.Commons;

namespace PresetStudio.ViewModel
{
    internal sealed class RuleImageEditorViewModel : Bindable
    {
        private const int PreviewSize = 240;

        private bool isPreviewPending;

        public IReadOnlyList<KeyValuePair<RuleImageShape, string>> Shapes { get; } =
        [
            new(RuleImageShape.Linear, Texts.ShapeLinear),
            new(RuleImageShape.Radial, Texts.ShapeRadial),
            new(RuleImageShape.Rectangle, Texts.ShapeRectangle),
            new(RuleImageShape.Diamond, Texts.ShapeDiamond),
            new(RuleImageShape.Clock, Texts.ShapeClock),
            new(RuleImageShape.Blinds, Texts.ShapeBlinds),
            new(RuleImageShape.Checker, Texts.ShapeChecker),
            new(RuleImageShape.Rings, Texts.ShapeRings),
            new(RuleImageShape.Spiral, Texts.ShapeSpiral),
            new(RuleImageShape.Random, Texts.ShapeRandom),
        ];

        private string name;
        public string Name { get => name; set => Set(ref name, value ?? string.Empty, nameof(Name), nameof(CanSubmit)); }

        private RuleImageShape shape;
        public RuleImageShape Shape { get => shape; set => SetParameter(ref shape, value, nameof(Shape), nameof(IsAngleEnabled), nameof(IsCountEnabled), nameof(IsSeedEnabled)); }

        private double angle;
        public double Angle { get => angle; set => SetParameter(ref angle, value, nameof(Angle)); }

        private int count;
        public int Count { get => count; set => SetParameter(ref count, Math.Clamp(value, 1, 64), nameof(Count)); }

        private double noise;
        public double Noise { get => noise; set => SetParameter(ref noise, value, nameof(Noise), nameof(IsSeedEnabled)); }

        private int seed;
        public int Seed { get => seed; set => SetParameter(ref seed, value, nameof(Seed)); }

        private bool isInverted;
        public bool IsInverted { get => isInverted; set => SetParameter(ref isInverted, value, nameof(IsInverted)); }

        public bool IsAngleEnabled => shape is RuleImageShape.Linear or RuleImageShape.Clock or RuleImageShape.Blinds or RuleImageShape.Checker or RuleImageShape.Spiral;

        public bool IsCountEnabled => shape is RuleImageShape.Blinds or RuleImageShape.Checker or RuleImageShape.Rings or RuleImageShape.Spiral or RuleImageShape.Random;

        public bool IsSeedEnabled => shape == RuleImageShape.Random || noise > 0;

        public bool CanSubmit => !string.IsNullOrWhiteSpace(name);

        private BitmapSource? preview;
        public BitmapSource? Preview { get => preview; private set => Set(ref preview, value); }

        private RuleImageEditorViewModel(string name, RuleImageParameters parameters)
        {
            this.name = name;
            shape = parameters.Shape;
            angle = parameters.Angle;
            count = parameters.Count;
            noise = parameters.Noise;
            seed = parameters.Seed;
            isInverted = parameters.IsInverted;
            Preview = RuleImageGenerator.Create(CreateParameters(), PreviewSize);
        }

        public RuleImageParameters CreateParameters() => new()
        {
            Shape = shape,
            Angle = angle,
            Count = count,
            Noise = noise,
            Seed = seed,
            IsInverted = isInverted,
        };

        private void SetParameter<T>(ref T field, T value, string propertyName, params string[] otherPropertyNames)
        {
            if (!Set(ref field, value, propertyName, otherPropertyNames))
                return;

            if (isPreviewPending)
                return;

            isPreviewPending = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                isPreviewPending = false;
                Preview = RuleImageGenerator.Create(CreateParameters(), PreviewSize);
            }, DispatcherPriority.Background);
        }

        public static (string Name, RuleImageParameters Parameters)? Show(string title, string name, RuleImageParameters parameters, Window? owner = null)
        {
            var viewModel = new RuleImageEditorViewModel(name, parameters);
            var window = new RuleImageEditorWindow(viewModel)
            {
                Owner = owner ?? Application.Current?.MainWindow,
                Title = title,
            };

            return window.ShowDialog() == true ? (viewModel.Name.Trim(), viewModel.CreateParameters()) : null;
        }
    }
}
