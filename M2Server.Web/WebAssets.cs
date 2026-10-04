using Microsoft.Extensions.FileProviders;

namespace M2Server.Web;

public static class WebAssets
{
    public static IFileProvider CreateFileProvider()
    {
        return new ManifestEmbeddedFileProvider(typeof(WebAssets).Assembly, "web");
    }
}