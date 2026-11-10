using ConsoleUpdateCenter.Core;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ConsoleUpdateCenter.Providers.Steam
{
    /// <summary>
    /// Notas de las últimas actualizaciones de un juego en Steam, en el idioma del usuario:
    /// 1) la versión oficial de Steam en ese idioma, si el estudio la publicó;
    /// 2) si no, traducción automática (Google Translate), si está activada.
    /// </summary>
    internal static class SteamNews
    {
        public enum Source
        {
            /// <summary>El texto ya estaba en el idioma del usuario (o no se tradujo).</summary>
            Original,
            /// <summary>Versión en el idioma del usuario publicada en Steam.</summary>
            SteamOfficial,
            /// <summary>Traducido automáticamente.</summary>
            Machine,
            /// <summary>Se intentó traducir y falló: se muestra el original.</summary>
            TranslationFailed
        }

        public class Item
        {
            public string Title { get; set; }
            public DateTime Date { get; set; }
            public string Text { get; set; }
            public Source Source { get; set; }
        }

        private static readonly Dictionary<string, Tuple<DateTime, List<Item>>> cache = new Dictionary<string, Tuple<DateTime, List<Item>>>();

        public static async Task<List<Item>> GetAsync(string appId, string language, bool translate)
        {
            var cacheKey = appId + "|" + language + "|" + translate;
            lock (cache)
            {
                if (cache.TryGetValue(cacheKey, out var hit) && (DateTime.Now - hit.Item1).TotalMinutes < 30)
                {
                    return hit.Item2;
                }
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var steamLang = SteamLanguage(language);

            List<Item> items = null;
            try
            {
                items = await FromEventsAsync(appId, steamLang).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Debug($"Novedades de Steam {appId}: la API de eventos falló ({e.Message}); se usa ISteamNews.");
            }

            if (items == null || items.Count == 0)
            {
                items = await FromNewsAsync(appId).ConfigureAwait(false);
            }

            if (translate)
            {
                foreach (var item in items.Where(i => i.Source != Source.SteamOfficial))
                {
                    try
                    {
                        var title = await Translator.TranslateAsync(item.Title, language).ConfigureAwait(false);
                        var body = await Translator.TranslateAsync(item.Text, language).ConfigureAwait(false);
                        item.Title = title.Text;
                        item.Text = body.Text;
                        item.Source = title.Translated || body.Translated ? Source.Machine : Source.Original;
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"No se pudo traducir una novedad de {appId}.", e);
                        item.Source = Source.TranslationFailed;
                    }
                }
            }

            lock (cache)
            {
                cache[cacheKey] = Tuple.Create(DateTime.Now, items);
            }

            Log.Debug($"Novedades de Steam {appId} ({steamLang}): {items.Count} mostradas; " +
                      string.Join(", ", items.Select(i => i.Source.ToString())));
            return items;
        }

        #region API de eventos de Steam (textos localizados por el estudio)

        private static async Task<List<Item>> FromEventsAsync(string appId, string steamLang)
        {
            var url = "https://store.steampowered.com/events/ajaxgetpartnereventspageable/?clan_accountid=0&appid=" +
                      Uri.EscapeDataString(appId) + "&offset=0&count=12&l=" + steamLang +
                      "&origin=" + Uri.EscapeDataString("https://store.steampowered.com");
            var json = await Download(url).ConfigureAwait(false);
            var data = Serialization.FromJson<EventsResponse>(json);
            var events = data?.events ?? new List<PartnerEvent>();

            var wanted = LanguageIds(steamLang);

            // Actualizaciones (12 pequeña, 13 normal, 14 grande) primero
            var chosen = events
                .Where(e => e.announcement_body != null && !string.IsNullOrWhiteSpace(e.announcement_body.body))
                .OrderByDescending(e => e.event_type >= 12 && e.event_type <= 14)
                .ThenByDescending(e => e.rtime32_start_time)
                .Take(3)
                .OrderByDescending(e => e.rtime32_start_time)
                .ToList();

            return chosen.Select(e => new Item
            {
                Title = Clean(e.announcement_body.headline ?? e.event_name),
                Date = DateTimeOffset.FromUnixTimeSeconds(e.rtime32_start_time > 0 ? e.rtime32_start_time : e.announcement_body.posttime).LocalDateTime,
                Text = Clean(e.announcement_body.body),
                Source = wanted.Contains(e.announcement_body.language) ? (steamLang == "english" ? Source.Original : Source.SteamOfficial) : Source.Original
            }).ToList();
        }

        public class EventsResponse
        {
            public int success { get; set; }
            public List<PartnerEvent> events { get; set; }
        }

        public class PartnerEvent
        {
            public string event_name { get; set; }
            public int event_type { get; set; }
            public long rtime32_start_time { get; set; }
            public AnnouncementBody announcement_body { get; set; }
        }

        public class AnnouncementBody
        {
            public string headline { get; set; }
            public string body { get; set; }
            public long posttime { get; set; }
            public int language { get; set; }
        }

        #endregion

        #region ISteamNews (respaldo)

        private static async Task<List<Item>> FromNewsAsync(string appId)
        {
            var url = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=" + Uri.EscapeDataString(appId) +
                      "&count=12&maxlength=1800&format=json";
            var json = await Download(url).ConfigureAwait(false);
            var data = Serialization.FromJson<NewsResponse>(json);
            var all = data?.appnews?.newsitems ?? new List<NewsItem>();

            // Notas de parche primero, después anuncios del desarrollador; sin noticias de webs externas
            var chosen = all
                .Where(n => n.feed_type == 1 || (n.feedname ?? "").IndexOf("steam_community_announcements", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(n => n.tags != null && n.tags.Contains("patchnotes"))
                .ThenByDescending(n => n.date)
                .Take(3)
                .ToList();

            if (chosen.Count == 0)
            {
                chosen = all.OrderByDescending(n => n.date).Take(3).ToList();
            }

            return chosen
                .OrderByDescending(n => n.date)
                .Select(n => new Item
                {
                    Title = Clean(n.title),
                    Date = DateTimeOffset.FromUnixTimeSeconds(n.date).LocalDateTime,
                    Text = Clean(n.contents),
                    Source = Source.Original
                })
                .ToList();
        }

        public class NewsResponse
        {
            public AppNews appnews { get; set; }
        }

        public class AppNews
        {
            public List<NewsItem> newsitems { get; set; }
        }

        public class NewsItem
        {
            public string title { get; set; }
            public string contents { get; set; }
            public string feedname { get; set; }
            public int feed_type { get; set; }
            public long date { get; set; }
            public List<string> tags { get; set; }
        }

        #endregion

        #region Utilidades

        private static async Task<string> Download(string url)
        {
            using (var web = new WebClient { Encoding = Encoding.UTF8 })
            {
                web.Headers[HttpRequestHeader.UserAgent] = "ConsoleUpdateCenter";
                return await web.DownloadStringTaskAsync(url).ConfigureAwait(false);
            }
        }

        /// <summary>Nombre del idioma en Steam para un código de Loc (igual que Console Overlay).</summary>
        public static string SteamLanguage(string code)
        {
            var c = (code ?? "en").ToLowerInvariant();
            if (c.StartsWith("es")) { return "spanish"; }
            if (c.StartsWith("fr")) { return "french"; }
            if (c.StartsWith("de")) { return "german"; }
            if (c.StartsWith("pt")) { return c.Contains("br") ? "brazilian" : "portuguese"; }
            if (c.StartsWith("it")) { return "italian"; }
            if (c.StartsWith("ru")) { return "russian"; }
            if (c == "zht") { return "tchinese"; }
            if (c.StartsWith("zh")) { return "schinese"; }
            if (c.StartsWith("ja")) { return "japanese"; }
            if (c.StartsWith("pl")) { return "polish"; }
            if (c.StartsWith("tr")) { return "turkish"; }
            if (c.StartsWith("ko")) { return "koreana"; }
            if (c.StartsWith("nl")) { return "dutch"; }
            if (c.StartsWith("uk")) { return "ukrainian"; }
            return "english";
        }

        /// <summary>Números de idioma de Steam que valen para ese idioma (p. ej. español de España y de Latinoamérica).</summary>
        private static HashSet<int> LanguageIds(string steamLang)
        {
            switch (steamLang)
            {
                case "german": return new HashSet<int> { 1 };
                case "french": return new HashSet<int> { 2 };
                case "italian": return new HashSet<int> { 3 };
                case "koreana": return new HashSet<int> { 4 };
                case "spanish": return new HashSet<int> { 5, 27 };
                case "schinese": return new HashSet<int> { 6 };
                case "tchinese": return new HashSet<int> { 7 };
                case "russian": return new HashSet<int> { 8 };
                case "japanese": return new HashSet<int> { 10 };
                case "portuguese":
                case "brazilian": return new HashSet<int> { 11, 22 };
                case "polish": return new HashSet<int> { 12 };
                case "dutch": return new HashSet<int> { 14 };
                case "turkish": return new HashSet<int> { 21 };
                case "ukrainian": return new HashSet<int> { 26 };
                default: return new HashSet<int> { 0 };
            }
        }

        /// <summary>Quita BBCode, HTML y enlaces de imágenes del texto de Steam.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            s = Regex.Replace(s, @"\{STEAM_CLAN_IMAGE\}\S*", string.Empty);
            s = Regex.Replace(s, @"\[\*\]", "\n• ");
            s = Regex.Replace(s, @"\[(h1|h2|h3)\]", "\n");
            s = Regex.Replace(s, @"\[/(h1|h2|h3|p|list|olist)\]", "\n");
            s = Regex.Replace(s, @"\[(img|previewyoutube|video)[^\]]*\].*?\[/\1\]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            s = Regex.Replace(s, @"\[[^\]]{1,300}\]", string.Empty);
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<li>", "\n• ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", string.Empty);
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"https?://\S+\.(png|jpg|jpeg|gif|webp)\S*", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @"\n\s*\n\s*\n+", "\n\n");
            s = s.Trim();

            // Notas muy largas: se recortan para la pantalla
            return s.Length > 4000 ? s.Substring(0, 4000).TrimEnd() + "…" : s;
        }

        #endregion
    }
}
