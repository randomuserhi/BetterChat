using System.Text;
using System.Runtime.CompilerServices;

using UnityEngine;
using HarmonyLib;

using API;

namespace BetterChat
{
    // TODO:: Clean up and seperate code => really messy rn
    // TODO:: Implement colors and stuff etc...
    // TODO:: Make plugin external => so my plugins can import it instead of having a copy of it on each one https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html#specifying-dependencies-on-other-plugins
    // TODO:: Tab auto complete on commands and command paths
    // TODO:: Command Help support
    // TODO:: Keep a permanent chat log so you can view entire log history including chat history
    //     :: Will need to patch into PUI_GameEvent AddLogItem to record the items being added including revive messages
    // TODO:: Chat options like filtering out revive messages
    //     :: Maybe will need to patch PUI_GameEventLog.ShowAndUpdateItemPositions() and modify the m_logItems list to delete list items
    // TODO:: Ability to up and down arrow previous messages to type again (simulate console behaviour)
    //     :: Look at PUI_GameEventLog.Setup() => PlayerChatManager.OnChatInputStringUpdated()
    //     :: Look at PlayerChatManager.UpdateMessage() => PlayerChatManager.UpdateTextChatInput() => m_currentValue
    //     :: Certain commands like scroll dont clear the input text so u can continuously enter them: .scroll+ and .scroll- to scroll up/down through chat history etc...
    //     :: => Can make cntrl + up arrow / just up arrow scroll up or something so you don't need to type .scroll kekw
    //     :: Maybe patch PlayerChatManager.EnterChatMode and PlayerChatManager.ExitChatMode to have it so when you enter chat it shows scrolled chat, and when u exit it shows new chat
    //     :: You can enter chat and then type .scroll reset to reset the scroll back to normal etc... (Just make it intuitive)
    // TODO:: Ability to cntrl left and right arrow etc => basic text editor control in chat
    //     :: Work out how caret works (from the looks of it, it doesn't kekw => will have to write my own implementation or something) => Probably downside to using UnityEngine.Text
    //     :: Sus implementation due to https://stackoverflow.com/questions/6792812/the-backspace-escape-character-b-unexpected-behavior => PlayerChatManager.UpdateTextChatInput()
    //     :: https://docs.unity3d.com/ScriptReference/Input-inputString.html
    //     :: Look at PUI_GameEventLog.UpdateInputString()
    //     :: Look at PUI_GameEventLog.Setup() => PlayerChatManager.OnChatInputStringUpdated()
    //     :: Look at PlayerChatManager.UpdateMessage() => PlayerChatManager.UpdateTextChatInput() => m_currentValue
    // TODO:: Someway to bypass PlayerChatManager.m_maxlen that doesn't look shit
    // TODO:: Make an API for chat commands with prefix
    //        => ".CombatIndicator enable true" => Call some instruction "enable" with parameter "true" from combat indicator
    // TODO:: Implement a context system [root] allows u access to all commands => ".CombatIndicator/Enable"
    //     :: Typing just "." just shows you what context you are in
    //     :: When you write a command it goes "[root]: {msg}" if in root or "[CombatIndicator]: {msg}" if in combat indicator plugin
    //     :: Each plugin has its own context to prevent naming conflicts
    // TODO:: Handle multiline text => mainly when printing long messages or dealing with long input
    // TODO:: Stop console from clearing at end of expedition
    // TODO:: Allow illegal characters
    // TODO:: Allow infinite length input
    // TODO:: Send infinite length input (split it as if you were sending multiple messages manually)
    // TODO:: Handle composition strings => https://docs.unity3d.com/ScriptReference/Input-compositionString.html
    // TODO:: Fix bug with trying to type "o" in map chat where it pulls up objective screen

    [HarmonyPatch]
    public static class ChatLogger
    {
        // Utility Functions

        // hex needs to include #, e.g #fff
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Color(string hex, object data)
        {
            return $"<color={hex}>{data}</color>";
        }

