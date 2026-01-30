using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Jumpscare.Windows;
using System.IO;
using System.Linq;

namespace Jumpscare;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/jumpscare";
    private const string CommandAlias = "/js";
    private const string ConfigCommandName = "/jumpscarecfg";
    private const string ConfigCommandAlias = "/jscfg";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Jumpscare");
    private ConfigWindow ConfigWindow { get; init; }
    public MainWindow MainWindow { get; private set; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureDefaults();
        Configuration.EnsureAtLeastOneEnabled(Configuration.Images);
        Configuration.EnsureAtLeastOneEnabled(Configuration.Sounds);
        Configuration.Save();

        ConfigWindow = new ConfigWindow(this);

        var paths = ConfigWindow.ResolveInitialMedia();

        MainWindow = new MainWindow(paths.imagePath, paths.soundPath, Configuration);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        var dllDir = PluginInterface.AssemblyLocation.Directory?.FullName
                     ?? PluginInterface.GetPluginConfigDirectory();
        string dataDir = Path.Combine(dllDir, "Data");

        // fallback to defaults if null
        if (paths.imagePath == null || !File.Exists(paths.imagePath))
            paths.imagePath = Path.Combine(dataDir, "visual", Configuration.DefaultImages.First(e => e.Enabled).Path);

        if (paths.soundPath == null || !File.Exists(paths.soundPath))
            paths.soundPath = Path.Combine(dataDir, "audio", Configuration.DefaultSounds.First(e => e.Enabled).Path);


        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Start/stop jumpscare timer"
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /jumpscare"
        });
        CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the jumpscare configuration window"
        });
        CommandManager.AddHandler(ConfigCommandAlias, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Alias for /jumpscarecfg"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
        CommandManager.RemoveHandler(ConfigCommandName);
        CommandManager.RemoveHandler(ConfigCommandAlias);
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }
    private void OnConfigCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
