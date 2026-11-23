using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MDSM.Testing;

internal static class GraphicsTestSuite
{
    private static readonly (string Name, Func<int> Run)[] Cases =
    {
        ("streaming", StreamingGraphicsChecks.Run),
        ("season and lead", SeasonLeadChecks.RunGraphics),
        ("world rules", WorldRuleGraphicsChecks.Run),
        ("interface", InterfaceGraphicsChecks.Run),
        ("audio", AudioGraphicsChecks.Run),
        ("collision", () => CollisionGraphicsChecks.Run()),
        ("doors", DoorChecks.RunGraphics),
        ("pigs", PigGraphicsChecks.Run),
        ("water", WaterGraphicsChecks.Run),
        ("mobs", MobGraphicsChecks.Run)
    };

    internal static int Run(params string[] names)
    {
        int result = 0;
        foreach (string name in names.Length == 0 ? Cases.Select(test => test.Name) : names)
        {
            Console.WriteLine($"Running graphics checks: {name}");
            // Restart the native SDL/OpenGL state between test games on Linux.
            int exitCode = OperatingSystem.IsLinux() ? RunIsolated(name) : RunCase(name);
            if (exitCode != 0) Console.Error.WriteLine($"Graphics checks failed: {name} (exit {exitCode}).");
            result = Math.Max(result, exitCode);
        }
        return result;
    }

    internal static int RunCase(string name)
    {
        foreach (var test in Cases)
            if (test.Name == name) return test.Run();

        Console.Error.WriteLine($"Unknown graphics test case: {name}");
        return 2;
    }

    private static int RunIsolated(string name)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet")
            start.ArgumentList.Add(typeof(GraphicsTestSuite).Assembly.Location);
        start.ArgumentList.Add("--graphics-test-case");
        start.ArgumentList.Add(name);
        using var process = Process.Start(start);
        process.WaitForExit();
        return process.ExitCode;
    }
}
