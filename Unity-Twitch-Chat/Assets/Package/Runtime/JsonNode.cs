using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lexone.UnityTwitchChat
{
    /// <summary>
    /// Lightweight JSON node used internally for parsing third-party emote API responses.
    /// JsonUtility cannot deserialize top-level arrays or fields whose names start with a digit
    /// (such as FFZ's "1x"/"2x"/"4x" image keys), so a minimal parser is provided here.
    /// </summary>
    internal sealed class JsonNode
    {
        public enum NodeType { Null, Bool, Number, String, Array, Object }

        public NodeType Type = NodeType.Null;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JsonNode> ArrayValue;
        public Dictionary<string, JsonNode> ObjectValue;

        public bool IsNull => Type == NodeType.Null;
        public bool IsObject => Type == NodeType.Object;
        public bool IsArray => Type == NodeType.Array;
        public bool IsString => Type == NodeType.String;

        public string AsString => StringValue ?? string.Empty;
        public int AsInt => (int)NumberValue;
        public long AsLong => (long)NumberValue;
        public double AsDouble => NumberValue;
        public bool AsBool
        {
            get
            {
                if (Type == NodeType.Bool) return BoolValue;
                if (Type == NodeType.Number) return NumberValue != 0;
                return false;
            }
        }

        public int Count
        {
            get
            {
                if (Type == NodeType.Array) return ArrayValue?.Count ?? 0;
                if (Type == NodeType.Object) return ObjectValue?.Count ?? 0;
                return 0;
            }
        }

        public bool HasKey(string key)
        {
            return Type == NodeType.Object
                && ObjectValue != null
                && ObjectValue.ContainsKey(key);
        }

        public JsonNode this[string key]
        {
            get
            {
                if (Type == NodeType.Object && ObjectValue != null && ObjectValue.TryGetValue(key, out var v))
                    return v;
                return Null;
            }
        }

        public JsonNode this[int idx]
        {
            get
            {
                if (Type == NodeType.Array && ArrayValue != null && idx >= 0 && idx < ArrayValue.Count)
                    return ArrayValue[idx];
                return Null;
            }
        }

        public IEnumerable<JsonNode> Items
        {
            get
            {
                if (Type == NodeType.Array && ArrayValue != null)
                {
                    for (int i = 0; i < ArrayValue.Count; ++i)
                        yield return ArrayValue[i];
                }
            }
        }

        public IEnumerable<KeyValuePair<string, JsonNode>> Pairs
        {
            get
            {
                if (Type == NodeType.Object && ObjectValue != null)
                {
                    foreach (var kvp in ObjectValue)
                        yield return kvp;
                }
            }
        }

        public static readonly JsonNode Null = new JsonNode { Type = NodeType.Null };

        public static JsonNode Parse(string source)
        {
            if (string.IsNullOrEmpty(source))
                return Null;

            int i = 0;
            SkipWhitespace(source, ref i);
            JsonNode result = ParseValue(source, ref i);
            return result ?? Null;
        }

        // ---- Parser ----

        private static JsonNode ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
                throw new FormatException("Unexpected end of JSON input");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonNode { Type = NodeType.String, StringValue = ParseString(s, ref i) };
                case 't':
                case 'f': return ParseBool(s, ref i);
                case 'n': return ParseNull(s, ref i);
                default:  return ParseNumber(s, ref i);
            }
        }

        private static JsonNode ParseObject(string s, ref int i)
        {
            var node = new JsonNode
            {
                Type = NodeType.Object,
                ObjectValue = new Dictionary<string, JsonNode>(),
            };

            ++i; // consume '{'
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == '}') { ++i; return node; }

            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);

                if (i >= s.Length || s[i] != ':')
                    throw new FormatException($"Expected ':' at position {i}");
                ++i; // consume ':'

                JsonNode value = ParseValue(s, ref i);
                node.ObjectValue[key] = value;

                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { ++i; continue; }
                if (i < s.Length && s[i] == '}') { ++i; break; }

                throw new FormatException($"Expected ',' or '}}' at position {i}");
            }

            return node;
        }

        private static JsonNode ParseArray(string s, ref int i)
        {
            var node = new JsonNode
            {
                Type = NodeType.Array,
                ArrayValue = new List<JsonNode>(),
            };

            ++i; // consume '['
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == ']') { ++i; return node; }

            while (i < s.Length)
            {
                JsonNode value = ParseValue(s, ref i);
                node.ArrayValue.Add(value);

                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { ++i; continue; }
                if (i < s.Length && s[i] == ']') { ++i; break; }

                throw new FormatException($"Expected ',' or ']' at position {i}");
            }

            return node;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
                throw new FormatException($"Expected '\"' at position {i}");

            ++i;
            var sb = new StringBuilder();

            while (i < s.Length)
            {
                char c = s[i++];

                if (c == '"')
                    return sb.ToString();

                if (c == '\\')
                {
                    if (i >= s.Length)
                        throw new FormatException("Unexpected end of JSON string");

                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/'); break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length)
                                throw new FormatException("Invalid unicode escape");
                            string hex = s.Substring(i, 4);
                            i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            throw new FormatException("Unterminated JSON string");
        }

        private static JsonNode ParseBool(string s, ref int i)
        {
            if (s[i] == 't' && i + 4 <= s.Length && s.Substring(i, 4) == "true")
            {
                i += 4;
                return new JsonNode { Type = NodeType.Bool, BoolValue = true };
            }
            if (s[i] == 'f' && i + 5 <= s.Length && s.Substring(i, 5) == "false")
            {
                i += 5;
                return new JsonNode { Type = NodeType.Bool, BoolValue = false };
            }
            throw new FormatException($"Invalid bool at position {i}");
        }

        private static JsonNode ParseNull(string s, ref int i)
        {
            if (s[i] == 'n' && i + 4 <= s.Length && s.Substring(i, 4) == "null")
            {
                i += 4;
                return Null;
            }
            throw new FormatException($"Invalid null at position {i}");
        }

        private static JsonNode ParseNumber(string s, ref int i)
        {
            int start = i;

            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
                ++i;

            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                ++i;

            string raw = s.Substring(start, i - start);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new FormatException($"Invalid number '{raw}' at position {start}");

            return new JsonNode { Type = NodeType.Number, NumberValue = v };
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    ++i;
                else
                    break;
            }
        }
    }
}
