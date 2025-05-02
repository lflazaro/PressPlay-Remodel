using System.Windows;

namespace PressPlay.Timeline
{
    public class FadeControl : System.Windows.Controls.Control
    {
        public static readonly DependencyProperty ControlTypeProperty =
            DependencyProperty.Register("ControlType", typeof(Models.FadeControlType), typeof(FadeControl),
                new PropertyMetadata(Models.FadeControlType.Left));

        public Models.FadeControlType ControlType
        {
            get { return (Models.FadeControlType)GetValue(ControlTypeProperty); }
            set { SetValue(ControlTypeProperty, value); }
        }

        static FadeControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FadeControl),
                new FrameworkPropertyMetadata(typeof(FadeControl)));
        }
    }
}