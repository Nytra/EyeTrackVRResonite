using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;

namespace EyeTrackVRResonite
{
    public class EyeTrackVR : ResoniteMod
    {
        public override string Name => "EyeTrackVRResonite";
        public override string Author => "PLYSHKA + dfgHiatus + galister + Artefact2 + Nytra";
        public override string Version => "3.1.0";
        public override string Link => "https://github.com/Nytra/EyeTrackVRResonite";

        public override void OnEngineInit()
        {
            _config = GetConfiguration();
            new Harmony("owo.Nytra.EyeTrackVRResonite").PatchAll();
            Engine.Current.OnShutdown += ETVROSC.Teardown;
            Engine.Current.RunPostInit(() =>
            {
                try
                {
                    _etvr = new ETVROSC(_config?.GetValue(OscPort));
                    var gen = new EyeTrackVRInterface();
                    Engine.Current.InputInterface.RegisterInputDriver(gen);
                }
                catch (Exception e)
                {
                    Warn("Module failed to initialize.");
                    Warn(e.ToString());
                }
            });
        }

        private static ETVROSC? _etvr;
        private static ModConfiguration? _config;

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> CreateDynVars = new("CreateDynVars", "Create dynamic variables", () => false);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> OscPort = new("osc_port", "EyeTrackVR OSC port", () => 9000);

        public static ValueStream<float> CreateStream(World world, string parameter)
        {
            return world.LocalUser.GetStreamOrAdd<ValueStream<float>>(parameter, stream =>
            {
                stream.Name = parameter;
                stream.SetUpdatePeriod(4, 0);
                stream.SetInterpolation();
                stream.Encoding = ValueEncoding.Quantized;
                stream.FullFrameBits = 8;
                stream.FullFrameMin = -1;
                stream.FullFrameMax = 1;
            });
        }

        public static void CreateVariable(Slot dvslot, string parameter, ValueStream<float> stream)
        {
            var dv = dvslot.AttachComponent<DynamicValueVariable<float>>();
            dv.VariableName.Value = "User/" + parameter;
            var dvdriver = dvslot.AttachComponent<ValueDriver<float>>();
            dvdriver.ValueSource.Target = stream;
            dvdriver.DriveTarget.Target = dv.Value;
        }

        [HarmonyPatch(typeof(UserRoot), "OnStart")]
        class VRCFTReceiverPatch
        {
            public static void Postfix(UserRoot __instance)
            {
                if (!__instance.ActiveUser.IsLocalUser) return;

                if (!_config!.GetValue(CreateDynVars)) return;

                var dvslot = __instance.Slot.FindChildOrAdd("VRCFTReceiver", true);

                if (!EyeTrackVRInterface.Lookups.TryGetValue(__instance.World, out var lookup))
                {
                    lookup = new();
                    EyeTrackVRInterface.Lookups[__instance.World] = lookup;
                }

                foreach (var key in EyeTrackVRInterface.FaceTrackParams)
                {
                    var stream = CreateStream(__instance.World, key);
                    CreateVariable(dvslot, key, stream);
                    lookup[key] = stream;
                }
            }
        }

        private class EyeTrackVRInterface : IInputDriver
        {
            private Eyes? _eyes;
            private Mouth? _mouth;
            private const float DefaultPupilSize = 0.0035f;
            public int UpdateOrder => 100;

            public static Dictionary<World, Dictionary<string, ValueStream<float>?>> Lookups = new();

            public static HashSet<string> FaceTrackParams =
            [
                "SmileSadLeft",
                "SmileSadRight",
                "BrowExpressionLeft",
                "BrowExpressionRight",
                "MouthStretchTightenLeft",
                "MouthStretchTightenRight",
                "MouthClosed",
                "MouthUpperUp",
                "MouthLowerDown",
                "MouthX",
                "JawX",
                "JawOpen",
                "CheekPuffLeft",
                "CheekPuffRight",
                "LipPucker",
                "LipFunnelUpper",
                "LipFunnelLower",
                "EyeLidLeft",
                "EyeLidRight",
                "EyeSquintLeft",
                "EyeSquintRight"
            ];

            public void CollectDeviceInfos(DataTreeList list)
            {
                var eyeDataTreeDictionary = new DataTreeDictionary();
                eyeDataTreeDictionary.Add("Name", "EyeTrackVR Eye Tracking");
                eyeDataTreeDictionary.Add("Type", "Eye Tracking");
                eyeDataTreeDictionary.Add("Model", "ETVR Module");
                list.Add(eyeDataTreeDictionary);

                DataTreeDictionary dict2 = new DataTreeDictionary();
                dict2.Add("Name", "EyeTrackVR Mouth Tracking");
                dict2.Add("Type", "Lip Tracking");
                dict2.Add("Model", "ETVR Module");
                list.Add(dict2);
            }

