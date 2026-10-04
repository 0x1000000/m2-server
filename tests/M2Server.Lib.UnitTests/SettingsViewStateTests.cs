using System.Text.Json;
using M2Server.App;
using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;
using NUnit.Framework;

namespace M2Server.Lib.UnitTests;

public sealed class SettingsViewStateTests
{
    [Test]
    public void DisablingWebIgnoresLanAndCertificateRequirements()
    {
        var state = Create(
            new AppSettings { WebEnabled = true, AllowLanAccess = true, AllowPublicLanAccess = true, HttpsPort = 52443 },
            true
        );
        state.SetWebEnabled(false);
        state.SetPort("invalid");

        Assert.That(state.TrySave(), Is.True);
        Assert.That(state.ErrorText, Is.Null);
        Assert.That(state.Port.Enabled, Is.False);
        Assert.That(state.Port.Value, Is.EqualTo("52443"));
        Assert.That(state.Password.Enabled, Is.False);
        Assert.That(state.PrivateLan.Enabled, Is.False);
        Assert.That(state.PublicLan.Enabled, Is.False);
        Assert.That(state.PreviewUrl("192.168.1.10"), Is.EqualTo("Web app disabled"));

        var saved = new AppSettings { HttpsPort = 52443 };
        state.ApplyTo(saved);
        Assert.Multiple(() =>
            {
                Assert.That(saved.WebEnabled, Is.False);
                Assert.That(saved.HttpsPort, Is.EqualTo(52443));
                Assert.That(saved.AllowLanAccess, Is.True);
                Assert.That(saved.AllowPublicLanAccess, Is.True);
                Assert.That(SettingsViewState.ValidatePersistedSettings(saved), Is.Null);
            }
        );
    }

    [Test]
    public void HttpIsAllowedForPrivateAndPublicNetworkAccess()
    {
        var state = Create(new AppSettings { WebEnabled = true, HttpsPort = 52443 }, true);
        state.SetPrivateLan(true);
        state.SetPublicLan(true);

        Assert.That(state.TrySave(), Is.True);
        Assert.That(state.PreviewUrl("192.168.1.10"), Is.EqualTo("http://192.168.1.10:52443"));
        var saved = new AppSettings { PasswordHash = "hash" };
        state.ApplyTo(saved);
        Assert.That(saved.CertificateThumbprint, Is.Null);
        Assert.That(SettingsViewState.ValidatePersistedSettings(saved), Is.Null);
    }

    [Test]
    public void PasswordIsRequiredOnlyWhenWebIsEnabled()
    {
        var state = Create(new AppSettings { WebEnabled = false }, false);
        Assert.That(state.TrySave(), Is.True);
        state.SetWebEnabled(true);
        Assert.That(state.TrySave(), Is.False);
        Assert.That(state.ErrorText, Is.EqualTo(SettingsViewState.PasswordRequiredError));
        state.SetPassword("short");
        Assert.That(state.TrySave(), Is.False);
        Assert.That(state.ErrorText, Is.EqualTo(SettingsViewState.PasswordTooShortError));
        state.SetPassword("long-enough");
        Assert.That(state.TrySave(), Is.True);
        Assert.That(state.ErrorText, Is.Null);
    }

    [Test]
    public void RequiredMarkersAndFieldErrorsFollowWebStateAndVisits()
    {
        var state = Create(new AppSettings(), false);
        Assert.That(state.PasswordRequired, Is.False);
        Assert.That(state.PortRequired, Is.False);

        state.SetWebEnabled(true);
        Assert.That(state.PasswordRequired, Is.True);
        Assert.That(state.PortRequired, Is.True);
        Assert.That(state.PasswordHasError, Is.False);
        state.SetPassword("");
        Assert.That(state.PasswordHasError, Is.True);
        Assert.That(state.ErrorText, Is.EqualTo(SettingsViewState.PasswordRequiredError));
        state.SetPort("");
        Assert.That(state.PortHasError, Is.True);
        state.SetPort("52443");
        state.SetPassword("long-enough");
        Assert.That(state.PortHasError, Is.False);
        Assert.That(state.PasswordHasError, Is.False);

        state.SetWebEnabled(false);
        Assert.That(state.PasswordRequired, Is.False);
        Assert.That(state.PortRequired, Is.False);
    }

