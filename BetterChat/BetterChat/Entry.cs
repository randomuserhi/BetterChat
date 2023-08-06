#define CHEATS

using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

using API;
using static BetterChat.ChatLogger;
using SNetwork;
using BetterChat.Patches;
using Player;
using Agents;
using GameData;

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
            APILogger.Debug(Module.Name, $"Loaded {Module.Name} {Module.Version}");
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
                    int lookup = -1;
                    switch (args[0])
                    {
                        case "pink": 
                        case "red":
                            lookup = 0;
                            break;
                        case "green":
                            lookup = 1;
                            break;
                        case "blue":
                            lookup = 2;
                            break;
                        case "purple":
                            lookup = 3;
                            break;
                        default:
                            if (args[0] == string.Empty)
                            {
                                n.Error("This channel is invalid.");
                                return;
                            }
                            else
                                channelFilters.Add(args[0]);
                            return;
                    }
                    if (lookup != -1)
                    {
                        bool found = false;
                        for (int i = 0; i < SNet.Slots.SlottedPlayers.Count; i++)
                        {
                            SNet_Player player = SNet.Slots.SlottedPlayers[i];
                            //n.Debug($"color: {player.PlayerColor} > {player.PlayerColor.r} {player.PlayerColor.g} {player.PlayerColor.b}");
                            if (Player.PlayerManager.GetStaticPlayerColor(lookup) == player.PlayerColor)
                            {
                                if (player.IsBot)
                                {
                                    n.Error("This player is a bot.");
                                    return;
                                }
                                else
                                {
                                    found = true;
                                    n.Debug($"Muted {player.GetName()}.");
                                    channelFilters.Add(player.GetName());
                                }
                            }
                        }
                        if (found == false)
                            n.Error($"Unable to find player slotted in colour {args[0]}.");
                    }
                },
                description = "Mute a chat channel.",
                syntax = "<channel>"
            });
            root.AddCommand("Chat/unmute", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    int lookup = -1;
                    switch (args[0])
                    {
                        case "pink":
                        case "red":
                            lookup = 0;
                            break;
                        case "green":
                            lookup = 1;
                            break;
                        case "blue":
                            lookup = 2;
                            break;
                        case "purple":
                            lookup = 3;
                            break;
                        default:
                            if (args[0] == string.Empty)
                            {
                                n.Error("This channel is invalid.");
                                return;
                            }
                            else
                                channelFilters.Remove(args[0]);
                            return;
                    }
                    if (lookup != -1)
                    {
                        bool found = false;
                        for (int i = 0; i < SNet.Slots.SlottedPlayers.Count; i++)
                        {
                            SNet_Player player = SNet.Slots.SlottedPlayers[i];
                            //n.Debug($"color: {player.PlayerColor} > {player.PlayerColor.r} {player.PlayerColor.g} {player.PlayerColor.b}");
                            if (Player.PlayerManager.GetStaticPlayerColor(lookup) == player.PlayerColor)
                            {
                                if (player.IsBot)
                                {
                                    n.Error("This player is a bot.");
                                    return;
                                }
                                else
                                {
                                    found = true;
                                    n.Debug($"Unmuted {player.GetName()}.");
                                    channelFilters.Remove(player.GetName());
                                }
                            }
                        }
                        if (found == false)
                            n.Error($"Unable to find player slotted in colour {args[0]}.");
                    }
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
            root.AddCommand("Chat/jumble", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    jumble = !jumble;
                },
                description = "Jumbles the chat :)"
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

            root.AddCommand("Stamina/");
            root.AddCommand("Stamina/rest", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
                    PlayerStamina stamina = player.Stamina;
                    n.Debug($"StaminaTimeBeforeResting: {stamina.PlayerData.StaminaTimeBeforeResting}");
                }
            });
            root.AddCommand("Stamina/eg", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    PlayerStamina.EnableStaminaDebugGraph = true;
                    n.Debug($"Enabled Stamina graph");
                }
            });
            root.AddCommand("Stamina/dg", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    PlayerStamina.EnableStaminaDebugGraph = false;
                    n.Debug($"Disabled Stamina graph");
                }
            });

#if CHEATS
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
            root.AddCommand("Cheats/godMode", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    Cheats.godMode = !Cheats.godMode;
                    string state = Cheats.godMode ? "active" : "deactivated";
                    n.Debug($"God mode is now {state}");
                    if (!SNet.IsMaster) n.Warn($"For clients, god mode is simply increased regeneration. If you take more damage than you can regenerate you will die.");
                }
            });
            root.AddCommand("Cheats/aimPunch", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    Cheats.aimPunch = !Cheats.aimPunch;
                    string state = Cheats.aimPunch ? "active" : "deactivated";
                    n.Debug($"Aim punch is now {state}");
                }
            });
            root.AddCommand("Cheats/revive", new Command()
            {
                action = (CmdNode n, Command cmd, string[] args) =>
                {
                    PlayerAgent? player = null;
                    if (args.Length == 0) player = PlayerManager.GetLocalPlayerAgent();
                    else if (args.Length == 1)
                    {
                        if (int.TryParse(args[0], out int value))
                        {
                            value -= 1;
                            if (value < 0 || value > 3)
                            {
                                n.Error("Slot value can only be 1-4.");
                                return;
                            }
                            player = PlayerManager.PlayerAgentsInLevel[value];
                        }
                    }
                    else
                    {
                        n.Debug(cmd.help);
                        return;
                    }
                    if (player != null && PlayerManager.GetLocalPlayerAgent() != null)
                    {
                        if (player.Alive)
                        {
                            n.Error("Player is alive.");
                            return;
                        }
                        AgentReplicatedActions.PlayerReviveAction(player, PlayerManager.GetLocalPlayerAgent(), player.transform.position);
                    }
                    else n.Error("Player does not exist in slot.");
                },
                description = "revive <slot>, if no slot is provided, revives self"
            });
#endif

        }

        private Harmony? harmony;
    }
}