            public void RegisterInputs(InputInterface inputInterface)
            {
                _eyes = new Eyes(inputInterface, "EyeTrackVR Eye Tracking", true);
                _mouth = new Mouth(inputInterface, "EyeTrackVR Mouth Tracking",
                [
                    MouthParameterGroup.JawPose,
                    MouthParameterGroup.JawOpen,
                    MouthParameterGroup.LipRaise,
                    MouthParameterGroup.LipHorizontal,
                    MouthParameterGroup.SmileFrown,
                    MouthParameterGroup.MouthPout,
                    MouthParameterGroup.LipOverturn,
                    MouthParameterGroup.LipStretchTighten,
                    MouthParameterGroup.CheekPuffSuck,
                ]);
            }

            public void UpdateInputs(float deltaTime)
            {
                var focusedWorld = Engine.Current.WorldManager?.FocusedWorld;

                if (focusedWorld != null)
                {
                    // user root if null
                    if (focusedWorld.LocalUser.Root == null)
                    {
                        Warn("Root not Found");
                        return;
                    }

                    if (_config!.GetValue(CreateDynVars))
                    {
                        // Get or create lookup for world
                        if (!Lookups.TryGetValue(focusedWorld, out var lookup))
                        {
                            lookup = new();
                            Lookups[focusedWorld] = lookup;
                        }

                        foreach (var kvp in _etvr!.Parameters)
                        {
                            if (!FaceTrackParams.Contains(kvp.Key))
                                continue;

                            if (!lookup.TryGetValue(kvp.Key, out var stream) || (stream != null && stream.IsRemoved))
                            {
                                lookup[kvp.Key] = null;
                                focusedWorld.RunInUpdates(0, () =>
                                {
                                    var s = CreateStream(focusedWorld, kvp.Key);
                                    s.Value = kvp.Value;
                                    s.ForceUpdate();
                                    lookup[kvp.Key] = s;
                                });
                            }

                            if (stream != null)
                            {
                                stream.Value = kvp.Value;
                                stream.ForceUpdate();
                            }
                        }
                    }
                }

                _eyes!.CombinedEye.IsDeviceActive = Engine.Current.InputInterface.VR_Active;
                _eyes.CombinedEye.IsTracking = _etvr!.LastUpdate > DateTime.Now.AddSeconds(-5);
                _eyes.CombinedEye.PupilDiameter = DefaultPupilSize;

                _eyes.LeftEye.RawPosition = float3.Zero;
                _eyes.RightEye.RawPosition = float3.Zero;

                var eyeLidLeft = Parameter("EyeLidLeft");
                var eyeLidRight = Parameter("EyeLidRight");

                _eyes.LeftEye.Openness = MathX.Remap(eyeLidLeft, 0f, 0.75f, 0f, 1f);
                _eyes.RightEye.Openness = MathX.Remap(eyeLidRight, 0f, 0.75f, 0f, 1f);

                _eyes.LeftEye.Widen = MathX.Remap(eyeLidLeft, 0.75f, 1f, 0f, 1f);
                _eyes.RightEye.Widen = MathX.Remap(eyeLidRight, 0.75f, 1f, 0f, 1f);

                var eyeSquintLeft = Parameter("EyeSquintLeft");
                var eyeSquintRight = Parameter("EyeSquintRight");

                _eyes.LeftEye.Squeeze = eyeSquintLeft;
                _eyes.RightEye.Squeeze = eyeSquintRight;
                _eyes.LeftEye.Frown = eyeSquintLeft;
                _eyes.RightEye.Frown = eyeSquintRight;

                var leftEyeRot = floatQ.Euler(
                    _etvr.EyeLeftRightEuler[0].x,
                    _etvr.EyeLeftRightEuler[0].y,
                    0);
                var rightEyeRot = floatQ.Euler(
                    _etvr.EyeLeftRightEuler[1].x,
                    _etvr.EyeLeftRightEuler[1].y,
                    0);

                _eyes.LeftEye.UpdateWithRotation(leftEyeRot);
                _eyes.RightEye.UpdateWithRotation(rightEyeRot);
                _eyes.CombinedEye.UpdateWithRotation(leftEyeRot);

                CombineEyeData();

                _eyes.LeftEye.InnerBrowVertical = Parameter("BrowExpressionLeft");
                _eyes.LeftEye.OuterBrowVertical = Parameter("BrowExpressionLeft");
                _eyes.RightEye.InnerBrowVertical = Parameter("BrowExpressionRight");
                _eyes.RightEye.OuterBrowVertical = Parameter("BrowExpressionRight");

                _eyes.ConvergenceDistance = 0f;
                _eyes.Timestamp += deltaTime;
                _eyes.FinishUpdate();

                _mouth!.IsTracking = _etvr.LastUpdate > DateTime.Now.AddSeconds(-5);
                _mouth.IsDeviceActive = Engine.Current.InputInterface.VR_Active;

                _mouth.Jaw = new float3(Parameter("JawX"), -Parameter("MouthClosed"), 0);
                _mouth.JawOpen = MathX.Clamp01(Parameter("JawOpen") - Parameter("MouthClosed"));

                _mouth.MouthLeftSmileFrown = Parameter("SmileSadLeft");
                _mouth.MouthRightSmileFrown = Parameter("SmileSadRight");

                _mouth.CheekLeftPuffSuck = Parameter("CheekPuffLeft");
                _mouth.CheekRightPuffSuck = Parameter("CheekPuffRight");

                _mouth.LipLeftStretchTighten = Parameter("MouthStretchTightenLeft");
                _mouth.LipRightStretchTighten = Parameter("MouthStretchTightenRight");

                _mouth.MouthPoutLeft = Parameter("LipPucker");
                _mouth.MouthPoutRight = Parameter("LipPucker");

                _mouth.LipTopLeftOverturn = Parameter("LipFunnelUpper");
                _mouth.LipTopRightOverturn = Parameter("LipFunnelUpper");
                _mouth.LipBottomLeftOverturn = Parameter("LipFunnelLower");
                _mouth.LipBottomRightOverturn = Parameter("LipFunnelLower");

                _mouth.LipUpperLeftRaise = Parameter("MouthUpperUp");
                _mouth.LipUpperRightRaise = Parameter("MouthUpperUp");

                _mouth.LipLowerLeftRaise = Parameter("MouthLowerDown");
                _mouth.LipLowerRightRaise = Parameter("MouthLowerDown");

                _mouth.LipUpperHorizontal = Parameter("MouthX");
            }

