using System;

namespace PennyPet
{
    // Pure policy for the optional global keyboard overlay. Windows focus
    // inspection remains in SensitiveInputDetector; UI confirmation remains
    // in PetForm.
    internal static class PetKeyboardPrivacyPolicy
    {
        internal const string FirstUseNotice =
            "按键显示会使用 Windows 全局键盘活动监听，在桌宠旁显示按键名称。\n\n" +
            "Penny 不会保存或上传按键内容，并会尽力识别密码框和敏感输入。" +
            "但第三方、自绘、跨权限或远程窗口可能无法被完全识别。\n\n" +
            "处理密码、验证码、支付或其他高敏感信息时，请先关闭按键显示。" +
            "是否确认开启？";

        internal static bool RequiresFirstUseNotice(bool desiredEnabled,
            bool noticeAccepted)
        {
            return desiredEnabled && !noticeAccepted;
        }

        internal static bool ShouldStartHook(bool desiredEnabled,
            bool noticeAccepted)
        {
            return desiredEnabled && noticeAccepted;
        }

        internal static bool ShouldDisableUnacknowledgedLegacyOptIn(
            bool storedEnabled, bool noticeAccepted)
        {
            return storedEnabled && !noticeAccepted;
        }
    }
}
