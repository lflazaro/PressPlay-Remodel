using System.Windows;
using System.Windows.Media;

namespace PressPlay.Helpers
{
    public static class VisualHelper
    {
        /// <summary>
        /// Finds an ancestor of the specified type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of ancestor to find.</typeparam>
        /// <param name="element">The element to start searching from.</param>
        /// <returns>The ancestor of the specified type, or null if not found.</returns>
        public static T GetAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            if (element == null)
                return null;

            var parent = VisualTreeHelper.GetParent(element);

            while (parent != null)
            {
                if (parent is T found)
                {
                    return found;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }

        /// <summary>
        /// Gets the parent of a dependency object in the visual tree.
        /// </summary>
        /// <param name="element">The element to get the parent for.</param>
        /// <returns>The parent element, or null if not found.</returns>
        public static DependencyObject GetParent(DependencyObject element)
        {
            if (element == null)
                return null;

            return VisualTreeHelper.GetParent(element);
        }

        /// <summary>
        /// Finds a visual child of the specified type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of child to find.</typeparam>
        /// <param name="parent">The parent element to search in.</param>
        /// <returns>The first child of the specified type, or null if not found.</returns>
        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            // Try to find direct child
            if (parent is T found)
                return found;

            // Search child elements
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Check if this child is of the requested type
                if (child is T childFound)
                    return childFound;

                // Recursively search children's children
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// Finds all visual children of the specified type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of children to find.</typeparam>
        /// <param name="parent">The parent element to search in.</param>
        /// <param name="results">The collection to add found children to.</param>
        public static void FindVisualChildren<T>(DependencyObject parent, System.Collections.Generic.List<T> results) where T : DependencyObject
        {
            if (parent == null || results == null)
                return;

            // Check each child
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Check if this child is of the requested type
                if (child is T foundChild)
                {
                    results.Add(foundChild);
                }

                // Recurse into this child's children
                FindVisualChildren<T>(child, results);
            }
        }
    }
}