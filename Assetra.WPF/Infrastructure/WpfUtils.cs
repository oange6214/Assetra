using System.Windows;
using System.Windows.Media;

namespace Assetra.WPF.Infrastructure;

/// <summary>Shared WPF visual-tree and input helpers used across code-behind files.</summary>
internal static class WpfUtils
{
    /// <summary>
    /// Walks the visual tree upward from <paramref name="d"/> and returns the first
    /// ancestor of type <typeparamref name="T"/>, or <c>null</c> if not found.
    /// </summary>
    public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T found)
                return found;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
