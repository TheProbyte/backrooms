using System.IO;
using Backrooms.UI;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>UI pass 5 (Docs/UiPlan.md U19): one set of five interface sounds used everywhere; menus that slide in,
    /// and REDUCE MOTION stills them.</summary>
    public class UiPass5Tests
    {
        [Test]
        public void FiveDistinctSoundsMoveShortestRefuseLowest()
        {
            var clips = new AudioClip[5];
            for (int i = 0; i < 5; i++) { clips[i] = UiSounds.Clip((UiSound)i); Assert.IsNotNull(clips[i]); }
            for (int i = 0; i < 5; i++) for (int j = i + 1; j < 5; j++) Assert.AreNotSame(clips[i], clips[j]);
            // A move is the lightest: the shortest of them.
            for (int i = 1; i < 5; i++) Assert.Less(clips[0].length, clips[i].length, ((UiSound)i).ToString());
            // A refusal is low: fewer zero crossings a second than a move.
            Assert.Less(Crossings(clips[(int)UiSound.Refuse]), Crossings(clips[(int)UiSound.Move]) * 0.5f);
            Assert.AreSame(clips[0], UiSounds.Clip(UiSound.Move), "made once");
        }

        static float Crossings(AudioClip c)
        {
            var d = new float[c.samples]; c.GetData(d, 0);
            int n = 0; for (int i = 1; i < d.Length; i++) if ((d[i - 1] < 0f) != (d[i] < 0f) && Mathf.Abs(d[i]) > 1e-4f) n++;
            return n / c.length;
        }

        [Test]
        public void NoMenuMakesItsOwnBeep()
        {
            foreach (var f in new[] { "Menu/MainMenu.cs", "Menu/SettingsMenu.cs", "Menu/LookMenu.cs", "CustomRooms/PauseMenu.cs", "Camcorder/CamcorderPlayerMenu.cs", "Interaction/Contracts.cs" })
            {
                var src = File.ReadAllText("Assets/Backrooms/Scripts/Runtime/" + f);
                StringAssert.DoesNotContain("SoundBank.MenuBeep", src, f);
                StringAssert.DoesNotContain("Tone(880f", src, f);
            }
            StringAssert.Contains(".kit-enter", File.ReadAllText("Assets/Backrooms/Resources/UI/Kit.uss"));
        }

        [Test]
        public void ReduceMotionStillsTheMenus()
        {
            float shake = GameSettings.Shake, blur = GameSettings.MotionBlur, bright = GameSettings.Brightness; bool still = GameSettings.StillMenus;
            try
            {
                GameSettings.StillMenus = false;
                GameSettings.ReduceMotion();
                Assert.IsTrue(GameSettings.StillMenus);
                GameSettings.ResetComfort();
                Assert.IsFalse(GameSettings.StillMenus);
            }
            finally { GameSettings.Shake = shake; GameSettings.MotionBlur = blur; GameSettings.Brightness = bright; GameSettings.StillMenus = still; }
        }
    }
}
