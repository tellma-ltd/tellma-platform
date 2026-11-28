// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Keys;

namespace Tellma.Identity.Tests.Services.Keys
{
    /// <summary>
    ///     The development certificates: generated once into an empty folder and reused after, with
    ///     hosts that start together all loading the one certificate rather than meeting a
    ///     replacement in flight.
    /// </summary>
    public sealed class DevelopmentCertificateSourceTests : IDisposable
    {
        /// <summary>A folder of the test's own, so the developer's own keys are never touched.</summary>
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tellma-dev-keys-tests", Guid.NewGuid().ToString("N"));

        /// <inheritdoc />
        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void A_certificate_generated_into_an_empty_folder_is_reused_by_the_next_load()
        {
            DevelopmentCertificateSource source = new(_directory);

            using X509Certificate2 first = Assert.Single(source.Load(new TellmaIdentityCertificateOptions(), CertificateUse.Signing));
            using X509Certificate2 second = Assert.Single(source.Load(new TellmaIdentityCertificateOptions(), CertificateUse.Signing));

            Assert.Equal(first.Thumbprint, second.Thumbprint);
            Assert.True(File.Exists(Path.Combine(_directory, "dev-signing.pfx")));
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }

        [Fact]
        public async Task Loads_that_start_together_on_an_empty_folder_all_read_the_one_certificate()
        {
            DevelopmentCertificateSource source = new(_directory);
            using Barrier start = new(16);

            // Sixteen hosts start at once, each on a thread of its own: one generates, and every other
            // reads what it wrote.
            X509Certificate2[] loaded = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    return Assert.Single(source.Load(new TellmaIdentityCertificateOptions(), CertificateUse.Encryption));
                },
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)));

            try
            {
                Assert.Single(loaded.Select(static certificate => certificate.Thumbprint).Distinct(StringComparer.Ordinal));
                Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
            }
            finally
            {
                foreach (X509Certificate2 certificate in loaded)
                {
                    certificate.Dispose();
                }
            }
        }
    }
}
