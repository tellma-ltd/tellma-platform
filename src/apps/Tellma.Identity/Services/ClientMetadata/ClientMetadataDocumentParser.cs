// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Text.Json;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>A document's validated members, before its key set is fetched.</summary>
    /// <param name="ClientName">The <c>client_name</c>, when the document has one.</param>
    /// <param name="RedirectUris">The <c>redirect_uris</c>, each absolute, distinct, in document order.</param>
    /// <param name="TokenEndpointAuthMethod"><c>none</c> or <c>private_key_jwt</c>.</param>
    /// <param name="JwksUri">The <c>jwks_uri</c>, on the document's origin; null for <c>none</c>.</param>
    public sealed record ParsedClientMetadata(
        string? ClientName,
        IReadOnlyList<Uri> RedirectUris,
        string TokenEndpointAuthMethod,
        Uri? JwksUri);

    /// <summary>
    ///     Validates a client ID metadata document and a <c>private_key_jwt</c> client's key set.
    ///     Every refusal is a <see cref="ClientMetadataException" /> whose reason names the member
    ///     at fault.
    /// </summary>
    public static class ClientMetadataDocumentParser
    {
        /// <summary>The <c>none</c> authentication method: a public client.</summary>
        public const string AuthMethodNone = "none";

        /// <summary>The <c>private_key_jwt</c> authentication method: a client that signs an assertion with a key from its key set.</summary>
        public const string AuthMethodPrivateKeyJwt = "private_key_jwt";

        /// <summary>The longest <c>client_name</c> accepted; it is shown on the consent page.</summary>
        public const int MaxClientNameLength = 100;

        /// <summary>The smallest RSA modulus accepted, in bits.</summary>
        public const int MinRsaModulusBits = 2048;

        /// <summary>The grant types a document may declare.</summary>
        private static readonly HashSet<string> AllowedGrantTypes = new(StringComparer.Ordinal)
        {
            "authorization_code",
            "refresh_token",
        };

        /// <summary>The curves an elliptic-curve key may use.</summary>
        private static readonly HashSet<string> AllowedCurves = new(StringComparer.Ordinal) { "P-256", "P-384", "P-521" };

        /// <summary>The members that carry private key material, which a published key set must never hold.</summary>
        private static readonly string[] PrivateKeyMembers = ["d", "p", "q", "dp", "dq", "qi", "oth", "k"];

        /// <summary>Redirect URI schemes refused outright: they run script or reach local files.</summary>
        private static readonly HashSet<string> RefusedRedirectSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            "javascript",
            "data",
            "file",
            "vbscript",
        };

        /// <summary>
        ///     The parser limits: no comments or trailing commas, and a shallow depth, since a
        ///     document has no reason to nest.
        /// </summary>
        private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = 8 };

        /// <summary>Validates a fetched document against the URL it was fetched from.</summary>
        /// <param name="clientId">The client id, the URL fetched.</param>
        /// <param name="body">The response body.</param>
        /// <returns>The validated members.</returns>
        /// <exception cref="ClientMetadataException">The document is refused.</exception>
        public static ParsedClientMetadata ParseDocument(Uri clientId, byte[] body)
        {
            ArgumentNullException.ThrowIfNull(clientId);
            ArgumentNullException.ThrowIfNull(body);

            using JsonDocument json = Parse(body, ClientMetadataRejectionReasons.Document);
            JsonElement root = json.RootElement;

            // client_id: the URL exactly, as presented. A document that names another client
            // cannot be used to register this one.
            if (!TryGetString(root, "client_id", out string? documentClientId)
                || !string.Equals(documentClientId, clientId.OriginalString, StringComparison.Ordinal))
            {
                throw Refuse(ClientMetadataRejectionReasons.ClientIdMismatch, "The document's client_id is not the URL it was fetched from.");
            }

            // A document client holds no secret, so a document that carries one is refused
            // rather than having it ignored.
            if (root.TryGetProperty("client_secret", out _) || root.TryGetProperty("client_secret_expires_at", out _))
            {
                throw Refuse(ClientMetadataRejectionReasons.Document, "The document carries a client secret.");
            }

            IReadOnlyList<Uri> redirectUris = ParseRedirectUris(root);
            string authMethod = ParseAuthMethod(root);

            // Keys come only from a key-set URL on the document's own origin, which the fetch
            // guards cover; an inline key set would be the document vouching for itself.
            if (root.TryGetProperty("jwks", out _))
            {
                throw Refuse(ClientMetadataRejectionReasons.InlineJwks, "The document carries an inline jwks, which is refused.");
            }

            // A none client has no use for keys, so a key-set URL on its document is a mistake
            // the author should hear about.
            bool none = authMethod is AuthMethodNone;
            if (none && root.TryGetProperty("jwks_uri", out _))
            {
                throw Refuse(ClientMetadataRejectionReasons.JwksUri, "The document's jwks_uri is set for the none method.");
            }

            Uri? jwksUri = none ? null : ParseJwksUri(root, clientId);
            ParseGrantTypes(root);
            string? clientName = ParseClientName(root);

            return new ParsedClientMetadata(clientName, redirectUris, authMethod, jwksUri);
        }

        /// <summary>Validates a fetched key set.</summary>
        /// <param name="body">The response body.</param>
        /// <returns>The key set, holding at least one signing key.</returns>
        /// <exception cref="ClientMetadataException">The key set is refused.</exception>
        public static JsonWebKeySet ParseKeySet(byte[] body)
        {
            ArgumentNullException.ThrowIfNull(body);

            using (JsonDocument json = Parse(body, ClientMetadataRejectionReasons.KeySet))
            {
                if (!json.RootElement.TryGetProperty("keys", out JsonElement keys)
                    || keys.ValueKind is not JsonValueKind.Array
                    || keys.GetArrayLength() == 0)
                {
                    throw Refuse(ClientMetadataRejectionReasons.KeySet, "The key set has no keys array.");
                }

                // Each key is checked on the raw JSON before the token library reads it: a
                // member it drops or tolerates could otherwise slip past.
                foreach (JsonElement key in keys.EnumerateArray())
                {
                    CheckKey(key);
                }
            }

            JsonWebKeySet set;
            try
            {
                set = JsonWebKeySet.Create(System.Text.Encoding.UTF8.GetString(body));
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or FormatException)
            {
                throw Refuse(ClientMetadataRejectionReasons.KeySet, "The key set cannot be read.", exception);
            }

            return set.Keys.Count == 0
                ? throw Refuse(ClientMetadataRejectionReasons.KeySet, "The key set holds no usable key.")
                : set;
        }

        /// <summary>Parses the body as one JSON object with no repeated member.</summary>
        private static JsonDocument Parse(byte[] body, string reason)
        {
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(body, ParseOptions);
            }
            catch (JsonException exception)
            {
                throw Refuse(reason, "The body is not valid JSON.", exception);
            }

            if (json.RootElement.ValueKind is not JsonValueKind.Object || HasDuplicateMember(json.RootElement))
            {
                json.Dispose();
                throw Refuse(reason, "The body is not a JSON object with distinct members.");
            }

            return json;
        }

        /// <summary>
        ///     Whether an object, or any object inside it, repeats a member name. Parsers disagree
        ///     on which of two repeated members wins, so a document with one is ambiguous and is
        ///     refused rather than read one way here and another way by its author.
        /// </summary>
        private static bool HasDuplicateMember(JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Array)
            {
                return element.EnumerateArray().Any(HasDuplicateMember);
            }

            if (element.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateMember(property.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Validates <c>redirect_uris</c>: a non-empty array of acceptable absolute URIs.</summary>
        private static List<Uri> ParseRedirectUris(JsonElement root)
        {
            if (!root.TryGetProperty("redirect_uris", out JsonElement value)
                || value.ValueKind is not JsonValueKind.Array
                || value.GetArrayLength() == 0)
            {
                throw Refuse(ClientMetadataRejectionReasons.RedirectUris, "The document's redirect_uris is not a non-empty array.");
            }

            List<Uri> redirectUris = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonElement item in value.EnumerateArray())
            {
                string? raw = item.ValueKind is JsonValueKind.String ? item.GetString() : null;
                if (raw is null || FindRedirectProblem(raw) is not null)
                {
                    throw Refuse(ClientMetadataRejectionReasons.RedirectUris, $"The document lists an unacceptable redirect URI: {raw ?? "(not a string)"}.");
                }

                if (seen.Add(raw))
                {
                    redirectUris.Add(new Uri(raw, UriKind.Absolute));
                }
            }

            return redirectUris;
        }

        /// <summary>
        ///     What is wrong with a documented redirect URI, or null when it is acceptable: it must
        ///     be absolute, free of whitespace and control characters, written in ASCII (it is
        ///     carried in a response header), without a fragment, user information or an
        ///     <c>iss</c> query parameter (which the engine adds itself), not in a scheme that runs
        ///     script or reaches files, and plain <c>http</c> only on loopback.
        /// </summary>
        /// <param name="raw">The redirect URI as written.</param>
        /// <returns>A short description of the problem, or null.</returns>
        public static string? FindRedirectProblem(string raw)
        {
            ArgumentNullException.ThrowIfNull(raw);

            // The first problem found, in the order the doc comment lists them.
            return raw.Length == 0 || raw.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c)) ? "whitespace"
                : !raw.All(char.IsAscii) ? "non_ascii"
                : raw.Contains('#', StringComparison.Ordinal) ? "fragment"
                : !Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) ? "relative"
                : RefusedRedirectSchemes.Contains(uri.Scheme) ? "scheme"
                : !string.IsNullOrEmpty(uri.UserInfo) ? "userinfo"
                : string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !DocumentClientRedirects.IsLoopback(uri) ? "http"
                : HasIssParameter(uri.Query) ? "iss"
                : null;
        }

        /// <summary>Validates <c>token_endpoint_auth_method</c>: required, <c>none</c> or <c>private_key_jwt</c>.</summary>
        private static string ParseAuthMethod(JsonElement root)
        {
            // An absent method defaults to client_secret_basic (RFC 7591 §2), a secret method no
            // document client can hold, so it is refused like any other.
            return !TryGetString(root, "token_endpoint_auth_method", out string? method)
                || method is not (AuthMethodNone or AuthMethodPrivateKeyJwt)
                    ? throw Refuse(ClientMetadataRejectionReasons.AuthMethod, "The document's token_endpoint_auth_method is not none or private_key_jwt.")
                    : method;
        }

        /// <summary>Validates a <c>private_key_jwt</c> document's <c>jwks_uri</c>: required, and on the document's origin.</summary>
        private static Uri ParseJwksUri(JsonElement root, Uri clientId)
        {
            return !TryGetString(root, "jwks_uri", out string? raw)
                || raw.Contains('#', StringComparison.Ordinal)
                || !Uri.TryCreate(raw, UriKind.Absolute, out Uri? jwksUri)
                || !string.Equals(jwksUri.Scheme, clientId.Scheme, StringComparison.Ordinal)
                || !string.Equals(jwksUri.Host, clientId.Host, StringComparison.Ordinal)
                || jwksUri.Port != clientId.Port
                || !string.IsNullOrEmpty(jwksUri.UserInfo)
                    ? throw Refuse(ClientMetadataRejectionReasons.JwksUri, "The document's jwks_uri is not a URL on the document's origin.")
                    : jwksUri;
        }

        /// <summary>Validates <c>grant_types</c>, when present: only <c>authorization_code</c> and <c>refresh_token</c>, with the former.</summary>
        private static void ParseGrantTypes(JsonElement root)
        {
            if (!root.TryGetProperty("grant_types", out JsonElement value))
            {
                return;
            }

            bool valid = value.ValueKind is JsonValueKind.Array && value.GetArrayLength() > 0;
            bool hasCode = false;
            if (valid)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    string? grant = item.ValueKind is JsonValueKind.String ? item.GetString() : null;
                    if (grant is null || !AllowedGrantTypes.Contains(grant))
                    {
                        valid = false;
                        break;
                    }

                    hasCode |= grant is "authorization_code";
                }
            }

            // The row is granted the code flow; a document that does not declare it describes a
            // client the row would not be.
            if (!valid || !hasCode)
            {
                throw Refuse(ClientMetadataRejectionReasons.GrantTypes, "The document's grant_types holds a grant other than authorization_code and refresh_token, or lacks authorization_code.");
            }
        }

        /// <summary>
        ///     Validates <c>client_name</c>, when present: a string of 1 to 100 characters, not only
        ///     whitespace, with no control or format characters, since the consent page shows it.
        /// </summary>
        private static string? ParseClientName(JsonElement root)
        {
            if (!root.TryGetProperty("client_name", out JsonElement value))
            {
                return null;
            }

            string? name = value.ValueKind is JsonValueKind.String ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(name)
                || name.Length > MaxClientNameLength
                || name.Any(static c => CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format)
                    ? throw Refuse(ClientMetadataRejectionReasons.ClientName, "The document's client_name is not a short, printable string.")
                    : name;
        }

        /// <summary>
        ///     Checks one key: a public RSA key of at least 2048 bits or a public EC key on an
        ///     accepted curve, usable for signatures, holding no private member.
        /// </summary>
        private static void CheckKey(JsonElement key)
        {
            if (key.ValueKind is not JsonValueKind.Object)
            {
                throw Refuse(ClientMetadataRejectionReasons.KeySet, "A key is not a JSON object.");
            }

            // Private material in a published key set means the key is compromised.
            foreach (string member in PrivateKeyMembers)
            {
                if (key.TryGetProperty(member, out _))
                {
                    throw Refuse(ClientMetadataRejectionReasons.KeySet, "A key holds private key material.");
                }
            }

            if (key.TryGetProperty("use", out JsonElement use)
                && (use.ValueKind is not JsonValueKind.String || use.GetString() is not "sig"))
            {
                throw Refuse(ClientMetadataRejectionReasons.KeySet, "A key is not for signatures.");
            }

            TryGetString(key, "kty", out string? keyType);
            switch (keyType)
            {
                case "RSA":
                    if (!TryGetString(key, "n", out string? modulus)
                        || !TryGetString(key, "e", out _)
                        || ModulusBits(modulus) < MinRsaModulusBits)
                    {
                        throw Refuse(ClientMetadataRejectionReasons.KeySet, "An RSA key is incomplete or shorter than 2048 bits.");
                    }

                    break;

                case "EC":
                    if (!TryGetString(key, "crv", out string? curve)
                        || !AllowedCurves.Contains(curve)
                        || !TryGetString(key, "x", out _)
                        || !TryGetString(key, "y", out _))
                    {
                        throw Refuse(ClientMetadataRejectionReasons.KeySet, "An EC key is incomplete or on an unaccepted curve.");
                    }

                    break;

                default:
                    throw Refuse(ClientMetadataRejectionReasons.KeySet, "A key is neither RSA nor EC.");
            }
        }

        /// <summary>The bit length of a base64url-encoded RSA modulus, or zero when it is not one.</summary>
        private static int ModulusBits(string modulus)
        {
            byte[] bytes;
            try
            {
                bytes = Base64UrlEncoder.DecodeBytes(modulus);
            }
            catch (FormatException)
            {
                return 0;
            }

            // Leading zero bytes carry no bits.
            int start = 0;
            while (start < bytes.Length && bytes[start] == 0)
            {
                start++;
            }

            if (start == bytes.Length)
            {
                return 0;
            }

            int leading = 32 - System.Numerics.BitOperations.LeadingZeroCount(bytes[start]);
            return ((bytes.Length - start - 1) * 8) + leading;
        }

        /// <summary>
        ///     Whether a query string carries an <c>iss</c> parameter. Pairs are separated by
        ///     <c>&amp;</c> or <c>;</c>, as the protocol engine itself splits them when it checks
        ///     the same rule on the stored registration.
        /// </summary>
        private static bool HasIssParameter(string query)
        {
            foreach (string pair in query.TrimStart('?').Split(['&', ';']))
            {
                string name = pair.Split('=', 2)[0];
                if (string.Equals(Uri.UnescapeDataString(name), "iss", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Reads a string member.</summary>
        private static bool TryGetString(JsonElement element, string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            value = element.TryGetProperty(name, out JsonElement member) && member.ValueKind is JsonValueKind.String
                ? member.GetString()
                : null;
            return value is not null;
        }

        /// <summary>A refusal.</summary>
        private static ClientMetadataException Refuse(string reason, string message, Exception? innerException = null)
        {
            return new ClientMetadataException(reason, message, innerException);
        }
    }
}
