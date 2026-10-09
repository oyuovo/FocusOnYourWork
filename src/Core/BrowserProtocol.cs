using System;
using System.Diagnostics;

namespace FocusIsland.Core
{
    public sealed class BrowserMessage
    {
        public string Type { get; set; }
        public string Instance { get; set; }
        public int TabId { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public bool Focused { get; set; }
        public string Error { get; set; }
    }

    public sealed class BrowserReply
    {
        public bool Ok { get; set; }
        public bool Tracking { get; set; }
        public bool Selected { get; set; }
        public string Message { get; set; }
    }

    public sealed class BrowserTracker
    {
        public string Instance { get; private set; }
        public int TabId { get; private set; }
        public string Url { get; private set; }
        public string Title { get; private set; }
        public bool Selected { get { return Instance != null; } }
        private BrowserMessage last;
        private long lastReceived;

        public static string CanonicalUrl(string raw)
        {
            Uri uri;
            if (String.IsNullOrEmpty(raw) || raw.Length > 8192 || !Uri.TryCreate(raw, UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            return uri.AbsoluteUri;
        }

        public void Select(BrowserMessage message, long nowMs)
        {
            string url = CanonicalUrl(message.Url);
            if (url == null || String.IsNullOrEmpty(message.Instance) || message.Instance.Length > 100 || message.TabId < 0)
                throw new ArgumentException("请在普通 HTTP/HTTPS 页面选择专注目标。");
            Instance = message.Instance; TabId = message.TabId; Url = url;
            Title = String.IsNullOrWhiteSpace(message.Title) ? new Uri(url).Host : message.Title.Substring(0, Math.Min(120, message.Title.Length));
            Update(message, nowMs);
        }

        public void Update(BrowserMessage message, long nowMs)
        {
            if (message.Instance != Instance) return;
            last = message;
            lastReceived = nowMs;
        }

        public Presence Evaluate(long nowMs, bool edgeForeground)
        {
            // A different foreground app is conclusive even if the browser connection is stale.
            if (Selected && !edgeForeground) return Presence.Away;
            if (!Selected || last == null || nowMs < lastReceived || nowMs - lastReceived > 2500 || last.Type == "disconnect" || last.Error != null)
                return Presence.Unknown;
            if (!edgeForeground || !last.Focused) return Presence.Away;
            return last.TabId == TabId && CanonicalUrl(last.Url) == Url ? Presence.Allowed : Presence.Away;
        }
    }
}
