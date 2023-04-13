using BepInEx; // requires BepInEx.dll and BepInEx.Harmony.dll
using BepInEx.Configuration;
using UnboundLib; // requires UnboundLib.dll
using UnboundLib.Cards; // " "
using UnityEngine; // requires UnityEngine.dll, UnityEngine.CoreModule.dll, and UnityEngine.AssetBundleModule.dll
using HarmonyLib; // requires 0Harmony.dll
using System.Collections;
using Photon.Pun;
using Jotunn.Utils;
using UnboundLib.GameModes;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnboundLib.Networking;
using UnboundLib.Utils;
using System;
using UnboundLib.Utils.UI;
using TMPro;

// requires Assembly-CSharp.dll
// requires MMHOOK-Assembly-CSharp.dll

namespace DrawNCards
{
    public class DrawNCards
    {
        internal const int maxDraws = 20;

        public static ConfigEntry<int> NumDrawsConfig;
        internal static int numDraws;

        private const float arc_A = 0.2102040816f;
        private const float arc_B = 1.959183674f;
        private const float arc_C = -1.959183674f;
        private const float Harc_A = 1f;
        private const float Harc_B = 2f;
        private const float Harc_C = -1.7f;
        internal static float Arc(float x, float offset = 0f)
        {
            // inputs and outputs are in SCREEN UNITS

            if (offset == 0f)
            {
                // approximate parabola of default card arc, correlation coeff = 1
                return arc_C * x * x + arc_B * x + arc_A + offset;
            }
            else if (offset < 0f)
            {
                // approximate hyperbola of default card arc
                return -UnityEngine.Mathf.Sqrt(1 + (Harc_B * Harc_B) * UnityEngine.Mathf.Pow(x - xC, 2f) / (Harc_A * Harc_A)) - Harc_C + offset;
            }
            else
            {
                // flattened hyperbola for top arc
                return -UnityEngine.Mathf.Sqrt(1 + (Harc_B * Harc_B) * UnityEngine.Mathf.Pow(x - xC, 2f) / (2f * Harc_A * Harc_A)) - Harc_C + offset;
            }
        }
        private const float xC = 0.5f;
        private const float yC = 0.5f;
        private const float absMaxX = 0.85f;
        private const float defMaxXWorld = 25f;
        internal const float z = -5f;
        public static List<Vector3> GetPositions(int N, float offset = 0f)
        {
            // everything is in SCREEN UNITS

            if (N == 0)
            {
                throw new Exception("Must have at least one card.");
            }
            else if (N == 1)
            {
                return new List<Vector3>() { new Vector3(xC, yC, z) };
            }
            else if (N==3)
            {
                offset -= 0.025f;
            }
            else if (N > DrawNCards.maxDraws / 2)
            {
                int N1 = (int)UnityEngine.Mathf.RoundToInt(N / 2);
                int N2 = N - N1;
                int k1;
                int k2;
                if (N1 >= N2) { k1 = N1; k2 = N2; }
                else { k1 = N2; k2 = N1; }
                List<Vector3> positions1 = GetPositions(k1, offset - 0.16f);
                List<Vector3> positions2 = GetPositions(k2, offset + 0.125f);
                return positions1.Concat(positions2).ToList();
            }

            float maxX = absMaxX;

            if (N < 4) { maxX = 0.75f; }
            else
            {
                maxX = UnityEngine.Mathf.Clamp(maxX + 0.025f * (N - 5), maxX, 0.925f);
            }

            // we assume symmetry about x = 0 and fill from the center out
            List<float> xs = new List<float>() { };

            float x_init = xC;

            // if N is odd, place a card exactly at the center
            if (N % 2 != 0)
            {
                x_init = xC;
                xs.Add(x_init);
                N--;

                // now N is guarunteed to be even, so:
                int k = N / 2;

                float step = (maxX - x_init) / k;

                float x = x_init;

                for (int i = 0; i < k; i++)
                {
                    x += step;
                    xs.Add(x); // add the next point to the right and its reflection over xC
                    xs.Add(2f*xC-x);
                }
            }
            // if N is even, do it the easy way
            else
            {
                x_init = 1f-maxX;

                float step = (maxX - x_init) / (N - 1);

                float x = x_init;

                for (int i = 0; i < N; i++)
                {
                    xs.Add(x);
                    x += step;
                }
            }

            // sort by x
            xs.Sort();

            List<Vector3> positions = new List<Vector3>() { };

            foreach (float x_ in xs)
            {
                positions.Add(new Vector3(x_, Arc(x_, offset), z));
            }

            return positions;
        }
        public static Vector3 GetScale(int N)
        {
            // camera scale factor
            float factor = 1.04f * absMaxX.xWorldPoint() / defMaxXWorld;

            if (N == 5)
            {
                return new Vector3(1f, 1f, 1f) * factor;
            }
            else if (N<5)
            {
                return new Vector3(1f, 1f, 1f) * factor * (1f + 1f / (2f*N));
            }
            else if (N > DrawNCards.maxDraws / 2)
            {
                return new Vector3(1f, 1f, 1f) * factor * UnityEngine.Mathf.Clamp(5f / (N / 2 + 2), 3f / 5f, 1f);
            }
            else
            {
                return new Vector3(1f, 1f, 1f) * factor * UnityEngine.Mathf.Clamp(5f / (N - 1), 3f / 5f, 1f);
            }

        }
        private const float maxPhi = 15f;
        internal static float ArcAngle(float x)
        {
            // x is in SCREEN units
            return (-maxPhi / (absMaxX-xC)) * (x-xC);
        }
        public static List<Quaternion> GetRotations(int N)
        {
            List<Vector3> positions = GetPositions(N);

            List<Quaternion> rotations = new List<Quaternion>() { };
            foreach (Vector3 pos in positions)
            {
                rotations.Add(Quaternion.Euler(0f, 0f, ArcAngle(pos.x)));
            }
            return rotations;
        }

    }
    // patch to change scale of cards
    [Serializable]
    [HarmonyPatch(typeof(CardChoice), "Spawn")]
    class CardChoicePatchSpawn
    {
        private static void Postfix(ref GameObject __result)
        {
            __result.transform.localScale = DrawNCards.GetScale(DrawNCards.numDraws);

            NetworkingManager.RPC_Others(typeof(CardChoicePatchSpawn), nameof(RPCO_AddRemotelySpawnedCard), new object[] { __result.GetComponent<PhotonView>().ViewID });
        }
        // set scale client-side
        [UnboundRPC]
        private static void RPCO_AddRemotelySpawnedCard(int viewID)
        {
            GameObject card = PhotonView.Find(viewID).gameObject;

            // set the scale
            card.transform.localScale = DrawNCards.GetScale(DrawNCards.numDraws);
        }
    }
    // reconfigure card placement before each pick in case the map size has changed
    [Serializable]
    [HarmonyPatch(typeof(CardChoice), "StartPick")]
    class CardChoicePatchStartPick
    {
        private static GameObject _cardVis = null;
        private static GameObject cardVis
        {
            get
            {
                if (_cardVis != null) { return _cardVis; }
                else
                {
                    _cardVis = ((Transform[])CardChoice.instance.GetFieldValue("children"))[0].gameObject;
                    return _cardVis;
                }
            }
            set { }
        }
        private static void Prefix(CardChoice __instance)
        {
            // remove all children except the zeroth
            foreach (Transform child in ((Transform[])CardChoice.instance.GetFieldValue("children")).Skip(1))
            {
                UnityEngine.GameObject.Destroy(child.gameObject);
            }

            List<Vector3> positions = DrawNCards.GetPositions(DrawNCards.numDraws).WorldPoint();
            List<Quaternion> rotations = DrawNCards.GetRotations(DrawNCards.numDraws);
            Vector3 scale = DrawNCards.GetScale(DrawNCards.numDraws);

            List<Transform> children = new List<Transform>() { cardVis.transform };

            // change properties of the first cardvis
            children[0].position = positions[0];
            children[0].rotation = rotations[0];
            children[0].localScale = scale;

            // start at 1 since the first cardVis should already be present
            for (int i = 1; i < DrawNCards.numDraws; i++)
            {
                GameObject newChild = UnityEngine.GameObject.Instantiate(cardVis, positions[i], rotations[i], __instance.transform);
                newChild.name = children.Count.ToString();
                children.Add(newChild.transform);
            }

            __instance.SetFieldValue("children", children.ToArray());
        }

        
    }
    // patch to fix armpos
    [Serializable]
    [HarmonyPatch(typeof(CardChoiceVisuals), "Update")]
    class CardChoiceVisualsPatchUpdate
    {
        private static bool Prefix(CardChoiceVisuals __instance)
        {
            if (!(bool)__instance.GetFieldValue("isShowinig"))
            {
                return false;
            }
            if (Time.unscaledDeltaTime > 0.1f)
            {
                return false;
            }
            if (__instance.currentCardSelected >= __instance.cardParent.transform.childCount || __instance.currentCardSelected < 0)
            {
                return false;
            }
            if (__instance.rightHandTarget.position.x == float.NaN || __instance.rightHandTarget.position.y == float.NaN || __instance.rightHandTarget.position.z == float.NaN)
            {
                __instance.rightHandTarget.position = Vector3.zero;
                __instance.SetFieldValue("rightHandVel", Vector3.zero);
            }
            if (__instance.leftHandTarget.position.x == float.NaN || __instance.leftHandTarget.position.y == float.NaN || __instance.leftHandTarget.position.z == float.NaN)
            {
                __instance.leftHandTarget.position = Vector3.zero;
                __instance.SetFieldValue("leftHandVel", Vector3.zero);
            }
            GameObject gameObject = __instance.cardParent.transform.GetChild(__instance.currentCardSelected).gameObject;
            Vector3 vector = gameObject.transform.GetChild(0).position;
            if (vector.x < 0f) // it was literally this simple Landfall...
            {
                __instance.SetFieldValue("leftHandVel", (Vector3)__instance.GetFieldValue("leftHandVel") + (vector - __instance.leftHandTarget.position) * __instance.spring * Time.unscaledDeltaTime);
                __instance.SetFieldValue("leftHandVel", (Vector3)__instance.GetFieldValue("leftHandVel") - ((Vector3)__instance.GetFieldValue("leftHandVel")) * Time.unscaledDeltaTime * __instance.drag);
                __instance.SetFieldValue("rightHandVel", (Vector3)__instance.GetFieldValue("rightHandVel") + (((Vector3)__instance.GetFieldValue("rightHandRestPos")) - __instance.rightHandTarget.position) * __instance.spring * Time.unscaledDeltaTime * 0.5f);
                __instance.SetFieldValue("rightHandVel", (Vector3)__instance.GetFieldValue("rightHandVel") - ((Vector3)__instance.GetFieldValue("rightHandVel")) * Time.unscaledDeltaTime * __instance.drag * 0.5f);
                __instance.SetFieldValue("rightHandVel", (Vector3)__instance.GetFieldValue("rightHandVel") + __instance.sway * new Vector3(-0.5f + Mathf.PerlinNoise(Time.unscaledTime * __instance.swaySpeed, 0f), -0.5f + Mathf.PerlinNoise(Time.unscaledTime * __instance.swaySpeed + 100f, 0f), 0f) * Time.unscaledDeltaTime);
                __instance.shieldGem.transform.position = __instance.rightHandTarget.position;
                if (__instance.framesToSnap > 0)
                {
                    __instance.leftHandTarget.position = vector;
                }
            }
            else
            {
                __instance.SetFieldValue("rightHandVel", (Vector3)__instance.GetFieldValue("rightHandVel") + (vector - __instance.rightHandTarget.position) * __instance.spring * Time.unscaledDeltaTime);
                __instance.SetFieldValue("rightHandVel", (Vector3)__instance.GetFieldValue("rightHandVel") - ((Vector3)__instance.GetFieldValue("rightHandVel")) * Time.unscaledDeltaTime * __instance.drag);
                __instance.SetFieldValue("leftHandVel", (Vector3)__instance.GetFieldValue("leftHandVel") + (((Vector3)__instance.GetFieldValue("leftHandRestPos")) - __instance.leftHandTarget.position) * __instance.spring * Time.unscaledDeltaTime * 0.5f);
                __instance.SetFieldValue("leftHandVel", (Vector3)__instance.GetFieldValue("leftHandVel") - ((Vector3)__instance.GetFieldValue("leftHandVel")) * Time.unscaledDeltaTime * __instance.drag * 0.5f);
                __instance.SetFieldValue("leftHandVel", (Vector3)__instance.GetFieldValue("leftHandVel") + __instance.sway * new Vector3(-0.5f + Mathf.PerlinNoise(Time.unscaledTime * __instance.swaySpeed, Time.unscaledTime * __instance.swaySpeed), -0.5f + Mathf.PerlinNoise(Time.unscaledTime * __instance.swaySpeed + 100f, Time.unscaledTime * __instance.swaySpeed + 100f), 0f) * Time.unscaledDeltaTime);
                __instance.shieldGem.transform.position = __instance.leftHandTarget.position;
                if (__instance.framesToSnap > 0)
                {
                    __instance.rightHandTarget.position = vector;
                }
            }
            __instance.framesToSnap--;
            __instance.leftHandTarget.position += (Vector3)__instance.GetFieldValue("leftHandVel") * Time.unscaledDeltaTime;
            __instance.rightHandTarget.position += (Vector3)__instance.GetFieldValue("rightHandVel") * Time.unscaledDeltaTime;

            return false; // skip original (BAD IDEA)
        }
    }
    public static class WorldToScreenExtensions
    {
        public static Vector3 ScreenPoint(this Vector3 v3)
        {
            Vector3 vec = MainCam.instance.transform.GetComponent<Camera>().WorldToScreenPoint(v3);
            vec.x /= (float)Screen.width;
            vec.y /= (float)Screen.height;
            vec.z = 0f;

            return vec;
        }

