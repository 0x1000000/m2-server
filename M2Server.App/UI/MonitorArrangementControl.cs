using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using M2Server.Lib.Domain;

namespace M2Server.App;

/// <summary>Profile monitor layout and settings, independent of the main window shell.</summary>
public sealed class MonitorArrangementControl : StackPanel
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#263746"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2563A6"));
    private static readonly IBrush NormalFill = new SolidColorBrush(Color.Parse("#DFE3E7"));
    private static readonly IBrush DisabledFill = new SolidColorBrush(Color.Parse("#F1F3F5"));
    private static readonly IBrush StatusFill = new SolidColorBrush(Color.Parse("#A0263746"));
    private static readonly IBrush DisabledStatusFill = new SolidColorBrush(Color.Parse("#E5E9EC"));
    private static readonly IBrush DisabledStatusInk = new SolidColorBrush(Color.Parse("#677580"));

    private readonly StackPanel _arrangement = new()
    {
        Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Spacing = 4
    };

    private readonly List<(string Id, Control Card)> _cards = [];
    private readonly IReadOnlyList<MonitorInfo> _connected;
    private readonly IReadOnlyList<MonitorSetting> _monitors;
    private readonly Action<string, string> _place;
    private readonly Action _refresh;
    private readonly Action<string> _remove;
    private readonly IReadOnlySet<string> _savedIds;
    private readonly Action<string> _select;
    private readonly Action<MonitorSetting> _setPrimary;
    private readonly StackPanel _settings = new() { Spacing = 8 };

    private readonly Action<MonitorSetting, Action<MonitorSetting>> _update;
    private bool _dragActive;
    private Point _dragStart;
    private string? _dragging;
    private string? _dropTargetId;
    private string? _selectedId;

    public MonitorArrangementControl(
        IReadOnlyList<MonitorSetting> settings,
        IReadOnlyList<MonitorInfo> connected,
        IReadOnlySet<string> savedIds,
        Action<MonitorSetting, Action<MonitorSetting>> update,
        Action<MonitorSetting> setPrimary,
        Action<string, string> place,
        Action refresh,
        string? selectedId,
        Action<string> select,
        Action<string> remove)
    {
        this._update = update;
        this._setPrimary = setPrimary;
        this._place = place;
        this._remove = remove;
        this._refresh = refresh;
        this._monitors = settings;
        this._connected = connected;
        this._savedIds = savedIds;
        this._select = select;
        this._selectedId = settings.Any(m => m.Id == selectedId)
            ? selectedId
            : settings.OrderBy(m => m.Order).FirstOrDefault()?.Id;
        this.Spacing = 12;
        this.Children.Add(new TextBlock { Text = "Monitors", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Ink });
        this.Children.Add(
            new TextBlock { Text = "Select a monitor to edit it. Drag a monitor to change its order.", Foreground = Ink }
        );
        this.Children.Add(
            new ScrollViewer
            {
                Content = this._arrangement,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        );
        this.Children.Add(this._settings);

        var ordered = settings.OrderBy(m => m.Order).ToList();
        var smallestSide = ordered.Count == 0 ? 1 : ordered.Min(ShortSide);
        foreach (var setting in ordered)
        {
            var card = this.CreateCard(
                setting,
                connected.FirstOrDefault(m => m.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase)),
                smallestSide
            );
            this._cards.Add((setting.Id, card));
            this._arrangement.Children.Add(card);
        }

        this.ShowSettings();
    }

    private Control CreateCard(MonitorSetting setting, MonitorInfo? live, int smallestSide)
    {
        var ratio = setting.Rotation is DisplayRotation.Rotate90 or DisplayRotation.Rotate270
            ? (double)setting.Mode.Height / Math.Max(1, setting.Mode.Width)
            : (double)setting.Mode.Width / Math.Max(1, setting.Mode.Height);
        var relativeSize = Math.Clamp((double)ShortSide(setting) / smallestSide, 1, 1.8);
        var area = 12000.0 * relativeSize * relativeSize;
        var width = Math.Sqrt(area * ratio);
        var height = Math.Sqrt(area / ratio);
        var selected = setting.Id == this._selectedId;
        var label = live?.Name ?? setting.Name;
        var device = live?.DeviceName;
        var model = label;
        if (!string.IsNullOrWhiteSpace(device) && label.EndsWith($" ({device})", StringComparison.OrdinalIgnoreCase))
        {
            model = label[..^(device.Length + 3)];
        }

        var title = string.IsNullOrWhiteSpace(device) ? model : device;
        var details = string.IsNullOrWhiteSpace(device) ? null : model;
        var status = $"{setting.Mode.Width}×{setting.Mode.Height} {setting.Mode.RefreshHz}Hz";
        status += setting.HdrEnabled switch { true => " HDR", false => " SDR", _ => "" };
        var surface = new Grid
        {
            Width = width,
            Height = height,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = TileFill(selected, !setting.Enabled),
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = Math.Max(50, width - 14),
                    Children
                        =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontSize = 12,
                                FontWeight = FontWeight.SemiBold,
                                TextAlignment = TextAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                Foreground = selected && setting.Enabled ? Brushes.White : Ink
                            },
                            new TextBlock
                            {
                                Text = details,
                                FontSize = 10,
                                TextAlignment = TextAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                Foreground = selected && setting.Enabled ? Brushes.White : Ink
                            }
                        }
                },
                new Border
                {
                    Background = setting.Enabled ? StatusFill : DisabledStatusFill,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Padding = new Thickness(3, 2),
                    Child = new TextBlock
                    {
                        Text = status,
                        FontSize = 9,
                        Foreground = setting.Enabled ? Brushes.White : DisabledStatusInk,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                },
                new Border
                {
                    IsVisible = setting.Primary,
                    Background = new SolidColorBrush(Color.Parse("#F4C542")),
                    CornerRadius = new CornerRadius(12),
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 4, 23),
                    Child = new TextBlock
                    {
                        Text = "★",
                        FontSize = 13,
                        Foreground = Ink,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        surface.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
            {
                return;
            }

            this._dragging = setting.Id;
            this._dragStart = e.GetPosition(this._arrangement);
            this._dragActive = false;
            e.Pointer.Capture(surface);
            e.Handled = true;
        };
        surface.PointerMoved += (_, e) =>
        {
            if (this._dragging != setting.Id)
            {
                return;
            }

            var point = e.GetPosition(this._arrangement);
            if (this._savedIds.Contains(setting.Id) && !this._dragActive &&
                Math.Abs(point.X - this._dragStart.X) + Math.Abs(point.Y - this._dragStart.Y) > 6)
            {
                this._dragActive = true;
                var draggedCard = this._cards.First(x => x.Id == setting.Id).Card;
                draggedCard.Opacity = .75;
                draggedCard.ZIndex = 10;
                surface.Cursor = new Cursor(StandardCursorType.SizeAll);
            }

            if (this._dragActive)
            {
                this._cards.First(x => x.Id == setting.Id).Card.RenderTransform = new TranslateTransform(
                    point.X - this._dragStart.X,
                    point.Y - this._dragStart.Y
                );
                this.UpdateDropTarget(setting.Id, point);
            }
        };
        surface.PointerReleased += (_, e) =>
        {
            if (this._dragging != setting.Id)
            {
                return;
            }

            if (this._dragActive)
            {
                this.UpdateDropTarget(setting.Id, e.GetPosition(this._arrangement));
            }

            var targetId = this._dropTargetId;
            var wasDragging = this._dragActive;
            this.ResetDragVisuals();
            surface.Cursor = new Cursor(StandardCursorType.Hand);
            this._dragging = null;
            e.Pointer.Capture(null);
            if (wasDragging && targetId is not null)
            {
                this._place(setting.Id, targetId);
            }
            else if (!wasDragging)
            {
                this._selectedId = setting.Id;
                this._select(setting.Id);
                this.ShowSettings();
                foreach (var (id, card) in this._cards)
                {
                    if (card is Grid cardGrid)
                    {
                        UpdateCardAppearance(cardGrid, id == setting.Id);
                    }
                }
            }

            e.Handled = true;
        };
        surface.PointerCaptureLost += (_, _) =>
        {
            if (this._dragging != setting.Id)
            {
                return;
            }

            this.ResetDragVisuals();
            surface.Cursor = new Cursor(StandardCursorType.Hand);
            this._dragging = null;
        };
        var outline = new MonitorOutline { IsDisabled = !setting.Enabled, IsSelected = selected, IsMissing = live is null };
        surface.PointerEntered += (_, _) => outline.IsHovered = true;
        surface.PointerExited += (_, _) => outline.IsHovered = false;
        return new Grid
        {
            Width = width + 8,
            Height = height + 8,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 2, 0),
            Children =
            {
                surface,
                outline,
                new Border
                {
                    IsVisible = false,
                    IsHitTestVisible = false,
                    Background = new SolidColorBrush(Color.Parse("#DDEBF7")),
                    BorderBrush = Accent,
                    BorderThickness = new Thickness(3),
                    Child = new TextBlock
                    {
                        Text = "Drop here",
                        Foreground = Accent,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
    }

    private void ShowSettings()
    {
        this._settings.Children.Clear();
        var setting = this._monitors.FirstOrDefault(m => m.Id == this._selectedId);
        if (setting is null)
        {
            return;
        }

        var live = this._connected.FirstOrDefault(m => m.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase));
        this._settings.Children.Add(new TextBlock { Text = setting.Name, FontWeight = FontWeight.SemiBold, Foreground = Ink });
        if (live is null)
        {
            var delete = new Button { Content = "Delete missing monitor", HorizontalAlignment = HorizontalAlignment.Left };
            delete.Click += (_, _) => this._remove(setting.Id);
            this._settings.Children.Add(delete);
            return;
        }

        var body = new StackPanel { Spacing = 8, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
        var enabled = new CheckBox
        {
            Content = "Enabled", IsChecked = setting.Enabled, Cursor = new Cursor(StandardCursorType.Hand)
        };
        enabled.IsCheckedChanged += (_, _) => this._update(setting, m => m.Enabled = enabled.IsChecked == true);
        body.Children.Add(enabled);
        var primary = new CheckBox
        {
            Content = "Primary", IsChecked = setting.Primary, Cursor = new Cursor(StandardCursorType.Hand)
        };
        primary.IsCheckedChanged += (_, _) =>
        {
            if (primary.IsChecked == true)
            {
                this._setPrimary(setting);
            }
        };
        body.Children.Add(primary);

        var modes = (live?.Modes ?? [setting.Mode]).Distinct().ToList();
        if (!modes.Contains(setting.Mode))
        {
            modes.Add(setting.Mode);
        }

        var resolutions = modes.Select(m => (m.Width, m.Height)).Distinct().ToList();
        var selectedResolution = resolutions.IndexOf((setting.Mode.Width, setting.Mode.Height));
        body.Children.Add(
            Choice(
                "Resolution",
                resolutions.Select(r => $"{r.Width} × {r.Height}").ToList(),
                selectedResolution,
                i =>
                {
                    var resolution = resolutions[i];
                    var supported = modes.Where(m => m.Width == resolution.Width && m.Height == resolution.Height).ToList();
                    var next = supported.FirstOrDefault(m => m.RefreshHz == setting.Mode.RefreshHz) ??
                               supported.OrderByDescending(m => m.RefreshHz).First();
                    this._update(setting, m => m.Mode = next);
                    this._refresh();
                }
            )
        );
        var refreshRates = modes.Where(m => m.Width == setting.Mode.Width && m.Height == setting.Mode.Height)
            .Select(m => m.RefreshHz)
            .Distinct()
            .OrderByDescending(hz => hz)
            .ToList();
        body.Children.Add(
            Choice(
                "Refresh rate",
                refreshRates.Select(hz => $"{hz} Hz").ToList(),
                refreshRates.IndexOf(setting.Mode.RefreshHz),
                i =>
                {
                    var selectedMode = modes.First(m
                        => m.Width == setting.Mode.Width && m.Height == setting.Mode.Height && m.RefreshHz == refreshRates[i]
                    );
                    this._update(setting, m => m.Mode = selectedMode);
                    this._refresh();
                }
            )
        );
        var rotations = new[]
        {
            DisplayRotation.Identity, DisplayRotation.Rotate90, DisplayRotation.Rotate180, DisplayRotation.Rotate270
        };
        body.Children.Add(
            Choice(
                "Orientation",
                ["0°", "90°", "180°", "270°"],
                Array.IndexOf(rotations, setting.Rotation),
                i =>
                {
                    this._update(setting, m => m.Rotation = rotations[i]);
                    this._refresh();
                }
            )
        );
        var scales = (live?.DpiScales ?? []).Distinct().Order().ToList();
        if (setting.DpiScale is { } selected && !scales.Contains(selected))
        {
            scales.Add(selected);
        }

        body.Children.Add(
            Choice(
                "Scale",
                ["Keep current", .. scales.Select(x => $"{x}%")],
                setting.DpiScale is { } n ? scales.IndexOf(n) + 1 : 0,
                i => this._update(setting, m => m.DpiScale = i > 0 ? scales[i - 1] : null)
            )
        );
        body.Children.Add(
            Choice(
                "HDR",
                ["Keep current", "On", "Off"],
                setting.HdrEnabled switch { true => 1, false => 2, _ => 0 },
                i =>
                {
                    this._update(setting, m => m.HdrEnabled = i switch { 1 => true, 2 => false, _ => null });
                    this._refresh();
                },
                live?.HdrSupported == true
            )
        );

        enabled.IsCheckedChanged += (_, _) =>
        {
            if (this._cards.FirstOrDefault(x => x.Id == setting.Id).Card is Grid card)
            {
                card.Children.OfType<MonitorOutline>().First().IsDisabled = enabled.IsChecked != true;
                UpdateCardAppearance(card, setting.Id == this._selectedId);
            }
        };
        this._settings.Children.Add(body);
    }

    private static Control Choice(
        string label,
        IReadOnlyList<string> options,
        int selected,
        Action<int> changed,
        bool enabled = true)
    {
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("120,300"), ColumnSpacing = 12 };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedIndex = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = enabled,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                changed(combo.SelectedIndex);
            }
        };
        Grid.SetColumn(combo, 1);
        panel.Children.Add(combo);
        return panel;
    }

    private static int ShortSide(MonitorSetting setting)
    {
        return Math.Max(1, Math.Min(setting.Mode.Width, setting.Mode.Height));
    }

    private void UpdateDropTarget(string sourceId, Point point)
    {
        var target = this._cards.FirstOrDefault(x
            => x.Id != sourceId && this._savedIds.Contains(x.Id) && x.Card.Bounds.Contains(point)
        );
        var targetId = target.Id;
        if (this._dropTargetId == targetId)
        {
            return;
        }

        this._dropTargetId = targetId;
        foreach (var (id, card) in this._cards)
        {
            if (card is not Grid grid)
            {
                continue;
            }

            grid.Children.OfType<MonitorOutline>().First().IsDropTarget = id == targetId;
            grid.Children.OfType<Border>().First().IsVisible = id == targetId;
        }
    }

    private void ResetDragVisuals()
    {
        this._dropTargetId = null;
        this._dragActive = false;
        foreach (var (_, card) in this._cards)
        {
            card.Opacity = 1;
            card.RenderTransform = null;
            card.ZIndex = 0;
            if (card is Grid grid)
            {
                grid.Children.OfType<MonitorOutline>().First().IsDropTarget = false;
                grid.Children.OfType<Border>().First().IsVisible = false;
            }
        }
    }

    private static IBrush TileFill(bool selected, bool disabled)
    {
        return disabled ? DisabledFill : selected ? Accent : NormalFill;
    }

    private static void UpdateCardAppearance(Grid card, bool selected)
    {
        var outline = card.Children.OfType<MonitorOutline>().First();
        var surface = card.Children.OfType<Grid>().First();
        outline.IsSelected = selected;
        surface.Background = TileFill(selected, outline.IsDisabled);
        var textColor = selected && !outline.IsDisabled ? Brushes.White : Ink;
        if (surface.Children.OfType<StackPanel>().FirstOrDefault() is { } labels)
        {
            foreach (var text in labels.Children.OfType<TextBlock>())
            {
                text.Foreground = textColor;
            }
        }

        if (surface.Children.OfType<Border>().FirstOrDefault() is { } status)
        {
            status.Background = outline.IsDisabled ? DisabledStatusFill : StatusFill;
            if (status.Child is TextBlock text)
            {
                text.Foreground = outline.IsDisabled ? DisabledStatusInk : Brushes.White;
            }
        }
    }

    private sealed class MonitorOutline : Control
    {
        private bool _isDisabled;
        private bool _isDropTarget;
        private bool _isHovered;
        private bool _isMissing;
        private bool _isSelected;

        public MonitorOutline()
        {
            this.IsHitTestVisible = false;
        }

        public bool IsSelected
        {
            get => this._isSelected;
            set
            {
                this._isSelected = value;
                this.InvalidateVisual();
            }
        }

        public bool IsMissing
        {
            get => this._isMissing;
            set
            {
                this._isMissing = value;
                this.InvalidateVisual();
            }
        }

        public bool IsDisabled
        {
            get => this._isDisabled;
            set
            {
                this._isDisabled = value;
                this.InvalidateVisual();
            }
        }

        public bool IsDropTarget
        {
            get => this._isDropTarget;
            set
            {
                this._isDropTarget = value;
                this.InvalidateVisual();
            }
        }

        public bool IsHovered
        {
            get => this._isHovered;
            set
            {
                this._isHovered = value;
                this.InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var brush = this._isMissing ? Brushes.Firebrick :
                this.IsHovered ? Accent :
                this._isDisabled ? Brushes.DimGray :
                this.IsSelected ? Ink : Brushes.Transparent;
            var dash = this._isMissing || this._isDisabled ? new DashStyle([1, 2], 0) : null;
            context.DrawRectangle(
                null,
                new Pen(brush, this.IsHovered ? 3 : 2.5, dash),
                new Rect(1.5, 1.5, this.Bounds.Width - 3, this.Bounds.Height - 3)
            );
            if (this.IsDropTarget)
            {
                context.DrawRectangle(
                    null,
                    new Pen(Accent, 4.5),
                    new Rect(2.5, 2.5, this.Bounds.Width - 5, this.Bounds.Height - 5)
                );
            }
        }
    }
}