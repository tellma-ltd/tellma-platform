// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.Testing.Diagnostics;

namespace Tellma.Core.Testing.Tests.Diagnostics
{
    /// <summary>
    ///     Covers the diagnostics the live suites lean on when a nightly failure is all anyone has.
    /// </summary>
    public class TestOutputDiagnosticsTests
    {
        [Fact]
        public void Routes_log_records_into_the_test_output()
        {
            ServiceCollection services = new();
            services.AddLogging(static builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddTestOutput();
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            ILogger<TestOutputDiagnosticsTests> logger =
                provider.GetRequiredService<ILogger<TestOutputDiagnosticsTests>>();

            // The sink is the ambient output helper rather than anything this test can read back, so
            // what is asserted is that a Debug record survives composition — the failure this guards
            // against is a provider that is registered but never consulted, which would silently
            // return the live suites to bare assertions. The record goes through ILogger.Log rather
            // than the LogDebug extension, which the analyzers reserve for source-generated messages.
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            logger.Log(
                LogLevel.Debug,
                default,
                "A live suite would put a provider's refusal here.",
                exception: null,
                static (state, _) => state);
        }

        [Fact]
        public void Composing_the_provider_twice_does_not_double_the_output()
        {
            ServiceCollection services = new();
            services.AddLogging(static builder => builder.AddTestOutput().AddTestOutput());

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Single(provider.GetServices<ILoggerProvider>().OfType<TestOutputLoggerProvider>());
        }

        [Theory]
        [InlineData("no-reply@tellma.com", "***@tellma.com")]
        [InlineData("a.b+tag@sub.tellma.com", "***@sub.tellma.com")]
        [InlineData("", "(not set)")]
        [InlineData(null, "(not set)")]
        [InlineData("not-an-address", "(no domain part)")]
        public void Masks_a_mailbox_to_its_domain(string? address, string expected)
        {
            Assert.Equal(expected, LiveTestEnvironment.MaskMailbox(address));
        }

        [Theory]
        [InlineData("org_1234567890abcdef", 8, "org_1234… (20 characters)")]
        [InlineData("MBP-LEUYV0IANC", 4, "MBP-… (14 characters)")]
        [InlineData("short", 8, "short (5 characters)")]
        [InlineData("", 8, "(not set)")]
        [InlineData(null, 8, "(not set)")]
        public void Masks_an_identifier_to_a_prefix_and_a_length(string? value, int prefixLength, string expected)
        {
            Assert.Equal(expected, LiveTestEnvironment.MaskIdentifier(value, prefixLength));
        }

        [Fact]
        public void Keeps_the_tail_of_an_identifier_out_of_the_log()
        {
            const string identifier = "org_prefix_and_a_distinctive_tail";

            string description = LiveTestEnvironment.MaskIdentifier(identifier);

            // The prefix is what says which environment a run was pointed at; the tail is the part
            // that would make the value usable, and a public repository publishes these logs.
            Assert.StartsWith("org_pref", description, StringComparison.Ordinal);
            Assert.DoesNotContain("distinctive_tail", description, StringComparison.Ordinal);
        }

        [Fact]
        public void Describes_a_secret_without_disclosing_it()
        {
            const string secret = "SG.a-live-looking-key";

            string description = LiveTestEnvironment.DescribeSecret(secret);

            // The whole point of the helper: a build log a public repository publishes learns the
            // shape of the credential and nothing else.
            Assert.DoesNotContain(secret, description, StringComparison.Ordinal);
            Assert.Contains(secret.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), description, StringComparison.Ordinal);
            Assert.Equal("(not set)", LiveTestEnvironment.DescribeSecret(null));
        }
    }
}
