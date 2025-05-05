using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PressPlayTitler
{
    /// <summary>
    /// A FrameworkElement that knows how to render exactly one TitleElement,
    /// and applies a DropShadowEffect for TextElements when requested.
    /// </summary>
    public class VisualElementPresenter : FrameworkElement
    {
        public static readonly DependencyProperty ElementProperty =
            DependencyProperty.Register(
                nameof(Element),
                typeof(TitleElement),
                typeof(VisualElementPresenter),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnElementChanged));

        public TitleElement? Element
        {
            get => (TitleElement?)GetValue(ElementProperty);
            set => SetValue(ElementProperty, value);
        }

        private static void OnElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var presenter = (VisualElementPresenter)d;

            if (e.OldValue is INotifyPropertyChanged oldNpc)
                oldNpc.PropertyChanged -= presenter.Element_PropertyChanged;
            if (e.NewValue is INotifyPropertyChanged newNpc)
                newNpc.PropertyChanged += presenter.Element_PropertyChanged;

            // update the shader effect immediately when Element changes
            presenter.UpdateEffect();
            presenter.InvalidateVisual();
        }

        private void Element_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // whenever any property changes—especially shadow settings—reapply effect & redraw
            UpdateEffect();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(ActualWidth, ActualHeight);

        protected override void OnRender(DrawingContext dc)
        {
            if (Element == null) return;

            // 1) Translate so Bounds.Location → (0,0)
            dc.PushTransform(new TranslateTransform(-Element.Bounds.X, -Element.Bounds.Y));

            // 3) Draw
            Element.Draw(dc, (int)ActualWidth, (int)ActualHeight);

        }


        /// <summary>
        /// If the current Element is a TextElement with DropShadow=true, apply
        /// a GPU‐accelerated DropShadowEffect to this FrameworkElement; otherwise clear it.
        /// </summary>
        private void UpdateEffect()
        {
            if (Element is TextElement te && te.DropShadow)
            {
                Effect = new DropShadowEffect
                {
                    Color = te.ShadowColor,
                    Direction = 315,
                    ShadowDepth = te.ShadowOffset.Length,
                    BlurRadius = 4,
                    Opacity = te.Opacity
                };
            }
            else
            {
                Effect = null;
            }
        }
    }
}
