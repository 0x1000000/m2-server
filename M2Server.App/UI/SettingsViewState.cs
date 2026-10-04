using System.Globalization;
using M2Server.Lib.Domain;
using M2Server.Lib.Services;

namespace M2Server.App;

public sealed class SettingsField<T>(T value)
{
    public T Value { get; private set; } = value;

    public bool Enabled { get; internal set; } = true;

    public bool Visited { get; private set; }

    internal void Set(T value)
    {
        this.Value = value;
        this.Visited = true;
    }

    public void MarkVisited()
    {
        this.Visited = true;
    }
}

public sealed class SettingsViewState
{
    public const string InvalidPortError = "Enter a port from 1 to 65535.";
    public const string PasswordRequiredError = "Set a password before enabling web access.";
    public const string PasswordTooShortError = "Use at least 8 characters for the web password.";
    public const string ReplacesOtherUserWarning = "Saving will replace another user's web service configuration.";
    private readonly bool _hasPassword;

    private readonly AppSettings _initial;
    private readonly string _initialPort;
    private bool _startupTaskMismatch;

    public SettingsViewState(
        AppSettings settings,
        bool hasPassword,
        bool serviceInstalled,
        bool serviceOwned,
        string? startupPathDisplay = null)
    {
        this._initial = new AppSettings
        {
            WebEnabled = settings.WebEnabled,
            AllowLanAccess = settings.AllowLanAccess,
            AllowPublicLanAccess = settings.AllowPublicLanAccess,
            AutoStartup = settings.AutoStartup,
            StartupPath = settings.StartupPath,
            HttpsPort = settings.HttpsPort,
            CertificateThumbprint = settings.CertificateThumbprint
        };
        this._hasPassword = hasPassword;
        this.WebEnabled = new SettingsField<bool>(settings.WebEnabled ?? hasPassword);
        this.Port = new SettingsField<string>(
            (settings.HttpsPort is >= 1 and <= 65535 ? settings.HttpsPort : AvailablePortService.Choose()).ToString(
                CultureInfo.InvariantCulture
            )
        );
        this._initialPort = this.Port.Value;
        this.CertificateThumbprint = new SettingsField<string>(settings.CertificateThumbprint ?? "");
        this.PrivateLan = new SettingsField<bool>(settings.AllowLanAccess);
        this.PublicLan = new SettingsField<bool>(settings.AllowPublicLanAccess);
        this.AutoStartup = new SettingsField<bool>(settings.AutoStartup);
        this.StartupPath = new SettingsField<string>(startupPathDisplay ?? settings.StartupPath);
        this.SetServiceState(serviceInstalled, serviceOwned);
        this.UpdateControlStates();
    }

    public SettingsField<bool> WebEnabled { get; }

    public SettingsField<string> Password { get; } = new("");

    public SettingsField<string> Port { get; }

    public SettingsField<string> CertificateThumbprint { get; }

    public SettingsField<bool> PrivateLan { get; }

    public SettingsField<bool> PublicLan { get; }

    public SettingsField<bool> AutoStartup { get; }

    public SettingsField<string> StartupPath { get; }

    public bool ServiceInstalled { get; private set; }

    public bool ServiceOwned { get; private set; }

    public bool SaveAttempted { get; private set; }

    public bool IsSaving { get; private set; }

    public string? OperationError { get; private set; }

    public bool IsDirty => this.NeedsWebServiceActivation || this.NeedsWebServiceDeactivation || this.NeedsStartupRepair ||
                           this.WebEnabled.Value != (this._initial.WebEnabled ?? this._hasPassword) ||
                           this.Password.Value.Length > 0 || this.Port.Value != this._initialPort ||
                           this.CertificateThumbprint.Value != (this._initial.CertificateThumbprint ?? "") ||
                           this.PrivateLan.Value != this._initial.AllowLanAccess ||
                           this.PublicLan.Value != this._initial.AllowPublicLanAccess ||
                           this.AutoStartup.Value != this._initial.AutoStartup ||
                           (this.AutoStartup.Value && this.StartupPath.Value != this._initial.StartupPath);

    public bool SaveEnabled => this.IsDirty && !this.IsSaving;

    public bool PasswordRequired => this.WebEnabled.Value && !this._hasPassword;

    public bool PortRequired => this.WebEnabled.Value;

    public bool PasswordHasError => this.WebEnabled.Value && (this.Password.Visited || this.SaveAttempted) &&
                                    (this.Password.Value.Length is > 0 and < 8 ||
                                     (this.PasswordRequired && this.Password.Value.Length == 0));

    public bool PortHasError => this.PortRequired && (this.Port.Visited || this.SaveAttempted) && !this.TryGetPort(out _);

    public bool NeedsStartupRepair => this.AutoStartup.Value && this._startupTaskMismatch;

    public bool NeedsWebServiceActivation => this.WebEnabled.Value && !this.ServiceOwned;

    public bool NeedsWebServiceDeactivation => !this.WebEnabled.Value && this.ServiceOwned;

    public bool ReplacesOtherUsersConfiguration => this.WebEnabled.Value && this.ServiceInstalled && !this.ServiceOwned;

