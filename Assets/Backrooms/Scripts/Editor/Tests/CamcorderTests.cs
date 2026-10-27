using Backrooms.Camcorder;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The camcorder picture: the camera's transfer curve and knee, the recording formats, presets, the settings
    /// migration, the auto white balance's limits, and the shader itself.</summary>
    public class CamcorderTests
    {
        [Test]
        public void TransferCurveAndKnee()
        {
            // Rec.709: toe slope 4.5 joined at 0.018, white at 1.
            Assert.AreEqual(4.5f * 0.018f, CamcorderRig.Oetf(0.018f, 0.45f), 2e-3f);
            Assert.AreEqual(1f, CamcorderRig.Oetf(1f, 0.45f), 1e-4f);
            Assert.AreEqual(CamcorderRig.Oetf(0.0179f, 0.5f), CamcorderRig.Oetf(0.0181f, 0.5f), 1e-3f, "continuous at the toe for any gamma");
            // The knee brings 400 % light exactly to the white clip.
            foreach (var kp in new[] { 0.8f, 0.85f, 0.9f })
            {
                float ks = CamcorderRig.KneeSlope(kp, 1f, 0.45f);
                Assert.That(ks, Is.InRange(0.05f, 0.5f));
                Assert.AreEqual(1f, kp + (CamcorderRig.Oetf(4f, 0.45f) - kp) * ks, 1e-4f);
            }
            Assert.AreEqual(1f, CamcorderRig.KneeSlope(1f, 1f, 0.45f), "knee point at the clip: no knee");
        }

        [Test]
        public void FormatsDoNotMix()
        {
            for (int i = 0; i < CamcorderFormat.Names.Length; i++)
            {
                var f = CamcorderFormat.Get(i);
                var w = CamcorderFormat.LumaWeights(f.lumaSigma);
                Assert.AreEqual(1f, w.x + 2f * w.y + 2f * w.z, 1e-5f, f.name + " luma filter keeps brightness");
                if (f.analog)
                {
                    Assert.AreEqual(0f, f.compression, f.name + ": tape has no DCT blocks");
                    Assert.Greater(f.chromaSigma, 2f, f.name + ": colour-under is narrow");
                    Assert.AreEqual(0.075f, f.setup, 1e-4f, "NTSC analog setup");
                }
                else
                {
                    Assert.AreEqual(0f, f.jitter + f.flagging + f.dropouts + f.lumaNoise + f.headSwitchLines + f.crossColour, f.name + ": no tape artefacts");
                    Assert.IsTrue(f.dv411);
                    Assert.AreEqual(0f, f.setup);
                }
            }
            // Sharper formats keep more of the centre tap.
            Assert.Greater(CamcorderFormat.LumaWeights(CamcorderFormat.Get(3).lumaSigma).x, CamcorderFormat.LumaWeights(CamcorderFormat.Get(2).lumaSigma).x);
            Assert.Greater(CamcorderFormat.LumaWeights(CamcorderFormat.Get(2).lumaSigma).x, CamcorderFormat.LumaWeights(CamcorderFormat.Get(1).lumaSigma).x);
        }

        [Test]
        public void PresetsAreRealCameras()
        {
            for (int i = 0; i < CamcorderSettings.PresetNames.Length; i++)
            {
                var s = CamcorderSettings.BuiltIn(i);
                string n = CamcorderSettings.PresetNames[i];
                Assert.AreEqual(CamcorderSettings.CurrentLookVersion, s.lookVersion, n);
                Assert.GreaterOrEqual(s.lensDistortion, 0f, n + ": wide camcorder lenses are barrel (positive)");
                Assert.LessOrEqual(s.chromaticAberration, 0.06f, n);
                Assert.LessOrEqual(s.scanlines, 0f, n + ": a recording has no scanlines");
                Assert.AreEqual(0, s.shutter, n + ": the auto shutter is 1/60");
                Assert.AreEqual(CamcorderFormat.Get(s.format).setup > 0f ? 0.075f : s.pedestal, s.pedestal, 1e-4f, n + ": setup comes with the format");
            }
            Assert.IsFalse(CamcorderSettings.BuiltIn(4).camcorderLook, "Clean is the plain image");
            Assert.AreEqual(536, CamcorderSettings.BuiltIn(7).videoLines, "Reference Match keeps the measured line count");
            Assert.AreEqual(1f / 60f, new CamcorderSettings().ShutterTime, 1e-6f);
        }

        [Test]
        public void MigrationKeepsThePlayersChoices()
        {
            var old = JsonUtility.FromJson<CamcorderSettings>("{\"fieldOfView\":75,\"aspect43\":false,\"lensDistortion\":-0.08,\"sharpening\":4," +
                "\"handheldRoughness\":1.3,\"audioVolume\":0.5,\"videoLines\":536,\"lineDoubling\":1,\"bottomLine\":1}");
            Assert.AreEqual(0, old.lookVersion, "saves from before the rebuild have no version");
            var n = CamcorderSettings.Migrate(old, 7);
            Assert.AreEqual(CamcorderSettings.CurrentLookVersion, n.lookVersion);
            Assert.AreEqual(75f, n.fieldOfView); Assert.IsFalse(n.aspect43); Assert.AreEqual(1.3f, n.handheldRoughness);
            Assert.AreEqual(0.5f, n.audioVolume); Assert.AreEqual(536, n.videoLines); Assert.AreEqual(1f, n.lineDoubling);
            Assert.Greater(n.lensDistortion, 0f, "the old negative (pincushion) value is replaced");
            Assert.AreEqual(CamcorderSettings.BuiltIn(7).sharpening, n.sharpening, "picture settings take the preset's new values");
        }

        [Test]
        public void AutoWhiteBalanceIsLimited()
        {
            var g = CamcorderAutoExposure.AwbGains(new Vector3(0.5f, 0.5f, 0.5f), 1f);
            Assert.AreEqual(1f, g.x, 1e-4f); Assert.AreEqual(1f, g.y, 1e-4f); Assert.AreEqual(1f, g.z, 1e-4f);
            // A yellow room: blue up, red down, brightness kept, never fully neutralised.
            var y = CamcorderAutoExposure.AwbGains(new Vector3(1f, 0.85f, 0.35f), 1f);
            Assert.Greater(y.z, 1f); Assert.Less(y.x, 1f);
            Assert.AreEqual(1f, 0.2126f * y.x + 0.7152f * y.y + 0.0722f * y.z, 1e-4f);
            Assert.Less(y.z / y.x, 1f / 0.35f, "limited like a real AWB: never fully neutralised");
            var weak = CamcorderAutoExposure.AwbGains(new Vector3(1f, 0.85f, 0.35f), 0.3f);
            Assert.Less(weak.z / weak.x, y.z / y.x);
        }

        [Test]
        public void ShaderCompiles()
        {
            var shader = Shader.Find("Hidden/Backrooms/CamcorderPipeline");
            Assert.IsNotNull(shader);
            Assert.AreEqual(6, shader.passCount, "integrate, meter, smear, head, encode, display");
            var mat = new Material(shader);
            try { for (int i = 0; i < shader.passCount; i++) ShaderUtil.CompilePass(mat, i, true); }
            finally { Object.DestroyImmediate(mat); }
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, m.severity, m.message);
            foreach (var k in shader.keywordSpace.keywords)
                Assert.AreEqual(UnityEngine.Rendering.ShaderKeywordType.BuiltinDefault, k.type, "no keyword variants of its own: " + k.name);
        }
    }
}
