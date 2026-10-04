using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using M2Server.App.Services;
using M2Server.Lib;
using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;

namespace M2Server.App;

public sealed class MainWindow : Window
{
    private static readonly SolidColorBrush Blue = new(Color.Parse("#2563A6"));
    private static readonly SolidColorBrush ErrorRed = new(Color.Parse("#B42318"));
    private static readonly SolidColorBrush WarningOrange = new(Color.Parse("#9A6700"));
    private static readonly SolidColorBrush Ink = new(Color.Parse("#263746"));
    private static readonly SolidColorBrush Pale = new(Color.Parse("#F3F6F9"));
    private readonly DispatcherTimer _activeProfileTimer;
    private readonly ScrollViewer _bodyScroll;
    private readonly CertificateService _certificates;
    private readonly DisplayService _displays;
    private readonly Border _draftBar;
    private readonly NavigationControl _nav;
    private readonly INotificationService _notifications;
    private readonly StackPanel _page = new() { Spacing = 12 };
    private readonly ContentControl _pageHeader = new() { IsVisible = false, Margin = new Thickness(28, 22, 28, 0) };
    private readonly LatestOperationQueue<ActivationResult> _profileQueue = new();
    private readonly Button _saveButton;

    private readonly TextBlock _saveError = new()
    {
        Foreground = ErrorRed,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    private readonly TextBlock _saveWarning = new()
    {
        Foreground = WarningOrange, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right
    };

    private readonly Security _security;
    private readonly DataStore _store;
    private readonly DesktopWorkflow _workflow;
    private IReadOnlySet<Guid> _activeProfileIds = new HashSet<Guid>();
    private Guid _id;

    private string _kind = "profiles";
    private bool _saveInProgress;
    private string? _saveOperationError;
    private string? _selectedMonitorId;
    private Action? _settingsRender;
    private SettingsViewState? _settingsState;

    public MainWindow(
        DataStore store,
        DisplayService displays,
        Security security,
        CertificateService certificates,
        DesktopWorkflow workflow,
        INotificationService notifications)
    {
        this._store = store;
        this._displays = displays;
        this._security = security;
        this._certificates = certificates;
        this._workflow = workflow;
        this._notifications = notifications;
        this._nav = new NavigationControl(
            this.Select,
            this.AddPreset,
            this.AddScript,
            this.ActivatePreset,
            id => _ = this.RunScriptAsync(id),
            id => _ = this.DeletePresetAsync(id),
            id => _ = this.DeleteScriptAsync(id)
        );
        this.Title = Constants.ProductName;
        this.Width = 1280;
        this.Height = 780;
        this.MinWidth = 850;
        this.MinHeight = 600;
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Background = Brushes.White;

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*") };
        var navBorder = new Border
        {
            Background = Pale, Padding = new Thickness(14, 18), Child = new ScrollViewer { Content = this._nav }
        };
        Grid.SetColumn(navBorder, 0);
        root.Children.Add(navBorder);
        this._bodyScroll = new ScrollViewer
        {
            Padding = new Thickness(28, 22),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = this._page
        };
        var bodyGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(this._pageHeader, 0);
        bodyGrid.Children.Add(this._pageHeader);
        Grid.SetRow(this._bodyScroll, 1);
        bodyGrid.Children.Add(this._bodyScroll);
        this._saveButton = Action("Save", () => _ = this.SaveDraftAsync(), true);
        var draftContent = new StackPanel { Spacing = 6 };
        var draftRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        draftRow.Children.Add(Label("Changes to apply"));
        this._saveError.Margin = new Thickness(8, 0, 12, 0);
        this._saveError.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(this._saveError, 1);
        draftRow.Children.Add(this._saveError);
        Grid.SetColumn(this._saveButton, 2);
        draftRow.Children.Add(this._saveButton);
        var cancelButton = Action("Cancel", this.CancelDraft);
        cancelButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cancelButton, 3);
        draftRow.Children.Add(cancelButton);
        draftContent.Children.Add(draftRow);
        draftContent.Children.Add(this._saveWarning);
        this._draftBar = new Border { Background = Pale, Padding = new Thickness(20, 10), Child = draftContent };
        Grid.SetRow(this._draftBar, 2);
        bodyGrid.Children.Add(this._draftBar);
        Grid.SetColumn(bodyGrid, 1);
        root.Children.Add(bodyGrid);
        this.Content = root;
        this.Refresh();
        this._activeProfileTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        this._activeProfileTimer.Tick += (_, _) => this.RefreshActiveProfileMarkers();
        this._activeProfileTimer.Start();
        this.Closed += (_, _) => this._activeProfileTimer.Stop();
    }

