using System.Windows.Controls;
using System.Windows;

namespace PressPlay.Timeline
{
    public class BindableScrollBar : System.Windows.Controls.Primitives.ScrollBar
    {
        public static readonly DependencyProperty BoundScrollViewerProperty =
            DependencyProperty.Register("BoundScrollViewer", typeof(ScrollViewer), typeof(BindableScrollBar),
                new PropertyMetadata(null, OnBoundScrollViewerChanged));

        public ScrollViewer BoundScrollViewer
        {
            get { return (ScrollViewer)GetValue(BoundScrollViewerProperty); }
            set { SetValue(BoundScrollViewerProperty, value); }
        }

        private static void OnBoundScrollViewerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var scrollBar = (BindableScrollBar)d;

            if (e.OldValue is ScrollViewer oldScrollViewer)
            {
                oldScrollViewer.ScrollChanged -= scrollBar.ScrollViewer_ScrollChanged;
            }

            if (e.NewValue is ScrollViewer newScrollViewer)
            {
                scrollBar.Value = newScrollViewer.HorizontalOffset;
                scrollBar.Maximum = newScrollViewer.ScrollableWidth;
                scrollBar.ViewportSize = newScrollViewer.ViewportWidth;

                newScrollViewer.ScrollChanged += scrollBar.ScrollViewer_ScrollChanged;
            }
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                Value = scrollViewer.HorizontalOffset;
                Maximum = scrollViewer.ScrollableWidth;
                ViewportSize = scrollViewer.ViewportWidth;
            }
        }

        protected override void OnValueChanged(double oldValue, double newValue)
        {
            base.OnValueChanged(oldValue, newValue);

            if (BoundScrollViewer != null)
            {
                BoundScrollViewer.ScrollToHorizontalOffset(newValue);
            }
        }
    }
}