    public string? ErrorText
    {
        get
        {
            if (this.WebEnabled.Value && this.Password.Value.Length is > 0 and < 8 &&
                (this.SaveAttempted || this.Password.Visited))
            {
                return PasswordTooShortError;
            }

            if (this.WebEnabled.Value)
            {
                if (this.PortHasError)
                {
                    return InvalidPortError;
                }

                if (this.PasswordHasError)
                {
                    return PasswordRequiredError;
                }
            }

            return this.OperationError;
        }
    }

    public void SetWebEnabled(bool value)
    {
        if (!this.WebEnabled.Enabled)
        {
            return;
        }

        this.WebEnabled.Set(value);
        if (!value)
        {
            this.Password.Set("");
        }

        this.InputChanged();
        this.UpdateControlStates();
    }

    public void SetPassword(string value)
    {
        if (!this.Password.Enabled)
        {
            return;
        }

        this.Password.Set(value);
        this.InputChanged();
    }

    public void SetPort(string value)
    {
        if (!this.Port.Enabled)
        {
            return;
        }

        this.Port.Set(value);
        this.InputChanged();
    }

    public void SetCertificateThumbprint(string value)
    {
        if (!this.CertificateThumbprint.Enabled)
        {
            return;
        }

        this.CertificateThumbprint.Set(value);
        this.InputChanged();
    }

    public void SetPrivateLan(bool value)
    {
        if (!this.PrivateLan.Enabled)
        {
            return;
        }

        this.PrivateLan.Set(value);
        this.InputChanged();
    }

    public void SetPublicLan(bool value)
    {
        if (!this.PublicLan.Enabled)
        {
            return;
        }

        this.PublicLan.Set(value);
        this.InputChanged();
    }

    public void SetAutoStartup(bool value)
    {
        if (!this.AutoStartup.Enabled)
        {
            return;
        }

        this.AutoStartup.Set(value);
        this.InputChanged();
        this.UpdateControlStates();
    }

    public void SetStartupPath(string value)
    {
        if (!this.StartupPath.Enabled)
        {
            return;
        }

        this.StartupPath.Set(value);
        this.InputChanged();
    }

    public void SetServiceState(bool installed, bool owned)
    {
        this.ServiceInstalled = installed;
        this.ServiceOwned = installed && owned;
    }

    public void SetStartupTaskMismatch(bool mismatch)
    {
        this._startupTaskMismatch = mismatch;
    }

    public void SetSaving(bool saving)
    {
        this.IsSaving = saving;
        this.UpdateControlStates();
    }

    public void SetOperationError(string? error)
    {
        this.OperationError = error;
    }

    public bool TrySave()
    {
        this.SaveAttempted = true;
        this.OperationError = null;
        return this.ErrorText is null;
    }

    public string PreviewUrl(string? lanIp)
    {
        if (!this.WebEnabled.Value)
        {
            return "Web app disabled";
        }

        var scheme = string.IsNullOrWhiteSpace(this.CertificateThumbprint.Value) ? "http" : "https";
        var host = (this.PrivateLan.Value || this.PublicLan.Value) && !string.IsNullOrWhiteSpace(lanIp) ? lanIp : "localhost";
        var port = this.TryGetPort(out var parsedPort) ? parsedPort : this._initial.HttpsPort;
        return $"{scheme}://{host}:{port}";
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.WebEnabled = this.WebEnabled.Value;
        settings.AllowLanAccess = this.PrivateLan.Value;
        settings.AllowPublicLanAccess = this.PublicLan.Value;
        settings.AutoStartup = this.AutoStartup.Value;
        if (this.AutoStartup.Value)
        {
            settings.StartupPath = this.StartupPath.Value.Trim();
        }

        if (this.TryGetPort(out var port))
        {
            settings.HttpsPort = port;
        }

        settings.CertificateThumbprint = string.IsNullOrWhiteSpace(this.CertificateThumbprint.Value)
            ? null
            : new string(this.CertificateThumbprint.Value.Where(c => !char.IsWhiteSpace(c) && c != ':').ToArray());
    }

    public static string? ValidatePersistedSettings(AppSettings settings)
    {
        if (settings.WebEnabled != true)
        {
            return null;
        }

        if (settings.HttpsPort is < 1 or > 65535)
        {
            return InvalidPortError;
        }

        if (string.IsNullOrEmpty(settings.PasswordHash))
        {
            return PasswordRequiredError;
        }

        return null;
    }

    private bool TryGetPort(out int port)
    {
        return int.TryParse(this.Port.Value, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
               port is >= 1 and <= 65535;
    }

    private void InputChanged()
    {
        this.OperationError = null;
    }

    private void UpdateControlStates()
    {
        this.WebEnabled.Enabled = !this.IsSaving;
        this.Password.Enabled = this.WebEnabled.Value && !this.IsSaving;
        this.Port.Enabled = this.WebEnabled.Value && !this.IsSaving;
        this.CertificateThumbprint.Enabled = this.WebEnabled.Value && !this.IsSaving;
        this.PrivateLan.Enabled = this.WebEnabled.Value && !this.IsSaving;
        this.PublicLan.Enabled = this.WebEnabled.Value && !this.IsSaving;
        this.AutoStartup.Enabled = !this.IsSaving;
        this.StartupPath.Enabled = false;
    }
}