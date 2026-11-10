using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleUpdateCenter.Core
{
    /// <summary>
    /// Traducción automática con Google Translate (servicio público sin clave).
    /// Se usa solo cuando Steam no tiene el texto en el idioma del usuario.
    /// </summary>
    internal static class Translator
    {
        public sealed class Result
        {
            public string Text;
            public string SourceLanguage;
            public bool Translated;
        }

        private const int ChunkSize = 1800;
        private static readonly Dictionary<string, Result> cache = new Dictionary<string, Result>();

        /// <summary>Código de Google para un código de Loc (es, en, zh, zht…).</summary>
        public static string GoogleCode(string loc)
        {
            var c = (loc ?? "en").ToLowerInvariant();
            if (c == "zht")
            {
                return "zh-TW";
            }

            if (c.StartsWith("zh"))
            {
                return "zh-CN";
            }

            var dash = c.IndexOfAny(new[] { '-', '_' });
            return dash > 0 ? c.Substring(0, dash) : c;
        }

        /// <summary>Traduce el texto al idioma indicado (código de Loc). Si ya está en ese idioma, lo devuelve igual.</summary>
        public static async Task<Result> TranslateAsync(string text, string targetLoc)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new Result { Text = text ?? string.Empty };
            }

            var target = GoogleCode(targetLoc);
            var key = target + "|" + text.GetHashCode().ToString(CultureInfo.InvariantCulture) + "|" + text.Length;
            lock (cache)
            {
                if (cache.TryGetValue(key, out var hit))
                {
                    return hit;
                }
            }

            var sb = new StringBuilder();
            string source = null;
            foreach (var chunk in Chunks(text))
            {
                var part = await TranslateChunkAsync(chunk, target).ConfigureAwait(false);
                source = source ?? part.Item2;
                sb.Append(part.Item1);
            }

            var result = new Result
            {
                Text = sb.ToString(),
                SourceLanguage = source,
                Translated = source != null && !SameLanguage(source, target)
            };

            if (!result.Translated)
            {
                result.Text = text;
            }

            lock (cache)
            {
                cache[key] = result;
            }

            return result;
        }

        private static bool SameLanguage(string a, string b)
        {
            string Norm(string x) => (x ?? string.Empty).ToLowerInvariant().Split('-')[0];
            if (b.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            return Norm(a) == Norm(b);
        }

        /// <summary>Trozos de hasta ChunkSize caracteres, cortando por párrafos o líneas.</summary>
        private static IEnumerable<string> Chunks(string text)
        {
            var current = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var piece = line + "\n";
                if (current.Length + piece.Length > ChunkSize && current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                // Línea muy larga: se corta por frases
                while (piece.Length > ChunkSize)
                {
                    var cut = piece.LastIndexOf(". ", ChunkSize, StringComparison.Ordinal);
                    if (cut < ChunkSize / 2)
                    {
                        cut = ChunkSize;
                    }
                    else
                    {
                        cut += 2;
                    }

                    yield return piece.Substring(0, cut);
                    piece = piece.Substring(cut);
                }

                current.Append(piece);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        private static async Task<Tuple<string, string>> TranslateChunkAsync(string chunk, string target)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl=" + Uri.EscapeDataString(target);

            string json;
            using (var web = new WebClient { Encoding = Encoding.UTF8 })
            {
                web.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded;charset=UTF-8";
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                json = await web.UploadStringTaskAsync(url, "POST", "q=" + Uri.EscapeDataString(chunk)).ConfigureAwait(false);
            }

            // Respuesta: [[["traducido","original",...],...], null, "en", ...]
            var root = MiniJson.Parse(json) as List<object>;
            var sb = new StringBuilder();
            if (root != null && root.Count > 0 && root[0] is List<object> segments)
            {
                foreach (var seg in segments)
                {
                    if (seg is List<object> s && s.Count > 0 && s[0] is string t)
                    {
                        sb.Append(t);
                    }
                }
            }

            var source = root != null && root.Count > 2 ? root[2] as string : null;
            if (sb.Length == 0)
            {
                throw new InvalidOperationException("Respuesta de traducción vacía.");
            }

            return Tuple.Create(sb.ToString(), source);
        }

        /// <summary>Lector JSON mínimo (listas, objetos, textos, números, true/false/null).</summary>
        private static class MiniJson
        {
            public static object Parse(string s)
            {
                var i = 0;
                return Value(s, ref i);
            }

            private static void Skip(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    i++;
                }
            }

            private static object Value(string s, ref int i)
            {
                Skip(s, ref i);
                if (i >= s.Length)
                {
                    return null;
                }

                var c = s[i];
                if (c == '[')
                {
                    i++;
                    var list = new List<object>();
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return list;
                    }

                    while (i < s.Length)
                    {
                        list.Add(Value(s, ref i));
                        Skip(s, ref i);
                        if (i < s.Length && s[i] == ',')
                        {
                            i++;
                            continue;
                        }

                        if (i < s.Length && s[i] == ']')
                        {
                            i++;
                        }

                        break;
                    }

                    return list;
                }

                if (c == '{')
                {
                    i++;
                    var obj = new Dictionary<string, object>();
                    while (i < s.Length)
                    {
                        Skip(s, ref i);
                        if (i < s.Length && s[i] == '}')
                        {
                            i++;
                            break;
                        }

                        var k = Value(s, ref i) as string ?? string.Empty;
                        Skip(s, ref i);
                        if (i < s.Length && s[i] == ':')
                        {
                            i++;
                        }

                        obj[k] = Value(s, ref i);
                        Skip(s, ref i);
                        if (i < s.Length && s[i] == ',')
                        {
                            i++;
                        }
                    }

                    return obj;
                }

                if (c == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            i++;
                            switch (s[i])
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case 'b': sb.Append('\b'); break;
                                case 'f': sb.Append('\f'); break;
                                case 'u':
                                    if (i + 4 < s.Length)
                                    {
                                        sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                                        i += 4;
                                    }

                                    break;
                                default: sb.Append(s[i]); break;
                            }
                        }
                        else
                        {
                            sb.Append(s[i]);
                        }

                        i++;
                    }

                    i++;
                    return sb.ToString();
                }

                // Número o literal
                var start = i;
                while (i < s.Length && s[i] != ',' && s[i] != ']' && s[i] != '}' && !char.IsWhiteSpace(s[i]))
                {
                    i++;
                }

                var token = s.Substring(start, i - start);
                if (token == "null")
                {
                    return null;
                }

                if (token == "true")
                {
                    return true;
                }

                if (token == "false")
                {
                    return false;
                }

                return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (object)d : token;
            }
        }
    }
}
