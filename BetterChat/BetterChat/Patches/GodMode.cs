﻿using HarmonyLib;
using Player;
using SNetwork;

namespace BetterChat.Patches {
    [HarmonyPatch]
    internal class Cheats {
        public static bool godMode = false;
        public static bool aimPunch = true;

        private static long lastHeal = 0;
        private static void SendFullHealth(Dam_PlayerDamageBase __instance) {
            if (SNet.IsMaster) return;
            long now = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
            if (now - lastHeal > 1000) {
                __instance.AddHealth(100, __instance.Owner);
                lastHeal = now;
            }
        }

        [HarmonyPatch(typeof(Dam_SyncedDamageBase), nameof(Dam_SyncedDamageBase.RegisterDamage))]
        [HarmonyPrefix]
        private static void Prefix_RegisterDamage(Dam_PlayerDamageBase __instance, ref float dam) {
            PlayerAgent? local = PlayerManager.GetLocalPlayerAgent();
            if (local != null &&
                __instance.GetInstanceID() == local.Damage.GetInstanceID()) {
                if (godMode) {
                    SendFullHealth(__instance);
                    dam = 0;
                }
            }
        }
        [HarmonyPatch(typeof(Dam_SyncedDamageBase), nameof(Dam_SyncedDamageBase.RegisterDamage))]
        [HarmonyPrefix]
        private static bool Prefix_RegisterDamage(Dam_PlayerDamageBase __instance, ref bool __result) {
            PlayerAgent? local = PlayerManager.GetLocalPlayerAgent();
            if (local != null &&
                __instance.GetInstanceID() == PlayerManager.GetLocalPlayerAgent().Damage.GetInstanceID()) {
                if (godMode) {
                    SendFullHealth(__instance);
                    __result = false;
                    return false;
                }
            }
            return true;
        }
        [HarmonyPatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.OnIncomingDamage))]
        [HarmonyPrefix]
        private static void Prefix_IncomingDamage(Dam_PlayerDamageBase __instance, ref float damage, ref float originalDamage) {
            PlayerAgent? local = PlayerManager.GetLocalPlayerAgent();
            if (local != null &&
                __instance.GetInstanceID() == PlayerManager.GetLocalPlayerAgent().Damage.GetInstanceID()) {
                if (godMode) {
                    damage = 0;
                    originalDamage = 0;
                }
            }
        }
        [HarmonyPatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.ReceiveSetHealth))]
        [HarmonyPrefix]
        private static bool ReceiveSetHealth(Dam_PlayerDamageBase __instance) {
            PlayerAgent? local = PlayerManager.GetLocalPlayerAgent();
            if (local != null &&
                __instance.GetInstanceID() == PlayerManager.GetLocalPlayerAgent().Damage.GetInstanceID()) {
                if (godMode) SendFullHealth(__instance);
                return !godMode;
            }
            return true;
        }
        [HarmonyPatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.ReceiveSetDead))]
        [HarmonyPrefix]
        private static bool ReceiveSetDead(Dam_PlayerDamageBase __instance) {
            PlayerAgent? local = PlayerManager.GetLocalPlayerAgent();
            if (local != null &&
                __instance.GetInstanceID() == PlayerManager.GetLocalPlayerAgent().Damage.GetInstanceID()) {
                return !godMode;
            }
            return true;
        }
        [HarmonyPatch(typeof(PlayerSync), nameof(PlayerSync.IncomingLocomotion))]
        [HarmonyPrefix]
        private static bool IncomingLocomotion(PlayerSync __instance, pPlayerLocomotion data) {
            PlayerAgent? local = PlayerManager.GetLocalPlayerAgent();
            if (local != null &&
                __instance.GetInstanceID() == PlayerManager.GetLocalPlayerAgent().Sync.GetInstanceID()) {
                if (data.State == PlayerLocomotion.PLOC_State.Downed)
                    return !godMode;
            }
            return true;
        }

        private static float m_punchAmountMax = 0;
        [HarmonyPatch(typeof(FPSCamera), nameof(FPSCamera.AddHitReact))]
        [HarmonyPrefix]
        public static void Prefix_FPSCamera_AddHitReact(FPSCamera __instance) {
            m_punchAmountMax = __instance.m_punchAmountMax;
            if (!aimPunch) __instance.m_punchAmountMax = 0;
        }
        [HarmonyPatch(typeof(FPSCamera), nameof(FPSCamera.AddHitReact))]
        [HarmonyPostfix]
        public static void Postfix_FPSCamera_AddHitReact(FPSCamera __instance) {
            __instance.m_punchAmountMax = m_punchAmountMax;
        }
    }
}
