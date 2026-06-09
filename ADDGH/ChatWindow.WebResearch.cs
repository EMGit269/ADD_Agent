using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static readonly object _webResearchTextCacheLock = new object();
        private static readonly Dictionary<string, string> _webResearchTextCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static async Task<string> ExecuteWebResearchAsync(
            string mode,
            string query,
            string url,
            JArray allowedDomains,
            int maxResults,
            int maxChars,
            System.Threading.CancellationToken ct)
        {
            string normalizedMode = (mode ?? "").Trim().ToLowerInvariant();
            if (normalizedMode != "fetch")
                normalizedMode = "search";

            maxResults = Math.Max(1, Math.Min(maxResults <= 0 ? 5 : maxResults, 10));
            maxChars = Math.Max(800, Math.Min(maxChars <= 0 ? 6000 : maxChars, 16000));
            var domains = ReadAllowedDomains(allowedDomains);

            if (normalizedMode == "fetch")
                return await FetchWebPageAsync(url, domains, maxChars, ct).ConfigureAwait(false);

            return await SearchWebAsync(query, domains, maxResults, maxChars, ct).ConfigureAwait(false);
        }

        private static List<string> ReadAllowedDomains(JArray allowedDomains)
        {
            var result = new List<string>();
            if (allowedDomains == null)
                return result;

            foreach (var item in allowedDomains)
            {
                string domain = (item?.ToString() ?? "").Trim().TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(domain))
                    continue;
                if (domain.Contains("/") || domain.Contains("\\") || domain.Contains(":"))
                    continue;
                if (!result.Contains(domain))
                    result.Add(domain);
            }
            return result;
        }

        private static bool IsAllowedWebUrl(string url, List<string> allowedDomains, out Uri uri, out string error)
        {
            uri = null;
            error = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                error = "url is required.";
                return false;
            }
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                error = "url is not an absolute URL.";
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "only http/https URLs are allowed.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                error = "URLs with credentials are not allowed.";
                return false;
            }
            string host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || host == "127.0.0.1" || host == "::1")
            {
                error = "localhost URLs are not allowed for web research.";
                return false;
            }
            if (allowedDomains != null && allowedDomains.Count > 0)
            {
                bool ok = allowedDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
                if (!ok)
                {
                    error = "host is outside allowed_domains.";
                    return false;
                }
            }
            return true;
        }

        private static async Task<string> SearchWebAsync(string query, List<string> allowedDomains, int maxResults, int maxChars, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Error: query is required for web search.";

            try
            {
                var mcneelResults = await SearchMcNeelApiDocsAsync(query, allowedDomains, maxResults, ct).ConfigureAwait(false);
                if (mcneelResults.Count > 0)
                {
                    var mcneelPayload = new JObject
                    {
                        ["mode"] = "search",
                        ["query"] = query.Trim(),
                        ["provider"] = "mcneel_api_index",
                        ["search_url"] = "https://mcneel.github.io/",
                        ["result_count"] = mcneelResults.Count,
                        ["results"] = new JArray(mcneelResults)
                    };
                    string mcneelJson = mcneelPayload.ToString(Formatting.None);
                    return mcneelJson.Length <= maxChars ? mcneelJson : mcneelJson.Substring(0, maxChars) + "...";
                }
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("McNeel API lookup failed: " + ex.Message);
            }

            string effectiveQuery = query.Trim();
            if (allowedDomains != null && allowedDomains.Count > 0)
                effectiveQuery += " " + string.Join(" ", allowedDomains.Select(d => "site:" + d));

            var errors = new JArray();
            var attempts = new[]
            {
                new
                {
                    Provider = "bing",
                    Url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(effectiveQuery)
                }
            };

            string usedProvider = null;
            string searchUrl = null;
            List<JObject> results = null;
            foreach (var attempt in attempts)
            {
                try
                {
                    string html = await DownloadTextAsync(attempt.Url, ct).ConfigureAwait(false);
                    results = ParseBingResults(html, allowedDomains, maxResults);
                    usedProvider = attempt.Provider;
                    searchUrl = attempt.Url;
                    if (results.Count > 0)
                        break;
                }
                catch (Exception ex)
                {
                    errors.Add(new JObject
                    {
                        ["provider"] = attempt.Provider,
                        ["url"] = attempt.Url,
                        ["error"] = ex.Message
                    });
                }
            }

            if (results == null)
                results = new List<JObject>();

            var payload = new JObject
            {
                ["mode"] = "search",
                ["query"] = query.Trim(),
                ["provider"] = usedProvider ?? "",
                ["search_url"] = searchUrl,
                ["result_count"] = results.Count,
                ["results"] = new JArray(results)
            };
            if (errors.Count > 0)
                payload["fallback_errors"] = errors;

            string json = payload.ToString(Formatting.None);
            return json.Length <= maxChars ? json : json.Substring(0, maxChars) + "...";
        }

        private sealed class ApiDocRoot
        {
            public string Name;
            public string BaseUrl;
            public string RootUrl;
            public string[] NamespaceHints;
        }

        private sealed class ApiDocCandidate
        {
            public string Title;
            public string Url;
            public string Snippet;
            public int Score;
        }

        private static async Task<List<JObject>> SearchMcNeelApiDocsAsync(
            string query,
            List<string> allowedDomains,
            int maxResults,
            System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<JObject>();

            if (allowedDomains != null
                && allowedDomains.Count > 0
                && !allowedDomains.Any(d => string.Equals(d, "mcneel.github.io", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<JObject>();
            }

            string normalized = query.Trim();
            string lower = normalized.ToLowerInvariant();
            bool constrainedToMcNeel = allowedDomains != null
                && allowedDomains.Any(d => string.Equals(d, "mcneel.github.io", StringComparison.OrdinalIgnoreCase));
            bool looksLikeApiQuery = constrainedToMcNeel
                || lower.Contains("api")
                || lower.Contains("doc")
                || lower.Contains("rhino")
                || lower.Contains("rhinocommon")
                || lower.Contains("grasshopper")
                || lower.Contains("gh_")
                || lower.Contains("igh");

            if (!looksLikeApiQuery)
                return new List<JObject>();

            var roots = new List<ApiDocRoot>();
            bool includeGrasshopper = constrainedToMcNeel
                || lower.Contains("grasshopper")
                || lower.Contains("gh_")
                || lower.Contains("igh")
                || lower.Contains("kernel");
            bool includeRhino = constrainedToMcNeel
                || !includeGrasshopper
                || lower.Contains("rhino")
                || lower.Contains("rhinocommon")
                || lower.Contains("geometry")
                || lower.Contains("docobjects")
                || lower.Contains("clipping")
                || lower.Contains("hiddenline")
                || lower.Contains("objecttable");

            if (includeRhino)
            {
                roots.Add(new ApiDocRoot
                {
                    Name = "RhinoCommon",
                    BaseUrl = "https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/",
                    RootUrl = "https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/R_Project_RhinoCommon.htm",
                    NamespaceHints = new[] { "Rhino", "Rhino.Geometry", "Rhino.DocObjects", "Rhino.DocObjects.Tables", "Rhino.Display", "Rhino.FileIO" }
                });
            }

            if (includeGrasshopper)
            {
                roots.Add(new ApiDocRoot
                {
                    Name = "Grasshopper",
                    BaseUrl = "https://mcneel.github.io/grasshopper-api-docs/api/grasshopper/html/",
                    RootUrl = "https://mcneel.github.io/grasshopper-api-docs/api/grasshopper/html/723c01da-9986-4db2-8f53-6f3a7494df75.htm",
                    NamespaceHints = new[] { "Grasshopper", "Grasshopper.Kernel", "Grasshopper.Kernel.Data", "Grasshopper.Kernel.Types" }
                });
            }

            var queryTokens = BuildSearchTokens(normalized);
            var allCandidates = new Dictionary<string, ApiDocCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var pagesToFetch = new List<string> { root.RootUrl };

                foreach (string ns in ExtractNamespaces(normalized))
                {
                    if (ns.StartsWith("Rhino.", StringComparison.OrdinalIgnoreCase) && root.Name == "RhinoCommon")
                        pagesToFetch.Add(root.BaseUrl + "N_" + ns.Replace(".", "_") + ".htm");
                    else if (ns.StartsWith("Grasshopper.", StringComparison.OrdinalIgnoreCase) && root.Name == "Grasshopper")
                        pagesToFetch.Add(root.BaseUrl + "N_" + ns.Replace(".", "_") + ".htm");
                }

                foreach (string directTypeUrl in BuildDirectTypeUrls(root, normalized))
                    pagesToFetch.Add(directTypeUrl);

                string rootHtml = await TryDownloadTextAsync(root.RootUrl, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(rootHtml))
                {
                    var rootLinks = ParseApiDocLinks(rootHtml, root.RootUrl, root.BaseUrl, queryTokens, includeZeroScore: true);
                    foreach (var link in rootLinks.Where(l => IsApiNamespacePage(l.Url)))
                    {
                        int nsScore = ScoreApiDocCandidate(link.Title, link.Url, queryTokens);
                        if (nsScore > 0 || constrainedToMcNeel || NamespaceLooksRelevant(lower, link.Title))
                            pagesToFetch.Add(link.Url);
                    }
                }

                foreach (string ns in root.NamespaceHints)
                {
                    if (NamespaceLooksRelevant(lower, ns))
                        pagesToFetch.Add(root.BaseUrl + "N_" + ns.Replace(".", "_") + ".htm");
                }

                var parsed = new List<ApiDocCandidate>();
                foreach (string pageUrl in pagesToFetch.Distinct(StringComparer.OrdinalIgnoreCase).Take(80))
                {
                    string html = await TryDownloadTextAsync(pageUrl, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(html))
                        continue;
                    parsed.AddRange(ParseApiDocLinks(html, pageUrl, root.BaseUrl, queryTokens, includeZeroScore: false));
                }

                foreach (var candidate in parsed)
                    UpsertApiDocCandidate(allCandidates, candidate);

                foreach (var typePage in parsed
                    .Where(c => c.Score > 0 && IsApiTypePage(c.Url))
                    .OrderByDescending(c => c.Score)
                    .Take(6))
                {
                    string html = await TryDownloadTextAsync(typePage.Url, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(html))
                        continue;
                    foreach (var child in ParseApiDocLinks(html, typePage.Url, root.BaseUrl, queryTokens, includeZeroScore: false))
                        UpsertApiDocCandidate(allCandidates, child);
                }
            }

            return allCandidates.Values
                .Where(c => c.Score > 0)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Title)
                .Take(Math.Max(1, Math.Min(maxResults, 10)))
                .Select(c => new JObject
                {
                    ["title"] = c.Title,
                    ["url"] = c.Url,
                    ["snippet"] = c.Snippet
                })
                .ToList();
        }

        private static async Task<string> TryDownloadTextAsync(string url, System.Threading.CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return "";

                lock (_webResearchTextCacheLock)
                {
                    if (_webResearchTextCache.TryGetValue(url, out string cached))
                        return cached;
                }

                string text = await DownloadTextAsync(url, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(text)
                    && Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                    && string.Equals(uri.Host, "mcneel.github.io", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_webResearchTextCacheLock)
                    {
                        if (_webResearchTextCache.Count > 300)
                            _webResearchTextCache.Clear();
                        _webResearchTextCache[url] = text;
                    }
                }
                return text;
            }
            catch
            {
                return "";
            }
        }

        private static bool NamespaceLooksRelevant(string lowerQuery, string ns)
        {
            string lowerNs = ns.ToLowerInvariant();
            if (lowerQuery.Contains(lowerNs))
                return true;
            string last = lowerNs.Split('.').LastOrDefault() ?? "";
            return last.Length > 0 && lowerQuery.Contains(last);
        }

        private static IEnumerable<string> ExtractNamespaces(string query)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(query ?? "", @"\b(?:Rhino|Grasshopper)(?:\.[A-Za-z_][A-Za-z0-9_]*)+"))
            {
                string value = match.Value.Trim('.');
                string[] parts = value.Split('.');
                for (int i = parts.Length; i >= 2; i--)
                {
                    string ns = string.Join(".", parts.Take(i));
                    if (seen.Add(ns))
                        yield return ns;
                }
            }
        }

        private static IEnumerable<string> BuildDirectTypeUrls(ApiDocRoot root, string query)
        {
            foreach (Match match in Regex.Matches(query ?? "", @"\b(?:Rhino|Grasshopper)(?:\.[A-Za-z_][A-Za-z0-9_]*)+"))
            {
                string fullName = match.Value.Trim('.');
                yield return root.BaseUrl + "T_" + fullName.Replace(".", "_") + ".htm";
            }
        }

        private static List<string> BuildSearchTokens(string query)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "api", "apis", "doc", "docs", "official", "reference", "rhinocommon", "rhino", "grasshopper",
                "class", "type", "method", "property", "namespace", "csharp", "c", "cs", "sdk", "html"
            };
            return Regex.Split(query ?? "", @"[^A-Za-z0-9_]+")
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length >= 2 && !stopWords.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ApiDocCandidate> ParseApiDocLinks(string html, string pageUrl, string baseUrl, List<string> queryTokens, bool includeZeroScore)
        {
            var results = new List<ApiDocCandidate>();
            if (string.IsNullOrWhiteSpace(html))
                return results;

            foreach (Match match in Regex.Matches(html, "<a[^>]+href=\"(?<href>[^\"]+)\"[^>]*>(?<title>[\\s\\S]*?)</a>", RegexOptions.IgnoreCase))
            {
                string href = WebUtility.HtmlDecode(match.Groups["href"].Value ?? "").Trim();
                string title = CleanText(match.Groups["title"].Value);
                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(title))
                    continue;
                if (!href.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                    continue;

                Uri pageUri = new Uri(pageUrl);
                Uri uri = Uri.TryCreate(href, UriKind.Absolute, out Uri absolute)
                    ? absolute
                    : new Uri(pageUri, href);

                if (!uri.AbsoluteUri.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                    continue;

                string file = uri.Segments.LastOrDefault() ?? "";
                if (!IsApiDocPage(file))
                    continue;

                int score = ScoreApiDocCandidate(title, uri.AbsoluteUri, queryTokens);
                if (score <= 0 && IsApiNamespacePage(uri.AbsoluteUri))
                    score = ScoreApiDocCandidate(file, uri.AbsoluteUri, queryTokens);
                if (score <= 0 && !includeZeroScore)
                    continue;

                results.Add(new ApiDocCandidate
                {
                    Title = title,
                    Url = uri.AbsoluteUri,
                    Snippet = "Official McNeel API documentation.",
                    Score = Math.Max(0, score)
                });
            }
            return results;
        }

        private static bool IsApiDocPage(string file)
        {
            return file.StartsWith("N_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("T_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("M_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("P_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("Overload_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("Methods_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("Properties_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsApiNamespacePage(string url)
        {
            string file = new Uri(url).Segments.LastOrDefault() ?? "";
            return file.StartsWith("N_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsApiTypePage(string url)
        {
            string file = new Uri(url).Segments.LastOrDefault() ?? "";
            return file.StartsWith("T_", StringComparison.OrdinalIgnoreCase);
        }

        private static int ScoreApiDocCandidate(string title, string url, List<string> queryTokens)
        {
            if (queryTokens == null || queryTokens.Count == 0)
                return 0;

            string haystack = ((title ?? "") + " " + (url ?? "")).ToLowerInvariant();
            int score = 0;
            foreach (string token in queryTokens)
            {
                if (haystack.Contains(token))
                    score += token.Length >= 8 ? 4 : 2;
            }
            if (url.IndexOf("/html/T_", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 2;
            if (url.IndexOf("/html/M_", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 2;
            if (url.IndexOf("/html/P_", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 1;
            return score;
        }

        private static void UpsertApiDocCandidate(Dictionary<string, ApiDocCandidate> candidates, ApiDocCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Url))
                return;

            if (!candidates.TryGetValue(candidate.Url, out ApiDocCandidate existing) || candidate.Score > existing.Score)
                candidates[candidate.Url] = candidate;
        }

        private static async Task<string> FetchWebPageAsync(string url, List<string> allowedDomains, int maxChars, System.Threading.CancellationToken ct)
        {
            if (!IsAllowedWebUrl(url, allowedDomains, out Uri uri, out string error))
                return "Error: " + error;

            string html = await DownloadTextAsync(uri.AbsoluteUri, ct).ConfigureAwait(false);
            string title = ExtractTitle(html);
            string text = HtmlToPlainText(html);
            if (text.Length > maxChars)
                text = text.Substring(0, maxChars) + "...";

            return new JObject
            {
                ["mode"] = "fetch",
                ["url"] = uri.AbsoluteUri,
                ["title"] = title,
                ["content"] = text
            }.ToString(Formatting.None);
        }

        private static async Task<string> DownloadTextAsync(string url, System.Threading.CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 ADDGH-WebResearch/1.0");
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.5");
                using (var response = await GetConfiguredHttpClient(GetProviderRuntimeSettings()).SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + "\n" + ClampDiagDetail(body, 1000));
                    return body ?? "";
                }
            }
        }

        private static List<JObject> ParseBingResults(string html, List<string> allowedDomains, int maxResults)
        {
            var results = new List<JObject>();
            if (string.IsNullOrWhiteSpace(html))
                return results;

            var matches = Regex.Matches(
                html,
                "<li[^>]+class=\"[^\"]*b_algo[^\"]*\"[\\s\\S]*?<h2[^>]*>[\\s\\S]*?<a[^>]+href=\"(?<href>[^\"]+)\"[^>]*>(?<title>[\\s\\S]*?)</a>[\\s\\S]*?</h2>(?<tail>[\\s\\S]*?)(?=<li[^>]+class=\"[^\"]*b_algo|</ol>)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string href = WebUtility.HtmlDecode(match.Groups["href"].Value ?? "").Trim();
                string title = CleanText(match.Groups["title"].Value);
                if (!IsAllowedWebUrl(href, allowedDomains, out Uri uri, out _))
                    continue;

                string snippet = "";
                string tail = match.Groups["tail"].Value ?? "";
                var snippetMatch = Regex.Match(tail, "<p[^>]*>(?<snippet>[\\s\\S]*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (snippetMatch.Success)
                    snippet = CleanText(snippetMatch.Groups["snippet"].Value);

                results.Add(new JObject
                {
                    ["title"] = title,
                    ["url"] = uri.AbsoluteUri,
                    ["snippet"] = snippet
                });
                if (results.Count >= maxResults)
                    break;
            }
            return results;
        }

        private static string ExtractTitle(string html)
        {
            var match = Regex.Match(html ?? "", "<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? CleanText(match.Groups["title"].Value) : "";
        }

        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";

            string text = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return CleanText(text);
        }

        private static string CleanText(string value)
        {
            string text = WebUtility.HtmlDecode(value ?? "");
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = Regex.Replace(text, "\\s+", " ").Trim();
            return text;
        }
    }
}