            private float Parameter(string key)
            {
                if (_etvr!.Parameters.TryGetValue(key, out var val))
                    return val;
                return 0;
            }

            private void CombineEyeData()
            {
                _eyes!.IsEyeTrackingActive = _eyes.CombinedEye.IsTracking;
                _eyes.IsDeviceActive = _eyes.CombinedEye.IsDeviceActive;
                _eyes.IsTracking = _eyes.CombinedEye.IsTracking;

                _eyes.LeftEye.IsDeviceActive = _eyes.CombinedEye.IsDeviceActive;
                _eyes.RightEye.IsDeviceActive = _eyes.CombinedEye.IsDeviceActive;
                _eyes.LeftEye.IsTracking = _eyes.CombinedEye.IsTracking;
                _eyes.RightEye.IsTracking = _eyes.CombinedEye.IsTracking;
                _eyes.LeftEye.PupilDiameter = _eyes.CombinedEye.PupilDiameter;
                _eyes.RightEye.PupilDiameter = _eyes.CombinedEye.PupilDiameter;

                _eyes.CombinedEye.IsTracking = false;

                _eyes.CombinedEye.RawPosition = MathX.Average(_eyes.LeftEye.RawPosition, _eyes.RightEye.RawPosition);

                _eyes.CombinedEye.Openness = MathX.Average(_eyes.LeftEye.Openness, _eyes.RightEye.Openness);
                _eyes.CombinedEye.Widen = MathX.Average(_eyes.LeftEye.Widen, _eyes.RightEye.Widen);
                _eyes.CombinedEye.Squeeze = MathX.Average(_eyes.LeftEye.Squeeze, _eyes.RightEye.Squeeze);
                _eyes.CombinedEye.Frown = MathX.Average(_eyes.LeftEye.Frown, _eyes.RightEye.Frown);
            }

            private static Func<float, KeyValuePair<string, float>> MkParam(string key, float min, float max)
            {
                return (float val) => new KeyValuePair<string, float>(key, MathX.Remap(val, min, max, 0f, 1f));
            }

            private static Func<float, KeyValuePair<string, float>> MkParam(string key)
            {
                return (float val) => new KeyValuePair<string, float>(key, val);
            }
        }
    }
}