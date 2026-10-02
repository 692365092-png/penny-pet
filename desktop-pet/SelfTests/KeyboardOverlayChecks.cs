using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private sealed class KeyboardOverlayCheckResult
        {
            internal bool HookOptInDefaultOk;
            internal bool TextScaleChoicesOk;
            internal bool ShortcutAndRepeatOk;
            internal bool HeldKeyStableOk;
            internal bool HookCapturePolicyOk;
            internal bool OwnProcessEligibilityOk;
            internal bool PrivacyGenerationOk;
            internal bool FocusSnapshotIdentityOk;
            internal bool AdaptiveContrastOk;
        }

        private static KeyboardOverlayCheckResult RunKeyboardOverlayChecks()
        {
            KeyboardOverlayCheckResult result =
                new KeyboardOverlayCheckResult();
            result.HookOptInDefaultOk =
                !new PetSettings().ShowKeyOverlay &&
                !new PetSettings().KeyboardPrivacyNoticeAccepted &&
                !PetKeyboardPrivacyPolicy.ShouldStartHook(false, false) &&
                !PetKeyboardPrivacyPolicy.ShouldStartHook(true, false) &&
                PetKeyboardPrivacyPolicy.ShouldStartHook(true, true) &&
                PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(true, false) &&
                !PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(true, true) &&
                PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                    true, false) &&
                PetForm.WindowsKeyboardFirstUseNotice.IndexOf("杀毒软件",
                    StringComparison.Ordinal) >= 0 &&
                PetForm.WindowsKeyboardFirstUseNotice.IndexOf("误报",
                    StringComparison.Ordinal) >= 0;
            result.TextScaleChoicesOk =
                KeyboardOverlayForm.NormalizeTextScalePercent(55) == 60 &&
                KeyboardOverlayForm.NormalizeTextScalePercent(100) == 100 &&
                KeyboardOverlayForm.NormalizeTextScalePercent(140) == 150 &&
                Math.Abs(KeyboardOverlayForm.TextFontSizePoints(60) - 9F) <
                    0.01F &&
                Math.Abs(KeyboardOverlayForm.TextFontSizePoints(100) - 15F) <
                    0.01F &&
                Math.Abs(KeyboardOverlayForm.TextFontSizePoints(150) - 22.5F) <
                    0.01F;
            string shortcut = KeyboardInputFormatter.ComposeKeyName(
                (int)Keys.W, true, false, false, false);
            string modifierChord = KeyboardInputFormatter.ComposeKeyName(
                (int)Keys.LShiftKey, true, true, false, false);
            int repeatOne = GlobalKeyboardActivity.NextRepeatCount(
                0, (uint)Keys.W, 0, 1000, 0);
            int repeatTwo = GlobalKeyboardActivity.NextRepeatCount(
                (uint)Keys.W, (uint)Keys.W, 1000, 1800, repeatOne);
            int repeatReset = GlobalKeyboardActivity.NextRepeatCount(
                (uint)Keys.W, (uint)Keys.A, 1800, 1850, repeatTwo);
            result.ShortcutAndRepeatOk = shortcut == "CTRL+W" &&
                modifierChord == "CTRL+SHIFT" && repeatOne == 1 &&
                repeatTwo == 2 && repeatReset == 1;
            result.HeldKeyStableOk =
                GlobalKeyboardActivity.ShouldPublishKeyDown(false) &&
                !GlobalKeyboardActivity.ShouldPublishKeyDown(true);
            result.HookCapturePolicyOk =
                GlobalKeyboardActivity.ShouldPublishKey(false) &&
                !GlobalKeyboardActivity.ShouldPublishKey(true);
            result.OwnProcessEligibilityOk =
                !PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    false, false) &&
                !PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    true, true) &&
                PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    true, false);
            result.PrivacyGenerationOk = RunKeyboardPrivacyNativeChecks();
            KeyboardFocusSnapshot captured = new KeyboardFocusSnapshot(
                new IntPtr(10), 20, 30, new IntPtr(40),
                new int[] { 1, 2, 3 });
            result.FocusSnapshotIdentityOk =
                KeyboardFocusSnapshot.IsSameTarget(captured,
                    new KeyboardFocusSnapshot(new IntPtr(10), 20, 30,
                        new IntPtr(40), new int[] { 1, 2, 3 })) &&
                !KeyboardFocusSnapshot.IsSameTarget(captured,
                    new KeyboardFocusSnapshot(new IntPtr(11), 20, 30,
                        new IntPtr(40), new int[] { 1, 2, 3 })) &&
                !KeyboardFocusSnapshot.IsSameTarget(captured,
                    new KeyboardFocusSnapshot(new IntPtr(10), 20, 30,
                        new IntPtr(40), new int[] { 1, 2, 4 }));
            result.AdaptiveContrastOk =
                KeyboardOverlayForm.ChooseTextColorFromLuminance(0.8) ==
                    Color.Black &&
                KeyboardOverlayForm.ChooseTextColorFromLuminance(0.2) ==
                    Color.White;
            return result;
        }
    }
}
