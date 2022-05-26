using System;
using BepInEx;
using BepInEx.Configuration;
using UnboundLib;
using HarmonyLib;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Reflection;
using UnboundLib.Utils.UI;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using UnboundLib.GameModes;
using System.Linq;
using Photon.Pun;
using UnboundLib.Networking;
using System.Collections.Generic;
using DrawNCards;
using System.Reflection.Emit;

namespace PickNCards
{
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(ModId, ModName, "0.2.3")]
    [BepInProcess("Rounds.exe")]
    public class PickNCards : BaseUnityPlugin
    {
        private const string ModId = "pykess.rounds.plugins.pickncards";
        private const string ModName = "Pick N Cards";
        private const string CompatibilityModName = "PickNCards";

        private const int maxPicks = 5;

        internal static PickNCards instance;

        public static ConfigEntry<int> PicksConfig;
        public static ConfigEntry<float> DelayConfig;
        internal static int picks;
        internal static float delay;

        internal static bool lockPickQueue = false;
        internal static List<int> playerIDsToPick = new List<int>() { };
        internal static bool extraPicksInPorgress = false;

        private void Awake()
        {

            PickNCards.instance = this;

            // bind configs with BepInEx
            PicksConfig = Config.Bind(CompatibilityModName, "Picks", 1, "Total number of card picks per player per pick phase");

            DrawNCards.DrawNCards.NumDrawsConfig = Config.Bind(CompatibilityModName, "Draws", 5, "Number of cards drawn from the deck to choose from");

            DelayConfig = Config.Bind(CompatibilityModName, "DelayBetweenDraws", 0.1f, "Delay (in seconds) between each card being drawn.");

            // apply patches
            new Harmony(ModId).PatchAll();
        }
        private void Start()
        {
            // call settings as to not orphan them
            picks = PicksConfig.Value;
            delay = DelayConfig.Value;

            // add credits
            Unbound.RegisterCredits("Pick N Cards", new string[] { "Pykess (Code)", "Willis (Original picktwocards concept, icon)"}, new string[] { "github", "Support Pykess" }, new string[] { "https://github.com/pdcook/PickNCards", "https://ko-fi.com/pykess" });

            // add GUI to modoptions menu
            Unbound.RegisterMenu(ModName, () => { }, this.NewGUI, null, false);
            
            // handshake to sync settings
            Unbound.RegisterHandshake(PickNCards.ModId, this.OnHandShakeCompleted);

            // hooks for picking N cards
            GameModeManager.AddHook(GameModeHooks.HookPickStart, (gm) => PickNCards.ResetPickQueue());
            GameModeManager.AddHook(GameModeHooks.HookPickEnd, PickNCards.ExtraPicks);

            // read settings to not orphan them
            DrawNCards.DrawNCards.numDraws = DrawNCards.DrawNCards.NumDrawsConfig.Value;

        }
        private void OnHandShakeCompleted()
        {
            if (PhotonNetwork.IsMasterClient)
            {
                NetworkingManager.RPC_Others(typeof(PickNCards), nameof(SyncSettings), new object[] { PickNCards.picks, DrawNCards.DrawNCards.numDraws });
            }
        }
        [UnboundRPC]
        private static void SyncSettings(int host_picks, int host_draws)
        {
            PickNCards.picks = host_picks;
            DrawNCards.DrawNCards.numDraws = host_draws;
        }
        private void NewGUI(GameObject menu)
        {

            MenuHandler.CreateText(ModName+" Options", menu, out TextMeshProUGUI _, 60);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);
            void PicksChanged(float val)
            {
                PickNCards.PicksConfig.Value = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp(val,0f,(float)PickNCards.maxPicks));
                PickNCards.picks = PickNCards.PicksConfig.Value;
                OnHandShakeCompleted();
            }
            MenuHandler.CreateSlider("Number of cards to pick", menu, 30, 0f, (float)PickNCards.maxPicks, PickNCards.PicksConfig.Value, PicksChanged, out UnityEngine.UI.Slider _, true);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);
            MenuHandler.CreateText("Draw N Cards Options", menu, out TextMeshProUGUI _, 60);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);
            void DrawsChanged(float val)
            {
                DrawNCards.DrawNCards.NumDrawsConfig.Value = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp(val, 1f, (float)DrawNCards.DrawNCards.maxDraws));
                DrawNCards.DrawNCards.numDraws = DrawNCards.DrawNCards.NumDrawsConfig.Value;
                OnHandShakeCompleted();
            }
            MenuHandler.CreateSlider("Number of cards to draw", menu, 30, 1f, (float)DrawNCards.DrawNCards.maxDraws, DrawNCards.DrawNCards.NumDrawsConfig.Value, DrawsChanged, out UnityEngine.UI.Slider _, true);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);
            void DelayChanged(float val)
            {
                PickNCards.DelayConfig.Value = UnityEngine.Mathf.Clamp(val, 0f, 0.5f);
                PickNCards.delay = PickNCards.DelayConfig.Value;
            }
            MenuHandler.CreateSlider("Time between each card draw", menu, 30, 0f, 0.5f, PickNCards.DelayConfig.Value, DelayChanged, out UnityEngine.UI.Slider _, false);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);

        }
        [UnboundRPC]
        public static void RPC_RequestSync(int requestingPlayer)
        {
            NetworkingManager.RPC(typeof(PickNCards), nameof(PickNCards.RPC_SyncResponse), requestingPlayer, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        [UnboundRPC]
        public static void RPC_SyncResponse(int requestingPlayer, int readyPlayer)
        {
            if (PhotonNetwork.LocalPlayer.ActorNumber == requestingPlayer)
            {
                PickNCards.instance.RemovePendingRequest(readyPlayer, nameof(PickNCards.RPC_RequestSync));
            }
        }

        private IEnumerator WaitForSyncUp()
        {
            if (PhotonNetwork.OfflineMode)
            {
                yield break;
            }
            yield return this.SyncMethod(nameof(PickNCards.RPC_RequestSync), null, PhotonNetwork.LocalPlayer.ActorNumber);
        }
        internal static IEnumerator ResetPickQueue()
        {
            if (!PickNCards.extraPicksInPorgress)
            {
                PickNCards.playerIDsToPick = new List<int>() { };
                PickNCards.lockPickQueue = false;
            }
            yield break;
        }
        internal static IEnumerator ExtraPicks(IGameModeHandler gm)
        {

            if (!PickNCards.extraPicksInPorgress)
            {
                if (PickNCards.picks <= 1 || PickNCards.playerIDsToPick.Count() < 1)
                {
                    yield break;
                }

                PickNCards.lockPickQueue = true;
                PickNCards.extraPicksInPorgress = true;
                yield return PickNCards.instance.WaitForSyncUp();

                for (int _ = 0; _ < PickNCards.picks - 1; _++)
                {
                    yield return PickNCards.instance.WaitForSyncUp();
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);
                    for (int i = 0; i < PickNCards.playerIDsToPick.Count(); i++)
                    {
                        yield return PickNCards.instance.WaitForSyncUp();
                        int playerID = PickNCards.playerIDsToPick[i];
                        yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);
                        CardChoiceVisuals.instance.Show(playerID, true);
                        yield return CardChoice.instance.DoPick(1, playerID, PickerType.Player);
                        yield return new WaitForSecondsRealtime(0.1f);
                        yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);
                        yield return new WaitForSecondsRealtime(0.1f);
                    }
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);
                }

                CardChoiceVisuals.instance.Hide();
                PickNCards.extraPicksInPorgress = false;
            }
            yield break;
        }
        // patch to skip pick phase if requested
        [Serializable]
        [HarmonyPatch(typeof(CardChoiceVisuals), "Show")]
        [HarmonyPriority(Priority.First)]
        class CardChoiceVisualsPatchShow
        {
            private static bool Prefix(CardChoice __instance)
            {
                if (PickNCards.picks == 0) { return false; }
                else { return true; }
            }
        }

        // patch to determine which players have picked this phase
        [Serializable]
        [HarmonyPatch(typeof(CardChoice), "DoPick")]
        [HarmonyPriority(Priority.First)]
        class CardChoicePatchDoPick
        {
            private static bool Prefix(CardChoice __instance)
            {
                if (PickNCards.picks == 0) { return false; }
                else { return true; }
            }
            private static void Postfix(CardChoice __instance, int picketIDToSet)
            {
                if (!PickNCards.lockPickQueue && /*checked if player is alreadly in the queue*/!PickNCards.playerIDsToPick.Contains(picketIDToSet)) { PickNCards.playerIDsToPick.Add(picketIDToSet); }
            }
        }

        // patch to change draw rate
        [HarmonyPatch]
        class CardChoicePatchReplaceCards
        {
            static Type GetNestedReplaceCardsType()
            {
                Type[] nestedTypes = typeof(CardChoice).GetNestedTypes(BindingFlags.Instance | BindingFlags.NonPublic);
                Type nestedType = null;

                foreach (Type type in nestedTypes)
                {
                    if (type.Name.Contains("ReplaceCards"))
                    {
                        nestedType = type;
                        break;
                    }
                }

                return nestedType;
            }

            static MethodBase TargetMethod()
            {
                return AccessTools.Method(GetNestedReplaceCardsType(), "MoveNext");
            }

            static float GetNewDelay()
            {
                return PickNCards.delay;
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();

                FieldInfo f_theInt = ExtensionMethods.GetFieldInfo(typeof(PublicInt), "theInt");
                MethodInfo m_GetNewDelay = ExtensionMethods.GetMethodInfo(typeof(CardChoicePatchReplaceCards), nameof(GetNewDelay));

                int index = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].StoresField(f_theInt) && codes[i+1].opcode == OpCodes.Ldarg_0 && codes[i+2].opcode == OpCodes.Ldc_R4 && (float)(codes[i+2].operand) == 0.1f && codes[i+3].opcode == OpCodes.Newobj)
                    {
                        index = i;
                        break;
                    }
                }
                if (index == -1)
                {
                    throw new Exception("[REPLACECARDS PATCH] INSTRUCTION NOT FOUND");
                }
                else
                {
                    codes[index + 2] = new CodeInstruction(OpCodes.Call, m_GetNewDelay);
                }

                return codes.AsEnumerable();
            }
        }
    }
}

