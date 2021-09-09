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

namespace PickNCards
{
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(ModId, ModName, "0.1.1")]
    [BepInProcess("Rounds.exe")]
    public class PickNCards : BaseUnityPlugin
    {
        private const string ModId = "pykess.rounds.plugins.pickncards";
        private const string ModName = "Pick N Cards";
        private const string CompatibilityModName = "PickNCards";

        private const int maxPicks = 5;


        public static ConfigEntry<int> PicksConfig;
        internal static int picks;

        internal static Dictionary<Player, bool> playerCanPickAgain = new Dictionary<Player, bool>() { };

        private void Awake()
        {
            // bind configs with BepInEx
            PicksConfig = Config.Bind(CompatibilityModName, "Picks", 1, "Total number of card picks per player per pick phase");

            DrawNCards.DrawNCards.NumDrawsConfig = Config.Bind(CompatibilityModName, "Draws", 5, "Number of cards drawn from the deck to choose from");

            // apply patches
            new Harmony(ModId).PatchAll();
        }
        private void Start()
        {
            // call settings as to not orphan them
            picks = PicksConfig.Value;

            // add credits
            Unbound.RegisterCredits("Pick N Cards", new string[] { "Pykess (Code)", "Willis (Original picktwocards concept, icon)"}, new string[] { "github", "Buy me a coffee" }, new string[] { "https://github.com/pdcook/PickNCards", "https://www.buymeacoffee.com/Pykess" });

            // add GUI to modoptions menu
            Unbound.RegisterMenu(ModName, () => { }, this.NewGUI, null, false);
            
            // handshake to sync settings
            Unbound.RegisterHandshake(PickNCards.ModId, this.OnHandShakeCompleted);

            // hooks for picking N cards
            GameModeManager.AddHook(GameModeHooks.HookPickStart, (gm) => PickNCards.SetPlayersCanPick(false));
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
                PickNCards.PicksConfig.Value = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp(val,1f,(float)PickNCards.maxPicks));
                PickNCards.picks = PickNCards.PicksConfig.Value;
                OnHandShakeCompleted();
            }
            MenuHandler.CreateSlider("Number of cards to pick", menu, 30, 1f, (float)PickNCards.maxPicks, PickNCards.PicksConfig.Value, PicksChanged, out UnityEngine.UI.Slider _, true);
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
        }
        internal static IEnumerator SetPlayersCanPick(bool set)
        {
            foreach (Player player in PlayerManager.instance.players)
            {
                PickNCards.playerCanPickAgain[player] = set;
            }
            yield break;
        }
        internal static IEnumerator ExtraPicks(IGameModeHandler gm)
        {

            if (PickNCards.picks <= 1 || !PlayerManager.instance.players.Where(player => PickNCards.playerCanPickAgain[player]).Any())
            {
                yield break;
            }

            yield return new WaitForSecondsRealtime(1f);

            for (int _ = 0; _ < PickNCards.picks - 1; _++)
            {
                foreach (Player player in PlayerManager.instance.players.Where(player => PickNCards.playerCanPickAgain[player]))
                {
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);
                    CardChoiceVisuals.instance.Show(Enumerable.Range(0, PlayerManager.instance.players.Count).Where(i => PlayerManager.instance.players[i].playerID == player.playerID).First(), true);
                    yield return CardChoice.instance.DoPick(1, player.playerID, PickerType.Player);
                    yield return new WaitForSecondsRealtime(0.1f);
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);
                    yield return new WaitForSecondsRealtime(0.1f);
                }
            }

            CardChoiceVisuals.instance.Hide();

            yield break;
        }

        // patch to determine which players have picked this phase
        [Serializable]
        [HarmonyPatch(typeof(CardChoice), "DoPick")]
        class CardChoicePatchDoPick
        {
            private static void Postfix(CardChoice __instance, int picketIDToSet)
            {
                Player player = (Player)typeof(PlayerManager).InvokeMember("GetPlayerWithID",
                    BindingFlags.Instance | BindingFlags.InvokeMethod |
                    BindingFlags.NonPublic, null, PlayerManager.instance, new object[] { picketIDToSet });

                PickNCards.playerCanPickAgain[player] = true;
            }
        }
    }
}

