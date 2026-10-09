using System;
using System.IO;
using System.Text;

namespace FocusIsland.Core
{
    public static class Framing
    {
        public const int MaxBytes = 65536;
        public static string Read(Stream input)
        {
            byte[] header = new byte[4];
            if (!ReadExact(input, header, 4, true)) return null;
            int length = BitConverter.ToInt32(header, 0);
            if (length <= 0 || length > MaxBytes) throw new InvalidDataException("Message exceeds size limit.");
            byte[] body = new byte[length];
            ReadExact(input, body, length, false);
            return new UTF8Encoding(false, true).GetString(body);
        }
        private static bool ReadExact(Stream stream, byte[] buffer, int length, bool allowEof)
        {
            int offset = 0;
            while (offset < length)
            {
                int n = stream.Read(buffer, offset, length - offset);
                if (n == 0)
                {
                    if (allowEof && offset == 0) return false;
                    throw new EndOfStreamException("Incomplete message.");
                }
                offset += n;
            }
            return true;
        }
        public static void Write(Stream output, string json)
        {
            byte[] body = new UTF8Encoding(false, true).GetBytes(json);
            if (body.Length == 0 || body.Length > MaxBytes) throw new InvalidDataException("Message exceeds size limit.");
            byte[] prefix = BitConverter.GetBytes(body.Length);
            output.Write(prefix, 0, prefix.Length); output.Write(body, 0, body.Length); output.Flush();
        }
    }
}