    public event EventHandler? ProfilesChanged;

    private async Task SaveDraftAsync()
    {
        if (this._saveInProgress)
        {
            return;
        }

        if (this._settingsState is not null && !this._settingsState.TrySave())
        {
            this.RefreshDraftIndicators();
            this._settingsRender?.Invoke();
            return;
        }

        if (this._settingsState is not null)
        {
            this._store.Update(document =>
                {
                    this._settingsState.ApplyTo(document.Settings);
                    return 0;
                }
            );
        }

        this._saveInProgress = true;
        this._settingsState?.SetSaving(true);
        this.RefreshDraftIndicators();
        this._settingsRender?.Invoke();
        try
        {
            var webEnabled = this._store.Read(document => document.Settings.WebEnabled ?? this._security.HasPassword);
            var serviceInstalled = ServiceControl.Status() != "Not installed";
            var serviceOwned = serviceInstalled && ServiceControl.UsesDataDirectory(this._store.DirectoryPath);
            if (this._settingsState?.WebEnabled.Value == true && this._settingsState.Password.Value.Length > 0)
            {
                this._security.SetPassword(this._settingsState.Password.Value);
            }

            if (webEnabled && !this._security.HasPassword)
            {
                throw new InvalidOperationException(SettingsViewState.PasswordRequiredError);
            }

            await ConfigurationSaveService.SaveAsync(this._store);
            if (this._settingsState is not null && ((webEnabled && !serviceOwned) || (!webEnabled && serviceOwned)))
            {
                try
                {
                    if (webEnabled && serviceInstalled)
                    {
                        await ServiceControl.ChangeAsync(false);
                    }

                    await ServiceControl.ChangeAsync(webEnabled, this._store.DirectoryPath);
                }
                catch
                {
                    this._store.Update(document =>
                        {
                            document.Settings.WebEnabled = ServiceControl.Status() != "Not installed" &&
                                                           ServiceControl.UsesDataDirectory(this._store.DirectoryPath);
                            return 0;
                        }
                    );
                    await ConfigurationSaveService.SaveAsync(this._store);
                    throw;
                }
            }

            this._settingsState = null;
            this._saveOperationError = null;
            this._notifications.Show("Changes saved.");
            this.Refresh();
        }
        catch (Exception error)
        {
            this._saveOperationError = error.Message;
            this._settingsState?.SetOperationError(error.Message);
        }
        finally
        {
            this._saveInProgress = false;
            this._settingsState?.SetSaving(false);
            this.RefreshDraftIndicators();
            this._settingsRender?.Invoke();
        }
    }

    private void CancelDraft()
    {
        this._settingsState = null;
        this._saveOperationError = null;
        this._store.DiscardDrafts();
        this._notifications.Show("Unsaved changes discarded.");
        this.Refresh();
    }

