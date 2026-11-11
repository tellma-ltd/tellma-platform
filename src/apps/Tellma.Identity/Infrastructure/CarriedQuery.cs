// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     The query values an identity page passes on to every identity page it links, posts or
    ///     redirects to: which application the user came from, the page there to return to, and
    ///     the language that application asked for.
    ///     <para>
    ///         All three arrive on a URL anyone can write, so they are read through this one type
    ///         and nowhere else. A value is taken only when it is given once, is not blank and
    ///         fits its length limit; anything else counts as absent. None of them is trusted for
    ///         what it says: the client and the return address are checked against the client's
    ///         registration before a link is made from them, and the language list is matched
    ///         against the languages the deployment offers.
    ///     </para>
    /// </summary>
    /// <param name="ClientId">The client the caller named, when it named one.</param>
    /// <param name="ReturnUrl">The address in that client's application to return to.</param>
    /// <param name="UiLocales">The requested languages, as well-formed tags separated by spaces.</param>
    public sealed record CarriedQuery(string? ClientId, string? ReturnUrl, string? UiLocales)
    {
        /// <summary>The query name of <see cref="ClientId" />.</summary>
        public const string ClientIdName = "client_id";

        /// <summary>The query name of <see cref="ReturnUrl" />.</summary>
        public const string ReturnUrlName = "return_url";

        /// <summary>The query name of <see cref="UiLocales" />.</summary>
        public const string UiLocalesName = "ui_locales";

        /// <summary>The longest client id that is carried: the registry stores none longer.</summary>
        public const int MaxClientIdLength = 100;

        /// <summary>The longest return address that is carried.</summary>
        public const int MaxReturnUrlLength = 2048;

        /// <summary>The longest language list that is read.</summary>
        public const int MaxUiLocalesLength = 255;

        /// <summary>The longest single language tag that is kept.</summary>
        private const int MaxLanguageTagLength = 35;

        /// <summary>A request that carries none of the values.</summary>
        public static CarriedQuery None { get; } = new(null, null, null);

        /// <summary>Whether no value is carried.</summary>
        public bool IsEmpty => ClientId is null && ReturnUrl is null && UiLocales is null;

        /// <summary>Reads the carried values of a request.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The values, each null when the request does not carry a usable one.</returns>
        public static CarriedQuery Read(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return Read(request.Query);
        }

        /// <summary>Reads the carried values of a query.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The values, each null when the query does not carry a usable one.</returns>
        public static CarriedQuery Read(IQueryCollection query)
        {
            ArgumentNullException.ThrowIfNull(query);

            string? clientId = Single(query, ClientIdName, MaxClientIdLength);
            string? returnUrl = Single(query, ReturnUrlName, MaxReturnUrlLength);
            string? uiLocales = NormalizeUiLocales(Single(query, UiLocalesName, MaxUiLocalesLength));

            return clientId is null && returnUrl is null && uiLocales is null
                ? None
                : new CarriedQuery(clientId, returnUrl, uiLocales);
        }

        /// <summary>
        ///     Reduces a <c>ui_locales</c> value to its well-formed language tags, in the order
        ///     given and separated by single spaces.
        /// </summary>
        /// <param name="value">The value as a caller sent it.</param>
        /// <returns>
        ///     The tags, or null when the value is missing, longer than
        ///     <see cref="MaxUiLocalesLength" /> or holds no well-formed tag.
        /// </returns>
        public static string? NormalizeUiLocales(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxUiLocalesLength)
            {
                return null;
            }

            // A malformed entry is dropped on its own: a list is a preference order, and one
            // entry this server cannot read says nothing about the entries around it.
            StringBuilder? tags = null;
            foreach (string tag in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IsLanguageTag(tag))
                {
                    continue;
                }

                tags ??= new StringBuilder(value.Length);
                if (tags.Length > 0)
                {
                    tags.Append(' ');
                }

                tags.Append(tag);
            }

            return tags?.ToString();
        }

        /// <summary>
        ///     Adds each value this holds to a URL, unless the URL's query names that value
        ///     already. The URL's own values always win: a link that states a language keeps it.
        /// </summary>
        /// <param name="url">The URL to add to; its fragment, when it has one, stays last.</param>
        /// <returns>The URL with the missing values appended to its query.</returns>
        public string AppendTo(string url)
        {
            ArgumentNullException.ThrowIfNull(url);

            if (IsEmpty)
            {
                return url;
            }

            List<KeyValuePair<string, string?>> missing = [];
            AddIfMissing(missing, url, ClientIdName, ClientId);
            AddIfMissing(missing, url, ReturnUrlName, ReturnUrl);
            AddIfMissing(missing, url, UiLocalesName, UiLocales);

            return missing.Count == 0 ? url : QueryHelpers.AddQueryString(url, missing);
        }

        /// <summary>
        ///     Replaces the <c>ui_locales</c> value of a URL that has one, and leaves a URL without
        ///     one as it is. Every other part of the URL is kept exactly as written.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="uiLocales">The value to put in place of the URL's own.</param>
        /// <returns>The URL with its language replaced, or the same URL.</returns>
        public static string WithUiLocales(string url, string uiLocales)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentException.ThrowIfNullOrWhiteSpace(uiLocales);

            (string path, string query, string fragment) = Split(url);
            if (!Names(query, UiLocalesName))
            {
                return url;
            }

            // Rebuilt pair by pair rather than parsed and re-encoded, so every other pair keeps
            // the exact encoding it arrived in.
            StringBuilder rebuilt = new(url.Length);
            rebuilt.Append(path);
            bool replaced = false;
            char separator = '?';
            foreach (QueryStringEnumerable.EncodedNameValuePair pair in new QueryStringEnumerable(query))
            {
                if (IsNamed(pair, UiLocalesName))
                {
                    // A repeated name collapses into the one replacement.
                    if (replaced)
                    {
                        continue;
                    }

                    replaced = true;
                    rebuilt.Append(separator).Append(UiLocalesName).Append('=').Append(UrlEncoder.Default.Encode(uiLocales));
                }
                else
                {
                    AppendAsWritten(rebuilt, separator, query, pair);
                }

                separator = '&';
            }

            return rebuilt.Append(fragment).ToString();
        }

        /// <summary>
        ///     Rewrites the value of the one query pair of a URL that has the given name, and
        ///     leaves the URL as it is when no pair has that name, when more than one does, or
        ///     when the rewrite returns the value it was given. Every other part of the URL is
        ///     kept exactly as written.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="name">The name of the pair, compared without case.</param>
        /// <param name="rewrite">Receives the pair's decoded value and returns the value to write.</param>
        /// <returns>The URL with the value rewritten, or the same URL.</returns>
        public static string WithValue(string url, string name, Func<string, string> rewrite)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(rewrite);

            (string path, string query, string fragment) = Split(url);

            // A name given twice has no one value to rewrite: which of the two a reader takes
            // is the reader's business, and this does not guess.
            int occurrences = 0;
            string current = string.Empty;
            foreach (QueryStringEnumerable.EncodedNameValuePair pair in new QueryStringEnumerable(query))
            {
                if (IsNamed(pair, name))
                {
                    occurrences++;
                    current = pair.DecodeValue().ToString();
                }
            }

            if (occurrences != 1)
            {
                return url;
            }

            string replacement = rewrite(current);
            if (string.Equals(replacement, current, StringComparison.Ordinal))
            {
                return url;
            }

            // Rebuilt pair by pair, as the language replacement above is, so only the one value
            // changes; the pair keeps its name as it was spelled.
            StringBuilder rebuilt = new(url.Length);
            rebuilt.Append(path);
            char separator = '?';
            foreach (QueryStringEnumerable.EncodedNameValuePair pair in new QueryStringEnumerable(query))
            {
                if (IsNamed(pair, name))
                {
                    rebuilt.Append(separator).Append(pair.EncodedName.Span).Append('=').Append(UrlEncoder.Default.Encode(replacement));
                }
                else
                {
                    AppendAsWritten(rebuilt, separator, query, pair);
                }

                separator = '&';
            }

            return rebuilt.Append(fragment).ToString();
        }

        /// <summary>Reads a value that was given exactly once, is not blank and fits its limit.</summary>
        private static string? Single(IQueryCollection query, string name, int maxLength)
        {
            StringValues values = query[name];
            return values.Count == 1 && values[0] is { } value && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength
                ? value
                : null;
        }

        /// <summary>
        ///     Whether text has the shape of a language tag: letters and digits in groups joined by
        ///     single hyphens. Shape only; whether the language is offered is decided elsewhere.
        /// </summary>
        private static bool IsLanguageTag(string tag)
        {
            if (tag.Length > MaxLanguageTagLength)
            {
                return false;
            }

            bool afterHyphen = true;
            foreach (char character in tag)
            {
                if (character == '-')
                {
                    if (afterHyphen)
                    {
                        return false;
                    }

                    afterHyphen = true;
                }
                else if (char.IsAsciiLetterOrDigit(character))
                {
                    afterHyphen = false;
                }
                else
                {
                    return false;
                }
            }

            return !afterHyphen;
        }

        /// <summary>Queues a value for appending when it is held and the URL does not name it.</summary>
        private static void AddIfMissing(List<KeyValuePair<string, string?>> missing, string url, string name, string? value)
        {
            if (value is not null && !Names(Split(url).Query, name))
            {
                missing.Add(new KeyValuePair<string, string?>(name, value));
            }
        }

        /// <summary>Splits a URL into what precedes its query, the query, and the fragment.</summary>
        private static (string Path, string Query, string Fragment) Split(string url)
        {
            int hash = url.IndexOf('#', StringComparison.Ordinal);
            string fragment = hash < 0 ? string.Empty : url[hash..];
            string beforeFragment = hash < 0 ? url : url[..hash];

            int mark = beforeFragment.IndexOf('?', StringComparison.Ordinal);
            return mark < 0
                ? (beforeFragment, string.Empty, fragment)
                : (beforeFragment[..mark], beforeFragment[mark..], fragment);
        }

        /// <summary>Whether a query has a pair with the given name.</summary>
        private static bool Names(string query, string name)
        {
            foreach (QueryStringEnumerable.EncodedNameValuePair pair in new QueryStringEnumerable(query))
            {
                if (IsNamed(pair, name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Whether a pair has the given name. Names compare without case, as the framework's
        ///     own query reading does, so no spelling of a name slips a second copy past this.
        /// </summary>
        private static bool IsNamed(QueryStringEnumerable.EncodedNameValuePair pair, string name)
        {
            return pair.DecodeName().Span.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Appends a query pair to a URL being rebuilt, exactly as it was written.</summary>
        /// <param name="rebuilt">The URL so far.</param>
        /// <param name="separator">The character that introduces the pair.</param>
        /// <param name="query">The query the pair was read from.</param>
        /// <param name="pair">The pair.</param>
        private static void AppendAsWritten(
            StringBuilder rebuilt, char separator, string query, QueryStringEnumerable.EncodedNameValuePair pair)
        {
            rebuilt.Append(separator).Append(pair.EncodedName.Span);
            if (!pair.EncodedValue.IsEmpty || HasEqualsSign(query, pair))
            {
                rebuilt.Append('=').Append(pair.EncodedValue.Span);
            }
        }

        /// <summary>
        ///     Whether a pair with an empty value was written with its equals sign (<c>a=</c>) or
        ///     without one (<c>a</c>), so rebuilding the query keeps whichever it was.
        /// </summary>
        private static bool HasEqualsSign(string query, QueryStringEnumerable.EncodedNameValuePair pair)
        {
            // The name is a slice of the query string itself, so its own offset says which
            // character follows it. Without that offset the pair is rebuilt with the sign, which
            // reads back as the same empty value.
            return !MemoryMarshal.TryGetString(pair.EncodedName, out _, out int start, out int length)
                || (start + length < query.Length && query[start + length] == '=');
        }
    }
}
