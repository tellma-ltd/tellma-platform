// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Localization;
using Tellma.Testing.Resources;

namespace Tellma.Module.Gl.Tests.Localization
{
    /// <summary>
    ///     The resource audit over GL: every key of the module's strings is derived by convention from
    ///     a composition of the GL feature, or spelled by a string literal of the module's source (its
    ///     server packages and, once it exists, its browser package), or waits for the browser package
    ///     that will use it; and every Arabic key is one the English file declares.
    /// </summary>
    public sealed class GlResourceAuditTests
    {
        /// <summary>
        ///     The keys GL ships ahead of the browser package that will look them up, each with what it
        ///     waits for. The audit fails once a key here is used, so the list empties as that package lands.
        /// </summary>
        private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
        {
            ["Gl_Nav_Financials"] = "The launcher section the GL screens sit under.",
            ["Gl_Center_CodeHint"] = "The hint of the center form's Code field.",
        };

        [Fact]
        public async Task Every_gl_resource_key_is_derived_from_the_model_spelled_in_the_source_or_pending()
        {
            await using ServiceProvider provider = GlComposition.Build();

            IReadOnlyList<ResourceAuditFinding> findings = ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles = [RepositoryRoot.Combine("src", "module", "gl", "Tellma.Module.Gl", "Resources", "Strings.resx")],
                DerivedKeys = ConventionalStringKeys.Of(provider),
                Literals = SourceLiterals.Read(SourceRoots()),
                Pending = Pending,
            });

            Assert.True(findings.Count == 0, ResourceAudit.Report(findings));
        }

        [Fact]
        public void The_strings_ship_in_english_and_arabic_under_the_modules_base_name()
        {
            string neutral = RepositoryRoot.Combine("src", "module", "gl", "Tellma.Module.Gl", "Resources", "Strings.resx");

            Assert.Equal(["ar"], ResxFile.Satellites(neutral).Select(static satellite => satellite.Culture));
            Assert.Equal(ResxFile.Read(neutral).Keys.Order(StringComparer.Ordinal), ResxFile.Read(ResxFile.Satellites(neutral)[0].Path).Keys.Order(StringComparer.Ordinal));
            Assert.Contains("Tellma.Module.Gl.Resources.Strings.resources", typeof(GlFeature).Assembly.GetManifestResourceNames());
        }

        /// <summary>The module's source trees: its server packages, and its browser package once it exists.</summary>
        /// <returns>The roots that exist.</returns>
        private static string[] SourceRoots()
        {
            return [.. new[]
            {
                RepositoryRoot.Combine("src", "module", "gl"),
                RepositoryRoot.Combine("client", "projects", "module", "tellma-module-gl"),
            }.Where(Directory.Exists)];
        }
    }
}
