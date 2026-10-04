using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using M2Server.Lib.Domain;

namespace M2Server.App;

/// <summary>Wrapped profile and script tiles with actions at the bottom.</summary>
public sealed class CategoryOverviewControl : StackPanel
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#263746"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#617182"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#2563A6"));
    private readonly List<(Guid Id, Grid Card, Border Highlight)> _cards = [];
    private readonly WrapPanel _tiles = new() { Orientation = Orientation.Horizontal };
    private readonly string _title;

    public CategoryOverviewControl(string title)
    {
        this._title = title;
        this.Spacing = 12;
        this.Children.Add(new TextBlock { Text = "Drag a tile to change its order.", Foreground = Muted });
        this.Children.Add(this._tiles);
    }

    public Control CreateHeader(Action add)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(
            new TextBlock
            {
                Text = this._title,
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center
            }
        );
        var button = ActionButton(this._title == "Profiles" ? "+ Add profile" : "+ Add script", add, true);
        Grid.SetColumn(button, 1);
        header.Children.Add(button);
        return header;
    }

    public void ShowProfiles(
        IReadOnlyList<Preset> profiles,
        IReadOnlySet<Guid> activeIds,
        Action<Guid> edit,
        Action<Guid> activate,
        Action<Guid> delete,
        Action<Guid, Guid> place)
    {
        this._tiles.Children.Clear();
        this._cards.Clear();
        if (profiles.Count == 0)
        {
            this._tiles.Children.Add(new TextBlock { Text = "Create a profile with Add profile above." });
            return;
        }

        foreach (var profile in profiles)
        {
            var active = activeIds.Contains(profile.Id);
            var details = new StackPanel { Spacing = 3 };
            if (profile.Monitors.Count == 0)
            {
                details.Children.Add(Detail("No monitors saved"));
            }

            foreach (var monitor in profile.Monitors.OrderBy(m => m.Order))
            {
                details.Children.Add(
                    new TextBlock
                    {
                        Text = monitor.Name + (monitor.Primary ? " (Primary)" : ""),
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Ink,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0)
                    }
                );
                if (!monitor.Enabled)
                {
                    details.Children.Add(Detail("Off"));
                    continue;
                }

                details.Children.Add(Detail($"Resolution  {monitor.Mode.Width} × {monitor.Mode.Height}"));
                details.Children.Add(Detail($"Scale  {(monitor.DpiScale is int scale ? $"{scale}%" : "Keep current")}"));
                details.Children.Add(Detail($"Refresh rate  {monitor.Mode.RefreshHz} Hz"));
                details.Children.Add(Detail($"Rotation  {RotationDegrees(monitor.Rotation)}°"));
                details.Children.Add(
                    Detail($"HDR  {monitor.HdrEnabled switch { true => "On", false => "Off", _ => "Keep current" }}")
                );
            }

            this._tiles.Children.Add(
                this.DraggableTile(
                    profile.Id,
                    Tile(
                        profile.Name,
                        details,
                        "Edit",
                        () => edit(profile.Id),
                        active ? "Active" : "Activate",
                        () => activate(profile.Id),
                        !active,
                        () => delete(profile.Id)
                    ),
                    place
                )
            );
        }
    }

    public void ShowScripts(
        IReadOnlyList<ScriptEntry> scripts,
        Action<Guid> edit,
        Action<Guid> run,
        Action<Guid> delete,
        Action<Guid, Guid> place)
    {
        this._tiles.Children.Clear();
        this._cards.Clear();
        if (scripts.Count == 0)
        {
            this._tiles.Children.Add(new TextBlock { Text = "Create a script with Add script above." });
            return;
        }

        foreach (var script in scripts)
        {
            this._tiles.Children.Add(
                this.DraggableTile(
                    script.Id,
                    Tile(
                        script.Name,
                        new StackPanel(),
                        "Edit",
                        () => edit(script.Id),
                        "Run",
                        () => run(script.Id),
                        true,
                        () => delete(script.Id)
                    ),
                    place
                )
            );
        }
    }

    private Control DraggableTile(Guid id, Control tile, Action<Guid, Guid> place)
    {
        var highlight = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            Margin = new Thickness(0, 0, 12, 12),
            Background = new SolidColorBrush(Color.Parse("#DDEBF7")),
            BorderBrush = Blue,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "Drop here",
                Foreground = Blue,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var card = new Grid { Children = { tile, highlight }, Cursor = new Cursor(StandardCursorType.Hand) };
        this._cards.Add((id, card, highlight));
        Point start = default;
        var pressed = false;
        var dragging = false;
        Guid? targetId = null;

        void UpdateTarget(Point point)
        {
            targetId = null;
            foreach (var candidate in this._cards)
            {
                var hit = candidate.Id != id && candidate.Card.Bounds.Contains(point);
                candidate.Highlight.IsVisible = hit;
                if (hit)
                {
                    targetId = candidate.Id;
                }
            }
        }

        void Reset()
        {
            pressed = false;
            dragging = false;
            targetId = null;
            card.Opacity = 1;
            card.RenderTransform = null;
            card.ZIndex = 0;
            card.Cursor = new Cursor(StandardCursorType.Hand);
            foreach (var candidate in this._cards)
            {
                candidate.Highlight.IsVisible = false;
            }
        }

        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.Source is Visual source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any()))
            {
                return;
            }

            start = e.GetPosition(this._tiles);
            pressed = true;
            e.Pointer.Capture(card);
            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (!pressed)
            {
                return;
            }

            var point = e.GetPosition(this._tiles);
            if (!dragging && Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) <= 6)
            {
                return;
            }

            dragging = true;
            card.Opacity = .75;
            card.ZIndex = 10;
            card.Cursor = new Cursor(StandardCursorType.SizeAll);
            card.RenderTransform = new TranslateTransform(point.X - start.X, point.Y - start.Y);
            UpdateTarget(point);
            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            if (!pressed)
            {
                return;
            }

            if (dragging)
            {
                UpdateTarget(e.GetPosition(this._tiles));
            }

            var destination = targetId;
            Reset();
            e.Pointer.Capture(null);
            if (destination is { } target)
            {
                place(id, target);
            }

            e.Handled = true;
        };
        card.PointerCaptureLost += (_, _) => Reset();
        return card;
    }

    private static Control Tile(
        string title,
        Control details,
        string editLabel,
        Action edit,
        string primaryLabel,
        Action primary,
        bool primaryEnabled,
        Action delete)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), MinHeight = 150 };
        layout.Children.Add(
            new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap
            }
        );
        Grid.SetRow(details, 1);
        layout.Children.Add(details);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(ActionButton(editLabel, edit));
        actions.Children.Add(ActionButton(primaryLabel, primary, true, primaryEnabled));
        actions.Children.Add(ActionButton("Delete", delete));
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        var tile = new Border
        {
            Width = 280,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#D6E0E8")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = layout
        };
        tile.PointerEntered += (_, _) =>
        {
            tile.Background = new SolidColorBrush(Color.Parse("#F8FBFE"));
            tile.BorderBrush = new SolidColorBrush(Color.Parse("#B8D0E2"));
        };
        tile.PointerExited += (_, _) =>
        {
            tile.Background = Brushes.White;
            tile.BorderBrush = new SolidColorBrush(Color.Parse("#D6E0E8"));
        };
        return tile;
    }

    private static Button ActionButton(string text, Action action, bool primary = false, bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            IsEnabled = enabled,
            Padding = new Thickness(8, 5),
            MinWidth = 60,
            Background = primary ? Blue : Brushes.White,
            Foreground = primary ? Brushes.White : Ink,
            BorderBrush = primary ? Blue : new SolidColorBrush(Color.Parse("#CFDCE7")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5)
        };
        UiButtonStyles.Apply(
            button,
            primary ? Blue : Brushes.White,
            primary ? UiButtonStyles.BlueHover : UiButtonStyles.SoftHover,
            primary ? Brushes.White : Ink
        );
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock Detail(string text)
    {
        return new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
    }

    private static int RotationDegrees(DisplayRotation rotation)
    {
        return rotation switch
        {
            DisplayRotation.Rotate90 => 90,
            DisplayRotation.Rotate180 => 180,
            DisplayRotation.Rotate270 => 270,
            _ => 0
        };
    }
}