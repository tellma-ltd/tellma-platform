// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Options
{
    /// <summary>
    ///     Startup validation of the development user seeds: in Development every entry needs an
    ///     email address and a subject, and neither may be another seeded identity's, the dev
    ///     admin's included; elsewhere they are never seeded, so never judged.
    /// </summary>
    public sealed class DevUserSeedValidationTests
    {
        private static readonly TellmaIdentityOptionsValidator Production = new(new FakeEnvironment(Environments.Production));
        private static readonly TellmaIdentityOptionsValidator Development = new(new FakeEnvironment(Environments.Development));

        [Fact]
        public void Complete_distinct_entries_are_accepted_beside_the_dev_admin()
        {
            TellmaIdentityOptions options = With(User("sales@localhost", "00000000-0000-0000-0000-000000000002"), User("centers@localhost", "00000000-0000-0000-0000-000000000003"));

            Assert.Equal(ValidateOptionsResult.Success, Development.Validate(null, options));
        }

        [Theory]
        [InlineData(null, "00000000-0000-0000-0000-000000000002", "Email is required")]
        [InlineData(" ", "00000000-0000-0000-0000-000000000002", "Email is required")]
        [InlineData("sales@localhost", null, "Subject is required")]
        [InlineData("sales@localhost", "", "Subject is required")]
        public void An_entry_without_an_email_or_a_subject_fails_naming_the_entry(string? email, string? subject, string failure)
        {
            ValidateOptionsResult result = Development.Validate(null, With(User(email, subject)));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, f => f.StartsWith("TellmaIdentity:Seed:DevUsers:0:", StringComparison.Ordinal) && f.Contains(failure, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("ADMIN@localhost", "00000000-0000-0000-0000-000000000002", "TellmaIdentity:Seed:DevAdmin")]
        [InlineData("sales@localhost", "00000000-0000-0000-0000-000000000001", "TellmaIdentity:Seed:DevAdmin")]
        public void An_entry_reusing_the_dev_admin_s_email_or_subject_fails(string email, string subject, string owner)
        {
            ValidateOptionsResult result = Development.Validate(null, With(User(email, subject)));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, f => f.StartsWith("TellmaIdentity:Seed:DevUsers:0:", StringComparison.Ordinal) && f.EndsWith($"already seeded by {owner}.", StringComparison.Ordinal));
        }

        [Fact]
        public void Two_entries_sharing_an_email_or_a_subject_fail_naming_the_later_one()
        {
            ValidateOptionsResult result = Development.Validate(null, With(
                User("sales@localhost", "00000000-0000-0000-0000-000000000002"),
                User("Sales@localhost", "00000000-0000-0000-0000-000000000003"),
                User("centers@localhost", "00000000-0000-0000-0000-000000000002")));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.StartsWith("TellmaIdentity:Seed:DevUsers:1: Email", StringComparison.Ordinal));
            Assert.Contains(result.Failures!, static f => f.StartsWith("TellmaIdentity:Seed:DevUsers:2: Subject", StringComparison.Ordinal));
        }

        [Fact]
        public void Entries_outside_development_are_not_judged()
        {
            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, With(User(null, null))));
        }

        /// <summary>Options that pass every other check, with the dev admin enabled and the given users.</summary>
        /// <param name="users">The development users.</param>
        /// <returns>The options.</returns>
        private static TellmaIdentityOptions With(params TellmaIdentityDevUserOptions[] users)
        {
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.example.com"),
                ConnectionString = "Server=.;Database=Id;Trusted_Connection=True",
            };
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Signing.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/etc/tellma/signing.pfx" });
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Encryption.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/etc/tellma/encryption.pfx" });
            options.Seed.DevAdmin.Enabled = true;
            foreach (TellmaIdentityDevUserOptions user in users)
            {
                options.Seed.DevUsers.Add(user);
            }

            return options;
        }

        /// <summary>A development user entry.</summary>
        /// <param name="email">The email address.</param>
        /// <param name="subject">The subject.</param>
        /// <returns>The entry.</returns>
        private static TellmaIdentityDevUserOptions User(string? email, string? subject)
        {
            return new TellmaIdentityDevUserOptions { Email = email, Subject = subject };
        }

        /// <summary>A host environment exposing only its name.</summary>
        /// <param name="environmentName">The environment name.</param>
        private sealed class FakeEnvironment(string environmentName) : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = environmentName;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Identity.Tests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = string.Empty;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