        private static IEnumerable<string> SplitArgs(string commandLine)
        {
            var result = new StringBuilder();

            var quoted = false;
            var escaped = false;
            var started = false;
            var allowcaret = false;
            for (int i = 0; i < commandLine.Length; i++)
            {
                var chr = commandLine[i];

                if (chr == '^' && !quoted)
                {
                    if (allowcaret)
                    {
                        result.Append(chr);
                        started = true;
                        escaped = false;
                        allowcaret = false;
                    }
                    else if (i + 1 < commandLine.Length && commandLine[i + 1] == '^')
                    {
                        allowcaret = true;
                    }
                    else if (i + 1 == commandLine.Length)
                    {
                        result.Append(chr);
                        started = true;
                        escaped = false;
                    }
                }
                else if (escaped)
                {
                    result.Append(chr);
                    started = true;
                    escaped = false;
                }
                else if (chr == '"')
                {
                    quoted = !quoted;
                    started = true;
                }
                else if (chr == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    escaped = true;
                }
                else if (chr == ' ' && !quoted)
                {
                    if (started) yield return result.ToString();
                    result.Clear();
                    started = false;
                }
                else
                {
                    result.Append(chr);
                    started = true;
                }
            }

            if (started) yield return result.ToString();
        }

        private static PUI_GameEventLog_Item? _parser = null;
        private static GameObject? logItemPrefab = null;
        public static string GetTMPString(string raw)
        {
            if (logItemPrefab == null)
            {
                APILogger.Debug(Module.Name, $"Parser object was not set yet.");
                return "";
            }

            if (_parser == null)
                _parser = GOUtil.SpawnChildAndGetComp<PUI_GameEventLog_Item>(logItemPrefab);

            _parser.gameObject.active = false;
            _parser.m_text.text = raw;
            _parser.m_text.ForceMeshUpdate(true);
            return _parser.m_text.GetParsedText();
        }

        // Main Functionality

        public class Command
        {
            public string help { get => $"{Color("#0f0", $"{name} {syntax}")}: {description}"; }
            public string name { get; private set; } = "";
            public string description = "";
            public string syntax = "";
            public Action<CmdNode, Command, string[]>? action { get; set; }
            public void SetName(string name) => this.name = name;
        }

        // TODO:: proper path parsing => rn its really shit accepting some strange paths
        // TODO:: Change APILogger to use Module.Name when I move this code into a seperate Plugin dll
        public class CmdNode
        {
            public CmdNode(CmdNode? parent = null, string name = "root", string fullPath = "root")
            {
                this.parent = parent;
                this.name = name;
                this.fullPath = fullPath;
            }

            public CmdNode? parent;
            public string name;
            public string fullPath;
            public Dictionary<string, CmdNode> path = new Dictionary<string, CmdNode>();
            public Dictionary<string, Command> commands = new Dictionary<string, Command>();

            // TODO:: option to show full path by default instead of name
            public void Debug(object data) => ChatLogger.Debug(name, data);
            public void Warn(object data) => ChatLogger.Warn(fullPath, data);
            public void Error(object data) => ChatLogger.Error(fullPath, data);

