using System.Globalization;
using System.Text;

namespace FOG.Agent;

internal static class DiscordGatewayEvidence
{
    internal static (bool? Gateway, bool? Voice, bool VoiceIdle) Read(DateTimeOffset since)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "discord", "logs", "renderer_js.log");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var truncated = stream.Length > 65536;
            if (truncated) stream.Seek(-65536, SeekOrigin.End);
            var buffer = new byte[65536];
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var text = Encoding.UTF8.GetString(buffer, 0, count);
            if (truncated)
            {
                var newline = text.IndexOf('\n');
                text = newline < 0 ? "" : text[(newline + 1)..];
            }
            var now = DateTimeOffset.Now;
            return (Parse(text, since, now), ParseVoice(text, since, now), IsVoiceIdle(text, since, now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null, false);
        }
    }

    internal static bool IsVoiceIdle(string text, DateTimeOffset since, DateTimeOffset now)
    {
        var idle = false;
        var newest = since;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length < 25 || !line.Contains("[RTCControlSocket(default)]", StringComparison.Ordinal) ||
                !DateTimeOffset.TryParseExact(line.AsSpan(1, 23), "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time) ||
                time < newest || time > now || now - time > TimeSpan.FromSeconds(120)) continue;
            if (line.Contains("[RTCControlSocket(default)] CLOSE", StringComparison.Ordinal)) idle = true;
            else if (line.Contains("[CONNECT]", StringComparison.Ordinal)) idle = false;
            else continue;
            newest = time;
        }
        return idle;
    }

    internal static bool? ParseVoice(string text, DateTimeOffset since, DateTimeOffset now)
    {
        bool? result = null;
        var newest = since;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length < 25 || line[0] != '[' || line[24] != ']' ||
                !DateTimeOffset.TryParseExact(line.AsSpan(1, 23), "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time) ||
                time < newest || time > now || now - time > TimeSpan.FromSeconds(120)) continue;
            var voiceSocket = line.Contains("[RTCControlSocket(default)]", StringComparison.Ordinal);
            var voiceConnection = line.Contains("[RTCConnection(", StringComparison.Ordinal) &&
                line.Contains(", default)]", StringComparison.Ordinal);
            if (!voiceSocket && !voiceConnection) continue;
            if (voiceConnection && line.Contains("=> RTC_CONNECTED", StringComparison.Ordinal))
                result = true;
            else if (voiceSocket && line.Contains("did not receive OP_HELLO in time", StringComparison.Ordinal))
                result = false;
            else if (voiceConnection && (line.Contains("=> DISCONNECTED", StringComparison.Ordinal) ||
                     line.Contains("=> RTC_DISCONNECTED", StringComparison.Ordinal)))
                result = null;
            else continue;
            newest = time;
        }
        return result;
    }

    internal static bool? Parse(string text, DateTimeOffset since, DateTimeOffset now)
    {
        bool? result = null;
        var newest = since;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length < 25 || line[0] != '[' || line[24] != ']' ||
                !line.Contains("[GatewaySocket]", StringComparison.Ordinal)) continue;
            if (!DateTimeOffset.TryParseExact(line.AsSpan(1, 23), "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp) ||
                timestamp < newest || timestamp > now || now - timestamp > TimeSpan.FromSeconds(120)) continue;
            bool? evidence = line.Contains("[READY]", StringComparison.Ordinal) ? true :
                line.Contains("did not receive OP_HELLO in time", StringComparison.Ordinal) ? false : null;
            if (evidence is null) continue;
            newest = timestamp;
            result = evidence;
        }
        return result;
    }
}