        public static Vector3 WorldPoint(this Vector3 v3)
        {
            v3.x *= (float)Screen.width;
            v3.y *= (float)Screen.height;
            Vector3 vec = MainCam.instance.transform.GetComponent<Camera>().ScreenToWorldPoint(v3);
            vec.z = DrawNCards.z;
            return vec;
        }

        public static float xScreenPoint(this float x)
        {
            return ((new Vector3(x, 0f, 0f)).ScreenPoint()).x;
        }
        public static float xWorldPoint(this float x)
        {
            return ((new Vector3(x, 0f, 0f)).WorldPoint()).x;
        }
        public static float yScreenPoint(this float y)
        {
            return ((new Vector3(0f, y, 0f)).ScreenPoint()).y;
        }
        public static float yWorldPoint(this float y)
        {
            return ((new Vector3(0f, y, 0f)).WorldPoint()).y;
        }

        public static List<Vector3> ScreenPoint(this List<Vector3> v3)
        {
            List<Vector3> v3screen = new List<Vector3>() { };
            foreach (Vector3 v in v3)
            {
                v3screen.Add(v.ScreenPoint());
            }
            return v3screen;
        }
        public static List<Vector3> WorldPoint(this List<Vector3> v3)
        {
            List<Vector3> v3screen = new List<Vector3>() { };
            foreach (Vector3 v in v3)
            {
                v3screen.Add(v.WorldPoint());
            }
            return v3screen;
        }
    }
}
