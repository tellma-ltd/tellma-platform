// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>
    ///     Server-built links: a tenant's home, a record's details page when its resource names a
    ///     stack that serves details, the home for everything else, and the public origin they all
    ///     start at.
    /// </summary>
    public class TellmaLinksTests
    {
        private const string Origin = "https://acme.app.tellma.com";

        [Fact]
        public void A_tenant_link_is_the_public_origin_and_the_tenant_id()
        {
            TellmaLinks links = Links();

            Assert.Equal("https://acme.app.tellma.com/42", links.Tenant(42));
            Assert.Equal("https://acme.app.tellma.com/1", links.Tenant(1));
        }

        [Fact]
        public void A_record_of_a_stack_with_details_links_to_its_details_page()
        {
            TellmaLinks links = Links(("acme.SalesInvoice", new StackRoute("sales-invoices", StackOperations.All)));

            Assert.Equal("https://acme.app.tellma.com/42/sales-invoices/17", links.Target(42, "acme.SalesInvoice", 17));
        }

        [Theory]
        [InlineData(StackOperations.Details)]
        [InlineData(StackOperations.Read)]
        [InlineData(StackOperations.Details | StackOperations.Save)]
        public void Details_among_a_stacks_operations_is_all_a_details_link_needs(StackOperations operations)
        {
            TellmaLinks links = Links(("gl.Center", new StackRoute("centers", operations)));

            Assert.Equal("https://acme.app.tellma.com/42/centers/9000000000", links.Target(42, "gl.Center", 9_000_000_000));
        }

        [Theory]
        [InlineData(StackOperations.None)]
        [InlineData(StackOperations.Query)]
        [InlineData(StackOperations.Query | StackOperations.Export | StackOperations.Save | StackOperations.Delete)]
        [InlineData(StackOperations.Enlist)]
        public void A_record_of_a_stack_without_details_links_to_the_tenant(StackOperations operations)
        {
            TellmaLinks links = Links(("acme.Ledger", new StackRoute("ledger", operations)));

            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "acme.Ledger", 17));
        }

        [Fact]
        public void A_resource_that_names_no_stack_links_to_the_tenant()
        {
            TellmaLinks links = Links(("acme.SalesInvoice", new StackRoute("sales-invoices", StackOperations.All)));

            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "acme.Unknown", 17));
            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "ACME.SALESINVOICE", 17));
            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "", 17));
        }

        [Fact]
        public void A_target_without_a_resource_or_an_id_links_to_the_tenant()
        {
            TellmaLinks links = Links(("acme.SalesInvoice", new StackRoute("sales-invoices", StackOperations.All)));

            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, null, null));
            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, null, 17));
            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "acme.SalesInvoice", null));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void An_id_no_record_has_links_to_the_tenant(long id)
        {
            TellmaLinks links = Links(("acme.SalesInvoice", new StackRoute("sales-invoices", StackOperations.All)));

            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "acme.SalesInvoice", id));
        }

        [Fact]
        public void A_link_is_written_alike_under_every_culture()
        {
            TellmaLinks links = Links(("acme.SalesInvoice", new StackRoute("sales-invoices", StackOperations.All)));
            CultureInfo original = CultureInfo.CurrentCulture;

            // A culture that groups digits and writes its own minus sign must not reach a link.
            var grouping = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
            grouping.NumberFormat.NumberGroupSeparator = "٬";
            grouping.NumberFormat.NegativeSign = "؜-";
            try
            {
                CultureInfo.CurrentCulture = grouping;

                Assert.Equal("https://acme.app.tellma.com/1234567", links.Tenant(1_234_567));
                Assert.Equal("https://acme.app.tellma.com/1234567/sales-invoices/7654321", links.Target(1_234_567, "acme.SalesInvoice", 7_654_321));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void A_segment_is_escaped_into_the_path()
        {
            TellmaLinks links = Links(("acme.Odd", new StackRoute("odd segment/?#", StackOperations.All)));

            Assert.Equal("https://acme.app.tellma.com/42/odd%20segment%2F%3F%23/17", links.Target(42, "acme.Odd", 17));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void A_tenant_id_that_names_no_tenant_is_refused(int tenantId)
        {
            TellmaLinks links = Links();

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => links.Tenant(tenantId));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => links.Target(tenantId, null, null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        public void No_link_is_built_without_a_public_origin(string? origin)
        {
            TellmaLinks links = new(
                new TellmaCompositionRecord(TestComposition.Slug, TestComposition.Configuration(TestComposition.Sound(("Tellma:PublicOrigin", origin))), new TestEnvironment()),
                new NoStackRoutes());

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => links.Tenant(42));

            Assert.Contains("Tellma:PublicOrigin is not set", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_composition_registers_the_links_over_the_configured_origin()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ITellmaLinks links = provider.GetRequiredService<ITellmaLinks>();

            Assert.Same(links, provider.GetRequiredService<ITellmaLinks>());
            Assert.Equal("https://acme.app.tellma.com/42", links.Tenant(42));

            // Until a stack registry is composed, no resource names a stack.
            Assert.IsType<NoStackRoutes>(provider.GetRequiredService<IStackRouteSource>());
            Assert.Equal("https://acme.app.tellma.com/42", links.Target(42, "acme.SalesInvoice", 17));
        }

        [Fact]
        public async Task A_stack_route_source_composed_before_the_platforms_default_is_the_one_used()
        {
            ServiceCollection services = [];
            _ = services.AddSingleton<IStackRouteSource>(new FakeStackRoutes(("acme.SalesInvoice", new StackRoute("sales-invoices", StackOperations.All))));
            IServiceCollection composed = TestComposition.Compose(static _ => { }, services: services);
            await using ServiceProvider provider = composed.BuildServiceProvider(validateScopes: true);

            Assert.Equal("https://acme.app.tellma.com/42/sales-invoices/17", provider.GetRequiredService<ITellmaLinks>().Target(42, "acme.SalesInvoice", 17));
        }

        /// <summary>The link builder over the sound origin and the given stacks.</summary>
        /// <param name="stacks">Each resource that names a stack, with its route.</param>
        /// <returns>The link builder.</returns>
        private static TellmaLinks Links(params (string Resource, StackRoute Route)[] stacks)
        {
            TellmaCompositionRecord record = new(
                TestComposition.Slug,
                TestComposition.Configuration(TestComposition.Sound(("Tellma:PublicOrigin", Origin))),
                new TestEnvironment());
            return new TellmaLinks(record, new FakeStackRoutes(stacks));
        }

        /// <summary>A stack registry reduced to what a link needs: resources matched exactly, as entity names are.</summary>
        /// <param name="stacks">Each resource that names a stack, with its route.</param>
        private sealed class FakeStackRoutes(params (string Resource, StackRoute Route)[] stacks) : IStackRouteSource
        {
            private readonly Dictionary<string, StackRoute> _stacks = stacks.ToDictionary(
                static stack => stack.Resource, static stack => stack.Route, StringComparer.Ordinal);

            /// <inheritdoc />
            public StackRoute? Find(string resource)
            {
                return _stacks.TryGetValue(resource, out StackRoute route) ? route : null;
            }
        }
    }
}
