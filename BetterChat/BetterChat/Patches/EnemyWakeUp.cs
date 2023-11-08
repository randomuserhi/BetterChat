using Agents;
using Enemies;
using HarmonyLib;
using UnityEngine;

namespace BetterChat.Patches {
    [HarmonyPatch]
    internal class EnemyWakeUp {
        public static bool enabled = true;

        [HarmonyPatch(typeof(EB_Hibernating), nameof(EB_Hibernating.UpdateDetection))]
        [HarmonyPrefix]
        private static bool Detection(EB_Hibernating __instance) {
            return enabled;
        }

        [HarmonyPatch(typeof(EB_Hibernating), nameof(EB_Hibernating.OnNoiseDetected))]
        [HarmonyPrefix]
        private static bool Noise(EB_Hibernating __instance) {
            return enabled;
        }

        [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ProcessReceivedDamage))]
        [HarmonyPrefix]
        private static void hitreact(float damage, Agent damageSource, Vector3 position, Vector3 direction, ref ES_HitreactType hitreact, ref bool tryForceHitreact, int limbID, ref float staggerDamageMulti, DamageNoiseLevel damageNoiseLevel) {
            if (!enabled) {
                hitreact = ES_HitreactType.None;
                tryForceHitreact = false;
                staggerDamageMulti = 0;
            }
        }

        [HarmonyPatch(typeof(EB_Hibernating), nameof(EB_Hibernating.CanDetectNoise))]
        [HarmonyPostfix]
        private static void CanDetectNoise(EB_Hibernating __instance, ref bool __result) {
            if (!enabled) {
                __result = false;
            }
        }
    }
}