using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PoSHBlox.Services;

namespace PoSHBlox;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Dev tools run headless (no GUI, no Avalonia):
        //   --regen-builtin  <Module> <OutputFile> [--category "Name"] [--only Cmd1,Cmd2]
        //   --regen-manifest <ManifestFile> [--dry-run]
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--regen-builtin":
                    AttachHostConsole();
                    return RunRegenSingle(args);
                case "--regen-manifest":
                    AttachHostConsole();
                    return RunRegenManifest(args);
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // ── Console plumbing for a WinExe running in CLI mode ──────
    // The csproj declares <OutputType>WinExe</OutputType> so double-clicks
    // don't flash a console window. But that also detaches stdout/stderr
    // when launched from PowerShell/cmd, so --regen-* output disappears.
    // Attaching to the parent process's console and re-binding the streams
    // makes writes show up in the calling shell.

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    private static void AttachHostConsole()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (!AttachConsole(ATTACH_PARENT_PROCESS)) return;

        // Re-point Console.Out / Console.Error at the freshly-attached console.
        // Without this, the .NET runtime's cached handles from WinExe startup
        // still point at the null device and writes silently no-op.
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var stderr = new StreamWriter(Console.OpenStandardError())  { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(stderr);

        // Print a leading newline so the first line of output isn't glued to
        // the PowerShell prompt that already returned.
        Console.Out.WriteLine();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int RunRegenSingle(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: PoSHBlox --regen-builtin <ModuleName> <OutputFile> [--category \"Name\"] [--only Cmd1,Cmd2,...] [--dry-run]\n" +
                "  <OutputFile>   path to write, e.g. Templates/Builtin/FileFolder.json\n" +
                "  --category     category string stored in the file; defaults to the existing file's category if present\n" +
                "  --only         comma-separated cmdlet names to include (default: all discovered)\n" +
                "  --dry-run      report what would happen without writing the file");
            return 1;
        }

        var module = args[1];
        var output = args[2];
        string? category = null;
        HashSet<string>? only = null;
        bool dryRun = false;

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--category":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--category requires a value"); return 1; }
                    category = args[++i];
                    break;
                case "--only":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--only requires a value"); return 1; }
                    only = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    Console.Error.WriteLine($"unknown flag: {args[i]}");
                    return 1;
            }
        }

        return TemplateRegenerator.RegenerateAsync(new TemplateRegenerator.Options
        {
            ModuleName = module,
            OutputPath = output,
            CategoryName = category,
            OnlyCmdlets = only,
        }, dryRun).GetAwaiter().GetResult();
    }

    private static int RunRegenManifest(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "usage: PoSHBlox --regen-manifest <ManifestFile> [--dry-run]\n" +
                "  <ManifestFile>  JSON manifest listing targets (output files + source modules + cmdlet filters).\n" +
                "                  See scripts/builtin-catalog.json for the reference manifest.\n" +
                "  --dry-run       report what would happen without writing any file");
            return 1;
        }

        var manifestPath = args[1];
        bool dryRun = false;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    Console.Error.WriteLine($"unknown flag: {args[i]}");
                    return 1;
            }
        }

        return TemplateRegenerator.RegenerateFromManifestAsync(manifestPath, dryRun).GetAwaiter().GetResult();
    }
}
