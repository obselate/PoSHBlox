using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using PoSHBlox.Services;

namespace PoSHBlox;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Dev tool: `PoSHBlox --regen-builtin <Module> <OutputFile> [--category "Name"] [--only Cmd1,Cmd2]`
        // Runs headless (no GUI, no Avalonia), regenerates a Templates/Builtin/*.json
        // catalog from a live PowerShell module, preserves curated defaults, exits.
        if (args.Length > 0 && args[0] == "--regen-builtin")
        {
            return RunRegen(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int RunRegen(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: PoSHBlox --regen-builtin <ModuleName> <OutputFile> [--category \"Name\"] [--only Cmd1,Cmd2,...]\n" +
                "  <OutputFile>   path to write, e.g. Templates/Builtin/FileFolder.json\n" +
                "  --category     category string stored in the file; defaults to the existing file's category if present\n" +
                "  --only         comma-separated cmdlet names to include (default: all discovered)");
            return 1;
        }

        var module = args[1];
        var output = args[2];
        string? category = null;
        HashSet<string>? only = null;

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
        }).GetAwaiter().GetResult();
    }
}
