using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PresetStudio.View
{
    internal sealed class PresetBarAdorner : Adorner
    {
        private readonly FrameworkElement child;

        public PresetBarAdorner(UIElement adornedElement, FrameworkElement child) : base(adornedElement)
        {
            this.child = child;
            AddVisualChild(child);
        }

        public void Release() => RemoveVisualChild(child);

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => child;

        protected override Size MeasureOverride(Size constraint)
        {
            child.Measure(new Size(AdornedElement.RenderSize.Width, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, -height, AdornedElement.RenderSize.Width, height));
            return finalSize;
        }
    }
}