    private static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text, FontWeight = FontWeight.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Button Action(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7),
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#CFDCE7"))
        };
        if (primary)
        {
            button.Background = Blue;
            button.Foreground = Brushes.White;
            button.BorderBrush = Blue;
        }

        UiButtonStyles.Apply(
            button,
            primary ? Blue : Brushes.White,
            primary ? UiButtonStyles.BlueHover : UiButtonStyles.SoftHover,
            primary ? Brushes.White : Ink
        );
        button.Click += (_, _) => action();
        return button;
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children)
        {
            row.Children.Add(child);
        }

        return row;
    }

    private void Select(string kind, Guid id = default)
    {
        this._kind = kind;
        this._id = id;
        this.Refresh();
    }

    private void Refresh()
    {
        this.RefreshDraftIndicators();
        this._settingsRender = null;
        this._pageHeader.Content = null;
        this._pageHeader.IsVisible = false;
        this._bodyScroll.Padding = new Thickness(28, 22);
        this._page.Children.Clear();
        switch (this._kind)
        {
            case "preset":
                this.ShowPreset(); break;
            case "script":
                this.ShowScript(); break;
            case "scripts":
                this.ShowOverview(false); break;
            case "settings":
                this.ShowSettings(); break;
            case "about":
                this.ShowAbout(); break;
            default:
                this.ShowOverview(true); break;
        }

        this.ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshDraftIndicators()
    {
        var navigation = this._store.Read(x => (
            Profiles: x.Presets.ToList(), Scripts: x.Scripts.ToList(), ActiveId: x.ActivePresetId)
        );
        var activeIds = this._displays.DetectActivePresets(navigation.Profiles);
        this._activeProfileIds = activeIds;
        this._nav.Show(
            navigation.Profiles,
            navigation.Scripts,
            activeIds,
            this._kind,
            this._id,
            this._store.IsDraftPreset,
            this._store.IsDraftScript
        );
        var error = this._settingsState?.ErrorText ?? this._saveOperationError;
        this._saveError.Text = error;
        this._saveError.IsVisible = !string.IsNullOrWhiteSpace(error);
        this._saveWarning.Text = this._settingsState?.ReplacesOtherUsersConfiguration == true
            ? SettingsViewState.ReplacesOtherUserWarning
            : null;
        this._saveWarning.IsVisible = !string.IsNullOrWhiteSpace(this._saveWarning.Text);
        var hasChanges = this._store.HasDrafts || this._settingsState?.SaveEnabled == true;
        this._saveButton.IsEnabled = !this._saveInProgress && hasChanges;
        this._draftBar.IsVisible = !this._saveInProgress && (hasChanges || this._saveError.IsVisible);
    }

    private void RefreshActiveProfileMarkers()
    {
        var profiles = this._store.Read(document => document.Presets.ToList());
        IReadOnlySet<Guid> activeIds;
        try
        {
            activeIds = this._displays.DetectActivePresets(profiles);
        }
        catch
        {
            return;
        }

        if (this._activeProfileIds.SetEquals(activeIds))
        {
            return;
        }

        this.RefreshDraftIndicators();
        if (this._kind == "profiles")
        {
            this._page.Children.Clear();
            this.ShowOverview(true);
        }
    }

    private void ShowOverview(bool profiles)
    {
        var overview = new CategoryOverviewControl(profiles ? "Profiles" : "Scripts");
        this._pageHeader.Content = overview.CreateHeader(profiles ? this.AddPreset : this.AddScript);
        this._pageHeader.IsVisible = true;
        this._bodyScroll.Padding = new Thickness(28, 12, 28, 22);
        this._page.Children.Add(overview);
        if (profiles)
        {
            var items = this._store.Read(x => x.Presets.ToList());
            overview.ShowProfiles(
                items,
                this._activeProfileIds,
                id => this.Select("preset", id),
                this.ActivatePreset,
                id => _ = this.DeletePresetAsync(id),
                (source, target) => this.PlaceOverviewItem(true, source, target)
            );
        }
        else
        {
            var items = this._store.Read(x => x.Scripts.ToList());
            overview.ShowScripts(
                items,
                id => this.Select("script", id),
                id => _ = this.RunScriptAsync(id),
                id => _ = this.DeleteScriptAsync(id),
                (source, target) => this.PlaceOverviewItem(false, source, target)
            );
        }
    }

    private void PlaceOverviewItem(bool profiles, Guid sourceId, Guid targetId)
    {
        this._store.Update(document =>
            {
                if (profiles)
                {
                    MoveOverviewItem(document.Presets, item => item.Id, sourceId, targetId);
                }
                else
                {
                    MoveOverviewItem(document.Scripts, item => item.Id, sourceId, targetId);
                }

                return 0;
            }
        );
        this.Refresh();
    }

    private static void MoveOverviewItem<T>(List<T> items, Func<T, Guid> getId, Guid sourceId, Guid targetId)
    {
        var source = items.FindIndex(item => getId(item) == sourceId);
        var target = items.FindIndex(item => getId(item) == targetId);
        if (source < 0 || target < 0 || source == target)
        {
            return;
        }

        var item = items[source];
        items.RemoveAt(source);
        items.Insert(target, item);
    }

    private void AddPreset()
    {
        var id = this._workflow.CreatePreset();
        var positions = this._displays.GetMonitors()
            .OrderBy(m => m.PositionX)
            .ThenBy(m => m.PositionY)
            .Select((monitor, index) => (monitor.Id, index))
            .ToDictionary(x => x.Id, x => x.index);
        this._store.Update(document =>
            {
                foreach (var monitor in document.Presets.First(p => p.Id == id).Monitors)
                {
                    if (positions.TryGetValue(monitor.Id, out var order))
                    {
                        monitor.Order = order;
                    }
                }

                return 0;
            }
        );
        this.Select("preset", id);
    }

    public void ActivatePreset(Guid id)
    {
        _ = this.ActivatePresetAsync(id);
    }

    private async Task ActivatePresetAsync(Guid id)
    {
        if (this._store.IsDraftPreset(id))
        {
            this._notifications.Show("Save this draft profile before activating it.", true);
            return;
        }

        try
        {
            var queued = await this._profileQueue.EnqueueAsync(_ => Task.Run(() => this._workflow.ActivatePreset(id)));
            if (queued.Superseded)
            {
                this._notifications.Show("Profile activation was replaced by a newer request.");
                return;
            }

            var result = queued.Value!;
            this._notifications.Show(result.Message, !result.Success);
            this.Refresh();
        }
        catch (Exception error)
        {
            this._notifications.Show(error.Message, true);
        }
    }

    private void ShowPreset()
    {
        var preset = this._store.Read(x => x.Presets.FirstOrDefault(p => p.Id == this._id));
        if (preset is null)
        {
            this.Select("profiles");
            return;
        }

        this._page.Children.Add(Heading("Profile"));
        var name = new TextBox { Text = preset.Name, Width = 360, PlaceholderText = "Profile name" };
        name.LostFocus += (_, _) =>
        {
            var value = name.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            this._workflow.RenamePreset(this._id, value);
            this.Refresh();
        };
        this._page.Children.Add(Row(Label("Name"), name));
        this._page.Children.Add(
            Row(
                Action("Activate", () => this.ActivatePreset(this._id), true),
                Action("Delete", () => _ = this.DeletePresetAsync(this._id))
            )
        );
        var connected = this._displays.GetEditorMonitors();
        var editorMonitors = DesktopWorkflow.EditorMonitors(preset, connected);
        var savedIds = preset.Monitors.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        this._page.Children.Add(
            new MonitorArrangementControl(
                editorMonitors,
                connected,
                savedIds,
                (monitor, update) => this.UpdateMonitor(monitor, update, savedIds.Contains(monitor.Id)),
                monitor =>
                {
                    this._workflow.SetPrimary(this._id, monitor.Id, savedIds.Contains(monitor.Id) ? null : monitor);
                    this.Refresh();
                },
                (id, target) =>
                {
                    this.PlaceMonitor(id, target);
                    this.Refresh();
                },
                this.Refresh,
                this._selectedMonitorId,
                id => this._selectedMonitorId = id,
                id =>
                {
                    this._workflow.RemoveMonitor(this._id, id);
                    this._selectedMonitorId = null;
                    this.Refresh();
                }
            )
        );
    }

    private void UpdateMonitor(MonitorSetting monitor, Action<MonitorSetting> update, bool saved)
    {
        this._workflow.UpdateMonitor(this._id, monitor.Id, update, saved ? null : monitor);
        if (!saved)
        {
            this.Refresh();
            return;
        }

        this.RefreshDraftIndicators();
        this.ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlaceMonitor(string monitorId, string targetId)
    {
        this._store.Update(document =>
            {
                var monitors = document.Presets.First(p => p.Id == this._id).Monitors.OrderBy(m => m.Order).ToList();
                var source = monitors.FindIndex(m => m.Id == monitorId);
                var target = monitors.FindIndex(m => m.Id == targetId);
                if (source < 0 || target < 0 || source == target)
                {
                    return 0;
                }

                var item = monitors[source];
                monitors.RemoveAt(source);
                monitors.Insert(target, item);
                for (var index = 0; index < monitors.Count; index++)
                {
                    monitors[index].Order = index;
                }

                return 0;
            }
        );
    }

    private async Task DeletePresetAsync(Guid id)
    {
        if (!await this.ConfirmAsync("Delete this profile?"))
        {
            return;
        }

        this._workflow.RemovePreset(id);
        if (this._kind == "preset" && this._id == id)
        {
            this.Select("profiles");
        }
        else
        {
            this.Refresh();
        }
    }

    private void AddScript()
    {
        this.Select("script", this._workflow.CreateScript());
    }

    private void ShowScript()
    {
        var script = this._store.Read(x => x.Scripts.FirstOrDefault(s => s.Id == this._id));
        if (script is null)
        {
            this.Select("scripts");
            return;
        }

        this._page.Children.Add(Heading("Script"));
        var name = new TextBox { Text = script.Name, Width = 360 };
        name.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                return;
            }

            this._workflow.RenameScript(this._id, name.Text.Trim());
            this.Refresh();
        };
        this._page.Children.Add(Row(Label("Name"), name));
        var code = new TextBox
        {
            Text = script.Script,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 300,
            TextWrapping = TextWrapping.NoWrap
        };
        code.LostFocus += (_, _) =>
        {
            this._workflow.UpdateScript(this._id, code.Text ?? "");
            this.RefreshDraftIndicators();
        };
        this._page.Children.Add(code);
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = TextWrapping.Wrap };
        this._page.Children.Add(
            Row(
                Action("Run", () => _ = this.RunScriptAsync(this._id, output), true),
                Action("Delete", () => _ = this.DeleteScriptAsync(this._id))
            )
        );
        this._page.Children.Add(Label("Output"));
        this._page.Children.Add(output);
    }

    private async Task RunScriptAsync(Guid id, TextBox? output = null)
    {
        if (this._store.IsDraftScript(id))
        {
            this._notifications.Show("Save this draft script before running it.", true);
            return;
        }

        if (!await this.ConfirmAsync("Run this script?"))
        {
            return;
        }

        try
        {
            if (output is not null)
            {
                output.Text = "Running…";
            }

            var result = await this._workflow.RunScriptAsync(id);
            var success = result.StartsWith("Exit code 0", StringComparison.Ordinal);
            this._notifications.Show(success ? "Script completed." : "Script failed.", !success);
            if (output is not null)
            {
                output.Text = result;
            }
        }
        catch (Exception error)
        {
            this._notifications.Show(error.Message, true);
            if (output is not null)
            {
                output.Text = error.Message;
            }
        }
    }

    private async Task DeleteScriptAsync(Guid id)
    {
        if (!await this.ConfirmAsync("Delete this script?"))
        {
            return;
        }

        this._workflow.RemoveScript(id);
        if (this._kind == "script" && this._id == id)
        {
            this.Select("scripts");
        }
        else
        {
            this.Refresh();
        }
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new Window
        {
            Title = Constants.ProductName,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxWidth = 520,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto") };
        layout.Children.Add(
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 440,
                Margin = new Thickness(0, 0, 0, 18)
            }
        );
        var buttons = Row(Action("Cancel", () => dialog.Close(false)), Action("Continue", () => dialog.Close(true), true));
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        dialog.Content = layout;
        return await dialog.ShowDialog<bool>(this);
    }

    private void ShowAbout()
    {
        this._page.Children.Add(Heading("About"));
        this._page.Children.Add(Label(Constants.ProductName));
        var version = typeof(MainWindow).Assembly.GetName().Version;
        this._page.Children.Add(new TextBlock { Text = $"Version {version?.ToString(3)}", Foreground = Ink });
        var link = new Button
        {
            Content = Constants.GitHubUrl,
            Foreground = Blue,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        link.Click += async (_, _) =>
        {
            try
            {
                if (this.Launcher is { } launcher)
                {
                    await launcher.LaunchUriAsync(new Uri(Constants.GitHubUrl));
                }
            }
            catch (Exception error)
            {
                this._notifications.Show(error.Message, true);
            }
        };
        this._page.Children.Add(link);
    }

    private void ShowSettings()
    {
        var settings = this._store.Read(x => x.Settings);
        var serviceStatus = ServiceControl.Status();
        var serviceInstalled = serviceStatus != "Not installed";
        var serviceOwned = serviceInstalled && ServiceControl.UsesDataDirectory(this._store.DirectoryPath);
        var currentTrayPath = Path.Combine(AppContext.BaseDirectory, "M2Server.Tray.exe");
        var state = this._settingsState ??= new SettingsViewState(
            settings,
            this._security.HasPassword,
            serviceInstalled,
            serviceOwned,
            currentTrayPath
        );
        state.SetServiceState(serviceInstalled, serviceOwned);

        string? startupWarning;
        try
        {
            var scheduledPath = StartupService.ScheduledExecutablePath(out var elevated);
            var mismatch = elevated || scheduledPath is null || !string.Equals(
                Path.GetFullPath(scheduledPath.Trim('"')),
                Path.GetFullPath(currentTrayPath),
                StringComparison.OrdinalIgnoreCase
            );
            state.SetStartupTaskMismatch(mismatch);
            startupWarning = mismatch
                ? scheduledPath is null ? "The startup task is missing. Save to create it." :
                elevated ? "The startup task uses elevation. Save to switch it to normal user privileges." :
                "The startup task points to a different executable. Save to update it."
                : null;
        }
        catch (Exception error)
        {
            startupWarning = "Could not inspect the startup task: " + error.Message;
        }

        var control = new SettingsControl(
            state,
            this._security.HasPassword,
            serviceStatus,
            this._certificates.LanIp,
            startupWarning,
            () =>
            {
                this._saveOperationError = null;
                this.RefreshDraftIndicators();
            }
        );
        this._page.Children.Add(control);
        this._settingsRender = control.Render;
    }
}