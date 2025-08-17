using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Noise;
using Unity.Collections;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GermanQualityLF
{

    [StaticConstructorOnStartup]
    public static class GermanQualityLF
    {
        static GermanQualityLF()
        {
            // This mod was originally named "German Quality", and the code will treat it as such.
            // It's been renamed to "Quality Affects HP" player-facing.

            Log.Message("[Quality Affects HP] Mod initiated.");
            var harmony = new Harmony("com.Fluxilis.GermanQualityLF");
            harmony.PatchAll();
        }
    }

    public static class GermanQualityUtility
    {
        public static int CalculateQualityMaxHP(int basemax, QualityCategory quality)
        {
            float factor = 1;

            switch (quality)
            {
                case QualityCategory.Awful: factor = 0.5f; break;
                case QualityCategory.Poor: factor = 0.75f; break;
                case QualityCategory.Normal: factor = 1f; break;
                case QualityCategory.Good: factor = 1.75f; break;
                case QualityCategory.Excellent: factor = 3f; break;
                case QualityCategory.Masterwork: factor = 6f; break;
                case QualityCategory.Legendary: factor = 10f; break;
            }

            float floatMax = basemax * factor;
            int newMax = (int)floatMax; //Mathf.RoundToInt(floatMax);

            //round to nearest 5 if above 200 because that's what it does for maxHP
            if (newMax > 200)
            {
                newMax = (int)GenMath.RoundTo(newMax, 5);
            }

            return newMax;
        }

    }

    //The moment before we Make a thing, change it's max hp (current hp?) as needed in the def
    //[HarmonyPatch(typeof(ThingMaker))]
    //[HarmonyPatch(nameof(ThingMaker.MakeThing))]
    //class ThingMakeHandler
    //{
    //    static void Prefix(ThingDef def, ThingDef stuff = null)
    //    {
    //        //Log.Message("[Quality Affects HP] called ThingMake Prefix");
    //        if(def.category == ThingCategory.Item)
    //        {
    //            Log.Message("[Quality Affects HP] called ThingMake Prefix");
    //        }

    //        //if (__result != null && __result.def != null && __result.compQuality != null)
    //        //{
    //        //    if (__result.def.useHitPoints)
    //        //    {
    //        //        int basemaxhp = UnityEngine.Mathf.RoundToInt((float)__result.MaxHitPoints * UnityEngine.Mathf.Clamp01(__result.def.startingHpRange.RandomInRange));
    //        //        __result.HitPoints = GermanQualityUtility.CalculateQualityMaxHP(basemaxhp, __result.compQuality.Quality);
    //        //    }
    //        //}
    //        //else
    //        //{
    //        //    Log.Warning("[Quality Affects HP] encountered an issue: result was null.");
    //        //}
    //    }
    //}



    // The moment we set Quality, also set currentHP of the Thing.
    [HarmonyPatch(typeof(CompQuality))]
    [HarmonyPatch("SetQuality")]
    class QualitySetter
    {
        static void Postfix(CompQuality __instance)
        {
            //Log.Message("[Quality Affects HP] SetQuality called");

            if (__instance != null && __instance.parent != null && __instance.parent.def != null)
            {
                ThingWithComps twc = __instance.parent;
                if (twc.def.useHitPoints)
                {
                    int basemaxhp = UnityEngine.Mathf.RoundToInt((float)twc.MaxHitPoints * UnityEngine.Mathf.Clamp01(twc.def.startingHpRange.RandomInRange));
                    //Log.Message("[Quality Affects HP] SetQuality basemaxhp: " + basemaxhp + "\nQuality is: " + __instance.Quality);
                    twc.HitPoints = GermanQualityUtility.CalculateQualityMaxHP(basemaxhp, __instance.Quality);
                    //Log.Message("[Quality Affects HP] SetQuality set hp to " + GermanQualityUtility.CalculateQualityMaxHP(basemaxhp, __instance.Quality));
                }
            }
            else
            {
                Log.Warning("[Quality Affects HP] encountered an issue: instance or parent was null.");
            }
        }
    }


    // in Frame.CompleteConstruction(), l 346 sets hitpoints. we're already setting (Quality-affected-) hitpoints in setquality, so change it to:
    // ONLY set hitpoints if compquality == null! (becuse if it isn't, we've set them already).
    [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
    public static class Frame_CompleteConstruction_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
        {
            //Log.Message("[Quality Affects HP] Transpiler called!");

            var instructionsList = instructions.ToList();

            
            var newInstructions = new CodeInstruction[]
            {
                    new CodeInstruction(OpCodes.Ldloc_S, 11),
                    new CodeInstruction(OpCodes.Brtrue_S),
            };

            bool foundSkipTo = false;

            int insertBeforeIndex = -1;
            bool foundInsertBefore = false;


            for (int i = 0; i < instructionsList.Count(); ++i)
            {
                //Log.Message("[Quality Affects HP] Code Instruction at index " + i + ": " + instructionsList.ElementAt<CodeInstruction>(i).ToString());


                if (instructionsList.ElementAt<CodeInstruction>(i).ToString() == "callvirt virtual System.Void Verse.Thing::set_HitPoints(System.Int32 value)"
                  && i+1 < instructionsList.Count()) // out of bounds guard
                {
                    //for Debug
                    //Log.Warning("--- Found skipto match at index " + i + "(+1): " + instructionsList.ElementAt<CodeInstruction>(i));

                    foundSkipTo = true;

                    // create a Label
                    Label skipToLbl = ilg.DefineLabel();

                    //stick Label on instruction we want to skip to, and set as destination in skip line
                    instructionsList.ElementAt<CodeInstruction>(i + 1).WithLabels(skipToLbl);
                    newInstructions[1].operand = skipToLbl;

                }

                //if (instructionsList.ElementAt<CodeInstruction>(i).ToString() == "ldloc.s 5 (Verse.Thing) [Label15]"
                //  && i+2 < instructions.Count() // out of bounds guard
                //  && instructions.ElementAt<CodeInstruction>(i+1).ToString() == "ldarg.0 NULL"
                //  && instructions.ElementAt<CodeInstruction>(i+2).ToString() == "callvirt virtual System.Int32 Verse.Thing::get_HitPoints()")

                // -> this change should fix the furniture HP not being set correctly - verbose log I got from a player was loading Label17, not Label15 as it did for me.
                if (instructions.ElementAt<CodeInstruction>(i).ToString() == "ldarg.0 NULL"
                  && i + 1 < instructions.Count() // out of bounds guard
                  && instructions.ElementAt<CodeInstruction>(i + 1).ToString() == "callvirt virtual System.Int32 Verse.Thing::get_HitPoints()")
                {
                    //Debug
                    //Log.Warning("--- Found insert match at index " + i-1 + ": " + instructionsList.ElementAt<CodeInstruction>(i));

                    insertBeforeIndex = i-1;
                    foundInsertBefore = true;
                }

                if (foundInsertBefore && foundSkipTo)
                {
                    //Log.Message("Found what we needed - break");
                    break; //found what we needed.
                }

            }

            if (foundInsertBefore && insertBeforeIndex > 0 && foundSkipTo)
            {
                //Log.Message("Inserting new instructions!");
                instructionsList.InsertRange(insertBeforeIndex, newInstructions);

                IEnumerable<CodeInstruction> returnList = (IEnumerable<CodeInstruction>)instructionsList;

                //For Debug
                //for (int i = 0; i < returnList.Count(); ++i)
                //{
                //    Log.Message("[Quality Affects HP] Code Instruction at index " + i + ": " + returnList.ElementAt<CodeInstruction>(i).ToString());
                //}

                return returnList;
            }
            else
            {
                Log.Warning("[Quality Affects HP] encountered an issue: couldn't insert instructions in Frame.CompleteConstruction! (Turn on Verbose Logging for more info)");


                PrefsData prefs = DirectXmlLoader.ItemFromXmlFile<PrefsData>(GenFilePaths.PrefsFilePath);
                if (prefs.logVerbose)
                {
                    Log.Message("foundInsertBefore=" + foundInsertBefore + " insertBeforeIndex=" + insertBeforeIndex + " foundSkipTo=" + foundSkipTo);
                    Log.Message("printing out original instructions List:");

                    //print out the whole instructions list

                    for (int i = 0; i < instructions.Count(); ++i)
                    {
                        Log.Message("[Quality Affects HP] Code Instruction at index " + i + ": " + instructions.ElementAt<CodeInstruction>(i).ToString());
                    }
                }
            }

            return instructions;
        }
    }


       
    // Exception 1/? When things are minified with the uninstall option, they don't call SetQuality, so handle this here now.
    [HarmonyPatch(typeof(MinifyUtility))]
    [HarmonyPatch("MakeMinified")]
    class MinifyHandler
    {
        static void Postfix(MinifiedThing __result)
        {
            //Log.Message("[Quality Affects HP] Minify Postfix called!");

            if (__result != null && __result.InnerThing != null && __result.InnerThing.def != null) //&& (ThingWithComps)(__result.InnerThing).compQuality != null)
            {
                if (__result.InnerThing.def.useHitPoints)
                {
                    //Log.Message("[Quality Affects HP] Minify: result maxHP=" + __result.MaxHitPoints + " currentHP=" + __result.HitPoints);
                    //Log.Message("[Quality Affects HP] Minify: result.InnerThing maxHP=" + __result.InnerThing.MaxHitPoints + " .InnerThingcurrentHP=" + __result.InnerThing.HitPoints);
                    //__result.HitPoints = GermanQualityUtility.CalculateQualityMaxHP(__result.MaxHitPoints, __result.InnerThing.compQuality.Quality);

                    QualityCategory qc;
                    if (__result.InnerThing.TryGetQuality(out qc))
                    {
                        //Log.Message("[Quality Affects HP] Minify: Got Quality!");
                        __result.HitPoints = GermanQualityUtility.CalculateQualityMaxHP(100, qc); //Minified things in vanilla ALWAYS have 100maxhp, regardless of the InnerThing
                    }
                    else
                    {
                        //if we couldn't get Quality, it's a thing without quality, so will be 100HP and don't need to modify.

                        //Log.Message("[Quality Affects HP] Minify: Did Not get Quality!!!");
                    }


                }
            }
            else
            {
                //Log.Warning("[Quality Affects HP] encountered an issue: something was null:");
                //if (__result == null)
                //{
                //    Log.Warning("[Quality Affects HP] encountered an issue: __result was null");
                //}
                //if (__result.InnerThing == null)
                //{
                //    Log.Warning("[Quality Affects HP] encountered an issue: __result was null");
                //}
                //if (__result.def == null)
                //{
                //    Log.Warning("[Quality Affects HP] encountered an issue: __result.def was null");
                //}
                //if (__result.compQuality == null)
                //{
                //    Log.Warning("[Quality Affects HP] encountered an issue: __result.compQuality was null");
                //}
            }
        }
    }
        

}