            private static readonly System.Text.RegularExpressions.Regex _validator = new System.Text.RegularExpressions.Regex(@"^[.]+$");
            public bool AddCommand(string fullPath, Command? command = null, string[]? cmd = null)
            {
                if (cmd == null) cmd = fullPath.Split("/");
                if (cmd.Length == 0) APILogger.Error(Module.Name, $"Failed to parse command path.");

                string p = cmd[0];
                string[] tail = new string[cmd.Length - 1];
                Array.Copy(cmd, 1, tail, 0, cmd.Length - 1);
                if (p != string.Empty && _validator.IsMatch(p))
                {
                    CmdNode root = this;
                    for (int i = 1; i < p.Length; ++i)
                    {
                        if (root.parent == null)
                        {
                            APILogger.Error(Module.Name, $"{fullPath} traverses to null parent.");
                            return false;
                        }
                        root = root.parent;
                    }
                    return root.AddCommand(fullPath, command, tail);
                }
                else if (cmd.Length != 1)
                {
                    if (p == string.Empty)
                    {
                        APILogger.Error(Module.Name, $"Unable to parse {fullPath}.");
                        return false;
                    }

                    if (!path.ContainsKey(p))
                    {
                        StringBuilder fp = new StringBuilder();
                        for (CmdNode? n = this; n != null; n = n.parent) fp.Insert(0, $"{n.name}/");
                        fp.Append(p);
                        path.Add(p, new CmdNode(this, p, fp.ToString()));
                    }
                    return path[p].AddCommand(fullPath, command, tail);
                }
                else
                {
                    if (p == string.Empty)
                    {
                        APILogger.Warn(Module.Name, $"Unable to add command for {fullPath} as no command was provided. If this was intentional, then ignore this warning.");
                        return true;
                    }

                    if (globalCommands.ContainsKey(p))
                    {
                        APILogger.Error(Module.Name, $"Unable to add command for {fullPath} as it already exists.");
                        return false;
                    }

                    if (commands.ContainsKey(p))
                    {
                        APILogger.Error(Module.Name, $"Unable to add command for {fullPath} as it already exists.");
                        return false;
                    }
                    else
                    {
                        if (command == null || command.action == null)
                        {
                            APILogger.Debug(Module.Name, $"Unable to add command for {fullPath} as it is null.");
                            return false;
                        }
                        if (ConfigManager.Debug) APILogger.Debug(Module.Name, $"Added {fullPath}.");
                        command.SetName(p);
                        commands.Add(p, command);
                        return true;
                    }
                }
            }

            public void Execute(string fullPath, string[] args, string[]? cmd = null)
            {
                if (cmd == null) cmd = fullPath.Split("/");
                if (cmd.Length == 0) Error($"Failed to parse command path.");

                string p = cmd[0];
                if (p == string.Empty)
                {
                    Error($"No command was provided.");
                    return;
                }
                string[] tail = new string[cmd.Length - 1];
                Array.Copy(cmd, 1, tail, 0, cmd.Length - 1);
                if (_validator.IsMatch(p))
                {
                    CmdNode root = this;
                    for (int i = 1; i < p.Length; ++i)
                    {
                        if (root.parent == null)
                        {
                            Error($"Command \"{fullPath}\" does not exist.");
                            return;
                        }
                        root = root.parent;
                    }
                    root.Execute(fullPath, args, tail);
                }
                else if (cmd.Length != 1)
                {
                    if (path.ContainsKey(p)) path[p].Execute(fullPath, args, tail);
                    else Error($"Command \"{fullPath}\" does not exist.");
                }
                else
                {
                    if (commands.ContainsKey(p))
                    {
                        try
                        {
                            if (commands[p] != null)
                                commands[p].action?.Invoke(this, commands[p], args);
                            else
                                Error("Command was set to null.");
                        }
                        catch (Exception e)
                        {
                            Error("An unexpected error occured.");
                            APILogger.Error(Module.Name, e);
                            if (ConfigManager.PrintExceptions) Print($"{e}", true);
                        }
                    }
                    else Error($"Command \"{p}\" does not exist.");
                }
            }

            public CmdNode? GetNode(string fullPath, string[]? cmd = null)
            {
                if (cmd == null) cmd = fullPath.Split("/");

                if (cmd.Length == 0) return this;
                string p = cmd[0];
                if (p == string.Empty)
                {
                    if (cmd.Length == 1) return this;
                    else
                    {
                        Error($"Path \"{fullPath}\" does not exist.");
                        return null;
                    }
                }
                string[] tail = new string[cmd.Length - 1];
                Array.Copy(cmd, 1, tail, 0, cmd.Length - 1);
                if (_validator.IsMatch(p))
                {
                    CmdNode root = this;
                    for (int i = 1; i < p.Length; ++i)
                    {
                        if (root.parent == null)
                        {
                            Error($"Path \"{fullPath}\" does not exist.");
                            return null;
                        }
                        root = root.parent;
                    }
                    return root.GetNode(fullPath, tail);
                }
                else if (cmd.Length != 0)
                {
                    if (path.ContainsKey(p)) return path[p].GetNode(fullPath, tail);
                    else
                    {
                        Error($"Path \"{fullPath}\" does not exist.");
                        return null;
                    }
                }
                else return this;
            }
        }
        
