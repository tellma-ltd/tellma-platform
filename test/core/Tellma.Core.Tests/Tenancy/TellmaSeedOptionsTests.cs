// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The seed section's own validation: each seed tenant requests an id the registration accepts,
    ///     has a name, and a sandbox derives from a live tenant, so a bad seed fails startup rather
    ///     than half way through <c>migrate</c>.
    /// </summary>
    public class TellmaSeedOptionsTests
    {
        [Fact]
        public void A_live_tenant_and_its_sandbox_are_accepted()
        {
            TellmaSeedOptions options = Bind(new Dictionary<string, string?>
            {
                ["Tellma:Seed:AdminEmail"] = "admin@localhost",
                ["Tellma:Seed:Tenants:0:Id"] = "1",
                ["Tellma:Seed:Tenants:0:Name"] = "Acme",
                ["Tellma:Seed:Tenants:0:Languages:0"] = "en",
                ["Tellma:Seed:Tenants:0:Calendars:0"] = "gc",
                ["Tellma:Seed:Tenants:1:Id"] = "2",
                ["Tellma:Seed:Tenants:1:Name"] = "Acme Sandbox",
                ["Tellma:Seed:Tenants:1:Category"] = "Sandbox",
                ["Tellma:Seed:Tenants:1:LiveTenantId"] = "1",
            });

            Assert.Equal(2, options.Tenants.Count);
            Assert.Equal(["en"], options.Tenants[0].Languages);
            Assert.Equal(TenantCategory.Sandbox, options.Tenants[1].Category);
            Assert.Empty(Validate(options));
        }

        [Fact]
        public void No_seed_at_all_is_accepted()
        {
            Assert.Empty(Validate(new TellmaSeedOptions()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1000)]
        public void An_id_outside_the_requestable_range_is_refused(int id)
        {
            TellmaSeedOptions options = new();
            options.Tenants.Add(new TellmaSeedTenant { Id = id, Name = "Acme" });

            Assert.Contains("a seed tenant requests an id from 1 to 999", Assert.Single(Validate(options)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_tenant_seeded_twice_is_refused()
        {
            TellmaSeedOptions options = new();
            options.Tenants.Add(new TellmaSeedTenant { Id = 1, Name = "Acme" });
            options.Tenants.Add(new TellmaSeedTenant { Id = 1, Name = "Acme again" });

            Assert.Contains("Tellma:Seed:Tenants:1 Id 1 is seeded twice", Assert.Single(Validate(options)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_tenant_without_a_name_is_refused()
        {
            TellmaSeedOptions options = new();
            options.Tenants.Add(new TellmaSeedTenant { Id = 1, Name = " " });

            Assert.Contains("Tellma:Seed:Tenants:0 Name is missing", Assert.Single(Validate(options)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_sandbox_without_its_live_tenant_is_refused()
        {
            TellmaSeedOptions options = new();
            options.Tenants.Add(new TellmaSeedTenant { Id = 2, Name = "Sandbox", Category = TenantCategory.Sandbox });

            Assert.Contains("is a sandbox without the LiveTenantId", Assert.Single(Validate(options)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_live_tenant_naming_a_live_tenant_is_refused()
        {
            TellmaSeedOptions options = new();
            options.Tenants.Add(new TellmaSeedTenant { Id = 1, Name = "Acme", LiveTenantId = 3 });

            Assert.Contains("names a LiveTenantId but is not a sandbox", Assert.Single(Validate(options)), StringComparison.Ordinal);
        }

        [Fact]
        public void A_sandbox_deriving_from_itself_or_from_a_seeded_sandbox_is_refused()
        {
            TellmaSeedOptions options = new();
            options.Tenants.Add(new TellmaSeedTenant { Id = 2, Name = "Self", Category = TenantCategory.Sandbox, LiveTenantId = 2 });
            options.Tenants.Add(new TellmaSeedTenant { Id = 3, Name = "Nested", Category = TenantCategory.Sandbox, LiveTenantId = 2 });

            List<string> problems = Validate(options);
            Assert.Contains(problems, static problem => problem.Contains("names itself as its LiveTenantId", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Contains("the sandbox 3 derives from 2, which is not a live tenant", StringComparison.Ordinal));
        }

        [Fact]
        public void A_blank_language_or_calendar_is_refused()
        {
            TellmaSeedOptions options = new();
            TellmaSeedTenant tenant = new() { Id = 1, Name = "Acme" };
            tenant.Calendars.Add("");
            options.Tenants.Add(tenant);

            Assert.Contains("lists a blank language or calendar", Assert.Single(Validate(options)), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("admin@")]
        [InlineData("a@b@c")]
        [InlineData("ad min@localhost")]
        public void An_administrator_address_that_is_not_one_is_refused(string email)
        {
            Assert.Contains("is not an email address", Assert.Single(Validate(new TellmaSeedOptions { AdminEmail = email })), StringComparison.Ordinal);
        }

        /// <summary>Binds the options from configuration values, as the host binds them.</summary>
        /// <param name="values">The values.</param>
        /// <returns>The options.</returns>
        private static TellmaSeedOptions Bind(Dictionary<string, string?> values)
        {
            TellmaSeedOptions options = new();
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(TellmaSeedOptions.SectionPath).Bind(options);
            return options;
        }

        /// <summary>The options' validation messages.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The messages.</returns>
        private static List<string> Validate(TellmaSeedOptions options)
        {
            return [.. options.Validate(new ValidationContext(options)).Select(static result => result.ErrorMessage!)];
        }
    }
}
