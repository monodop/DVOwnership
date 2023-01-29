using DV.ServicePenalty.UI;
using Harmony12;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace DVOwnership.Patches
{
    static class CareerManager_Helpers
    {
        public static TextMeshPro FindTmp(TextMeshPro[] tmps)
        {
            return tmps.Single(t => t.gameObject.name == "DVOwnership_Contracts");
        }

        public static int FindTmpIndex(TextMeshPro[] tmps)
        {
            return Array.FindIndex(tmps, t => t.gameObject.name == "DVOwnership_Contracts");
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), "Awake")]
    static class CareerManagerMainScreen_Awake_Patches
    {
        static void Postfix(ref IntIterator ___selector, ref TextMeshPro[] ___selectableText)
        {
            ___selector = new IntIterator(0, ___selector.Length + 1, ___selector.isWrappable);
            var nextLast = ___selectableText[___selectableText.Length - 2];
            var last = ___selectableText[___selectableText.Length - 1];

            var delta = last.gameObject.transform.position - nextLast.gameObject.transform.position;
            var gameObject = GameObject.Instantiate(last.gameObject, last.gameObject.transform.position + delta, last.gameObject.transform.rotation, last.transform.parent);
            gameObject.name = "DVOwnership_Contracts";
            var newTmp = gameObject.GetComponent<TextMeshPro>();

            ___selectableText = ___selectableText.Union(new[] { newTmp }).ToArray();
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), "Activate")]
    static class CareerManagerMainScreen_Activate_Patches
    {
        static void Prefix(CareerManagerMainScreen __instance, TextMeshPro[] ___selectableText)
        {
            CareerManager_Helpers.FindTmp(___selectableText).SetText("Regen Contracts");

            var newScreenGO = new GameObject("DVOwnership_ContractsScreen");
            newScreenGO.transform.parent = __instance.transform.parent;

            var lps = __instance.licensesScreen.licensePayScreen;

            var contractsScreen = newScreenGO.AddComponent<CareerManagerContractsScreen>();
            contractsScreen.screenSwitcher = __instance.screenSwitcher;
            contractsScreen.mainScreen = __instance;
            contractsScreen.cashReg = lps.cashReg;

            contractsScreen.title1 = lps.title1;
            contractsScreen.title2 = lps.title2;
            contractsScreen.desc1 = lps.licenseNameText;
            contractsScreen.desc2 = lps.licensePriceText;
            contractsScreen.insertWallet = lps.insertWallet;
            contractsScreen.depositedText = lps.depositedText;
            contractsScreen.depositedValue = lps.depositedValue;
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), "Disable")]
    static class CareerManagerMainScreen_Disable_Patches
    {
        static void Prefix(CareerManagerMainScreen __instance, TextMeshPro[] ___selectableText)
        {
            CareerManager_Helpers.FindTmp(___selectableText).SetText(String.Empty);
            GameObject.Destroy(__instance.gameObject.transform.parent.gameObject.GetComponentInChildren<CareerManagerContractsScreen>().gameObject);
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), "HandleInputAction")]
    static class CareerManagerMainScreen_HandleInputAction_Patches
    {
        static void PrintGraph(GameObject go, int depth = 0)
        {
            string p = "";
            for (int i = 0; i < depth; i++)
            {
                p += "    ";
            }
            DVOwnership.LogDebug(() => $"{p} {go.name}");
            var components = go.GetComponents<Component>();
            foreach (var component in components)
            {
                DVOwnership.LogDebug(() => $"{p} - {component.GetType().FullName}");
            }

            for (var i = 0; i < go.transform.childCount; i++)
            {
                PrintGraph(go.transform.GetChild(i).gameObject, depth + 1);
            }
        }

        static bool Prefix(InputAction input, CareerManagerMainScreen __instance, IntIterator ___selector, TextMeshPro[] ___selectableText)
        {
            var index = CareerManager_Helpers.FindTmpIndex(___selectableText);
            if (input == InputAction.Confirm && ___selector?.Current == index)
            {
                DVOwnership.LogDebug(() => $"go: {__instance.gameObject.name} - {__instance.gameObject.transform.parent.name}");
                PrintGraph(__instance.gameObject, 1);

                DVOwnership.LogDebug(() => $"fees: {__instance.feesScreen.gameObject.name} - {__instance.feesScreen.transform.parent.name}");
                PrintGraph(__instance.feesScreen.gameObject, 1);

                DVOwnership.LogDebug(() => $"licenses: {__instance.licensesScreen.gameObject.name} - {__instance.licensesScreen.gameObject.transform.parent.name}");
                PrintGraph(__instance.licensesScreen.gameObject, 1);

                DVOwnership.LogDebug(() => $"feestmp: {__instance.fees.gameObject.name} - {__instance.fees.gameObject.transform.parent.name} - {__instance.fees.gameObject.transform.parent.transform.parent.name}");
                PrintGraph(__instance.fees.gameObject, 1);

                DVOwnership.LogDebug(() => "Career Manager");
                PrintGraph(__instance.fees.gameObject.transform.parent.transform.parent.gameObject, 1);

                DVOwnership.LogDebug(() => "Switched to Contracts screen");
                __instance.screenSwitcher.SetActiveDisplay(__instance.gameObject.transform.parent.gameObject.GetComponentInChildren<CareerManagerContractsScreen>());
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(CareerManagerMainScreen), "GetCurrentSelection")]
    static class CareerManagerMainScreen_GetCurrentSelection_Patches
    {
        static bool Prefix(ref string __result, IntIterator ___selector, TextMeshPro[] ___selectableText)
        {
            var index = CareerManager_Helpers.FindTmpIndex(___selectableText);
            if (___selector?.Current == index)
            {
                __result = "Contracts";
                return false;
            }

            return true;
        }
    }
}
