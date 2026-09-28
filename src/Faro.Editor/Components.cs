using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Faro.Editor;

/// <summary>
/// Tools Faro fetches on demand (Preferences › Components) instead of shipping them: a JDK 21, Maven and jdtls for Java projects, netcoredbg for debugging C#,
/// Gluon's GraalVM for Java APKs, the .NET "android" workload for C# APKs. Downloads go to Faro's own tools folder and are
/// used only by Faro: <see cref="Activate"/> puts their bin folders first on its PATH and sets JAVA_HOME / GRAALVM_HOME.
/// </summary>
public static class Components
{
    public sealed record Component(string Name, string Purpose, Func<bool> Installed, Func<Action<string>, CancellationToken, Task<bool>> Install, bool Available = true);

    static readonly string Tools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Faro", "tools");
    static string Dir(string name) => Path.Combine(Tools, name);

    const string MavenVersion = "3.9.9";
    const string JdtlsUrl = "https://download.eclipse.org/jdtls/milestones/1.50.0/jdt-language-server-1.50.0-202509041425.tar.gz";

    const string NetcoredbgRelease = "https://github.com/Samsung/netcoredbg/releases/download/3.2.0-1092/";

    /// <summary>netcoredbg's executable (the C# debugger), once installed.</summary>
    public static string? Netcoredbg => Path.Combine(Dir("netcoredbg"), OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg") is var exe && File.Exists(exe) ? exe : null;

    // Samsung builds it for Windows x64, Linux x64/arm64 and Intel macOS (none for Apple silicon).
    static string NetcoredbgAsset => OperatingSystem.IsWindows() ? "netcoredbg-win64.zip" : OperatingSystem.IsMacOS() ? "netcoredbg-osx-amd64.tar.gz"
        : Arch == "aarch64" ? "netcoredbg-linux-arm64.tar.gz" : "netcoredbg-linux-amd64.tar.gz";

    const string JavaDebugUrl = "https://repo1.maven.org/maven2/com/microsoft/java/com.microsoft.java.debug.plugin/0.53.1/com.microsoft.java.debug.plugin-0.53.1.jar";

    /// <summary>Microsoft's java-debug, a jdtls plugin (the Java debugger), once installed.</summary>
    public static string? JavaDebug => Path.Combine(Dir("java-debug"), "java-debug.jar") is var jar && File.Exists(jar) ? jar : null;

    /// <summary>The Java language server's folder, once installed.</summary>
    public static string? Jdtls => Directory.Exists(Dir("jdtls")) ? Dir("jdtls") : null;
    const string GraalVmUrl = "https://github.com/gluonhq/graal/releases/download/gluon-23%2B25.1-dev-2409082136/graalvm-java23-linux-amd64-gluon-23+25.1-dev.tar.gz";

    static string Os => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
    static string Arch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";

    /// <summary>A JDK folder's home (macOS JDKs keep it in Contents/Home).</summary>
    static string JavaHome(string dir) => Directory.Exists(Path.Combine(dir, "Contents", "Home")) ? Path.Combine(dir, "Contents", "Home") : dir;

    public static IReadOnlyList<Component> All { get; } =
    [
        new("JDK 21", "Java projects (Run, builds)", () => Directory.Exists(Dir("jdk")),
            (log, cancel) => Fetch($"https://api.adoptium.net/v3/binary/latest/21/ga/{Os}/{Arch}/jdk/hotspot/normal/eclipse", Dir("jdk"), OperatingSystem.IsWindows(), log, cancel)),
        new("Maven", "Java projects (Run, builds)", () => Directory.Exists(Dir("maven")),
            (log, cancel) => Fetch($"https://archive.apache.org/dist/maven/maven-3/{MavenVersion}/binaries/apache-maven-{MavenVersion}-bin.{(OperatingSystem.IsWindows() ? "zip" : "tar.gz")}",
                Dir("maven"), OperatingSystem.IsWindows(), log, cancel)),
        new("Java language server (jdtls)", "Completion and errors in Java code (installed on first use too)", () => Jdtls is not null,
            (log, cancel) => Fetch(JdtlsUrl, Dir("jdtls"), false, log, cancel)),
        new("Java debugger (java-debug)", "Debugging Java code (a jdtls plugin, installed with it)", () => JavaDebug is not null,
            async (log, cancel) =>
            {
                try
                {
                    log(L.F("Downloading {0}…", JavaDebugUrl));
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Faro"); // Maven Central refuses requests without one (429)
                    var bytes = await http.GetByteArrayAsync(JavaDebugUrl, cancel);
                    Directory.CreateDirectory(Dir("java-debug"));
                    await File.WriteAllBytesAsync(Path.Combine(Dir("java-debug"), "java-debug.jar"), bytes, cancel);
                    return true;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
                {
                    log(L.T("Couldn't install it: ") + e.Message);
                    return false;
                }
            }),
        new("netcoredbg", "The C# debugger (installed on first use too)", () => Netcoredbg is not null,
            (log, cancel) => Fetch(NetcoredbgRelease + NetcoredbgAsset, Dir("netcoredbg"), OperatingSystem.IsWindows(), log, cancel), !(OperatingSystem.IsMacOS() && Arch == "aarch64")),
        new("Gluon GraalVM", "Android APKs of Java projects (Linux only)", () => Directory.Exists(Dir("graalvm")),
            (log, cancel) => Fetch(GraalVmUrl, Dir("graalvm"), false, log, cancel), OperatingSystem.IsLinux()),
        new(".NET Android workload", "Android APKs of C# projects", AndroidWorkloadInstalled,
            async (log, cancel) => await AndroidApk.Exec("dotnet", ["workload", "install", "android"], log, cancel) == 0),
    ];

    static bool AndroidWorkloadInstalled()
    {
        try
        {
            using var list = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", ["workload", "list"]) { RedirectStandardOutput = true, CreateNoWindow = true })!;
            var text = list.StandardOutput.ReadToEnd();
            list.WaitForExit();
            return text.Split('\n').Any(l => l.TrimStart().StartsWith("android ", StringComparison.Ordinal));
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>At startup: the installed tools first on PATH (child processes inherit it), JAVA_HOME / GRAALVM_HOME set.</summary>
    public static void Activate()
    {
        var bins = new List<string>();
        if (Directory.Exists(Dir("jdk")))
        {
            Environment.SetEnvironmentVariable("JAVA_HOME", JavaHome(Dir("jdk")));
            bins.Add(Path.Combine(JavaHome(Dir("jdk")), "bin"));
        }
        if (Directory.Exists(Dir("maven"))) bins.Add(Path.Combine(Dir("maven"), "bin"));
        if (Directory.Exists(Dir("graalvm"))) Environment.SetEnvironmentVariable("GRAALVM_HOME", Dir("graalvm"));
        if (bins.Count > 0)
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, bins.Append(Environment.GetEnvironmentVariable("PATH") ?? "")));
    }

    /// <summary>Downloads an archive and unpacks its single top folder as <paramref name="target"/>.</summary>
    static async Task<bool> Fetch(string url, string target, bool zip, Action<string> log, CancellationToken cancel)
    {
        var temp = Path.Combine(Tools, ".download");
        try
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            log(L.F("Downloading {0}…", url));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Faro");
            await using (var body = await http.GetStreamAsync(url, cancel))
            {
                log(L.T("Unpacking…"));
                if (zip)
                {
                    await using var file = new MemoryStream();
                    await body.CopyToAsync(file, cancel);
                    file.Position = 0;
                    ZipFile.ExtractToDirectory(file, temp);
                }
                else
                {
                    await using var gz = new GZipStream(body, CompressionMode.Decompress);
                    await TarFile.ExtractToDirectoryAsync(gz, temp, overwriteFiles: true, cancel);
                }
            }
            var top = Directory.GetDirectories(temp) is [var single] && Directory.GetFiles(temp).Length == 0 ? single : temp;
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(top, target);
            log(L.F("Installed in {0}.", target));
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        {
            log(L.T("Couldn't install it: ") + e.Message);
            return false;
        }
        finally
        {
            if (Directory.Exists(temp)) try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }
}
