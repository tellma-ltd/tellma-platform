// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Tellma.Core.Abstractions.Localization;
using Tellma.Distro.Acme.Development;
using Tellma.Distro.Acme.Entities;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;
using Tellma.Module.Gl;

namespace Tellma.Distro.Acme.Web.Tests.Development
{
    /// <summary>
    ///     The development data's shape, with no database: the center tree, the customers and
    ///     invoices, the name slots the step writes, and the development identity server's seeded
    ///     users matching the principals the step binds.
    /// </summary>
    public sealed class AcmeSampleDataTests
    {
        /// <summary>The center tree: forty centers on four levels, its lists naming centers of the tree and its rules held.</summary>
        [Fact]
        public void The_center_tree_holds_forty_centers_on_four_levels_and_its_lists_name_them()
        {
            IReadOnlyList<SampleCenter> centers = new AcmeSampleCenters().Centers;
            var byCode = centers.ToDictionary(static center => center.Code, StringComparer.Ordinal);

            Assert.Equal(AcmeSampleCenters.Count, centers.Count);
            Assert.Equal(4, centers.Max(center => Depth(center, byCode)));
            Assert.Single(centers, static center => center.ParentCode is null);

            // --- Only an abstract center or a business unit has children, and every center is named in both languages ---
            Assert.All(centers.Where(static center => center.ParentCode is not null), center =>
                Assert.Contains(byCode[center.ParentCode!].CenterType, (CenterType[])[CenterType.Abstract, CenterType.BusinessUnit]));
            Assert.All(centers, static center => Assert.Equal(["en", "ar"], center.Names.Select(static name => name.Language)));

            // --- The lists: four centers to deactivate, every child of a deactivated parent among them, and active sales centers to invoice ---
            Assert.Equal(4, AcmeSampleCenters.Inactive.Count);
            Assert.All(AcmeSampleCenters.Inactive, code => Assert.True(byCode.ContainsKey(code), code));
            Assert.All(
                centers.Where(center => center.ParentCode is string parent && AcmeSampleCenters.Inactive.Contains(parent)),
                static center => Assert.Contains(center.Code, AcmeSampleCenters.Inactive));
            Assert.All(AcmeSampleCenters.InvoicedSales, code =>
            {
                Assert.Equal(CenterType.Sale, byCode[code].CenterType);
                Assert.DoesNotContain(code, AcmeSampleCenters.Inactive);
            });
        }

