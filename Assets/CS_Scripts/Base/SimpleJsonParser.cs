using System.Collections;
using System.Collections.Generic;

namespace CS.Base
{
    // Very small, permissive JSON parser for simple payloads used by custom_event.
    // Handles objects, arrays, numbers, booleans and strings.
    public static class SimpleJsonParser
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0; return ParseValue(json, ref i);
        }

        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '"') return ParseString(s, ref i);
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (char.IsDigit(c) || c == '-' || c == '+') return ParseNumber(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            return null;
        }

        private static string ParseString(string s, ref int i)
        {
            i++; var start = i; var result = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        default: result.Append(e); break;
                    }
                }
                else result.Append(c);
            }
            return result.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i; while (i < s.Length && "-+0123456789.eE".IndexOf(s[i]) >= 0) i++;
            var num = s.Substring(start, i - start);
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                if (d % 1 == 0) return (int)d;
                return (float)d;
            }
            return 0f;
        }

        private static IDictionary ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // skip {
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                var val = ParseValue(s, ref i);
                dict[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
            }
            return dict;
        }

        private static IList ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // skip [
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                var val = ParseValue(s, ref i);
                list.Add(val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
            }
            return list;
        }
    }
}
