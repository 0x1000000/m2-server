using System.Runtime.Versioning;
using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;
using NUnit.Framework;

namespace M2Server.Lib.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class DesktopWorkflowMonitorTests
{
    [Test]
    public void PreviewDoesNotCreateDraftUntilNewMonitorIsChanged()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "workflow-tests", Guid.NewGuid().ToString("N"));
        var store = new DataStore(root, false);
        var preset = new Preset { Name = "Existing" };
        preset.Monitors.Add(new MonitorSetting { Id = "missing", Name = "Missing", Enabled = false });
        store.Update(document =>
            {
                document.Presets.Add(preset);
                return 0;
            }
        );
        store.Save();

        var live = new MonitorInfo(
            "new",
            "New",
            "DISPLAY1",
            true,
            false,
            new DisplayMode(1920, 1080, 60),
            [new DisplayMode(1920, 1080, 60)],
            0,
            0,
            [],
            100,
            false,
            null,
            DisplayRotation.Identity
        );
        var preview = DesktopWorkflow.EditorMonitors(store.Read(document => document.Presets.Single()), [live]);
        Assert.Multiple(() =>
            {
                Assert.That(preview.Select(monitor => monitor.Id), Is.EquivalentTo(new[] { "missing", "new" }));
                Assert.That(preview.Single(monitor => monitor.Id == "new").Enabled, Is.False);
                Assert.That(store.HasDrafts, Is.False);
            }
        );

        var workflow = new DesktopWorkflow(store, new DisplayService());
        var newMonitor = preview.Single(monitor => monitor.Id == "new");
        workflow.UpdateMonitor(preset.Id, newMonitor.Id, monitor => monitor.Enabled = true, newMonitor);
        Assert.That(store.HasDrafts, Is.True);
        Assert.That(
            store.Read(document => document.Presets.Single().Monitors.Single(monitor => monitor.Id == "new").Enabled),
            Is.True
        );

        workflow.RemoveMonitor(preset.Id, "missing");
        Assert.That(
            store.Read(document => document.Presets.Single().Monitors.Select(monitor => monitor.Id)),
            Is.EquivalentTo(new[] { "new" })
        );
    }
}