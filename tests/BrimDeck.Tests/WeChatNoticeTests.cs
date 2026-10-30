using BrimDeck.Core;

internal static class WeChatNoticeTests
{
    public static void Run(Action<string, bool> check)
    {
        const int left = 0, top = 0, right = 1920, bottom = 1040;
        check("A WeChat notification at the bottom right is a notice",
            WeChatNotice.LooksLikeNotification(new WeChatWindow("Qt51514QWindowIcon", "", true, 1500, 880, 1880, 1000), left, top, right, bottom));
        check("A WeChat window in the middle of the screen is not a notice",
            !WeChatNotice.LooksLikeNotification(new WeChatWindow("Qt51514QWindowIcon", "", true, 700, 300, 1100, 420), left, top, right, bottom));
        check("The WeChat main window is not a notice",
            !WeChatNotice.LooksLikeNotification(new WeChatWindow("Qt51514QWindowIcon", "微信", true, 300, 100, 1250, 900), left, top, right, bottom));
        check("The tray icon window is not a notice",
            !WeChatNotice.LooksLikeNotification(new WeChatWindow("Qt51514WxTrayIconMessageWindowClass", "", true, 1800, 900, 1880, 1000), left, top, right, bottom));
        check("A hidden popup is not a notice",
            !WeChatNotice.LooksLikeNotification(new WeChatWindow("Qt51514QWindowIcon", "", false, 1500, 880, 1880, 1000), left, top, right, bottom));
        check("A notification above a taskbar on another monitor's work area is found",
            WeChatNotice.LooksLikeNotification(new WeChatWindow("Qt51514QWindowIcon", "", true, 3500, 1400, 3880, 1520), 3000, 0, 4000, 1560));
        check("The notice names the app rather than the sender", WeChatNotice.Text == "微信 · 新消息");
    }
}
