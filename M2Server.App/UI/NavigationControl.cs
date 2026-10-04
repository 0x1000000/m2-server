using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using M2Server.Lib.Domain;

namespace M2Server.App;

/// <summary>The desktop navigation and per-item action menus.</summary>
public sealed class NavigationControl : StackPanel
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#263746"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#2563A6"));
    private static readonly IBrush SelectedFill = new SolidColorBrush(Color.Parse("#E1EDF8"));
    private readonly Action<Guid> _activateProfile;
    private readonly Action _addProfile;
    private readonly Action _addScript;
    private readonly Action<Guid> _deleteProfile;
    private readonly Action<Guid> _deleteScript;
    private readonly Action<Guid> _runScript;
    private readonly Action<string, Guid> _select;

    public NavigationControl(
        Action<string, Guid> select,
        Action addProfile,
        Action addScript,
        Action<Guid> activateProfile,
        Action<Guid> runScript,
        Action<Guid> deleteProfile,
        Action<Guid> deleteScript)
    {
        this._select = select;
        this._addProfile = addProfile;
        this._addScript = addScript;
        this._activateProfile = activateProfile;
        this._runScript = runScript;
        this._deleteProfile = deleteProfile;
        this._deleteScript = deleteScript;
        this.Spacing = 4;
    }

    public void Show(
        IReadOnlyList<Preset> profiles,
        IReadOnlyList<ScriptEntry> scripts,
        IReadOnlySet<Guid> activeProfileIds,
        string selectedKind,
        Guid selectedId,
        Func<Guid, bool> isDraftProfile,
        Func<Guid, bool> isDraftScript)
    {
        this.Children.Clear();
        this.Children.Add(
            new TextBlock
            {
                Text = "M2 Server",
                FontWeight = FontWeight.Bold,
                Foreground = Blue,
                FontSize = 17,
                Margin = new Thickness(4, 0, 0, 20)
            }
        );

        this.Children.Add(this.Header("▣  Profiles", "profiles", selectedKind == "profiles", this._addProfile));
        foreach (var profile in profiles.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            var active = activeProfileIds.Contains(profile.Id);
            var draft = isDraftProfile(profile.Id);
            this.Children.Add(
                this.Item(
                    profile.Name + (draft ? " (Draft)" : "") + (active ? " (Active)" : ""),
                    "preset",
                    profile.Id,
                    selectedKind == "preset" && selectedId == profile.Id,
                    active || draft ? null : () => this._activateProfile(profile.Id),
                    "Activate",
                    () => this._deleteProfile(profile.Id)
                )
            );
        }

        this.Children.Add(Divider());
        this.Children.Add(this.Header(">_  Scripts", "scripts", selectedKind == "scripts", this._addScript));
        foreach (var script in scripts.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
        {
            var draft = isDraftScript(script.Id);
            this.Children.Add(
                this.Item(
                    script.Name + (draft ? " (Draft)" : ""),
                    "script",
                    script.Id,
                    selectedKind == "script" && selectedId == script.Id,
                    draft ? null : () => this._runScript(script.Id),
                    "Run",
                    () => this._deleteScript(script.Id)
                )
            );
        }

        this.Children.Add(Divider());
        this.Children.Add(
            NavigationButton("⚙  Settings", () => this._select("settings", Guid.Empty), selectedKind == "settings", true)
        );
        this.Children.Add(NavigationButton("ⓘ  About", () => this._select("about", Guid.Empty), selectedKind == "about", true));
    }

    private Control Header(string text, string kind, bool selected, Action add)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(NavigationButton(text, () => this._select(kind, Guid.Empty), selected, true));
        var plus = new Button
        {
            Content = "+",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Background = selected ? SelectedFill : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Blue,
            Padding = new Thickness(8, 2),
            MinWidth = 32,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        UiButtonStyles.Apply(
            plus,
            selected ? SelectedFill : Brushes.Transparent,
            selected ? UiButtonStyles.SelectedHover : UiButtonStyles.SoftHover,
            Blue
        );
        ToolTip.SetTip(plus, kind == "profiles" ? "Add profile" : "Add script");
        plus.Click += (_, _) => add();
        Grid.SetColumn(plus, 1);
        grid.Children.Add(plus);
        return grid;
    }

    private Control Item(
        string text,
        string kind,
        Guid id,
        bool selected,
        Action? primaryAction,
        string primaryLabel,
        Action delete)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var label = NavigationButton(text, () => this._select(kind, id), selected, false);
        label.Padding = new Thickness(20, 8, 4, 8);
        grid.Children.Add(label);
        var more = new Button
        {
            Content = "⋯",
            FontSize = 18,
            Background = selected ? SelectedFill : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = selected ? Blue : Ink,
            Padding = new Thickness(4, 2),
            MinWidth = 30,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        UiButtonStyles.Apply(
            more,
            selected ? SelectedFill : Brushes.Transparent,
            selected ? UiButtonStyles.SelectedHover : UiButtonStyles.SoftHover,
            selected ? Blue : Ink
        );
        ToolTip.SetTip(more, "Actions");
        var menu = new ContextMenu();
        if (primaryAction is not null)
        {
            var primary = new MenuItem { Header = primaryLabel, Cursor = new Cursor(StandardCursorType.Hand) };
            primary.Click += (_, _) => primaryAction();
            menu.Items.Add(primary);
        }

        var remove = new MenuItem { Header = "Delete", Cursor = new Cursor(StandardCursorType.Hand) };
        remove.Click += (_, _) => delete();
        menu.Items.Add(remove);
        grid.ContextMenu = menu;
        var openedByDots = false;
        var normal = selected ? SelectedFill : Brushes.Transparent;
        var hover = selected ? UiButtonStyles.SelectedHover : UiButtonStyles.SoftHover;

        void UpdateHighlight()
        {
            label.Background = menu.IsOpen && !openedByDots ? hover : label.IsPointerOver ? hover : normal;
            more.Background = menu.IsOpen ? hover : more.IsPointerOver ? hover : normal;
        }

        grid.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(grid).Properties.IsRightButtonPressed)
            {
                openedByDots = more.Bounds.Contains(e.GetPosition(grid));
            }
        };
        label.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(label).Properties.IsRightButtonPressed)
            {
                openedByDots = false;
            }
        };
        more.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(more).Properties.IsRightButtonPressed)
            {
                openedByDots = true;
            }
        };
        more.Click += (_, _) =>
        {
            openedByDots = true;
            menu.Open(grid);
        };
        menu.Opened += (_, _) => UpdateHighlight();
        menu.Closed += (_, _) =>
        {
            openedByDots = false;
            UpdateHighlight();
        };
        label.PointerExited += (_, _) =>
        {
            if (menu.IsOpen)
            {
                UpdateHighlight();
            }
        };
        more.PointerExited += (_, _) =>
        {
            if (menu.IsOpen)
            {
                UpdateHighlight();
            }
        };
        Grid.SetColumn(more, 1);
        grid.Children.Add(more);
        return grid;
    }

    private static Button NavigationButton(string text, Action action, bool selected, bool header)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = selected ? SelectedFill : Brushes.Transparent,
            Foreground = selected ? Blue : Ink,
            FontWeight = header || selected ? FontWeight.Bold : FontWeight.Normal,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 9)
        };
        UiButtonStyles.Apply(
            button,
            selected ? SelectedFill : Brushes.Transparent,
            selected ? UiButtonStyles.SelectedHover : UiButtonStyles.SoftHover,
            selected ? Blue : Ink
        );
        button.Click += (_, _) => action();
        return button;
    }

    private static Border Divider()
    {
        return new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#D6E0E8")), Margin = new Thickness(0, 12) };
    }
}