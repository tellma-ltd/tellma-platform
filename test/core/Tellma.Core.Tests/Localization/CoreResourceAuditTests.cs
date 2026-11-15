// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Resources;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The resource audit over Core: every key of Core's shared and server-only resources is
    ///     derived by convention from the composed platform model, or spelled by a string literal of
    ///     the platform's source (the server's and the browser application's), or waits for the
    ///     feature that will use it; and every satellite key is one the neutral file declares.
    /// </summary>
    public sealed class CoreResourceAuditTests
    {
        /// <summary>
        ///     The keys Core ships ahead of the features that will look them up, each with what it waits
        ///     for. The audit fails once a key here is used, so the list shrinks as the features land.
        /// </summary>
        private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
        {
            // --- The stacks' built-in actions (spec 0015): no stack is composed yet ---
            ["Action_Deactivate"] = "The deactivate action of an activatable entity's stack.",

            // --- The permissions editor's reasons (spec 0032 §12.4) ---
            ["Access_Reason_self"] = "The permissions editor's label of a grant over the user's own records.",

            // --- Problem codes whose raise sites have not landed (spec 0015, spec 0016) ---
            ["Problem_concurrency_conflict"] = "The save pipeline raises it.",
            ["Problem_concurrency_conflict_Detail"] = "The save pipeline raises it.",
            ["Problem_method_not_allowed"] = "The HTTP surface raises it for a route's other methods.",
            ["Problem_unsupported_media"] = "The HTTP surface raises it for a body it cannot read.",
            ["Problem_unsupported_credential"] = "The HTTP surface raises it for a credential it does not accept.",

            // --- The settings save's validation codes (spec 0013 §6.5) ---
            ["Settings_CalendarNotRegistered"] = "The settings save refuses a calendar no feature composes.",
            ["Settings_CalendarsNotDistinct"] = "The settings save refuses a calendar pair of one calendar.",
            ["Settings_EntryTypeMismatch"] = "The settings save refuses a value of the wrong type.",
            ["Settings_FieldUnknown"] = "The settings save refuses a field mask naming no field.",
            ["Settings_KeyUndeclared"] = "The settings save refuses a key no feature declares.",
            ["Settings_LanguageNotOffered"] = "The settings save refuses a language the distribution does not offer.",
            ["Settings_LanguagesNotDistinct"] = "The settings save refuses a language twice.",
            ["Settings_RegionNotOffered"] = "The settings save refuses a region its language does not offer.",
            ["Settings_TertiaryRequiresSecondary"] = "The settings save refuses a tertiary language without a secondary one.",
            ["Settings_TimeZoneUnknown"] = "The settings save refuses a zone outside the time-zone set.",

            // --- Reports and dashboards (specs 0033, 0035, and the export job of spec 0019) ---
            ["ReportKind_List"] = "A report's kind.",
            ["ReportKind_Summary"] = "A report's kind.",
            ["ReportKind_Pivot"] = "A report's kind.",
            ["ReportKind_Chart"] = "A report's kind.",
            ["ShareAccess_View"] = "A share's access.",
            ["ShareAccess_Edit"] = "A share's access.",
            ["Core_Report_Synonyms"] = "The reports' search synonyms.",
            ["Core_Report_DefaultTitle"] = "A new report's title.",
            ["Core_Dashboard_Synonyms"] = "The dashboards' search synonyms.",
            ["Core_Dashboard_DefaultTitle"] = "A new dashboard's title.",
            ["Core_Job_ExportReport"] = "The report export job's label.",
            ["Notification_core_report_shared"] = "The notification of a shared report.",
            ["Notification_core_dashboard_shared"] = "The notification of a shared dashboard.",
        };

        /// <summary>
        ///     The trees under the scanned roots whose code never ships: the test support, the
        ///     component harnesses, the build tools' fixtures and the showcase. A literal there uses no key.
        /// </summary>
        private static readonly string[] NeverShips =
        [
            RepositoryRoot.Combine("src", "core", "Tellma.Core.Testing"),
            RepositoryRoot.Combine("client", "projects", "core", "tellma-core-testing"),
            RepositoryRoot.Combine("client", "projects", "core", "tellma-core-ui-testing"),
            RepositoryRoot.Combine("client", "projects", "core", "tellma-core-tools", "fixtures"),
            RepositoryRoot.Combine("client", "projects", "internal", "showcase"),
        ];

        [Fact]
        public async Task Every_core_resource_key_is_derived_from_the_model_spelled_in_the_source_or_pending()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

            IReadOnlyList<ResourceAuditFinding> findings = ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles =
                [
                    RepositoryRoot.Combine("src", "core", "Tellma.Core", "Resources", "Strings.resx"),
                    RepositoryRoot.Combine("src", "core", "Tellma.Core", "Resources", "ServerStrings.resx"),
                ],
                DerivedKeys = ConventionalStringKeys.Of(provider),
                Literals = SourceLiterals.Read([RepositoryRoot.Combine("src"), RepositoryRoot.Combine("client", "projects")], NeverShips),
                Pending = Pending,
            });

            Assert.True(findings.Count == 0, ResourceAudit.Report(findings));
        }
    }
}
