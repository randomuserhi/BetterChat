using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

using API;

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
        }

        private Harmony harmony;
    }
}