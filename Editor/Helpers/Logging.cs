using System.Text;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Thry.ThryEditor.Helpers
{
    public enum LoggingLevel { None, Normal, Detailed, StackTraced }

    public class ThryLogger
    {
        // Checked before any stack walking happens. Building a StackTrace costs ~14KB and
        // ~10us at inspector stack depth, so it must never run for a message that is about
        // to be discarded by the logging level.
        private static bool NormalEnabled => Config.Instance.loggingLevel != LoggingLevel.None;
        private static bool DetailEnabled => (int)Config.Instance.loggingLevel >= (int)LoggingLevel.Detailed;
        private static string GetPrefixFromStackTrace()
        {
            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(2);
            // Any of these can be null once the JIT inlines a caller, which previously threw
            // a NullReferenceException from inside the logger itself.
            return stackFrame?.GetMethod()?.DeclaringType?.Name ?? "ThryEditor";
        }

        public static void Log(string message)
        {
            if (!NormalEnabled) return;
            Print(GetPrefixFromStackTrace(), "#ff78e0", message);
        }

        public static void Log(string prefix, string message)
        {
            if (!NormalEnabled) return;
            Print(prefix, "#ff78e0", message);
        }

        public static void LogDetail(string message)
        {
            if (!DetailEnabled) return;
            Print(GetPrefixFromStackTrace(), "#d778ff", message);
        }

        public static void LogDetail(string prefix, string message)
        {
            if (!DetailEnabled) return;
            Print(prefix, "#d778ff", message);
        }

        public static void LogErr(string message)
        {
            LogErr(GetPrefixFromStackTrace(), message);
        }

        public static void LogErr(string prefix, string message)
        {
            Print(prefix, "#ff0000", message);
        }

        public static void LogWarn(string message)
        {
            LogWarn(GetPrefixFromStackTrace(), message);
        }

        public static void LogWarn(string prefix, string message)
        {
            Print(prefix, "#ff7800", message);
        }

        private static void Print(string prefix, string color, string message)
        {
            StringBuilder sb = new StringBuilder((message?.Length ?? 0) + 48);
            sb.Append("[<color=");
            sb.Append(color);
            sb.Append(">");
            sb.Append(prefix);
            sb.Append("</color>] ");
            sb.Append(message);
            if (Config.Instance.loggingLevel == LoggingLevel.StackTraced)
            {
                sb.Append('\n');
                sb.Append(new StackTrace().ToString());
            }
            Debug.Log(sb.ToString());
        }

    }
}