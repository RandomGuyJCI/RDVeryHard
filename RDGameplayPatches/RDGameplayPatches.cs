﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RDGameplayPatches
{
    [BepInPlugin("com.rhythmdr.gameplaypatches", "Rhythm Doctor Gameplay Patches", "1.6.0")]
    [BepInProcess("Rhythm Doctor.exe")]
    public class RDGameplayPatches : BaseUnityPlugin
    {
        private static ConfigEntry<VeryHardMode> configVeryHardMode;
        private ConfigEntry<bool> configAccurateReleaseMargins;
        private ConfigEntry<bool> configCountOffsetOnRelease;
        private ConfigEntry<bool> configAntiCheeseHolds;
        private ConfigEntry<bool> configPersistentP1AndP2Positions;
        private ConfigEntry<bool> configRankColorOnSpeedChange;
        private ConfigEntry<bool> configChangeRankButtonPerDifficulty;
        private enum VeryHardMode
        {
            None,
            P1,
            P2,
            Both,
        }
        
        void Awake()
        {
            configVeryHardMode = Config.Bind("Difficulty", "VeryHardMode", VeryHardMode.None,
                "Sets the player(s) in which Very Hard difficulty is enabled. Not affected by the difficulty setting in Rhythm Doctor when enabled.");

            configAccurateReleaseMargins = Config.Bind("Holds", "AccurateReleaseMargins", false,
                "Changes the hold release margins to better reflect the player difficulty, including Very Hard.");

            configCountOffsetOnRelease = Config.Bind("Holds", "CountOffsetOnRelease", true,
                "Shows the millisecond offset and counts the number of offset frames on hold releases.");

            configAntiCheeseHolds = Config.Bind("Holds", "AntiCheeseHolds", true,
                "Prevents you from cheesing levels by abusing a hold's auto-hits.");

            configPersistentP1AndP2Positions = Config.Bind("2P", "PersistentP1AndP2Positions", true,
                "Reverts back to old game behavior and makes P1 and P2 positions persistent between level restarts.");

            configRankColorOnSpeedChange = Config.Bind("Results", "RankColorOnSpeedChange", true,
                "Changes the color of the rank text depending on the level's speed (blue on chill speed, red on chili speed).");

            configChangeRankButtonPerDifficulty = Config.Bind("Results", "ChangeSmallHandPerDifficulty", true,
                "Changes the player's button in the rank screen depending on the difficulty.");

            switch (configVeryHardMode.Value)
            {
                case VeryHardMode.P1:
                case VeryHardMode.P2:
                case VeryHardMode.Both:
                    Harmony.CreateAndPatchAll(typeof(VeryHard));
                    break;
                case VeryHardMode.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (configAccurateReleaseMargins.Value)
                Harmony.CreateAndPatchAll(typeof(AccurateReleaseMargins));

            if (configAntiCheeseHolds.Value)
                Harmony.CreateAndPatchAll(typeof(AntiCheeseHolds));

            if (configCountOffsetOnRelease.Value)
                Harmony.CreateAndPatchAll(typeof(CountOffsetOnRelease));

            if (configPersistentP1AndP2Positions.Value)
                Harmony.CreateAndPatchAll(typeof(PersistentP1AndP2Positions));
            
            if (configRankColorOnSpeedChange.Value)
                Harmony.CreateAndPatchAll(typeof(RankColorOnSpeedChange));
            
            if (configChangeRankButtonPerDifficulty.Value)
                Harmony.CreateAndPatchAll(typeof(ChangeRankButtonPerDifficulty));

            Logger.LogInfo("Plugin enabled!");
        }

        void OnDestroy()
        {
            Harmony.UnpatchAll();
        }

        public static class VeryHard
        {
            private static readonly bool isP1VeryHard = configVeryHardMode.Value == VeryHardMode.P1 || configVeryHardMode.Value == VeryHardMode.Both;
            private static readonly bool isP2VeryHard = configVeryHardMode.Value == VeryHardMode.P2 || configVeryHardMode.Value == VeryHardMode.Both;

            [HarmonyPostfix]
            [HarmonyPatch(typeof(scnGame), "GetHitMargin")]
            public static void Postfix(RDPlayer player, ref float __result)
            {
                if ((player == RDPlayer.P1 && isP1VeryHard) || (player == RDPlayer.P2 && isP2VeryHard))
                    __result = 0.025f;
            }

            [HarmonyPatch(typeof(RDHitStrip), "Setup")]
            public static void Postfix(RDPlayer player, RDHitStrip __instance)
            {
                if ((player == RDPlayer.P1 && isP1VeryHard) || (player == RDPlayer.P2 && isP2VeryHard))
                    __instance.quad.size = new Vector2(8f, __instance.quad.size.y);
            }

            [HarmonyPatch(typeof(scnGame), "Awake")]
            public static void Postfix()
            {
                if (isP1VeryHard) scnGame.p1DefibMode = DefibMode.Hard;
                if (isP2VeryHard) scnGame.p2DefibMode = DefibMode.Hard;
            }
        }

        public static class AccurateReleaseMargins
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(scnGame), "GetReleaseMargin")]
            public static void Postfix(RDPlayer player, ref float __result)
            {
                __result = scnGame.GetHitMargin(player);
            }

            [HarmonyPatch(typeof(scrPlayerbox), "releaseOffsetType", MethodType.Getter)]
            public static void Postfix(scrRowEntities ___ent, ref OffsetType __result)
            {
                var player = ___ent.row.playerProp.GetCurrentPlayer();
                if ((player == RDPlayer.P2 ? scnGame.p2DefibMode : scnGame.p1DefibMode) == DefibMode.Unmissable)
                    __result = OffsetType.Perfect;
            }
        }

        public static class CountOffsetOnRelease
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(scrPlayerbox), "SpaceBarReleased")]
            public static bool Prefix(RDPlayer player, bool cpuTriggered, scrPlayerbox __instance, double ___beatReleaseTime)
            {
                if ((player != __instance.player) || (!__instance.beatBeingHeld && !cpuTriggered)) return true;

                var audioPos = __instance.conductor.audioPos;
                var timeOffset = (float) (audioPos - ___beatReleaseTime);
                
                if (GC.showAbsoluteOffsets)
                {
                    var offsetFrames = Mathf.RoundToInt(timeOffset * 60);

                    if (RDC.auto || Mathf.Abs(offsetFrames) <= 1)
                        offsetFrames = 0;

                    if (__instance.player != RDPlayer.CPU)
                        __instance.game.mistakesManager.AddAbsoluteMistake(__instance.player, offsetFrames);
                }

                if (GC.d_showMarginsNumerically)
                {
                    var timeOffsetInMilliseconds = timeOffset * 1000f;
                    var offsetMs = timeOffsetInMilliseconds.ToString("N3");

                    if (timeOffsetInMilliseconds >= 0)
                        offsetMs = "+" + offsetMs;

                    HUD.status = "[ " + offsetMs + " " + RDString.Get("editor.unit.ms") + " ]";
                }

                return true;
            }
        }

        public static class AntiCheeseHolds
        {
            private static double holdReleaseTime;

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Beat), "LateUpdate")]
            public static bool Prefix(Beat __instance)
            {
                if (RDC.auto) return true;
                
                var player = __instance.row.playerProp.GetCurrentPlayer();
                var isPlayerHolding = __instance.game.rows.Any(row => 
                    row.playerBox != null && 
                    player == row.playerProp.GetCurrentPlayer() &&
                    row.playerBox.beatBeingHeld);
                    
                if (player != RDPlayer.CPU && isPlayerHolding)
                {
                    if (__instance.isHeldClap && holdReleaseTime != __instance.releaseTime)
                        holdReleaseTime = __instance.releaseTime;

                    var emuState = RDInput.emuStates[(int)player];

                    if (!__instance.isHeldClap && __instance.conductor.audioPos >= __instance.inputTime && __instance.inputTime <= holdReleaseTime)
                    {
                        emuState.SetKey(RDInput.PlayerEmuKey.Down);
                        Timer.Add(delegate { emuState.SetKey(RDInput.PlayerEmuKey.IsUp); }, 0.2f);
                    }

                    if (__instance.isHeldClap && __instance.conductor.audioPos >= __instance.releaseTime + 0.40000000596046448)
                        emuState.SetKey(RDInput.PlayerEmuKey.Up);
                }

                if (__instance.dead) __instance.DestroyBeat(killSounds: false, evenIfHalfwayPlaying: false);
                
                return false;
            }
        }

        public static class PersistentP1AndP2Positions
        {
            [HarmonyTranspiler]
            [HarmonyPatch(typeof(scnGame), "Start")]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return new CodeMatcher(instructions)
                    .MatchForward(false,
                        new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(RDInput), "SwapP1AndP2Controls")))
                    .SetOpcodeAndAdvance(OpCodes.Nop)
                    // Start of stupid fix for Level_Intro issue (this took me FIVE DAYS to debug)
                    .MatchForward(false,
                        new CodeMatch(OpCodes.Ldstr, "Level_"))
                    .Advance(3)
                    .InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldstr, ", Assembly-CSharp"),
                        new CodeInstruction(OpCodes.Call, AccessTools.Method("System.String:Concat", new [] { typeof(string), typeof(string) })))
                    // End of stupid fix
                    .InstructionEnumeration();
            }
        }

        public static class RankColorOnSpeedChange
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(HUD), "ShowAndSaveRank")]
            public static void Postfix(HUD __instance)
            {
                var levelType = __instance.game.currentLevel.levelType;

                if (levelType == LevelType.Boss || levelType == LevelType.Challenge) return;
                
                if (RDTime.speed > 1f)
                    __instance.rank.color = new Color(0.93f, 0.44f, 0.44f);
                    
                if (RDTime.speed < 1f)
                    __instance.rank.color = new Color(0.44f, 0.85f, 0.93f);
            }
        }

        public static class ChangeRankButtonPerDifficulty
        {
            private const string assetsPath = "BepInEx/plugins/RDGameplayPatches/Assets/";

            [HarmonyPostfix]
            [HarmonyPatch(typeof(HUD), "Setup")]
            public static void Postfix(ref Image ___smallHand)
            {
                var hardSmallHand1 = new Texture2D(47, 24);
                hardSmallHand1.LoadImage(File.ReadAllBytes(assetsPath + "hard-small-hand-1.png"));
                hardSmallHand1.filterMode = FilterMode.Point;
                
                var hardSmallHand2 = new Texture2D(47, 24);
                hardSmallHand2.LoadImage(File.ReadAllBytes(assetsPath + "hard-small-hand-2.png"));
                hardSmallHand2.filterMode = FilterMode.Point;
                
                var easySmallHand1 = new Texture2D(47, 24);
                easySmallHand1.LoadImage(File.ReadAllBytes(assetsPath + "easy-small-hand-1.png"));
                easySmallHand1.filterMode = FilterMode.Point;
                
                var easySmallHand2 = new Texture2D(47, 24);
                easySmallHand2.LoadImage(File.ReadAllBytes(assetsPath + "easy-small-hand-2.png"));
                easySmallHand2.filterMode = FilterMode.Point;
                
                var rect = new Rect(0.0f, 0.0f, 47, 24);
                var vector = new Vector2(0.5f, 0.5f);

                var hardSmallHandSprite = Sprite.Create(hardSmallHand1, rect, vector);
                var easySmallHandSprite = Sprite.Create(easySmallHand1, rect, vector);
                Sprite[] hardSmallHandSprites = { hardSmallHandSprite, Sprite.Create(hardSmallHand2, rect, vector) };
                Sprite[] easySmallHandSprites = { easySmallHandSprite, Sprite.Create(easySmallHand2, rect, vector) };

                var spriteAnimComponent = ___smallHand.GetComponent<SpriteAnimation>();
                var imageComponent = ___smallHand.GetComponent<Image>();
                
                if (scnGame.p1DefibMode > DefibMode.Normal)
                {
                    imageComponent.sprite = hardSmallHandSprite;
                    spriteAnimComponent.currentAnimationData.sprites = hardSmallHandSprites;
                }

                if (scnGame.p1DefibMode < DefibMode.Normal)
                {
                    imageComponent.sprite = easySmallHandSprite;
                    spriteAnimComponent.currentAnimationData.sprites = easySmallHandSprites;
                }
            }
        }
    }
}
