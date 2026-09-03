using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YoridoriModifiers.FacialMapper
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Yoridori Modifiers/YM Facial Mapper")]
    public sealed class YMFacialMapper : MonoBehaviour, IEditorOnly
    {
        public enum HandSign
        {
            Neutral = 0,
            Fist = 1,
            HandOpen = 2,
            FingerPoint = 3,
            Victory = 4,
            RockNRoll = 5,
            HandGun = 6,
            ThumbsUp = 7
        }

        public enum HandSide
        {
            Left = 0,
            Right = 1
        }

        public enum ConflictPriority
        {
            Right = 0,
            Left = 1
        }

        public static readonly HandSign[] HandSignOrder =
        {
            HandSign.Fist,
            HandSign.HandOpen,
            HandSign.FingerPoint,
            HandSign.Victory,
            HandSign.RockNRoll,
            HandSign.HandGun,
            HandSign.ThumbsUp
        };

        [Serializable]
        public sealed class ExpressionSlot
        {
            public bool stopEyelidLeft;
            public bool stopEyelidRight;
            public bool stopViseme;
            public List<string> shapeKeys = new List<string>();
        }

        [Serializable]
        public sealed class HandSignSetting
        {
            public HandSign sign;
            public ExpressionSlot left = new ExpressionSlot();
            public ExpressionSlot right = new ExpressionSlot();
        }

        public ExpressionSlot neutral = new ExpressionSlot();

        public List<HandSignSetting> handSigns = new List<HandSignSetting>
        {
            new HandSignSetting { sign = HandSign.Fist },
            new HandSignSetting { sign = HandSign.HandOpen },
            new HandSignSetting { sign = HandSign.FingerPoint },
            new HandSignSetting { sign = HandSign.Victory },
            new HandSignSetting { sign = HandSign.RockNRoll },
            new HandSignSetting { sign = HandSign.HandGun },
            new HandSignSetting { sign = HandSign.ThumbsUp }
        };

        [TextArea(3, 10)]
        public string presetMemo = string.Empty;

        public ConflictPriority conflictPriority = ConflictPriority.Right;

        public string disableHandGesturesParameter = string.Empty;

        public bool writeDefaults;

        public bool verboseLog;
    }

    public static class YMFacialMapperDefaults
    {
        public const string EyeMouthSplitForVRoidName = "Eye-Mouth Split for VRoid";

        public const string EyeMouthSplitForVRoidMemo =
            "◆ 左手に眉と目、右手に口  Left: Eyebrows+Eyes / Right: Mouth\n" +
            "　Fist: Angry\n" +
            "　Victory: Fun\n" +
            "　RockNRoll: Sorrow\n" +
            "　HandGun: Surprised\n" +
            "　ThumbsUp: Joy\n\n" +
            "◆ 左手に左目、右手に右目  Left: Left eye / Right: Right eye\n" +
            "　HandOpen: Joy\n" +
            "　FingerPoint: Close";

        public static void ApplyEyeMouthSplitForVRoid(YMFacialMapper component)
        {
            if (component == null) return;

            component.presetMemo = EyeMouthSplitForVRoidMemo;
            ConfigureSlot(component.neutral, false, false, false, "Fcl_ALL_Neutral");

            EnsureHandSigns(component);
            foreach (var setting in component.handSigns)
            {
                switch (setting.sign)
                {
                    case YMFacialMapper.HandSign.Fist:
                        ConfigureSlot(setting.left, true, true, false, "Fcl_BRW_Angry", "Fcl_EYE_Angry");
                        ConfigureSlot(setting.right, false, false, false, "Fcl_MTH_Angry");
                        break;
                    case YMFacialMapper.HandSign.HandOpen:
                        ConfigureSlot(setting.left, true, false, false, "Fcl_EYE_Joy_L");
                        ConfigureSlot(setting.right, false, true, false, "Fcl_EYE_Joy_R");
                        break;
                    case YMFacialMapper.HandSign.FingerPoint:
                        ConfigureSlot(setting.left, true, false, false, "Fcl_EYE_Close_L");
                        ConfigureSlot(setting.right, false, true, false, "Fcl_EYE_Close_R");
                        break;
                    case YMFacialMapper.HandSign.Victory:
                        ConfigureSlot(setting.left, true, true, false, "Fcl_BRW_Fun", "Fcl_EYE_Fun");
                        ConfigureSlot(setting.right, false, false, false, "Fcl_MTH_Fun");
                        break;
                    case YMFacialMapper.HandSign.RockNRoll:
                        ConfigureSlot(setting.left, true, true, false, "Fcl_BRW_Sorrow", "Fcl_EYE_Sorrow");
                        ConfigureSlot(setting.right, false, false, false, "Fcl_MTH_Sorrow");
                        break;
                    case YMFacialMapper.HandSign.HandGun:
                        ConfigureSlot(setting.left, true, true, false, "Fcl_BRW_Surprised", "Fcl_EYE_Surprised");
                        ConfigureSlot(setting.right, false, false, true, "Fcl_MTH_Surprised");
                        break;
                    case YMFacialMapper.HandSign.ThumbsUp:
                        ConfigureSlot(setting.left, true, true, false, "Fcl_BRW_Joy", "Fcl_EYE_Joy");
                        ConfigureSlot(setting.right, false, false, true, "Fcl_MTH_Joy");
                        break;
                }
            }
        }

        public static void EnsureHandSigns(YMFacialMapper component)
        {
            if (component == null) return;
            component.handSigns ??= new List<YMFacialMapper.HandSignSetting>();

            foreach (var sign in YMFacialMapper.HandSignOrder)
            {
                if (component.handSigns.Exists(setting => setting != null && setting.sign == sign)) continue;
                component.handSigns.Add(new YMFacialMapper.HandSignSetting { sign = sign });
            }

            component.handSigns.RemoveAll(setting => setting == null || setting.sign == YMFacialMapper.HandSign.Neutral);
            component.handSigns.Sort((a, b) => Array.IndexOf(YMFacialMapper.HandSignOrder, a.sign)
                .CompareTo(Array.IndexOf(YMFacialMapper.HandSignOrder, b.sign)));
        }

        private static void ConfigureSlot(
            YMFacialMapper.ExpressionSlot slot,
            bool eyelidLeft,
            bool eyelidRight,
            bool viseme,
            params string[] shapeKeys)
        {
            if (slot == null) return;
            slot.stopEyelidLeft = eyelidLeft;
            slot.stopEyelidRight = eyelidRight;
            slot.stopViseme = viseme;
            slot.shapeKeys = shapeKeys != null
                ? new List<string>(shapeKeys)
                : new List<string>();
        }
    }
}
