// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using System.Text;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     Runs the axe rule set over a rendered page, mirroring the client workspace's battery so
    ///     both halves of the platform are held to one standard.
    ///     <para>
    ///         The content-security policy is deliberately left in force. axe is injected over the
    ///         debugging protocol rather than as a script element, so the policy never engages —
    ///         and turning it off would change what is being measured: inline style attributes the
    ///         policy strips in production would apply, and axe would report on colors, sizes and
    ///         visibility no user ever sees. This project has already been bitten by exactly that,
    ///         which is why the policy's own refusals are collected and asserted alongside.
    ///     </para>
    /// </summary>
    public static class AxeAssertions
    {
        /// <summary>The conformance tags scanned for, matching the client suite.</summary>
        private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

        /// <summary>Collects the policy's refusals; installed before any document script runs.</summary>
        private const string ViolationCollector = """
            () => {
                window.__cspViolations = [];
                document.addEventListener('securitypolicyviolation',
                    e => window.__cspViolations.push(e.violatedDirective + ' :: ' + e.blockedURI));
            }
            """;

        /// <summary>Arms a page to record what the content-security policy refuses.</summary>
        /// <param name="page">The page, before it navigates anywhere.</param>
        /// <returns>A task that completes once the collector is installed.</returns>
        public static async Task CollectPolicyViolationsAsync(IPage page)
        {
            ArgumentNullException.ThrowIfNull(page);
            await page.AddInitScriptAsync(ViolationCollector);
        }

        /// <summary>Asserts the current page has no accessibility violations and broke no policy.</summary>
        /// <param name="page">The page to scan.</param>
        /// <param name="label">What is being scanned, for the failure message.</param>
        /// <returns>A task that completes when the scan passes.</returns>
        public static async Task AssertNoViolationsAsync(IPage page, string label)
        {
            ArgumentNullException.ThrowIfNull(page);

            AxeResult result = await page.RunAxe(new AxeRunOptions
            {
                RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] },
            });

            if (result.Violations.Length > 0)
            {
                StringBuilder report = new();
                report.Append(label).Append(" has ").Append(result.Violations.Length)
                    .AppendLine(" accessibility violation(s):");
                foreach (AxeResultItem violation in result.Violations)
                {
                    report.Append("  [").Append(violation.Impact).Append("] ").Append(violation.Id)
                        .Append(" — ").AppendLine(violation.Help);
                    foreach (AxeResultNode node in violation.Nodes)
                    {
                        report.Append("      ").AppendLine(string.Join(", ", node.Target));
                    }
                }

                Assert.Fail(report.ToString());
            }

            string[] refused = await page.EvaluateAsync<string[]>("() => window.__cspViolations ?? []");
            Assert.True(
                refused.Length == 0,
                $"{label} was rendered with content the security policy refused: {string.Join(" | ", refused)}");
        }

        /// <summary>
        ///     Asserts the page reflows without a second scrolling axis. There is no axe rule for
        ///     this, and a self-contained scroller inside the page — the account nav strip — is
        ///     conformant, so only the document itself is measured.
        /// </summary>
        /// <param name="page">The page to measure, already at the narrow viewport.</param>
        /// <param name="label">What is being measured, for the failure message.</param>
        /// <returns>A task that completes when the measurement passes.</returns>
        public static async Task AssertNoSidewaysScrollAsync(IPage page, string label)
        {
            ArgumentNullException.ThrowIfNull(page);

            int overflow = await page.EvaluateAsync<int>(
                "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            Assert.True(overflow <= 0, $"{label} scrolls sideways by {overflow}px at this width.");
        }
    }
}