        private static void ExecuteGlobalCommand(string cmd, string[] args)
        {
            if (cmd == string.Empty)
            {
                current.Error($"No command was provided.");
                return;
            }

            if (globalCommands.ContainsKey(cmd))
            {
                try
                {
                    if (globalCommands[cmd] != null)
                        globalCommands[cmd].action?.Invoke(current, globalCommands[cmd], args);
                    else 
                        current.Error("Command was set to null.");
                }
                catch (Exception e)
                {
                    current.Error("An unexpected error occured.");
                    APILogger.Error(Module.Name, e);
                    if (ConfigManager.PrintExceptions) Print($"{e}", true);
                }
            }
            else current.Error($"Command \"{cmd}\" does not exist.");
        }
        public static bool AddGlobalCommand(string cmd, Command command)
        {
            if (cmd == string.Empty)
            {
                APILogger.Warn(Module.Name, $"Unable to add global command as no command was provided.");
                return true;
            }

            if (globalCommands.ContainsKey(cmd))
            {
                APILogger.Error(Module.Name, $"Unable to add command {cmd} as it already exists.");
                return false;
            }
            else
            {
                if (ConfigManager.Debug) APILogger.Debug(Module.Name, $"Added {cmd}.");
                command.SetName(cmd);
                globalCommands.Add(cmd, command);
                return true;
            }
        }

        public static string prefix = ".";
        public static Dictionary<string, Command> globalCommands = new Dictionary<string, Command>();
        public static readonly CmdNode root = new CmdNode(); // TODO:: make root private and create AddCommand and AddDirectory functions
        public static CmdNode current = root;

        // TODO:: cleanup and move dictionary into LogHistory class
        public class LogHistory
        {
            public struct LogItem
            {
                public enum Type
                {
                    Internal,
                    GTFO
                }

                public string log;
                public Type internalType;
                public eGameEventChatLogType type;
            }

            public List<LogItem> logs = new List<LogItem>();
        }
        public static float smoothHistoryIndex = 0;
        public static int historyIndex { get => Mathf.RoundToInt(smoothHistoryIndex); set => smoothHistoryIndex = value; }
        public static float _scrollDir = 0;
        // TODO:: add scroll speeds to config file
        public const float historyScrollDelay = 0.2f;
        public static float historyScrollDelayTimer = 0;
        public static float discreteScrollSpeed = 15f; // lines per second
        public static float continuousScrollSpeed = 1f;
        public static Dictionary<int, LogHistory> logHistory = new Dictionary<int, LogHistory>();

        // Log History
        [HarmonyPatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.AddLogItem))]
        [HarmonyPrefix]
        public static bool AddLogItem_Prefix(PUI_GameEventLog __instance, string log, eGameEventChatLogType type)
        {
            // Scroll if at bottom
            int minLogLength = int.MaxValue;
            foreach (LogHistory history in logHistory.Values) if (history.logs.Count < minLogLength) minLogLength = history.logs.Count;
            if (historyIndex == minLogLength - 1) historyIndex++;

            if (!logHistory.ContainsKey(__instance.GetInstanceID())) logHistory.Add(__instance.GetInstanceID(), new LogHistory());
            logHistory[__instance.GetInstanceID()].logs.Add(new LogHistory.LogItem() { log = log, type = type, internalType = LogHistory.LogItem.Type.GTFO });

            return false;
        }

        private static void AddLogItem(string log, eGameEventChatLogType type)
        {
            // Scroll if at bottom
            int minLogLength = int.MaxValue;
            foreach (LogHistory history in logHistory.Values) if (history.logs.Count < minLogLength) minLogLength = history.logs.Count;
            if (historyIndex == minLogLength - 1) historyIndex++;
            
            foreach (LogHistory history in logHistory.Values) history.logs.Add(new LogHistory.LogItem() { log = log, type = type, internalType = LogHistory.LogItem.Type.Internal });
        }