    [Test]
    public void DisablingWebClearsAnIncompletePassword()
    {
        var state = Create(new AppSettings { WebEnabled = true }, true);
        state.SetPassword("short");
        Assert.That(state.ErrorText, Is.EqualTo(SettingsViewState.PasswordTooShortError));
        state.SetWebEnabled(false);
        Assert.That(state.Password.Value, Is.Empty);
        Assert.That(state.Password.Enabled, Is.False);
        Assert.That(state.TrySave(), Is.True);
    }

    [TestCase("0", false)]
    [TestCase("65536", false)]
    [TestCase("bad", false)]
    [TestCase("1", true)]
    [TestCase("65535", true)]
    public void PortValidationFollowsEnabledWebState(string input, bool valid)
    {
        var state = Create(new AppSettings { WebEnabled = true }, true);
        state.SetPort(input);
        Assert.That(state.Port.Visited, Is.True);
        Assert.That(state.TrySave(), Is.EqualTo(valid));
        Assert.That(state.ErrorText, Is.EqualTo(valid ? null : SettingsViewState.InvalidPortError));
    }

    [Test]
    public void CertificateSelectionChangesPreviewToHttps()
    {
        var state = Create(new AppSettings { WebEnabled = true, AllowLanAccess = true, HttpsPort = 52443 }, true);
        state.SetCertificateThumbprint("AB CD:12");
        var saved = new AppSettings();
        state.ApplyTo(saved);

        Assert.That(saved.CertificateThumbprint, Is.EqualTo("ABCD12"));
        Assert.That(state.PreviewUrl("192.168.1.10"), Is.EqualTo("https://192.168.1.10:52443"));
    }

    [Test]
    public void StartupFieldsFollowStartupSwitchAndSavingState()
    {
        var state = Create(new AppSettings { AutoStartup = false }, false);
        Assert.That(state.StartupPath.Enabled, Is.False);
        state.SetAutoStartup(true);
        Assert.That(state.AutoStartup.Enabled, Is.True);
        Assert.That(state.StartupPath.Enabled, Is.False);
        state.SetSaving(true);
        Assert.That(state.AutoStartup.Enabled, Is.False);
        Assert.That(state.WebEnabled.Enabled, Is.False);
        Assert.That(state.SaveEnabled, Is.False);
        state.SetWebEnabled(true);
        Assert.That(state.WebEnabled.Value, Is.False);
        state.SetSaving(false);
        Assert.That(state.SaveEnabled, Is.True);
    }

    [Test]
    public void DirtyVisitedAndErrorStatesReactToInput()
    {
        var state = Create(new AppSettings { WebEnabled = true, HttpsPort = 52443 }, true);
        state.SetServiceState(true, true);
        Assert.That(state.IsDirty, Is.False);
        Assert.That(state.Port.Visited, Is.False);
        state.SetPort("not a port");
        Assert.That(state.IsDirty, Is.True);
        Assert.That(state.Port.Visited, Is.True);
        Assert.That(state.ErrorText, Is.EqualTo(SettingsViewState.InvalidPortError));
        state.SetPort("52443");
        Assert.That(state.IsDirty, Is.False);
        Assert.That(state.ErrorText, Is.Null);
        state.SetOperationError("service failed");
        Assert.That(state.ErrorText, Is.EqualTo("service failed"));
        state.SetPrivateLan(true);
        Assert.That(state.ErrorText, Is.Null);
    }

    [Test]
    public void SaveActivatesThisUsersWebConfigurationAndWarnsBeforeReplacingAnotherUsers()
    {
        var state = Create(new AppSettings { WebEnabled = true, HttpsPort = 52443 }, true);
        Assert.That(state.NeedsWebServiceActivation, Is.True);
        Assert.That(state.SaveEnabled, Is.True);
        Assert.That(state.ReplacesOtherUsersConfiguration, Is.False);
        state.SetServiceState(true, true);
        Assert.That(state.NeedsWebServiceActivation, Is.False);
        Assert.That(state.SaveEnabled, Is.False);
        state.SetServiceState(true, false);
        Assert.That(state.NeedsWebServiceActivation, Is.True);
        Assert.That(state.ReplacesOtherUsersConfiguration, Is.True);
        Assert.That(state.SaveEnabled, Is.True);
        state.SetWebEnabled(false);
        Assert.That(state.NeedsWebServiceActivation, Is.False);
        Assert.That(state.ReplacesOtherUsersConfiguration, Is.False);
    }

