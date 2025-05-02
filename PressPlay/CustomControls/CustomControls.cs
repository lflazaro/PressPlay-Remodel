using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PressPlay.CustomControls
{
    /// <summary>
    /// Placeholder enum for timeline tools 
    /// (since your XAML references TimelineSelectedTool.SelectionTool, etc.).
    /// </summary>
    public enum TimelineSelectedTool
    {
        SelectionTool,
        CuttingTool
    }
    public class TimelineControl : Control
    {
        // Example of a DependencyProperty for "Project"
        public static readonly DependencyProperty ProjectProperty =
            DependencyProperty.Register("Project", typeof(object), typeof(TimelineControl), new PropertyMetadata(null));

        public object Project
        {
            get => GetValue(ProjectProperty);
            set => SetValue(ProjectProperty, value);
        }

        static TimelineControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TimelineControl),
                new FrameworkPropertyMetadata(typeof(TimelineControl)));
        }
    }
}