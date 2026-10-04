using System.Diagnostics;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;
using NUnit.Framework;

namespace M2Server.Lib.UnitTests;

[TestFixture]
public sealed class InstallerCleanupTests
{
    [SetUp]
    public void SetUp()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Version.props")))
        {
            root = root.Parent;
        }

        this._workspace = Path.Combine(root!.FullName, ".tmp", "tests", "installer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._workspace);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(this._workspace, true);
    }

    private string _workspace = null!;

    [Test]
    public void PrepareDoesNotReadConfigurationOrDeleteData()
    {
        var system = new FakeSystem(this._workspace);
        Assert.That(InstallerCleanup.Execute(Constants.InstallerPrepareArgument, this._workspace, system, _ => { }), Is.True);
        Assert.That(system.Operations, Is.EqualTo(new[] { "service", "task", "processes", "firewall" }));
    }

    [Test]
    public void PurgeCleansEveryDistinctProfileAndProgramData()
    {
        var system = new FakeSystem(this._workspace);
        Assert.That(InstallerCleanup.Execute(Constants.InstallerPurgeArgument, this._workspace, system, _ => { }), Is.True);
        Assert.That(system.Operations, Is.EqualTo(new[] { "profile-a", "profile-b", "program-data" }));
    }

    [Test]
    public void FailuresAreReportedAndOtherCleanupContinues()
    {
        var system = new FakeSystem(this._workspace) { FailService = true };
        var messages = new List<string>();
        Assert.That(
            InstallerCleanup.Execute(Constants.InstallerPrepareArgument, this._workspace, system, messages.Add),
            Is.False
        );
        Assert.That(system.Operations, Does.Contain("firewall"));
        Assert.That(messages.Any(message => message.StartsWith("FAILED:")), Is.True);
    }

    [Test]
    public void MissingApplicationDirectoryIsSuccessful()
    {
        Assert.DoesNotThrow(() => ApplicationDataCleanup.Delete(this._workspace, true));
    }

    [Test]
    public void DeletesOnlyApplicationOwnedData()
    {
        var application = ApplicationDataCleanup.Target(this._workspace, true);
        Directory.CreateDirectory(Path.Combine(application, "logs"));
        File.WriteAllText(Path.Combine(application, "data.json"), "invalid configuration");
        File.WriteAllText(Path.Combine(application, "logs", "test.log"), "log");
        var unrelated = Path.Combine(this._workspace, Constants.LocalDataRelativePath, "Other App");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");
        ApplicationDataCleanup.Delete(this._workspace, true);
        Assert.That(Directory.Exists(application), Is.False);
        Assert.That(File.Exists(Path.Combine(unrelated, "keep.txt")), Is.True);
    }

    [Test]
    public void RejectsRelativeAndVolumeRootTargets()
    {
        Assert.Throws<ArgumentException>(() => ApplicationDataCleanup.Target("relative", true));
        Assert.Throws<ArgumentException>(() => ApplicationDataCleanup.Target(Path.GetPathRoot(this._workspace)!, false));
        Assert.Throws<ArgumentException>(() => InstallerCleanup.Execute(
                Constants.InstallerPurgeArgument,
                "relative",
                new FakeSystem(this._workspace),
                _ => { }
            )
        );
    }

    [Test]
    public void RefusesApplicationDirectoryJunction()
    {
        var outside = Path.Combine(this._workspace, "unrelated");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        var application = ApplicationDataCleanup.Target(this._workspace, true);
        Directory.CreateDirectory(Path.GetDirectoryName(application)!);
        CreateJunction(application, outside);
        try
        {
            Assert.Throws<IOException>(() => ApplicationDataCleanup.Delete(this._workspace, true));
            Assert.That(File.Exists(Path.Combine(outside, "keep.txt")), Is.True);
        }
        finally
        {
            Directory.Delete(application);
        }
    }

    [Test]
    public void UnlinksNestedJunctionWithoutDeletingItsDestination()
    {
        var outside = Path.Combine(this._workspace, "unrelated");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        var application = ApplicationDataCleanup.Target(this._workspace, true);
        Directory.CreateDirectory(application);
        CreateJunction(Path.Combine(application, "linked"), outside);
        ApplicationDataCleanup.Delete(this._workspace, true);
        Assert.That(Directory.Exists(application), Is.False);
        Assert.That(File.Exists(Path.Combine(outside, "keep.txt")), Is.True);
    }

    private static void CreateJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows junction coverage.");
        }

        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.Arguments = $"/c mklink /J \"{link}\" \"{target}\"";
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.Zero, "Junction creation failed.");
    }

    private sealed class FakeSystem(string workspace) : IInstallerCleanupSystem
    {
        public List<string> Operations { get; } = [];

        public bool FailService { get; init; }

        public string ProgramDataDirectory => Path.Combine(workspace, "program-data");

        public void RemoveService()
        {
            this.Operations.Add("service");
            if (this.FailService)
            {
                throw new IOException("Service unavailable");
            }
        }

        public void RemoveStartupTask()
        {
            this.Operations.Add("task");
        }

        public void StopApplications(string installDirectory)
        {
            this.Operations.Add("processes");
        }

        public void RemoveFirewallRules()
        {
            this.Operations.Add("firewall");
        }

        public IEnumerable<string> ProfileDirectories()
        {
            return new[]
            {
                Path.Combine(workspace, "profile-a"),
                Path.Combine(workspace, "profile-b"),
                Path.Combine(workspace, "profile-a")
            };
        }

        public void DeleteData(string baseDirectory, bool userProfile)
        {
            this.Operations.Add(Path.GetFileName(baseDirectory));
        }
    }
}