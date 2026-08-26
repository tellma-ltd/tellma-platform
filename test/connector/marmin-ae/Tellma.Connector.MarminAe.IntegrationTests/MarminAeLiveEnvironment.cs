// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Testing.Diagnostics;

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>What the live suite is pointed at, and whether it can run at all.</summary>
    /// <remarks>
    ///     <para>
    ///         One gate for every class in the suite. The credentials come from the environment, so
    ///         a developer without them, and every pull-request job, skips before a test body runs
    ///         rather than failing on a connection.
    ///     </para>
    ///     <para>
    ///         One client and one token provider serve the whole suite, built on first use rather
    ///         than by a fixture — a fixture is constructed even when every test in its class skips,
    ///         which is exactly what attribute-level gating buys. Sharing them is also the honest
    ///         shape: the vendor rate-limits the token endpoint far harder than the document ones,
    ///         and a real host holds one provider per organization for the same reason.
    ///     </para>
    /// </remarks>
    public static class MarminAeLiveEnvironment
    {
        /// <summary>The variable carrying the sandbox client id.</summary>
        public const string ClientIdVariable = "TELLMA_MARMINAE_TEST_CLIENTID";

        /// <summary>The variable carrying the matching client secret.</summary>
        public const string ClientSecretVariable = "TELLMA_MARMINAE_TEST_CLIENTSECRET";

        /// <summary>The variable naming the business profile documents are issued under.</summary>
        public const string ProfileIdVariable = "TELLMA_MARMINAE_TEST_PROFILEID";

        /// <summary>The variable overriding the host, for a deployment other than the sandbox.</summary>
        public const string BaseAddressVariable = "TELLMA_MARMINAE_TEST_BASEADDRESS";

        /// <summary>Why a run without credentials skipped.</summary>
        public const string SkipReason =
            "Set TELLMA_MARMINAE_TEST_CLIENTID, TELLMA_MARMINAE_TEST_CLIENTSECRET and "
            + "TELLMA_MARMINAE_TEST_PROFILEID to run the live Marmin UAE suite.";

        private static readonly Lazy<SharedClient> Shared = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        ///     The marker every document this suite creates carries, so an operator sweeping the
        ///     sandbox can find them and a stray one can be traced to the run that made it.
        /// </summary>
        /// <remarks>
        ///     The vendor offers no delete, so documents accumulate; the literal prefix cannot occur
        ///     in a real document, the run identifier ties one back to an Actions run, and the random
        ///     tail keeps two runs distinguishable and gives the listing case a unique key.
        /// </remarks>
        public static string Marker { get; } = string.Create(
            CultureInfo.InvariantCulture,
            $"TELLMA-LIVE-{Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local"}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");

        /// <summary>Whether the environment carries the credentials this suite needs.</summary>
        public static bool HasCredentials { get; } =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClientIdVariable))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClientSecretVariable))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProfileIdVariable));

        /// <summary>The business profile documents are issued under.</summary>
        public static string ProfileId =>
            Environment.GetEnvironmentVariable(ProfileIdVariable)
            ?? throw new InvalidOperationException($"{ProfileIdVariable} is not set.");

        /// <summary>Opens one test's view of the shared client and its transcript.</summary>
        /// <returns>The session, which the test disposes to publish what it recorded.</returns>
        public static MarminAeLiveSession CreateSession()
        {
            SharedClient shared = Shared.Value;

            return new MarminAeLiveSession(shared.Client, shared.Transcript);
        }

        /// <summary>Writes what the run was pointed at, in a form safe for a public build log.</summary>
        public static void Report()
        {
            LiveTestEnvironment.Report(
                ("Base address", ResolveBaseAddress().AbsoluteUri),
                ("Client id", LiveTestEnvironment.MaskIdentifier(
                    Environment.GetEnvironmentVariable(ClientIdVariable))),
                ("Client secret", LiveTestEnvironment.DescribeSecret(
                    Environment.GetEnvironmentVariable(ClientSecretVariable))),
                ("Profile id", LiveTestEnvironment.MaskIdentifier(
                    Environment.GetEnvironmentVariable(ProfileIdVariable))),
                ("Api version", MarminAeClient.ApiVersion),
                ("Marker", Marker));
        }

        /// <summary>The values that name this account, wherever one of them might be echoed.</summary>
        /// <remarks>
        ///     Collected before any body is read, so a failure page that quotes the query string
        ///     back is scrubbed even though it carries no fields to walk.
        /// </remarks>
        /// <returns>The values.</returns>
        internal static string[] AccountValues()
        {
            MarminAeClientOptions options = Options();

            return [options.ClientId, options.ClientSecret, ProfileId];
        }

        /// <summary>The settings the shared client was built with.</summary>
        /// <returns>The settings.</returns>
        public static MarminAeClientOptions Options()
        {
            return new MarminAeClientOptions
            {
                BaseAddress = ResolveBaseAddress(),
                ClientId = Environment.GetEnvironmentVariable(ClientIdVariable)
                    ?? throw new InvalidOperationException($"{ClientIdVariable} is not set."),
                ClientSecret = Environment.GetEnvironmentVariable(ClientSecretVariable)
                    ?? throw new InvalidOperationException($"{ClientSecretVariable} is not set."),
                Timeout = TimeSpan.FromSeconds(60),
            };
        }

        private static SharedClient Build()
        {
            MarminAeClientOptions options = Options();
            MarminAeLiveTranscript transcript = new(new HttpClientHandler(), AccountValues());
            HttpClient httpClient = new(transcript, disposeHandler: true)
            {
                // The client owns its own timeout; leaving the transport's in place would race it.
                Timeout = Timeout.InfiniteTimeSpan,
            };

            return new SharedClient(new MarminAeClient(httpClient, options), transcript);
        }

        private static Uri ResolveBaseAddress()
        {
            string? configured = Environment.GetEnvironmentVariable(BaseAddressVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return MarminAeClientOptions.SandboxBaseAddress;
            }

            // Deliberately not part of the credential gate, and deliberately fatal when it is set
            // to nonsense: a typo here must not quietly turn the whole suite into a skip.
            return Uri.TryCreate(configured, UriKind.Absolute, out Uri? parsed)
                && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
                ? parsed
                : throw new InvalidOperationException(
                    $"{BaseAddressVariable} is set to '{configured}', which is not an absolute http or https URI.");
        }

        private sealed record SharedClient(MarminAeClient Client, MarminAeLiveTranscript Transcript);
    }

    /// <summary>One test's view of the shared client, and of what it recorded.</summary>
    /// <param name="Client">The client under test.</param>
    /// <param name="Transcript">The record of the exchanges.</param>
    public sealed record MarminAeLiveSession(MarminAeClient Client, MarminAeLiveTranscript Transcript) : IDisposable
    {
        /// <summary>Publishes what this test recorded, and clears it for the next one.</summary>
        /// <remarks>
        ///     The transport itself is shared and outlives the session, which is why nothing here
        ///     closes it.
        /// </remarks>
        public void Dispose()
        {
            Transcript.Publish();
        }
    }
}
