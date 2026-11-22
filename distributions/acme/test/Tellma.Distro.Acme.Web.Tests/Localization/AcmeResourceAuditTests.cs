// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Tellma.Core.Localization;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;
using Tellma.Testing.Resources;

namespace Tellma.Distro.Acme.Web.Tests.Localization
{
    /// <summary>
    ///     The resource audit over the distribution: every key of Acme's strings is derived by
    ///     convention from the web host's composition, or spelled by a string literal of the
    ///     distribution's source (its server projects and its client app), or waits for the client
    ///     page that will use it; and every Arabic key is one the English file declares.
    /// </summary>
    public sealed class AcmeResourceAuditTests
    {
        /// <summary>
        ///     The keys Acme ships ahead of the client pages that will look them up, each with what it
        ///     waits for. The audit fails once a key here is used, so the list empties as those pages land.
        /// </summary>
        private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
        {
            ["Acme_Settings_Numbering"] = "The heading of the invoicing settings tab's numbering section.",
            ["Acme_Settings_NextNumber"] = "The invoicing settings tab's preview of the next invoice number.",
        };

        /// <summary>The neutral strings file.</summary>
        private static string Neutral => Path.Combine(AcmePaths.Distribution, "src", "Tellma.Distro.Acme", "Resources", "Strings.resx");

        /// <summary>Every neutral key is derived, spelled or pending, and every Arabic key is declared.</summary>
        /// <returns>A task that completes when the audit has run.</returns>
        [Fact]
        public async Task Every_acme_resource_key_is_derived_from_the_model_spelled_in_the_source_or_pending()
        {
            await using WebApplication app = AcmeWebApplication.Build("Development");

            IReadOnlyList<ResourceAuditFinding> findings = ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles = [Neutral],
                DerivedKeys = ConventionalStringKeys.Of(app.Services),
                Literals = SourceLiterals.Read([Path.Combine(AcmePaths.Distribution, "src")]),
                Pending = Pending,
            });

            Assert.True(findings.Count == 0, ResourceAudit.Report(findings));
        }

        /// <summary>The strings ship in English and Arabic, key for key, under the library's base name.</summary>
        [Fact]
        public void The_strings_ship_in_english_and_arabic_under_the_library_s_base_name()
        {
            Assert.Equal(["ar"], ResxFile.Satellites(Neutral).Select(static satellite => satellite.Culture));
            Assert.Equal(ResxFile.Read(Neutral).Keys.Order(StringComparer.Ordinal), ResxFile.Read(ResxFile.Satellites(Neutral)[0].Path).Keys.Order(StringComparer.Ordinal));
            Assert.Contains("Tellma.Distro.Acme.Resources.Strings.resources", typeof(AcmeFeature).Assembly.GetManifestResourceNames());
        }

        /// <summary>Every entity, member, action, enum value and setting Acme declares has an English label of its own.</summary>
        /// <returns>A task that completes when the keys are compared.</returns>
        [Fact]
        public async Task Every_label_the_acme_model_derives_in_its_own_schema_is_shipped()
        {
            await using WebApplication app = AcmeWebApplication.Build("Development");
            IReadOnlyDictionary<string, string> shipped = ResxFile.Read(Neutral);

            // The audit admits a key nothing derives; this is its converse for Acme's own families.
            // The audit members, the create labels and the platform's own actions fall back to
            // Core's shared labels, and a reference's navigation to its foreign key's label.
            string[] shared =
            [
                "_CreatedAt", "_CreatedBy", "_CreatedById", "_ModifiedAt", "_ModifiedBy", "_ModifiedById", "_Id", "_New",
                "_Action_Read", "_Action_Save", "_Action_Delete", "_Action_Activate", "_Action_Deactivate",
            ];
            string[] missing =
            [
                .. ConventionalStringKeys.Of(app.Services)
                    .Where(static key => key.StartsWith("Acme_", StringComparison.Ordinal) || key.StartsWith("Setting_acme_", StringComparison.Ordinal))
                    .Where(key => !shared.Any(suffix => key.EndsWith(suffix, StringComparison.Ordinal)))
                    .Where(key => !shipped.ContainsKey(key) && !shipped.ContainsKey(key + "Id"))
                    .Order(StringComparer.Ordinal),
            ];

            Assert.True(missing.Length == 0, "Acme derives labels it does not ship: " + string.Join(", ", missing));
        }
    }
}
