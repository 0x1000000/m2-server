namespace M2Server.Lib.Infrastructure;

public static class ServiceDataDirectory
{
    public static string ReferencePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Constants.DataDirectoryName,
        Constants.ServiceDataDirectoryFileName
    );

    public static string CurrentUser => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Constants.DataDirectoryName
    );

    public static string Read()
    {
        if (!File.Exists(ReferencePath))
        {
            var legacyDirectory = Path.GetDirectoryName(ReferencePath)!;
            if (File.Exists(Path.Combine(legacyDirectory, Constants.DataFileName)))
            {
                return legacyDirectory;
            }

            throw new FileNotFoundException("The web service configuration reference is missing.", ReferencePath);
        }

        var directory = File.ReadAllText(ReferencePath).Trim();
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidDataException("The web service configuration path must be absolute.");
        }

        return Path.GetFullPath(directory);
    }
}