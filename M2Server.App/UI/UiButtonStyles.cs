using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace M2Server.App;

internal static class UiButtonStyles
{
    internal static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#263746"));
    internal static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#2563A6"));
    internal static readonly IBrush SoftHover = new SolidColorBrush(Color.Parse("#E8F0F7"));
    internal static readonly IBrush SelectedHover = new SolidColorBrush(Color.Parse("#C9DEF1"));
    internal static readonly IBrush BlueHover = new SolidColorBrush(Color.Parse("#1A538B"));

    internal static void Apply(
        Button button,
        IBrush normal,
        IBrush hover,
        IBrush? foreground = null,
        IBrush? hoverForeground = null)
    {
        button.Background = normal;
        button.Cursor = new Cursor(StandardCursorType.Hand);
        if (foreground is not null)
        {
            button.Foreground = foreground;
        }

        button.PointerEntered += (_, _) =>
        {
            if (!button.IsEnabled)
            {
                return;
            }

            button.Background = hover;
            if (hoverForeground is not null)
            {
                button.Foreground = hoverForeground;
            }
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = normal;
            if (foreground is not null)
            {
                button.Foreground = foreground;
            }
        };
    }
}