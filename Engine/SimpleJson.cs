using System.Collections.Generic;

namespace MystiaRecommendation.Engine;

/// <summary>
/// 极简 JSON 解析器，不依赖任何第三方库
/// 只支持本插件需要的数据结构
/// </summary>
public static class SimpleJson
{
    public static List<Dictionary<string, object>> ParseArray(string json)
    {
        var result = new List<Dictionary<string, object>>();
        int pos = 0;
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length || json[pos] != '[') return result;
        pos++; // skip '['
        
        while (pos < json.Length)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) break;
            if (json[pos] == ']') { pos++; break; }
            if (json[pos] == ',') { pos++; continue; }
            
            var obj = ParseObject(json, ref pos);
            if (obj != null) result.Add(obj);
        }
        return result;
    }

    private static Dictionary<string, object> ParseObject(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length || json[pos] != '{') return null;
        pos++; // skip '{'
        
        var dict = new Dictionary<string, object>();
        while (pos < json.Length)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) break;
            if (json[pos] == '}') { pos++; return dict; }
            if (json[pos] == ',') { pos++; continue; }
            
            string key = ParseString(json, ref pos);
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ':') pos++; // skip ':'
            SkipWhitespace(json, ref pos);
            
            object value = ParseValue(json, ref pos);
            if (key != null) dict[key] = value;
        }
        return dict;
    }

    private static object ParseValue(string json, ref int pos)
    {
        if (pos >= json.Length) return null;
        char c = json[pos];
        
        if (c == '"') return ParseString(json, ref pos);
        if (c == '{') return ParseObject(json, ref pos);
        if (c == '[') return ParseArrayValue(json, ref pos);
        if (c == 't' || c == 'f') return ParseBool(json, ref pos);
        if (c == 'n') { pos += 4; return null; }
        return ParseNumber(json, ref pos);
    }

    private static List<object> ParseArrayValue(string json, ref int pos)
    {
        var list = new List<object>();
        pos++; // skip '['
        while (pos < json.Length)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) break;
            if (json[pos] == ']') { pos++; return list; }
            if (json[pos] == ',') { pos++; continue; }
            list.Add(ParseValue(json, ref pos));
        }
        return list;
    }

    private static string ParseString(string json, ref int pos)
    {
        if (pos >= json.Length || json[pos] != '"') return null;
        pos++; // skip opening "
        int start = pos;
        while (pos < json.Length)
        {
            if (json[pos] == '\\') { pos += 2; continue; }
            if (json[pos] == '"') break;
            pos++;
        }
        string result = json.Substring(start, pos - start);
        if (pos < json.Length) pos++; // skip closing "
        return result;
    }

    private static double ParseNumber(string json, ref int pos)
    {
        int start = pos;
        if (pos < json.Length && json[pos] == '-') pos++;
        while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '.' || json[pos] == 'e' || json[pos] == 'E' || json[pos] == '+' || json[pos] == '-'))
            pos++;
        if (double.TryParse(json.Substring(start, pos - start), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
            return val;
        return 0;
    }

    private static bool ParseBool(string json, ref int pos)
    {
        if (json.Substring(pos, 4) == "true") { pos += 4; return true; }
        if (json.Substring(pos, 5) == "false") { pos += 5; return false; }
        return false;
    }

    private static void SkipWhitespace(string json, ref int pos)
    {
        while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == '\n' || json[pos] == '\r'))
            pos++;
    }

    // Helper methods for type-safe access
    public static int ToInt(object val)
    {
        if (val is double d) return (int)d;
        if (val is int i) return i;
        if (val is long l) return (int)l;
        return 0;
    }

    public static double ToDouble(object val)
    {
        if (val is double d) return d;
        if (val is int i) return i;
        return 0;
    }

    public static string ToString(object val) => val?.ToString() ?? "";

    public static List<string> ToStringList(object val)
    {
        var result = new List<string>();
        if (val is List<object> list)
            foreach (var item in list)
                result.Add(item?.ToString() ?? "");
        return result;
    }

    public static List<int> ToIntList(object val)
    {
        var result = new List<int>();
        if (val is List<object> list)
            foreach (var item in list)
                result.Add(ToInt(item));
        return result;
    }
}