        /// <summary>The customers: sixty of all three types in both currencies, ten with pictures, four deactivated and none of them invoiced.</summary>
        [Fact]
        public void The_customers_span_every_type_both_currencies_and_some_pictures_and_the_invoiced_stay_active()
        {
            IReadOnlyList<SampleCustomer> customers = AcmeSampleData.Customers;

            Assert.Equal(AcmeSampleData.CustomerCount, customers.Count);
            Assert.Equal(customers.Count, customers.Select(static customer => customer.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(customers.Count, customers.Select(static customer => customer.English).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(customers.Count, customers.Select(static customer => customer.Arabic).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(Enum.GetValues<CustomerType>().Order(), customers.Select(static customer => customer.Type).Distinct().Order());
            Assert.Equal(
                AcmeSampleData.Currencies.Select(static currency => currency.Code).Order(StringComparer.Ordinal),
                customers.Select(static customer => customer.CurrencyCode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            Assert.Equal(10, customers.Count(static customer => customer.ImageNumber is not null));
            Assert.Contains(customers, static customer => customer.CreditLimit == 0);
            Assert.Contains(customers, static customer => customer.Contacts.Count == 2);
            Assert.All(customers, static customer => Assert.True(customer.CreditLimit >= 0, customer.Code));

            // --- The deactivated customers are customers, and no invoice is theirs ---
            Assert.Equal(4, AcmeSampleData.DeactivatedCustomers.Count);
            Assert.All(AcmeSampleData.DeactivatedCustomers, code => Assert.Contains(customers, customer => customer.Code == code));
            Assert.All(AcmeSampleData.Invoices, static invoice => Assert.DoesNotContain(invoice.CustomerCode, AcmeSampleData.DeactivatedCustomers));
        }

        /// <summary>The invoices: twenty, numbered from one, each customer of them with several, the newest dated today, lines over the invoiced centers.</summary>
        [Fact]
        public void The_invoices_number_twenty_share_customers_and_analyse_their_lines_by_the_invoiced_centers()
        {
            IReadOnlyList<SampleInvoice> invoices = AcmeSampleData.Invoices;

            Assert.Equal(Enumerable.Range(1, AcmeSampleData.InvoiceCount), invoices.Select(static invoice => invoice.SerialNumber));
            Assert.All(invoices.GroupBy(static invoice => invoice.CustomerCode), static group => Assert.True(group.Count() >= 2, group.Key));
            Assert.Equal(0, invoices[^1].DaysAgo);
            Assert.All(invoices.Zip(invoices.Skip(1)), static pair => Assert.True(pair.First.DaysAgo > pair.Second.DaysAgo));
            Assert.All(invoices, static invoice => Assert.InRange(invoice.Lines.Count, 1, 3));
            Assert.All(invoices.SelectMany(static invoice => invoice.Lines), static line =>
            {
                Assert.Contains(line.CenterCode, AcmeSampleCenters.InvoicedSales);
                Assert.True(line.Amount > 0);
            });
            Assert.All(AcmeSampleData.ClosedInvoices, static serial => Assert.InRange(serial, 1, AcmeSampleData.InvoiceCount));
        }

        /// <summary>The name slots follow the tenant's languages by primary subtag, the first falling back to English.</summary>
        /// <param name="languages">The tenant's language codes, in position order.</param>
        /// <param name="name">The expected primary name.</param>
        /// <param name="name2">The expected secondary name.</param>
        /// <param name="name3">The expected tertiary name.</param>
        [Theory]
        [InlineData("en,ar", "Sales", "المبيعات", null)]
        [InlineData("ar-SA,en-GB", "المبيعات", "Sales", null)]
        [InlineData("en", "Sales", null, null)]
        [InlineData("fr,ar,en", "Sales", "المبيعات", "Sales")]
        [InlineData("am,fr", "Sales", null, null)]
        public void The_name_slots_follow_the_tenant_s_languages_by_primary_subtag(string languages, string name, string? name2, string? name3)
        {
            LanguageInfo[] tenant = [.. languages.Split(',').Select(static code => new LanguageInfo(code, code, code, IsRightToLeft: false))];

            Assert.Equal((name, name2, name3), AcmeSampleDataStep.Names("Sales", "المبيعات", tenant));
        }

        /// <summary>The key filter names one parameter per key, in lists of at most 128 keys, disjoined.</summary>
        [Fact]
        public void The_key_filter_names_one_parameter_per_key_in_lists_of_at_most_128()
        {
            Assert.Equal("(Code in (@k0, @k1))", AcmeSampleDataStep.KeyFilter("Code", 2));

            string filter = AcmeSampleDataStep.KeyFilter("Code", 129);
            Assert.StartsWith("(Code in (@k0, ", filter, StringComparison.Ordinal);
            Assert.EndsWith(", @k127) or Code in (@k128))", filter, StringComparison.Ordinal);
        }

        /// <summary>The development identity server seeds each end-to-end individual at the address and subject the sample data binds.</summary>
        [Fact]
        public void The_development_identity_server_seeds_every_principal_the_sample_data_binds()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AcmePaths.Web, "appsettings.Development.json"), optional: false)
                .Build();

            IEnumerable<(string?, string?)> seeded = configuration.GetSection("TellmaIdentity:Seed:DevUsers").GetChildren()
                .Select(static user => (user["Email"], user["Subject"]));

            Assert.Equal(AcmeDevelopmentPrincipals.Individuals.Select(static principal => ((string?)principal.Email, (string?)principal.Subject)), seeded);
            Assert.Equal("true", configuration["TellmaIdentity:Seed:DevAdmin:Enabled"], ignoreCase: true);
        }

        /// <summary>The depth of a center: one for the root.</summary>
        /// <param name="center">The center.</param>
        /// <param name="byCode">The tree by code.</param>
        /// <returns>The depth.</returns>
        private static int Depth(SampleCenter center, Dictionary<string, SampleCenter> byCode)
        {
            return center.ParentCode is string parent ? 1 + Depth(byCode[parent], byCode) : 1;
        }
    }
}
