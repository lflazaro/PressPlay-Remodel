using PressPlay.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PressPlay.CustomControls
{
    public partial class ProjectClipsControl : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource),
                typeof(IEnumerable<ProjectClip>),
                typeof(ProjectClipsControl),
                new PropertyMetadata(null));

        public IEnumerable<ProjectClip> ItemsSource
        {
            get => (IEnumerable<ProjectClip>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public ProjectClipsControl()
        {
            InitializeComponent();

            // Initialize drag-drop diagnostics
            this.Loaded += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("ProjectClipsControl loaded");
            };
        }

        // This event is wired up in the XAML: PART_ClipsList_PreviewMouseMove
        private void PART_ClipsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            // Debug info
            System.Diagnostics.Debug.WriteLine("ProjectClipsControl: Mouse move with left button pressed");

            var listBox = (ListBox)sender;

            // Check minimum drag distance to prevent accidental drags
            Point currentPosition = e.GetPosition(listBox);
            if (!HasMouseMovedFarEnough(currentPosition))
            {
                return;
            }

            // Get the clicked item
            var item = ItemsUnderMouse(e.GetPosition(listBox), listBox);
            if (item is ProjectClip clip)
            {
                System.Diagnostics.Debug.WriteLine($"Starting drag operation for clip: {clip.FileName}");

                try
                {
                    // Start drag operation with the proper type
                    DragDrop.DoDragDrop(listBox, clip, DragDropEffects.Copy);
                    System.Diagnostics.Debug.WriteLine("Drag operation completed");
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in DragDrop operation: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No valid clip found under mouse");
            }
        }

        // Store the initial position when mouse button is pressed
        private Point _dragStartPoint;
        private bool _isDragging = false;

        // Add these new mouse handlers
        private void PART_ClipsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(PART_ClipsList);
            _isDragging = false;
            System.Diagnostics.Debug.WriteLine($"Mouse down at {_dragStartPoint}");
        }

        // Helper method to determine if the mouse has moved far enough to start a drag
        private bool HasMouseMovedFarEnough(Point currentPosition)
        {
            if (!_isDragging)
            {
                // Calculate distance moved
                Vector dragDistance = currentPosition - _dragStartPoint;
                if (Math.Abs(dragDistance.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(dragDistance.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    return true;
                }
                return false;
            }
            return true;
        }

        // Helper method to find item under mouse pointer
        private static object ItemsUnderMouse(Point mousePosition, ItemsControl itemsControl)
        {
            UIElement element = itemsControl.InputHitTest(mousePosition) as UIElement;
            while (element != null)
            {
                if (element is FrameworkElement frameworkElement)
                {
                    // Try to find the data item
                    object item = itemsControl.ItemContainerGenerator.ItemFromContainer(frameworkElement);

                    // If we didn't find it, check if this might be a container itself
                    if (item == DependencyProperty.UnsetValue && frameworkElement.DataContext is ProjectClip clip)
                    {
                        return clip;
                    }
                    else if (item != DependencyProperty.UnsetValue)
                    {
                        return item;
                    }
                }
                element = VisualTreeHelper.GetParent(element) as UIElement;
            }
            return null;
        }
    }
}