namespace BrimDeck.Core;

// A candidate top-level window from WeChat, in physical pixels as Windows reports it.
public readonly record struct WeChatWindow(string ClassName, string Title, bool Visible, int Left, int Top, int Right, int Bottom);

// WeChat 4 (Weixin.exe) draws its own UI and exposes no accessible control tree, so its unread state cannot be read
// without patching the process. Its new-message popup is still an ordinary Win32 window; this decides whether a window
// from that process is one, by its class, its small size and its place at the bottom right of the work area. Only the
// window's rectangle and title are used; message text is never read.
public static class WeChatNotice
{
    public const string DefaultText = "微信 · 新消息";

    public static bool LooksLikeNotification(WeChatWindow window, int workLeft, int workTop, int workRight, int workBottom)
    {
        if (!window.Visible) return false;
        int width = window.Right - window.Left, height = window.Bottom - window.Top;
        // A notification popup is wide and short. The main window, a chat window and an image preview are either much
        // larger or much closer to square, whatever the display scale.
        if (width < 200 || width > 1200 || height < 60 || height > 400 || width < height * 2) return false;
        string cls = window.ClassName ?? "", title = window.Title ?? "";
        foreach (string excluded in new[] { "TrayIcon", "MSCTFIME", "IME", "PowerMessage", "SystemMessage", "DisplayICC", "Chrome_WidgetWin", "MMUIRenderSubWindowHW" })
            if (cls.Contains(excluded, StringComparison.OrdinalIgnoreCase)) return false;
        if ((title.Equals("微信", StringComparison.Ordinal) || title.Equals("Weixin", StringComparison.OrdinalIgnoreCase)) && width > 700) return false;
        if (workRight <= workLeft || workBottom <= workTop) return false;
        // Notifications appear at the bottom right of the monitor WeChat is on. The limits leave room for a notification
        // above a taskbar, a multi-monitor arrangement and a high-DPI work area.
        bool atRight = window.Right <= workRight + 24 && window.Right >= workRight - 900;
        bool atBottom = window.Bottom <= workBottom + 24 && window.Top >= workBottom - 700;
        return atRight && atBottom;
    }

    // The notice names the app, not the sender: the window title is not read into the panel, so nothing of the message
    // reaches the screen beyond the fact that one arrived.
    public static string Text => DefaultText;
}
