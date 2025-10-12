using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CS.Base
{
    // Minimal JSON serializer for simple dictionaries/lists/numbers/strings/bools
    public static class MiniJson
    {
        public static string Serialize(object obj)
        {
            var sb = new StringBuilder();
            WriteValue(sb, obj);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object obj)
        {
            switch (obj)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("0.###", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture));
                    break;
                case IDictionary dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry de in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('"').Append(de.Key.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"').Append(':');
                        WriteValue(sb, de.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstE = true;
                    foreach (var item in list)
                    {
                        if (!firstE) sb.Append(',');
                        firstE = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    // Fallback to string
                    sb.Append('"').Append(obj.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    break;
            }
        }
    }
}
