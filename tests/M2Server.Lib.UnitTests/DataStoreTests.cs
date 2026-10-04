using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;
using NUnit.Framework;

namespace M2Server.Lib.UnitTests;

public sealed class DataStoreTests
{
    [Test]
    public void DiscardDraftsRestoresSavedSettingsIncludingNulls()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "data-store-tests", Guid.NewGuid().ToString("N"));
        var store = new DataStore(root, false);
        store.Update(document =>
            {
                document.Settings.WebEnabled = true;
                document.Settings.AllowLanAccess = true;
                document.Settings.AllowPublicLanAccess = true;
                return 0;
            }
        );

        store.DiscardDrafts();

        var settings = store.Read(document => document.Settings);
        Assert.Multiple(() =>
            {
                Assert.That(settings.WebEnabled, Is.Null);
                Assert.That(settings.AllowLanAccess, Is.False);
                Assert.That(settings.AllowPublicLanAccess, Is.False);
                Assert.That(store.HasDrafts, Is.False);
            }
        );
    }

    [Test]
    public void SeparateDirectoriesKeepSeparateProfilesAndScripts()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "data-store-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = new DataStore(Path.Combine(root, "first"), false);
            var second = new DataStore(Path.Combine(root, "second"), false);
            first.Update(document =>
                {
                    document.Settings.WebEnabled = true;
                    document.Presets.Add(new Preset { Name = "First user's profile" });
                    document.Scripts.Add(new ScriptEntry { Name = "First user's script" });
                    return 0;
                }
            );
            first.Save();

            Assert.That(new DataStore(Path.Combine(root, "first")).Read(x => x.Settings.WebEnabled), Is.True);
            Assert.That(second.Read(x => x.Settings.WebEnabled), Is.Not.True);
            Assert.That(second.Read(x => x.Presets), Is.Empty);
            Assert.That(second.Read(x => x.Scripts), Is.Empty);
            Assert.That(File.Exists(Path.Combine(root, "second", "data.json")), Is.False);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public void SavingDraftPreservesExistingSessionsWhenPasswordIsUnchanged()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "data-store-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var draft = new DataStore(root, false);
            draft.Update(document =>
                {
                    document.Settings.WebEnabled = true;
                    document.Settings.PasswordHash = "hash";
                    return 0;
                }
            );
            draft.Save();

            var service = new DataStore(root);
            service.Update(document =>
                {
                    document.Tokens["session"] = DateTimeOffset.UtcNow.AddDays(1);
                    return 0;
                }
            );
            draft.Update(document =>
                {
                    document.Settings.HttpsPort = 4321;
                    return 0;
                }
            );
            draft.Save();

            Assert.That(new DataStore(root).Read(x => x.Tokens.ContainsKey("session")), Is.True);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public void RestoringSavedFileKeepsFailedSaveAsDraft()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "data-store-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var draft = new DataStore(root, false);
            draft.Save();
            var previous = draft.ReadSaved(document => document);
            draft.Update(document =>
                {
                    document.Settings.HttpsPort = 4321;
                    return 0;
                }
            );
            draft.Save();
            draft.RestoreSaved(previous);

            Assert.That(draft.HasDrafts, Is.True);
            Assert.That(draft.Read(document => document.Settings.HttpsPort), Is.EqualTo(4321));
            Assert.That(
                new DataStore(root).Read(document => document.Settings.HttpsPort),
                Is.EqualTo(previous.Settings.HttpsPort)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}