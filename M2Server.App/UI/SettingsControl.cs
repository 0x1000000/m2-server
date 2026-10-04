using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace M2Server.App;

public sealed class SettingsControl : StackPanel
{
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#2563A6"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#B42318"));
    private readonly TextBox _certificate;
    private readonly Action _changed;

    private readonly string? _lanIp;
    private readonly TextBox _password;
    private readonly TextBlock _passwordRequired = RequiredMarker();
    private readonly TextBox _port;
    private readonly TextBlock _portRequired = RequiredMarker();
    private readonly CheckBox _privateLan;
    private readonly CheckBox _publicLan;
    private readonly CheckBox _startup;
    private readonly TextBox _startupPath;
    private readonly TextBlock _startupWarning;
    private readonly SettingsViewState _state;

    private readonly TextBox _url = new()
    {
        Foreground = Blue,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(0)
    };

    private readonly CheckBox _web;

    public SettingsControl(
        SettingsViewState state,
        bool hasPassword,
        string serviceStatus,
        string? lanIp,
        string? startupWarning,
        Action changed)
    {
        this._state = state;
        this._lanIp = lanIp;
        this._changed = changed;
        this.Spacing = 12;
        this._web = new CheckBox { Content = "Enable web app", IsChecked = state.WebEnabled.Value };
        this._password = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = hasPassword ? "Leave blank to keep password" : "Set a password",
            Text = state.Password.Value,
            Width = 260
        };
        this._port = new TextBox { Text = state.Port.Value, Width = 90 };
        this._certificate = new TextBox
        {
            Text = state.CertificateThumbprint.Value, Width = 400, PlaceholderText = "Leave blank for HTTP"
        };
        this._privateLan = new CheckBox { Content = "Allow private LAN", IsChecked = state.PrivateLan.Value };
        this._publicLan = new CheckBox { Content = "Allow public network", IsChecked = state.PublicLan.Value };
        this._startup = new CheckBox { Content = "Start at sign-in", IsChecked = state.AutoStartup.Value };
        this._startupPath = new TextBox { Text = state.StartupPath.Value, MinWidth = 400, IsReadOnly = true, IsEnabled = false };

        this.Children.Add(Title("Settings"));
        this.Children.Add(Title("Startup"));
        this.Children.Add(this._startup);
        this.Children.Add(Row("Executable", this._startupPath));
        this._startupWarning = new TextBlock { Text = startupWarning, Foreground = Warning, TextWrapping = TextWrapping.Wrap };
        this.Children.Add(this._startupWarning);
        this.Children.Add(Title("Web server"));
        this.Children.Add(this._web);
        this.Children.Add(Row("Password", this._password, this._passwordRequired));
        this.Children.Add(Row("Port", this._port, this._portRequired));
        this.Children.Add(Row("HTTPS certificate thumbprint", this._certificate));
        this.Children.Add(
            new TextBlock
            {
                Text
                    = "Install your certificate with its private key in Local Machine / Personal. The service must be able to use its private key. HTTPS is used when a thumbprint is set; otherwise HTTP is used, including for LAN access.",
                TextWrapping = TextWrapping.Wrap
            }
        );
        this.Children.Add(this._privateLan);
        this.Children.Add(this._publicLan);
        this.Children.Add(this._url);
        this.Children.Add(
            new TextBlock
            {
                Text = "Windows service: " + serviceStatus + (state.ServiceInstalled && !state.ServiceOwned
                    ? " (another user's configuration)"
                    : "")
            }
        );

        this._web.Click += (_, _) => this.Change(view => view.SetWebEnabled(this._web.IsChecked == true));
        this._password.TextChanged += (_, _) => this.Change(view => view.SetPassword(this._password.Text ?? ""));
        this._port.TextChanged += (_, _) => this.Change(view => view.SetPort(this._port.Text ?? ""));
        this._certificate.TextChanged
            += (_, _) => this.Change(view => view.SetCertificateThumbprint(this._certificate.Text ?? ""));
        this._privateLan.Click += (_, _) => this.Change(view => view.SetPrivateLan(this._privateLan.IsChecked == true));
        this._publicLan.Click += (_, _) => this.Change(view => view.SetPublicLan(this._publicLan.IsChecked == true));
        this._startup.Click += (_, _) => this.Change(view => view.SetAutoStartup(this._startup.IsChecked == true));
        this.Render();
    }

    public void Render()
    {
        this._web.IsChecked = this._state.WebEnabled.Value;
        this._privateLan.IsChecked = this._state.PrivateLan.Value;
        this._publicLan.IsChecked = this._state.PublicLan.Value;
        this._startup.IsChecked = this._state.AutoStartup.Value;
        this._web.IsEnabled = this._state.WebEnabled.Enabled;
        this._password.IsEnabled = this._state.Password.Enabled;
        this._passwordRequired.IsVisible = this._state.PasswordRequired;
        this._portRequired.IsVisible = this._state.PortRequired;
        SetErrorBorder(this._password, this._state.PasswordHasError);
        SetErrorBorder(this._port, this._state.PortHasError);
        if (this._password.Text != this._state.Password.Value)
        {
            this._password.Text = this._state.Password.Value;
        }

        this._port.IsEnabled = this._state.Port.Enabled;
        this._certificate.IsEnabled = this._state.CertificateThumbprint.Enabled;
        this._privateLan.IsEnabled = this._state.PrivateLan.Enabled;
        this._publicLan.IsEnabled = this._state.PublicLan.Enabled;
        this._startup.IsEnabled = this._state.AutoStartup.Enabled;
        this._startupWarning.IsVisible = this._state.AutoStartup.Value && !string.IsNullOrWhiteSpace(this._startupWarning.Text);
        this._url.Text = this._state.PreviewUrl(this._lanIp);
    }

    private void Change(Action<SettingsViewState> update)
    {
        update(this._state);
        this._changed();
        this.Render();
    }

    private static TextBlock Title(string title)
    {
        return new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold };
    }

    private static TextBlock RequiredMarker()
    {
        return new TextBlock { Text = "*", Foreground = Warning, IsVisible = false };
    }

    private static void SetErrorBorder(TextBox field, bool hasError)
    {
        if (hasError)
        {
            field.BorderBrush = Warning;
            field.BorderThickness = new Thickness(2);
        }
        else
        {
            field.ClearValue(TextBox.BorderBrushProperty);
            field.ClearValue(TextBox.BorderThicknessProperty);
        }
    }

    private static StackPanel Row(string label, Control control, TextBlock? required = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var caption = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center
        };
        caption.Children.Add(new TextBlock { Text = label });
        if (required is not null)
        {
            caption.Children.Add(required);
        }

        row.Children.Add(caption);
        row.Children.Add(control);
        return row;
    }
}