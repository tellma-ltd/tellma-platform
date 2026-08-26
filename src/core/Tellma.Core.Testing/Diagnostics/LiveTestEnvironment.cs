// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Xunit;

namespace Tellma.Core.Testing.Diagnostics
{
    /// <summary>
    ///     Writes what a live suite is pointed at into the test's output, so a nightly failure can be
    ///     read against the account, resource, and sender that produced it.
    /// </summary>
    /// <remarks>
    ///     A live suite fails for two kinds of reason — the code is wrong, or the environment is not
    ///     what the run assumed — and a bare assertion cannot tell them apart. The helpers here
    ///     deliberately disclose shapes rather than values: a CI log is readable by everyone who can
    ///     read the repository, which is the wrong audience for a mailbox or a key.
    /// </remarks>
    public static class LiveTestEnvironment
    {
        /// <summary>Writes a labelled block of settings to the running test's output.</summary>
        /// <param name="settings">The settings to report, already reduced to safe values by
        ///     <see cref="MaskMailbox" />, <see cref="DescribeSecret" />, or the caller's judgment.</param>
        public static void Report(params (string Label, string? Value)[] settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            // Absent outside a test — a suite that reports from a fixture constructor, say — in which
            // case there is nowhere to write and nothing worth failing over.
            ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
            if (output is null || settings.Length == 0)
            {
                return;
            }

            int width = settings.Max(static setting => setting.Label.Length);
            foreach ((string label, string? value) in settings)
            {
                output.WriteLine($"{(label + ':').PadRight(width + 2)}{value ?? "(not set)"}");
            }
        }

        /// <summary>Reduces a mailbox to its domain.</summary>
        /// <remarks>
        ///     The domain is the half that explains a refusal — it has to be one the provider is
        ///     authorized to send from — while the local part is the half worth keeping out of a log
        ///     that a public repository's Actions runs publish.
        /// </remarks>
        /// <param name="address">The address, which may be absent.</param>
        /// <returns>The masked address, or a description of why there is none.</returns>
        public static string MaskMailbox(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return "(not set)";
            }

            int at = address.LastIndexOf('@');

            return at < 0 ? "(no domain part)" : $"***{address[at..]}";
        }

        /// <summary>Reduces an identifier to a leading fragment and its length.</summary>
        /// <remarks>
        ///     For the values that are not quite secrets and not quite public — a client id, an
        ///     account or profile identifier. The prefix is usually the part that says which
        ///     environment a run was pointed at, and the length is what distinguishes a truncated
        ///     paste from a wrong value, while neither is enough to act on.
        /// </remarks>
        /// <param name="value">The identifier, which may be absent.</param>
        /// <param name="prefixLength">How many leading characters to keep.</param>
        /// <returns>A description safe to write to a build log.</returns>
        public static string MaskIdentifier(string? value, int prefixLength = 8)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);

            if (string.IsNullOrWhiteSpace(value))
            {
                return "(not set)";
            }

            string prefix = value.Length <= prefixLength ? value : value[..prefixLength] + "…";

            return string.Create(
                CultureInfo.InvariantCulture, $"{prefix} ({value.Length} characters)");
        }

        /// <summary>Describes a secret's presence and length, never its value.</summary>
        /// <remarks>
        ///     The length distinguishes the failure modes that matter — an unset variable, an empty
        ///     one, a truncated paste — without disclosing anything usable.
        /// </remarks>
        /// <param name="secret">The secret, which may be absent.</param>
        /// <returns>A description safe to write to a build log.</returns>
        public static string DescribeSecret(string? secret)
        {
            return string.IsNullOrWhiteSpace(secret)
                ? "(not set)"
                : string.Create(
                    CultureInfo.InvariantCulture, $"set, {secret.Length} characters");
        }
    }
}
