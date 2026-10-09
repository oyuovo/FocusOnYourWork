using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using FocusIsland.Core;

namespace FocusIsland.Desktop
{
    public sealed class BrowserBridge : IDisposable
    {
        public static string PipeName { get { return "FocusIsland.v1." + WindowsIdentity.GetCurrent().User.Value; } }
        private readonly Func<BrowserMessage, BrowserReply> handler;
        private volatile bool stopping;
        private NamedPipeServerStream pipe;
        public BrowserBridge(Func<BrowserMessage, BrowserReply> handler)
        {
            this.handler = handler;
            new Thread(Loop) { IsBackground = true, Name = "Edge native bridge" }.Start();
        }
        private void Loop()
        {
            while (!stopping)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        pipe = server;
                        if (stopping) break;
                        server.WaitForConnection();
                        // Bound each connection so an interrupted host cannot block subsequent connections.
                        var read = System.Threading.Tasks.Task.Factory.StartNew(delegate { return Framing.Read(server); });
                        if (!read.Wait(2000)) continue;
                        string raw = read.Result;
                        if (raw == null) continue;
                        var serializer = new JavaScriptSerializer { MaxJsonLength = Framing.MaxBytes };
                        BrowserMessage message = serializer.Deserialize<BrowserMessage>(raw);
                        BrowserReply reply = message == null ? new BrowserReply { Message = "消息无效" } : handler(message);
                        Framing.Write(server, serializer.Serialize(reply));
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is ObjectDisposedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.AggregateException))
                        System.Diagnostics.Trace.WriteLine("Browser bridge: " + ex.GetType().Name);
                    if (!stopping) Thread.Sleep(100);
                }
            }
        }
        public void Dispose() { stopping = true; var current = pipe; if (current != null) current.Dispose(); }
    }
}
