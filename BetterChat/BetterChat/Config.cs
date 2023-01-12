using BepInEx.Configuration;
using BepInEx;

namespace BetterChat
{
    public static class ConfigManager
    {
        static ConfigManager()
        {
            string text = Path.Combine(Paths.ConfigPath, $"{Module.Name}.cfg");
            ConfigFile configFile = new ConfigFile(text, true);

            debug = configFile.Bind(
                "Debug",
                "enable",
                false,
                "Enables debug messages when true.");

            autoExitChat = configFile.Bind(
                "Settings",
                "autoExitChat",
                true,
                "Exit chat after entering a command.");

            printExceptions = configFile.Bind(
                "Settings",
                "printExceptions",
                false,
                "Print code exceptions to chat.");
        }

        public static bool Debug
        {
            get { return debug.Value; }
            set { debug.Value = value; }
        }
        public static bool AutoExitChat
        {
            get { return autoExitChat.Value; }
            set { autoExitChat.Value = value; }
        }
        public static bool PrintExceptions
        {
            get { return printExceptions.Value; }
            set { printExceptions.Value = value; }
        }

        private static ConfigEntry<bool> debug;
        private static ConfigEntry<bool> autoExitChat;
        private static ConfigEntry<bool> printExceptions;
    }
}