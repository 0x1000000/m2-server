namespace M2Server.Lib.Infrastructure;

/// <summary>Deletes only the application-owned child of a trusted profile or ProgramData directory.</summary>
public static class ApplicationDataCleanup
{
    public static string Target(string baseDirectory, bool userProfile)
    {
        if (!Path.IsPathFullyQualified(baseDirectory))
        {
            throw new ArgumentException("Cleanup base directory must be absolute.", nameof(baseDirectory));
        }

        var root = Path.GetFullPath(baseDirectory);
        if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Cleanup base directory cannot be a volume root.", nameof(baseDirectory));
        }

        return userProfile
            ? Path.Combine(root, Constants.LocalDataRelativePath, Constants.DataDirectoryName)
            : Path.Combine(root, Constants.DataDirectoryName);
    }

    public static void Delete(string baseDirectory, bool userProfile)
    {
        var target = Target(baseDirectory, userProfile);
        // Check all existing ancestors, including the application directory itself.
        for (var ancestor = new DirectoryInfo(target); ancestor is not null; ancestor = ancestor.Parent)
        {
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0 && ancestor.Exists)
            {
                throw new IOException($"Refusing cleanup through a reparse point: {ancestor.FullName}");
            }
        }

        if (Directory.Exists(target))
        {
            DeleteTree(new DirectoryInfo(target));
        }
    }

    private static void DeleteTree(DirectoryInfo directory)
    {
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Unlink the entry itself; never enumerate its destination.
                item.Delete();
            }
            else if (item is DirectoryInfo child)
            {
                DeleteTree(child);
            }
            else
            {
                item.Attributes &= ~FileAttributes.ReadOnly;
                item.Delete();
            }
        }

        directory.Delete();
    }
}