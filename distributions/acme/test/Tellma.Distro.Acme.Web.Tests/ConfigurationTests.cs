// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The distribution's tracked configuration holds no secret: no password, key, token or
    ///     client secret, in a configuration file or a launch profile, in any form a reader could use.
    /// </summary>
    public sealed partial class ConfigurationTests
    {
        [Fact]
        public void The_tracked_configuration_holds_no_secret()
        {
            string[] files = [.. ConfigurationFiles()];
            Assert.Contains(files, static file => file.EndsWith("appsettings.Development.json", StringComparison.Ordinal));

            List<string> secrets = [];
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(AcmePaths.Distribution, file);
                using var document = JsonDocument.Parse(
                    File.ReadAllText(file),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                Walk(document.RootElement, path: "", (path, value) =>
                {
                    if (Secret(path, value) is { } reason)
                    {
                        secrets.Add($"{relative}: {path} {reason}");
                    }
                });
            }

            Assert.Empty(secrets);
        }

        [Theory]
        [InlineData("Tellma:Identity:ClientSecret", "s3cr3t", true)]
        [InlineData("Tellma:Identity:ClientSecret", "", false)]
        [InlineData("Tellma:Catalog:ConnectionString", "Server=x;Database=y;User ID=sa;Password=hunter2", true)]
        [InlineData("Tellma:Catalog:ConnectionString", "Server=x;Database=y;Integrated Security=True", false)]
        [InlineData("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=00000000-0000-0000-0000-000000000000", true)]
        [InlineData("Email:SendGrid:ApiKey", "SG.abc", true)]
        [InlineData("Email:AcsEmail:Webhook:Tokens:0", "token-value", true)]
        [InlineData("Tellma:DataProtection:KeyId", "https://vault.vault.azure.net/keys/ring", false)]
        [InlineData("TellmaIdentity:Keys:Signing:Source", "DevelopmentSelfSigned", false)]
        public void The_scan_tells_a_secret_from_a_setting(string path, string value, bool isSecret)
        {
            Assert.Equal(isSecret, Secret(path, value) is not null);
        }

        /// <summary>The distribution's tracked configuration files: settings and launch profiles, outside build output.</summary>
        /// <returns>The files.</returns>
        private static IEnumerable<string> ConfigurationFiles()
        {
            return Directory.EnumerateFiles(Path.Combine(AcmePaths.Distribution, "src"), "*.json", SearchOption.AllDirectories)
                .Where(static file =>
                {
                    string[] segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !segments.Contains("bin") && !segments.Contains("obj") && !segments.Contains("node_modules");
                });
        }

        /// <summary>Calls back with every string value under an element, by its configuration path.</summary>
        /// <param name="element">The element.</param>
        /// <param name="path">Its path.</param>
        /// <param name="visit">The callback.</param>
        private static void Walk(JsonElement element, string path, Action<string, string> visit)
        {
            // Objects and arrays are walked into; strings are visited; numbers, booleans and nulls hold no secret.
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Walk(property.Value, path.Length == 0 ? property.Name : path + ":" + property.Name, visit);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, path + ":" + index++, visit);
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                visit(path, element.GetString()!);
            }
        }

        /// <summary>Why a setting is a secret, or null when it is not one.</summary>
        /// <param name="path">The setting's configuration path.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The reason, or null.</returns>
        private static string? Secret(string path, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // A key that names a secret, holding a value; KeyId names a key, it is not one.
            string leaf = path.Split(':').Last(static segment => !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _));
            if (SecretName().IsMatch(leaf))
            {
                return "names a secret and holds a value";
            }

            // A connection string or other value carrying a credential inline.
            return InlineCredential().IsMatch(value) ? "carries a credential inline" : null;
        }

        [GeneratedRegex("(secret|password|pwd|apikey|accesskey|accountkey|token|tokens|credential)$", RegexOptions.IgnoreCase)]
        private static partial Regex SecretName();

        [GeneratedRegex(@"(^|;)\s*(password|pwd|accountkey|sharedaccesskey|instrumentationkey|user\s*id|uid)\s*=", RegexOptions.IgnoreCase)]
        private static partial Regex InlineCredential();
    }
}
