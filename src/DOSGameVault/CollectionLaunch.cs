using System.Diagnostics;
using System.IO;

namespace DOSGameVault;

public static class CollectionLaunch
{
    public static string ResolveForGame(GameEntry game, AppSettings settings, DosBoxOptions options)
    {
        if (game.LaunchProfile is { EmulatorPath.Length: > 0 } profile) return profile.EmulatorPath;
        var key = string.IsNullOrWhiteSpace(game.EmulatorVersion) ? options.Emulator ?? (string.IsNullOrWhiteSpace(settings.DefaultEmulatorKey) ? null : settings.DefaultEmulatorKey) : game.EmulatorVersion;
        var registered = settings.Emulators.FirstOrDefault(e => e.Key.Equals(key ?? (string.IsNullOrEmpty(game.CollectionRoot) ? "" : "DOSBOX"), StringComparison.OrdinalIgnoreCase));
        if (registered != null) return registered.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(settings.DefaultEmulatorKey) && key == settings.DefaultEmulatorKey) throw new FileNotFoundException("The default DOSBox is not registered. Choose a default version in Settings.");
        if (!string.IsNullOrWhiteSpace(game.EmulatorVersion)) throw new FileNotFoundException($"DOSBox version '{game.EmulatorVersion}' is not registered. Add it in Settings or choose Automatic in Game Settings.");
        return string.IsNullOrEmpty(game.CollectionRoot) ? settings.EmulatorPath : ResolveEmulator(game.CollectionRoot, options.Emulator, settings.EmulatorPath);
    }
    // Match folder names case-insensitively; a requested version must never fall back to a different version.
    public static string ResolveEmulator(string root, string? requested, string fallback)
    {
        var folders = Directory.GetDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var names = new[] { requested ?? "DOSBOX" };
        foreach (var name in names)
        {
            var folder = folders.FirstOrDefault(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (folder is null) continue;
            var found = FindExecutable(folder);
            if (found != null) return found;
        }
        throw new FileNotFoundException($"Cannot find {(requested ?? "DOSBOX")} in {root}. Place the DOSBox program in that folder beside the game folders.");
    }

    internal static string? FindExecutable(string folder)
    {
        var names = new[] { "dosbox.exe", "dosbox-x.exe", "dosbox-staging.exe", "dosboxpp.exe", "dosboxdawn.exe", "dosboxbrz.exe", "dosvsv.exe", "dosboxsv.exe" };
        var files = Directory.GetFiles(folder, "*.exe");
        return names.Select(name => files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(f => f != null);
    }

    internal static IReadOnlyList<string> ConfigurationFiles(GameEntry game,AppSettings settings,DosBoxOptions options,string executable)
    {
        var files=new List<string>();
        var registrationKey=string.IsNullOrWhiteSpace(game.EmulatorVersion) ? options.Emulator ?? (string.IsNullOrWhiteSpace(settings.DefaultEmulatorKey) ? "DOSBOX" : settings.DefaultEmulatorKey) : game.EmulatorVersion;
        var registered=settings.Emulators.FirstOrDefault(e=>e.Key.Equals(registrationKey,StringComparison.OrdinalIgnoreCase) && e.ExecutablePath.Equals(executable,StringComparison.OrdinalIgnoreCase));
        if(registered is {ConfigPath.Length:>0})
        {
            if(!File.Exists(registered.ConfigPath))throw new FileNotFoundException("The registered DOSBox configuration file is missing.",registered.ConfigPath);
            files.Add(registered.ConfigPath);
        }
        if(options.Custom)
        {
            var directory=Path.GetDirectoryName(Path.GetFullPath(game.ExecutablePath))!;
            var config=new[] {"game.conf","dosbox.conf","dosbox.cfg"}.Select(name=>Path.Combine(directory,name)).FirstOrDefault(File.Exists);
            if(config==null)throw new FileNotFoundException("This row requests CUSTOM. Provide its original game.conf, dosbox.conf or dosbox.cfg in the game folder.");
            files.Add(config);
        }
        if(game.LaunchProfile is {ConfigPath.Length:>0} profile)
        {
            if(!Path.IsPathFullyQualified(profile.ConfigPath) || !File.Exists(profile.ConfigPath))
                throw new FileNotFoundException("Choose an existing DOSBox configuration file, or clear the configuration override.",profile.ConfigPath);
            files.Add(profile.ConfigPath);
        }
        return files;
    }

    public static ProcessStartInfo Build(GameEntry game, AppSettings settings)
    {
        if (!File.Exists(game.ExecutablePath)) throw new FileNotFoundException("The game's startup file is missing. Check the collection folder.", game.ExecutablePath);
        var options = DosBoxOptions.Parse(game.LaunchProfile?.Options ?? game.DosBoxOptions,tolerateLegacy:game.LaunchProfile==null);
        if (options.Warnings.Count != 0) throw new InvalidDataException(string.Join(" ", options.Warnings));
        var executable = ResolveForGame(game, settings, options);
        if (!File.Exists(executable)) throw new FileNotFoundException("The per-game DOSBox executable is missing.", executable);
        var directory = Path.GetDirectoryName(Path.GetFullPath(game.ExecutablePath))!;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (game.LaunchProfile?.Fullscreen ?? settings.Fullscreen) info.ArgumentList.Add("-fullscreen");
        void Command(string command) { info.ArgumentList.Add("-c"); info.ArgumentList.Add(command); }
        foreach(var config in ConfigurationFiles(game,settings,options,executable))
        {info.ArgumentList.Add("-conf");info.ArgumentList.Add(config);}
        var configuration=DosBoxLaunchConfig.Contents(game,settings.Fullscreen);
        if(configuration.Length>0) {info.ArgumentList.Add("-conf");info.ArgumentList.Add(DosBoxLaunchConfig.PathFor(configuration));}
        if(game.LaunchProfile?.SkipAutoexec==true)info.ArgumentList.Add("-noautoexec");
        foreach (var setting in options.Settings)
        {
            // Profiles load all values at startup. Keep the legacy runtime commands
            // for mutable CPU/memory options; hardware initialization is not mutable.
            var key=setting[..setting.IndexOf('=')];
            if(game.LaunchProfile==null || key is "cycles" or "core" or "ems" or "xms" or "umb")Command("config -set " + setting);
        }
        Command($"mount c {Quote(directory)}");
        var mediaType = game.LaunchProfile?.MediaType ?? game.MediaType;
        var mediaPath = game.LaunchProfile?.MediaPath ?? game.MediaPath;
        switch (mediaType.ToLowerInvariant())
        {
            case "": case "no": break;
            case "img":
                var image = AppListImport.ResolveUnder(directory, mediaPath);
                if (!File.Exists(image)) throw new FileNotFoundException("The game's disc image is missing.", image);
                Command($"imgmount d {Quote(image)} -t iso"); break;
            case "cdrom":
                var cd = string.IsNullOrWhiteSpace(mediaPath) || mediaPath.Equals("no", StringComparison.OrdinalIgnoreCase)
                    ? directory : AppListImport.ResolveUnder(directory, mediaPath);
                if (!Directory.Exists(cd)) throw new DirectoryNotFoundException($"CD directory missing: {cd}");
                Command($"mount d {Quote(cd)} -t cdrom"); break;
            case "floppy":
                var drive = mediaPath.ToLowerInvariant() is "a" or "b" ? mediaPath.ToLowerInvariant() : "a";
                Command($"mount {drive} {Quote(directory)} -t floppy");
                Command(drive + ":");
                Command(ProgramName(game.ExecutablePath));
                if(game.LaunchProfile?.ExitAfterGame==true)Command("exit");
                return info;
            default: throw new InvalidDataException($"Unresolved media mode '{mediaType}'. Set its media mode in Game Settings.");
        }
        Command("c:");
        Command(ProgramName(game.ExecutablePath));
        if(game.LaunchProfile?.ExitAfterGame==true)Command("exit");
        return info;
    }

    private static string Quote(string value)
    {
        if (value.IndexOfAny(['\'', '"', '\r', '\n', '\0']) >= 0) throw new InvalidDataException("Invalid DOSBox command path.");
        // Classic DOSBox parses the raw Windows command line itself. Embedded double
        // quotes escaped by ArgumentList are not handled like the Windows CRT.
        // Its documented Windows syntax uses single quotes inside a -c argument.
        return $"'{value}'";
    }
    private static string ProgramName(string path)
    {
        var name = Path.GetFileName(path);
        return name.Any(char.IsWhiteSpace) ? Quote(name) : name;
    }
}

