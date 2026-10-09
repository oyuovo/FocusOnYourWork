using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FocusIsland.Core;

internal static class NativeHostProgram
{
    private static string Forward(string raw)
    {
        using (var pipe = new NamedPipeClientStream(".", "FocusIsland.v1." + WindowsIdentity.GetCurrent().User.Value, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            pipe.Connect(600); Framing.Write(pipe, raw);
            var read = Task.Factory.StartNew(delegate { return Framing.Read(pipe); });
            if (!read.Wait(2000)) throw new IOException("Desktop response timed out.");
            return read.Result;
        }
    }
    public static int Main()
    {
        var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
        var serializer = new JavaScriptSerializer { MaxJsonLength = Framing.MaxBytes };
        string instance = null;
        try
        {
            string raw;
            while ((raw = Framing.Read(input)) != null)
            {
                string reply;
                try
                {
                    var message = serializer.Deserialize<BrowserMessage>(raw); if (message != null) instance = message.Instance;
                    reply = Forward(raw);
                }
                catch (Exception ex)
                {
                    if (!(ex is TimeoutException || ex is IOException || ex is ArgumentException || ex is AggregateException)) throw;
                    reply = serializer.Serialize(new BrowserReply { Message = "请先打开专注岛桌面客户端。" });
                }
                Framing.Write(output, reply ?? "{\"Ok\":false}");
            }
        }
        catch (IOException) { return 1; }
        catch (ArgumentException) { return 1; }
        finally
        {
            if (instance != null)
            {
                try { Forward(serializer.Serialize(new BrowserMessage { Type = "disconnect", Instance = instance })); }
                catch (Exception) { /* App might already be closed; no stdout diagnostics in this protocol. */ }
            }
        }
        return 0;
    }
}