        public static HashSet<string> channelFilters = new HashSet<string>();
        private static bool ValidateLog(LogHistory.LogItem item)
        {
            string parse = GetTMPString(item.log);
            foreach (string channel in channelFilters)
            {
                if (item.internalType != LogHistory.LogItem.Type.Internal)
                {
                    if (parse.StartsWith($"> > {channel}")) return false;
                }
            }
            return true;
        }

        // TODO:: somehow add functionality to make this work without this code here (basically make it general and external)
        public static bool jumble = false;
        private static string PreProcess(string raw, int seed)
        {
            if (!jumble) return raw;

            UnityEngine.Random.seed = seed;

            StringBuilder sb = new StringBuilder();
            bool rot = true;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '<') rot = false;
                else if (raw[i] == '>') rot = true;
                else if (rot) sb.Append($"<rotate={UnityEngine.Random.RandomRangeInt(-180, 180)}>{raw[i]}</rotate>");
            }
            return sb.ToString();
        }

        [HarmonyPatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.ShowAndUpdateItemPositions))]
        [HarmonyPrefix]
        public static bool ShowAndUpdateItemPositions_Prefix(PUI_GameEventLog __instance)
        {
            int numLines = __instance.m_limitNRLines;
            while (__instance.m_logItems.Count < numLines)
            {
                PUI_GameEventLog_Item pUI_GameEventLog_Item = GOUtil.SpawnChildAndGetComp<PUI_GameEventLog_Item>(__instance.m_logItemPrefab, __instance.m_itemAlign);
                pUI_GameEventLog_Item.Setup();
                pUI_GameEventLog_Item.SetText(string.Empty);
                //pUI_GameEventLog_Item.SetType(type);
                __instance.m_logItems.Add(pUI_GameEventLog_Item);
            }

            // Position them properly
            for (int i = 0; i < __instance.m_logItems.Count; i++)
            {
                __instance.m_logItems[i].transform.localPosition = new Vector3(0f, i * __instance.m_logItemHeight);
            }

            if (!logHistory.ContainsKey(__instance.GetInstanceID())) logHistory.Add(__instance.GetInstanceID(), new LogHistory());
            LogHistory history = logHistory[__instance.GetInstanceID()];
            if (PlayerChatManager.InChatMode)
            {
                for (int i = 0, logItem = 0; i < __instance.m_logItems.Count; i++)
                {
                    int target = historyIndex - i;

                    if (target >= 0 && target < history.logs.Count)
                    {
                        if (ValidateLog(history.logs[target]))
                        {
                            __instance.m_logItems[logItem].SetText(PreProcess(history.logs[target].log, logItem));
                            __instance.m_logItems[logItem].SetType(history.logs[target].type);
                            logItem++;
                        }
                    }
                    else
                    {
                        __instance.m_logItems[logItem++].SetText(string.Empty);
                    }
                }
            }
            else
            {
                for (int i = 0, logItem = 0; i < __instance.m_logItems.Count; i++)
                {
                    int target = history.logs.Count - 1 - i;

                    if (target >= 0 && target < history.logs.Count)
                    {
                        if (ValidateLog(history.logs[target]))
                        {
                            __instance.m_logItems[logItem].SetText(PreProcess(history.logs[target].log, logItem));
                            __instance.m_logItems[logItem].SetType(history.logs[target].type);
                            logItem++;
                        }
                    }
                    else
                    {
                        __instance.m_logItems[logItem++].SetText(string.Empty);
                    }
                }
            }

            return false;
        }

        [HarmonyPatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.Update))]
        [HarmonyPostfix]
        public static void PUI_GameEventLog_Update_Postfix(PUI_GameEventLog __instance)
        {
            __instance.ShowAndUpdateItemPositions();
        }

        // TODO:: allow long multi line messages etc...
        //     :: Handle text mesh pro properly
        [HarmonyPatch(typeof(PlayerChatManager), nameof(PlayerChatManager.UpdateTextChatInput))]
        [HarmonyPrefix]
        public static bool UpdateTextChatInput_Prefix(PlayerChatManager __instance)
        {
            if (!PlayerChatManager.TextChatInputEnabled)
            {
                return false;
            }
            if (PlayerChatManager.Current != null && !PlayerChatManager.InChatMode && InputMapper.GetButtonDown.Invoke(InputAction.TextChatOpenSend))
            {
                PlayerChatManager.Log("Open chat window");
                __instance.EnterChatMode();
            }
            else
            {
                if (!(PlayerChatManager.Current != null) || !PlayerChatManager.InChatMode)
                {
                    return false;
                }
                if (InputMapper.GetButtonDown.Invoke(InputAction.TextChatClose))
                {
                    __instance.ExitChatMode();
                    return false;
                }

                // Manage input:
                string inputString = Input.inputString;
                for (int i = 0; i < inputString.Length; i++)
                {
                    char c = inputString[i];
                    /*bool flag = false;
                    for (int j = 0; j < __instance.m_forbiddenChars.Length; j++)
                    {
                        if (c.Equals((char)__instance.m_forbiddenChars[j]))
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag)
                    {
                        continue;
                    }*/
                    if (c == "\b"[0])
                    {
                        if (__instance.m_currentValue.Length > 0)
                        {
                            __instance.m_currentValue = __instance.m_currentValue.Substring(0, __instance.m_currentValue.Length - 1);
                        }
                    }
                    else if (c == "\n"[0] || c == "\r"[0])
                    {
                        __instance.PostMessage();
                    }
                    else// if (__instance.m_currentValue.Length < __instance.m_maxLen)
                    {
                        __instance.m_currentValue += c;
                    }
                }
                __instance.m_currentImeCompositionString = Input.compositionString;
                __instance.UpdateMessage();

                // Allow scrolling of chat
                float menuVertical = InputMapper.GetAxis.Invoke(InputAction.MenuMoveVertical);
                if (menuVertical != 0)
                {
                    foreach (PUI_GameEventLog log in chatlogs) log.ResetHideTimer();

                    if (_scrollDir == 0) _scrollDir = menuVertical;
                    if (Mathf.Sign(_scrollDir) != Mathf.Sign(menuVertical))
                    {
                        historyScrollDelayTimer = 0;
                        _scrollDir = menuVertical;
                    }

                    if (historyScrollDelayTimer == 0)
                    {
                        if (menuVertical > 0) historyIndex--;
                        else historyIndex++;
                    }

                    if (historyScrollDelayTimer < historyScrollDelay) historyScrollDelayTimer += Time.deltaTime;
                    else smoothHistoryIndex -= discreteScrollSpeed * menuVertical * Time.deltaTime;
                }
                else
                {
                    historyScrollDelayTimer = 0;
                    _scrollDir = 0;

                    // Scroll wheel
                    float menuScroll = InputMapper.GetAxis.Invoke(InputAction.MenuScroll);
                    if (menuScroll != 0)
                    {
                        smoothHistoryIndex -= continuousScrollSpeed * menuScroll * 1000f * Time.deltaTime;
                        foreach(PUI_GameEventLog log in chatlogs) log.ResetHideTimer();
                    }
                    else smoothHistoryIndex = historyIndex;
                }

                //Bound smoothHistoryIndex
                int minLogLength = int.MaxValue;
                foreach(LogHistory history in logHistory.Values) if (history.logs.Count < minLogLength) minLogLength = history.logs.Count;
                if (smoothHistoryIndex < 0) smoothHistoryIndex = 0;
                else if (smoothHistoryIndex > minLogLength - 1) smoothHistoryIndex = minLogLength - 1;
            }

            return false;
        }


        [HarmonyPatch(typeof(PlayerChatManager), nameof(PlayerChatManager.PostMessage))]
        [HarmonyPrefix]
        public static bool PostMessage_Prefix(PlayerChatManager __instance)
        {
            string msg = __instance.m_currentValue;
            __instance.m_currentValue = prefix;
            if (ConfigManager.Debug) APILogger.Debug(Module.Name, $"Want to post message {msg}");
            if (msg.StartsWith(prefix))
            {
                string trim = msg[prefix.Length..].Trim();

                if (trim == string.Empty) current.Debug($"{current.fullPath}");
                else
                {
                    current.Debug(Color("#aaa", msg));
                    try
                    {
                        string[] args = SplitArgs(trim).ToArray();

                        if (globalCommands.ContainsKey(args[0])) globalCommands[args[0]].action?.Invoke(current, globalCommands[args[0]], args[1..]);
                        else
                        {
                            string[] drive = args[0].Split(":");
                            if (drive.Length > 2) Error(Module.Name, $"Unable to parse path \"{args[0]}\".");
                            else if (drive.Length == 2)
                            {
                                if (drive[0] == "root" || drive[0] == "r")
                                {
                                    root.Execute(drive[1], args[1..]);
                                }
                                else Error(Module.Name, $"Drive \"{drive[0]}\" does not exist.");
                            }
                            else current.Execute(args[0], args[1..]);
                        }
                    }
                    catch (Exception e)
                    {
                        Error(Module.Name, "An unexpected error occured.");
                        APILogger.Error(Module.Name, e);
                        if (ConfigManager.PrintExceptions) Print($"{e}", true);
                    }
                }

                if (ConfigManager.AutoExitChat)
                {
                    __instance.m_currentValue = string.Empty;
                    __instance.ExitChatMode();
                }
                return false;
            }
            __instance.m_currentValue = msg; // Reset message if it is not a command to follow through and send as normal.
            return true;
        }

        // List of all chatlogs to write to
        private static List<PUI_GameEventLog> chatlogs = new List<PUI_GameEventLog>();

        // Grab all chat logs in game to write to
        [HarmonyPatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.Setup))]
        [HarmonyPostfix]
        public static void PUI_GameEventLog_Setup(PUI_GameEventLog __instance)
        {
            logItemPrefab = __instance.m_logItemPrefab;
            chatlogs.Add(__instance);
        }

        // TODO:: Adapt to use https://stackoverflow.com/questions/54100688/would-like-to-count-the-characters-in-a-string-excluding-rich-text-formatting
        // TODO:: Fix cases where text on new line wont have color if color tag is left behind on previous line
        public static void Print(string data, bool raw = false)
        {
            if (data == string.Empty)
            {
                AddLogItem($"", eGameEventChatLogType.IncomingChat);
                return;
            }

            const int chunk = 60;
            string gap = string.Empty;
            StringBuilder sb = new StringBuilder();
            bool counting = true;
            bool countIndent = true;
            bool escaped = false;
            int indent = 0;
            for (int i = 0, count = 0; i < data.Length; i++)
            {
                char c = data[i];
                sb.Append(c);

                if (c != ' ') countIndent = false;
                else if (countIndent) ++indent;

                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                
                if (!raw && !escaped && c == '<') counting = false;
                else if (!raw && !escaped && c == '>') counting = true;
                else if (c == '\n')
                {
                    AddLogItem($"{gap}{sb}", eGameEventChatLogType.IncomingChat);

                    if (gap == string.Empty)
                    {
                        for (int j = 0; j < indent + 2; j++) gap += " ";
                    }

                    sb.Clear();
                    count = 0;
                }
                else if (counting)
                {
                    ++count;
                    if (count >= chunk)
                    {
                        AddLogItem($"{gap}{sb}", eGameEventChatLogType.IncomingChat);

                        if (gap == string.Empty)
                        {
                            for (int j = 0; j < indent + 2; j++) gap += " ";
                        }

                        sb.Clear();
                        count = 0;
                    }
                }
            }
            if (sb.Length != 0) AddLogItem($"{gap}{sb}", eGameEventChatLogType.IncomingChat);
        }

        public static void Debug(string module, object data)
        {
            string log = $"{Color("#fc6203", $"[{module}]")}: {data}";
            Print(log);
        }
        public static void Warn(string module, object data)
        {
            string log = $"{Color("#f5f10a", $"[WARNING:{module}]")}: {data}";
            Print(log);
        }
        public static void Error(string module, object data)
        {
            string log = $"{Color("#f00", $"[ERROR  :{module}]")}: {data}";
            Print(log);
        }
    }
}
