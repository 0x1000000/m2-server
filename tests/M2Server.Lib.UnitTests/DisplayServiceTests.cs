using M2Server.Lib.Domain;
using M2Server.Lib.Services;
using NUnit.Framework;

namespace M2Server.Lib.UnitTests;

[TestFixture]
public sealed class DisplayServiceTests
{
    private const string LeftId = "left";
    private const string RightId = "right";
    private static readonly DisplayMode Mode = new(1920, 1080, 60);

    [Test]
    public void DisabledMonitorCanBecomePrimaryAfterDelayedEnumeration()
    {
        var platform = new FakeDisplayPlatform(true, false) { DelayedTopologyReads = 1 };
        var result = new DisplayService(platform: platform).Activate(Profile(true));

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(platform.TopologyCalls, Is.EqualTo(1));
                Assert.That(platform.PrimaryCalls, Is.EqualTo(1));
                Assert.That(platform.GetMonitors().Single(monitor => monitor.Id == RightId).Primary, Is.True);
            }
        );
    }

    [Test]
    public void DetachedMonitorModesAreCheckedAfterTopologyActivation()
    {
        var platform = new FakeDisplayPlatform(true, false) { StaleDetachedModes = true };

        var result = new DisplayService(platform: platform).Activate(Profile(true));

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(platform.TopologyCalls, Is.EqualTo(1));
                Assert.That(platform.GetMonitors().Single(monitor => monitor.Id == RightId).Enabled, Is.True);
            }
        );
    }

    [Test]
    public void TransientLayoutFailureIsRetried()
    {
        var platform = new FakeDisplayPlatform(false, false) { ApplyFailures = 1 };
        var result = new DisplayService(platform: platform).Activate(Profile(false));

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(platform.ApplyCalls, Is.EqualTo(2));
            }
        );
    }

    [Test]
    public void RotationOnlyChangeIsApplied()
    {
        var platform = new FakeDisplayPlatform(false, false);
        var profile = Profile(false);
        profile.Monitors[0].Rotation = DisplayRotation.Rotate270;

        var result = new DisplayService(platform: platform).Activate(profile);

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(platform.ApplyCalls, Is.EqualTo(1));
                Assert.That(platform.GetMonitors().Single().Rotation, Is.EqualTo(DisplayRotation.Rotate270));
            }
        );
    }

    [Test]
    public void QuarterTurnSwapsModeDimensions()
    {
        Assert.Multiple(() =>
            {
                Assert.That(Mode.Orient(DisplayRotation.Identity), Is.EqualTo(Mode));
                Assert.That(Mode.Orient(DisplayRotation.Rotate180), Is.EqualTo(Mode));
                Assert.That(Mode.Orient(DisplayRotation.Rotate90), Is.EqualTo(new DisplayMode(1080, 1920, 60)));
                Assert.That(Mode.Orient(DisplayRotation.Rotate270), Is.EqualTo(new DisplayMode(1080, 1920, 60)));
            }
        );
    }

    [Test]
    public void PersistentLayoutFailureStopsAfterSixAttempts()
    {
        var platform = new FakeDisplayPlatform(false, false) { ApplyFailures = 100 };

        var result = new DisplayService(platform: platform).Activate(Profile(false));

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(platform.ApplyCalls, Is.EqualTo(6));
            }
        );
    }

    [Test]
    public void UnsupportedModeStopsWithoutApplying()
    {
        var platform = new FakeDisplayPlatform(false, false);
        var profile = Profile(false);
        profile.Monitors[0].Mode = new DisplayMode(3840, 2160, 60);

        var result = new DisplayService(platform: platform).Activate(profile);

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Steps?.Single().Phase, Is.EqualTo("mode"));
                Assert.That(platform.ApplyCalls, Is.Zero);
            }
        );
    }

    [Test]
    public void IncompleteReadbackIsRetried()
    {
        var platform = new FakeDisplayPlatform(false, false) { IncompleteReadbacks = 1 };
        var result = new DisplayService(platform: platform).Activate(Profile(false));

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(platform.ApplyCalls, Is.EqualTo(2));
            }
        );
    }

    [Test]
    public void WatchdogStopsBeforeAttemptWhenTimeLimitExpires()
    {
        var platform = new FakeDisplayPlatform(false, false);
        var service = new DisplayService(platform: platform, timeProvider: new ExpiredTimeProvider());

        var result = service.Activate(Profile(false));

        Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Steps?.Single().Phase, Is.EqualTo("watchdog"));
                Assert.That(platform.ApplyCalls, Is.Zero);
            }
        );
    }

    [Test]
    public void EveryMatchingProfileIsMarkedActiveFromCurrentDisplays()
    {
        var platform = new FakeDisplayPlatform(false, false);
        var first = Profile(false);
        var second = Profile(false);
        second.Monitors[0].DpiScale = 125;
        second.Monitors[0].HdrEnabled = true;
        var different = Profile(true);
        var active = new DisplayService(platform: platform).DetectActivePresets([first, second, different]);

        Assert.That(active, Is.EquivalentTo(new[] { first.Id }));

        second.Monitors[0].HdrEnabled = false;
        platform.SetCurrentVisualState(125, null);
        active = new DisplayService(platform: platform).DetectActivePresets([first, second]);
        Assert.That(active, Is.EquivalentTo(new[] { first.Id, second.Id }));

        second.Monitors[0].HdrEnabled = true;
        platform.SetCurrentVisualState(125, false);
        active = new DisplayService(platform: platform).DetectActivePresets([first, second]);
        Assert.That(active, Is.EquivalentTo(new[] { first.Id }));

        platform.SetCurrentVisualState(100, true);
        active = new DisplayService(platform: platform).DetectActivePresets([first, second]);
        Assert.That(active, Is.EquivalentTo(new[] { first.Id }));

        platform.SetCurrentVisualState(125, true);
        active = new DisplayService(platform: platform).DetectActivePresets([first, second, different]);
        Assert.That(active, Is.EquivalentTo(new[] { first.Id, second.Id }));

        second.Monitors[0].Mode = new DisplayMode(1920, 1080, 75);
        active = new DisplayService(platform: platform).DetectActivePresets([first, second]);
        Assert.That(active, Is.EquivalentTo(new[] { first.Id }));
    }

    [Test]
    public void EditorIncludesAvailableMonitorAbsentFromGdiEnumeration()
    {
        var platform = new FakeDisplayPlatform(true, false) { OmitRightFromGdi = true };
        var service = new DisplayService(platform: platform);

        Assert.That(service.GetMonitors().Select(monitor => monitor.Id), Is.EquivalentTo(new[] { LeftId }));
        Assert.That(service.GetEditorMonitors().Select(monitor => monitor.Id), Is.EquivalentTo(new[] { LeftId, RightId }));
    }

    private static Preset Profile(bool includeRight)
    {
        var profile = new Preset { Name = "Test" };
        profile.Monitors.Add(
            new MonitorSetting
            {
                Id = LeftId,
                Name = "Left",
                Enabled = true,
                Primary = !includeRight,
                Order = 0,
                Mode = Mode
            }
        );
        if (includeRight)
        {
            profile.Monitors.Add(
                new MonitorSetting
                {
                    Id = RightId,
                    Name = "Right",
                    Enabled = true,
                    Primary = true,
                    Order = 1,
                    Mode = Mode
                }
            );
        }

        return profile;
    }

    private sealed class ExpiredTimeProvider : TimeProvider
    {
        private int _calls;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Interlocked.Increment(ref this._calls) <= 2 ? 0 : TimeSpan.FromSeconds(11).Ticks;
        }
    }

    private sealed class FakeDisplayPlatform : IDisplayPlatform
    {
        private readonly List<MonitorInfo> _monitors;
        private bool _topologyChanged;

        public FakeDisplayPlatform(bool includeRight, bool rightEnabled)
        {
            this._monitors = [Monitor(LeftId, true, true, 0)];
            if (includeRight)
            {
                this._monitors.Add(Monitor(RightId, rightEnabled, false, 1920));
            }
        }

        public int DelayedTopologyReads { get; set; }

        public bool OmitRightFromGdi { get; set; }

        public bool StaleDetachedModes { get; set; }

        public int IncompleteReadbacks { get; set; }

        public int ApplyFailures { get; set; }

        public int TopologyCalls { get; private set; }

        public int PrimaryCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public IReadOnlyList<MonitorInfo> GetMonitors()
        {
            if (this._topologyChanged && this.DelayedTopologyReads-- > 0)
            {
                return this._monitors.Select(monitor => monitor.Id == RightId ? monitor with { Enabled = false } : monitor)
                    .ToList();
            }

            if (this._topologyChanged)
            {
                for (var index = 0; index < this._monitors.Count; index++)
                {
                    if (this._monitors[index].Enabled && this._monitors[index].CurrentMode is null)
                    {
                        this._monitors[index] = this._monitors[index] with { CurrentMode = Mode };
                    }
                }
            }

            if (this.ApplyCalls > 0 && this.IncompleteReadbacks-- > 0)
            {
                return this._monitors.Select(monitor => monitor with { CurrentMode = null }).ToList();
            }

            var result = this.OmitRightFromGdi
                ? this._monitors.Where(monitor => monitor.Id != RightId).ToList()
                : this._monitors.ToList();
            return this.StaleDetachedModes
                ? result.Select(monitor
                        => monitor.Id == RightId ? monitor with { Modes = [new DisplayMode(1920, 1080, 300)] } : monitor
                    )
                    .ToList()
                : result;
        }

        public IReadOnlyList<string> DiagnoseCurrentModes()
        {
            return [];
        }

        public IReadOnlyList<DisplayTargetIdentity> AvailableTargets()
        {
            return this._monitors.Select(monitor => new DisplayTargetIdentity(
                        monitor.Id,
                        monitor.Name,
                        monitor.Id,
                        new HashSet<string> { monitor.DeviceName },
                        true,
                        DisplayRotation.Identity
                    )
                )
                .ToList();
        }

        public (bool Success, string Message, int? WindowsError) SetTopology(
            IReadOnlyList<DisplayTopologyTarget> targets,
            bool apply)
        {
            this.TopologyCalls++;
            if (apply)
            {
                var ids = targets.Select(target => target.Path).ToHashSet();
                for (var index = 0; index < this._monitors.Count; index++)
                {
                    this._monitors[index] = this._monitors[index] with { Enabled = ids.Contains(this._monitors[index].Id) };
                }

                this._topologyChanged = true;
            }

            return (true, "Topology applied.", null);
        }

        public (bool Success, string Message, int? WindowsError) SetPrimary(string targetPath)
        {
            this.PrimaryCalls++;
            for (var index = 0; index < this._monitors.Count; index++)
            {
                this._monitors[index] = this._monitors[index] with { Primary = this._monitors[index].Id == targetPath };
            }

            return (true, "Primary applied.", null);
        }

        public int TestLayout(DisplayLayoutRequest request)
        {
            return 0;
        }

        public int ApplyLayout(DisplayLayoutRequest request)
        {
            this.ApplyCalls++;
            if (this.ApplyFailures-- > 0)
            {
                return -1;
            }

            var index = this._monitors.FindIndex(monitor => monitor.DeviceName == request.DeviceName);
            this._monitors[index] = this._monitors[index] with
            {
                CurrentMode = request.Mode,
                PositionX = request.PositionX,
                PositionY = request.PositionY,
                Rotation = request.Rotation
            };
            return 0;
        }

        public int SetDpiScale(DisplayTargetIdentity target, int desiredPercent)
        {
            return 0;
        }

        public (bool Supported, bool Enabled) GetHdrInfo(DisplayTargetIdentity target)
        {
            return (false, false);
        }

        public int SetHdr(DisplayTargetIdentity target, bool enabled)
        {
            return 0;
        }

        public void SetCurrentVisualState(int? dpiScale, bool? hdrEnabled)
        {
            this._monitors[0] = this._monitors[0] with { CurrentDpiScale = dpiScale, HdrEnabled = hdrEnabled };
        }

        private static MonitorInfo Monitor(string id, bool enabled, bool primary, int positionX)
        {
            return new MonitorInfo(
                id,
                id,
                "DISPLAY-" + id,
                enabled,
                primary,
                enabled ? Mode : null,
                [Mode],
                positionX,
                0,
                [],
                null,
                false,
                null,
                DisplayRotation.Identity
            );
        }
    }
}