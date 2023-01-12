using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

using API;
using static BetterChat.ChatLogger;

namespace BetterChat
{
    public static class Module
    {
        public const string GUID = "randomuserhi.BetterChat";
        public const string Name = "BetterChat";
        public const string Version = "0.0.1";
    }

    [BepInPlugin(Module.GUID, Module.Name, Module.Version)]
    internal class Entry : BasePlugin
    {
        public override void Load()
        {
            APILogger.Debug(Module.Name, "Loaded BetterChat");
            harmony = new Harmony(Module.GUID);
            harmony.PatchAll();

            APILogger.Debug(Module.Name, "Debug is " + (ConfigManager.Debug ? "Enabled" : "Disabled"));

            AddGlobalCommand("cd", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    string[] drive = args[0].Split(":");
                    if (drive.Length > 2) n.Error($"Unable to parse path \"{args[0]}\".");
                    else if (drive.Length == 2)
                    {
                        if (drive[0] == "root" || drive[0] == "r")
                        {
                            CmdNode? to = root.GetNode(drive[1]);
                            if (to != null)
                            {
                                current = to;
                                current.Debug($"{current.fullPath}");
                            }
                        }
                        else n.Error($"Drive \"{drive[0]}\" does not exist.");
                    }
                    else
                    {
                        CmdNode? to = n.GetNode(args[0]);
                        if (to != null)
                        {
                            current = to;
                            current.Debug($"{current.fullPath}");
                        }
                    }
                },
                description = "Traverse to given path.",
                syntax = "<path>"
            });

            AddGlobalCommand("clear", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    foreach (LogHistory history in logHistory.Values)
                        history.logs.Clear();
                    historyIndex = 0;
                },
                description = "Clears log history."
            });

            // TODO:: show node descriptions => need to restructure system to somehow store help data
            AddGlobalCommand("ls", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    n.Debug($"Command Nodes in {n.fullPath}:");
                    foreach (string node in n.path.Keys)
                        Print($"  {Color("#f00", node)}");
                    Print("  -- End --");
                },
                description = "List command directories in current directory."
            });

            // TODO:: show command descriptions => need to restructure system to somehow store help data
            AddGlobalCommand("help", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    n.Debug($"Global commands:");
                    foreach (string cmds in globalCommands.Keys)
                        Print($"  {globalCommands[cmds].help}");
                    Print("  -- End --");
                    n.Debug($"Commands in {n.fullPath}:");
                    foreach (string cmds in n.commands.Keys)
                        Print($"  {n.commands[cmds].help}");
                    Print("  -- End --");
                },
                description = "List all commands in current directory."
            });

            AddGlobalCommand("bottom", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    // TODO:: several bits use the below code snippet => convert to utility function
                    int minLogLength = int.MaxValue;
                    foreach (LogHistory history in logHistory.Values) if (history.logs.Count < minLogLength) minLogLength = history.logs.Count;
                    historyIndex = minLogLength - 1;
                },
                description = "Scroll to bottom of chat."
            });

            root.AddCommand("Chat/", null);
            root.AddCommand("Chat/post", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    PlayerChatManager.WantToSentTextMessage(Player.PlayerManager.GetLocalPlayerAgent(), Color("#f00", args[0]));
                },
                description = "Post a chat message in red.",
                syntax = "<message>"
            });
            root.AddCommand("Chat/mute", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    channelFilters.Add(args[0]);
                },
                description = "Mute a chat channel.",
                syntax = "<channel>"
            });
            root.AddCommand("Chat/unmute", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    if (channelFilters.Contains(args[0]))
                        channelFilters.Remove(args[0]);
                    else
                        n.Error($"Channel \"{args[0]}\" does not exist");
                },
                description = "Unmute a chat channel.",
                syntax = "<channel>"
            });
            root.AddCommand("Chat/filters", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    n.Debug($"Channel filters:");
                    foreach (string filters in channelFilters)
                        Print($"  {filters}");
                    Print("  -- End --");
                },
                description = "Shows all channel filters."
            });

            root.AddCommand("ChatConsole/", null);
            root.AddCommand("ChatConsole/AutoExitChat", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    if (args.Length == 0)
                    {
                        n.Debug(cmd.help);
                        return;
                    }
                    int value;
                    if (int.TryParse(args[0], out value))
                    {
                        if (value != 0 && value != 1)
                        {
                            n.Debug(cmd.help);
                            return;
                        }
                        ConfigManager.AutoExitChat = value == 1;
                        n.Debug($"AutoExitChat set to {(ConfigManager.AutoExitChat ? "True" : "False")}");
                    }
                    else
                    {
                        n.Debug(cmd.help);
                        return;
                    }
                },
                description = "1 for enable, 0 for disable",
                syntax = "<value>"
            });
            root.AddCommand("ChatConsole/PrintExceptions", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    if (args.Length == 0)
                    {
                        n.Debug(cmd.help);
                        return;
                    }
                    int value;
                    if (int.TryParse(args[0], out value))
                    {
                        if (value != 0 && value != 1)
                        {
                            n.Debug(cmd.help);
                            return;
                        }
                        ConfigManager.PrintExceptions = value == 1;
                        n.Debug($"PrintExceptions set to {(ConfigManager.PrintExceptions ? "True" : "False")}");
                    }
                    else
                    {
                        n.Debug(cmd.help);
                        return;
                    }
                },
                description = "PrintExceptions <value>, 1 for enable, 0 for disable"
            });

            // SUSSY CODE => some check for minimum time seems to be in place to prevent cheating, thats why we set progression time
            root.AddCommand("Cheats/");
            root.AddCommand("Cheats/completeExpedition", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    // TODO:: add a check if you are actually in a level

                    // Test if ChangeState works when not host.
                    if (SNetwork.SNet.IsMaster)
                    {
                        Clock.ExpeditionProgressionTime = 10000;
                        WardenObjectiveManager.ForceCompleteObjective(LevelGeneration.LG_LayerType.MainLayer);
                        WardenObjectiveManager.ForceCompleteObjective(LevelGeneration.LG_LayerType.SecondaryLayer);
                        WardenObjectiveManager.ForceCompleteObjective(LevelGeneration.LG_LayerType.ThirdLayer);
                        GameStateManager.ChangeState(eGameStateName.ExpeditionSuccess);
                    }
                    else n.Error($"You need to be host in order to execute this command.");
                }
            });
        }

        private Harmony? harmony;
    }
}