    [Test]
    public void SaveCanDeactivateAnOwnedServiceWithoutOtherEdits()
    {
        var state = new SettingsViewState(new AppSettings { WebEnabled = false, HttpsPort = 52443 }, true, true, true);
        Assert.That(state.NeedsWebServiceDeactivation, Is.True);
        Assert.That(state.SaveEnabled, Is.True);
    }

    [Test]
    public void PersistedValidationAllowsDisabledInvalidWebFields()
    {
        var settings = new AppSettings
        {
            WebEnabled = false, HttpsPort = 0, AllowPublicLanAccess = true, CertificateThumbprint = null
        };
        Assert.That(SettingsViewState.ValidatePersistedSettings(settings), Is.Null);
        settings.WebEnabled = true;
        Assert.That(SettingsViewState.ValidatePersistedSettings(settings), Is.EqualTo(SettingsViewState.InvalidPortError));
        settings.HttpsPort = 52443;
        Assert.That(SettingsViewState.ValidatePersistedSettings(settings), Is.EqualTo(SettingsViewState.PasswordRequiredError));
        settings.PasswordHash = "hash";
        Assert.That(SettingsViewState.ValidatePersistedSettings(settings), Is.Null);
    }

    [Test]
    public void MissingPortGetsAvailableSafePort()
    {
        var state = new SettingsViewState(new AppSettings { WebEnabled = true }, true, true, true);
        var port = int.Parse(state.Port.Value);
        Assert.That(port, Is.InRange(49152, 65535));
        Assert.That(state.IsDirty, Is.False);
        state.SetPort(port == 52443 ? "52444" : "52443");
        Assert.That(state.IsDirty, Is.True);
        state.SetPort(port.ToString());
        Assert.That(state.IsDirty, Is.False);
        var saved = new AppSettings();
        state.ApplyTo(saved);
        Assert.That(saved.HttpsPort, Is.EqualTo(port));
    }

    [Test]
    public void EmptySettingsDoNotCreateDraftOnOpen()
    {
        var state = Create(new AppSettings(), false);

        Assert.That(state.IsDirty, Is.False);
        Assert.That(state.SaveEnabled, Is.False);
    }

    [Test]
    public void StartupTaskMismatchCanBeSavedWithoutEditingExecutable()
    {
        var state = Create(new AppSettings { AutoStartup = true, StartupPath = "tray.exe", HttpsPort = 52443 }, false);
        Assert.That(state.IsDirty, Is.False);
        state.SetStartupTaskMismatch(true);
        Assert.That(state.NeedsStartupRepair, Is.True);
        Assert.That(state.SaveEnabled, Is.True);
        Assert.That(state.StartupPath.Enabled, Is.False);
        state.SetStartupPath("other.exe");
        Assert.That(state.StartupPath.Value, Is.EqualTo("tray.exe"));
        state.SetAutoStartup(false);
        Assert.That(state.NeedsStartupRepair, Is.False);
    }

    [Test]
    public void DisplayOnlyExecutableDoesNotCreateDraftWhenStartupIsOff()
    {
        var settings = new AppSettings { HttpsPort = 52443, StartupPath = "old.exe" };
        var state = new SettingsViewState(settings, false, false, false, "current.exe");
        Assert.That(state.IsDirty, Is.False);
        state.ApplyTo(settings);
        Assert.That(settings.StartupPath, Is.EqualTo("old.exe"));
        state.SetAutoStartup(true);
        state.ApplyTo(settings);
        Assert.That(settings.StartupPath, Is.EqualTo("current.exe"));
    }

    [Test]
    public void LegacyElevatedStartupSettingIsIgnoredAndRemovedWhenSaved()
    {
        var document = JsonSerializer.Deserialize(
            "{\"settings\":{\"autoStartup\":true,\"startElevated\":true}}",
            AppDocumentJsonContext.Default.AppDocument
        )!;
        Assert.That(document.Settings.AutoStartup, Is.True);
        var saved = JsonSerializer.Serialize(document, AppDocumentJsonContext.Default.AppDocument);
        Assert.That(saved, Does.Not.Contain("startElevated"));
    }

    private static SettingsViewState Create(AppSettings settings, bool hasPassword)
    {
        return new SettingsViewState(settings, hasPassword, false, false);
    }
}