using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;

namespace Pusula.Startup;

/// <summary>
/// What is built into the program: the web page (<c>wwwroot</c>) and the default settings (<c>appsettings.json</c>). The
/// single-file download is started from any folder and has neither of them next to it.
/// </summary>
internal static class BuiltInContent
{
    private const string SettingsResource = "pusula.appsettings.json";

    /// <summary>
    /// The settings of <c>appsettings.json</c> as the program was built with them, as a source to put under all the others:
    /// an <c>appsettings.json</c> in the content root (<c>dotnet run</c>), the environment and the command line come on top.
    /// </summary>
    /// <exception cref="InvalidOperationException">The program was built without the settings.</exception>
    public static MemoryConfigurationSource Settings()
    {
        using Stream stream = typeof(BuiltInContent).Assembly.GetManifestResourceStream(SettingsResource)
            ?? throw new InvalidOperationException($"The program was built without its settings ({SettingsResource}).");
        IConfigurationRoot settings = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return new MemoryConfigurationSource { InitialData = [.. settings.AsEnumerable().Where(setting => setting.Value is not null)] };
    }

    /// <summary>The web page as the program was built with it.</summary>
    public static IFileProvider WebRoot() => new ManifestEmbeddedFileProvider(typeof(BuiltInContent).Assembly, "wwwroot");
}
