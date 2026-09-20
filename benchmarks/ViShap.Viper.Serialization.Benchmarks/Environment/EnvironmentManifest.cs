using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace ViShap.Viper.Serialization.Benchmarks.Environment;

/// <summary>
/// The manifest of Benchmark-Plan §4: everything a published number belongs to, captured by code so
/// that no field is typed by hand.
/// </summary>
internal static class EnvironmentManifest
{
    internal static Dictionary<string, object?> Capture()
    {
        var manifest = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["capturedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["os"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["cpu"] = CpuName(),
            ["logicalCores"] = System.Environment.ProcessorCount,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["runtimeIdentifier"] = RuntimeInformation.RuntimeIdentifier,
            ["serverGc"] = GCSettings.IsServerGC,
            ["gcLatencyMode"] = GCSettings.LatencyMode.ToString(),
            ["is64BitProcess"] = System.Environment.Is64BitProcess,
            ["aesHardwareAcceleration"] = System.Runtime.Intrinsics.X86.Aes.IsSupported
                || System.Runtime.Intrinsics.Arm.Aes.IsSupported,
            ["vectorHardwareAcceleration"] = System.Numerics.Vector.IsHardwareAccelerated,
            ["debuggerAttached"] = Debugger.IsAttached,
            ["configuration"] = Configuration(),
            ["benchmarkDotNet"] = typeof(BenchmarkDotNet.Attributes.BenchmarkAttribute).Assembly
                .GetName().Version?.ToString(),
            ["revision"] = Git("rev-parse HEAD"),
            ["branch"] = Git("rev-parse --abbrev-ref HEAD"),
            ["dirty"] = !string.IsNullOrWhiteSpace(Git("status --porcelain")),
            ["packages"] = Packages(),
        };

        return manifest;
    }

    internal static void Write(string path)
    {
        var json = JsonSerializer.Serialize(Capture(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string Configuration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    /// <summary>The processor, read from the host rather than from a library that may rename its API.</summary>
    private static string CpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var name = WindowsProcessorName();
                var identifier = System.Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");

                return (name, identifier) switch
                {
                    ({ Length: > 0 }, { Length: > 0 }) => $"{name} ({identifier})",
                    ({ Length: > 0 }, _) => name,
                    (_, { Length: > 0 }) => identifier,
                    _ => "unknown",
                };
            }

            const string CpuInfo = "/proc/cpuinfo";

            if (File.Exists(CpuInfo))
            {
                foreach (var line in File.ReadLines(CpuInfo))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                    {
                        return line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
                    }
                }
            }

            return "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>The marketing name of the processor, which the identifier alone does not carry.</summary>
    private static string WindowsProcessorName()
    {
        var output = Run("reg", @"query ""HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0"" /v ProcessorNameString");

        foreach (var line in output.Split('\n'))
        {
            var marker = line.IndexOf("REG_SZ", StringComparison.Ordinal);

            if (marker >= 0)
            {
                return line[(marker + "REG_SZ".Length)..].Trim();
            }
        }

        return string.Empty;
    }

    private static string Git(string arguments) => Run("git", arguments);

    private static string Run(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            });

            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5_000);

            return output.Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Every pinned package version, read from the project file rather than from memory.</summary>
    private static Dictionary<string, string> Packages()
    {
        var packages = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var project = FindProjectFile();

            if (project is null)
            {
                return packages;
            }

            foreach (var reference in XDocument.Load(project).Descendants("PackageReference"))
            {
                var id = reference.Attribute("Include")?.Value;
                var version = reference.Attribute("Version")?.Value;

                if (id is not null && version is not null)
                {
                    packages[id] = version;
                }
            }
        }
        catch (Exception)
        {
            // A manifest without package versions is still a manifest; the gap is visible in it.
        }

        return packages;
    }

    private static string? FindProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var project = directory.GetFiles("ViShap.Viper.Serialization.Benchmarks.csproj").FirstOrDefault();

            if (project is not null)
            {
                return project.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
