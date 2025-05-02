using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PressPlay.Helpers;
using PressPlay.Models;
using PressPlay.Utilities;

namespace PressPlay.Timeline
{
    /// <summary>
    /// Interaction logic for TrackItemControl.xaml
    /// </summary>
    public partial class TrackItemControl : Border
    {
        private TimelineControl _timelineControl;

        public TrackItemControl()
        {
            InitializeComponent();
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            _timelineControl ??= VisualHelper.GetAncestor<TimelineControl>(this);
            var project = _timelineControl.Project;

            if (project.SelectedTool == TimelineSelectedTool.SelectionTool
                && DataContext is TrackItem trackItem
                && !trackItem.IsChangingFadeIn
                && !trackItem.IsChangingFadeOut)
            {
                var mouseX = e.GetPosition(this).X;

                // resize on borders
                if (Math.Abs(mouseX) <= 3)
                {
                    Cursor = Cursors.SizeWE;
                    resizeBorder.BorderThickness = new Thickness(2, 0, 0, 0);
                }
                else if (Math.Abs(mouseX - Width) <= 3)
                {
                    Cursor = Cursors.SizeWE;
                    resizeBorder.BorderThickness = new Thickness(0, 0, 2, 0);
                }
                else
                {
                    Cursor = Cursors.Arrow;
                    resizeBorder.BorderThickness = new Thickness(0);
                }
            }
            else if (project.SelectedTool == TimelineSelectedTool.CuttingTool)
            {
                Cursor = Cursors.IBeam;
            }
            else
            {
                resizeBorder.BorderThickness = new Thickness(0);
                Cursor = Cursors.Arrow;
            }

            base.OnPreviewMouseMove(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            resizeBorder.BorderThickness = new Thickness(0);
            base.OnMouseLeave(e);
        }

        private void CutItem_Click(object sender, RoutedEventArgs e)
        {
            _timelineControl.Project.Cut();
        }

        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            _timelineControl.Project.Copy();
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is TrackItem)
            {
                _timelineControl.Project.DeleteSelectedItems();
            }
        }

        private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is AudioTrackItem trackItem)
            {
                var project = MainWindowViewModel.Instance.CurrentProject;
                var startLength = PixelCalculator.GetPixels(trackItem.Start.TotalFrames, project.TimelineZoom);
                img.Margin = new Thickness(-startLength, 0, 0, 0);
            }
        }
    }
}
