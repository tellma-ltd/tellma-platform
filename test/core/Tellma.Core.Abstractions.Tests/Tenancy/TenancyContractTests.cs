// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Tests.Tenancy
{
    /// <summary>
    ///     The tenancy contract: descriptors compared by value and immune to the caller's list, the
    ///     three exceptions and their codes, the membership row's table type, the listener as a
    ///     collection service, and the pinned telemetry names.
    /// </summary>
    public class TenancyContractTests
    {
        [Fact]
        public void Two_descriptors_with_equal_members_are_equal_languages_compared_by_element()
        {
            TenantDescriptor first = new(17, "Acme", "أكمي", null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null);
            TenantDescriptor second = new(17, "Acme", "أكمي", null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null);

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.NotEqual(first, second with { State = TenantState.Suspended });
            Assert.NotEqual(first, new TenantDescriptor(17, "Acme", "أكمي", null, ["en", "am"], TenantCategory.Live, TenantState.Active, null));
            Assert.NotEqual(first, first with { Name = "ACME" });
        }

        [Fact]
        public void A_descriptor_keeps_a_copy_of_the_languages_it_was_given()
        {
            List<string> languages = ["en"];
            TenantDescriptor descriptor = new(17, "Acme", null, null, languages, TenantCategory.Live, TenantState.Active, null);

            languages.Add("ar");

            Assert.Equal(["en"], descriptor.Languages);
            Assert.False(descriptor.Languages is IList<string> { IsReadOnly: false });
            _ = Assert.Throws<ArgumentException>(() => new TenantDescriptor(17, "Acme", null, null, [], TenantCategory.Live, TenantState.Active, null));
            _ = Assert.Throws<ArgumentNullException>(() => new TenantDescriptor(17, null!, null, null, ["en"], TenantCategory.Live, TenantState.Active, null));
        }

        [Fact]
        public void Display_names_compare_by_value_too()
        {
            Assert.Equal(new TenantDisplayNames("Acme", "أكمي", null, ["en", "ar"]), new TenantDisplayNames("Acme", "أكمي", null, ["en", "ar"]));
            Assert.NotEqual(new TenantDisplayNames("Acme", null, null, ["en"]), new TenantDisplayNames("Acme", null, null, ["ar"]));
        }

        [Fact]
        public void A_tenant_not_found_says_nothing_beyond_the_id_asked_for()
        {
            TenantNotFoundException notFound = new(17);

            Assert.Equal("tenant-not-found", notFound.Code);
            Assert.Equal(17, notFound.TenantId);
            Assert.Empty(notFound.Arguments);
            Assert.Equal("Tenant 17 was not found.", notFound.Message);
        }

        [Theory]
        [InlineData("catalog-unavailable")]
        [InlineData("tenant-schema-behind")]
        [InlineData("tenant-suspended")]
        [InlineData("tenant-read-only")]
        [InlineData("tenant-frozen")]
        public void An_unavailable_tenant_carries_one_of_its_five_codes(string code)
        {
            TenantUnavailableException unavailable = new(17, TenantState.Suspended, code, TimeSpan.FromSeconds(15));

            Assert.Equal(code, unavailable.Code);
            Assert.Equal((17, (TenantState?)TenantState.Suspended, (TimeSpan?)TimeSpan.FromSeconds(15)), (unavailable.TenantId, unavailable.State, unavailable.RetryAfter));
        }

        [Fact]
        public void An_unavailable_tenant_refuses_another_code_or_a_negative_wait()
        {
            _ = Assert.Throws<ArgumentException>(() => new TenantUnavailableException(17, null, "tenant-not-found", null));
            _ = Assert.Throws<ArgumentException>(() => new TenantUnavailableException(17, null, "forbidden", null));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TenantUnavailableException(17, null, "catalog-unavailable", TimeSpan.FromSeconds(-1)));
        }

        [Theory]
        [InlineData("tenant-registration-refused")]
        [InlineData("tenant-illegal-transition")]
        [InlineData("tenant-location-refused")]
        public void A_refused_catalog_write_carries_its_code_and_its_detail_as_the_message(string code)
        {
            TenantStateException refused = new(code, "Tenant 17 cannot move from Retired to Active.");

            Assert.Equal(code, refused.Code);
            Assert.Equal("Tenant 17 cannot move from Retired to Active.", refused.Detail);
            Assert.Equal(refused.Detail, refused.Message);
        }

        [Fact]
        public void A_refused_catalog_write_refuses_another_code_or_an_empty_detail()
        {
            _ = Assert.Throws<ArgumentException>(() => new TenantStateException("tenant-not-found", "x"));
            _ = Assert.Throws<ArgumentException>(() => new TenantStateException("tenant-illegal-transition", " "));
        }

        [Fact]
        public void The_membership_row_is_the_catalogs_binary_collated_table_type()
        {
            TableTypeAttribute type = typeof(TenantMembershipRecord).GetCustomAttribute<TableTypeAttribute>()!;
            CollationAttribute collation = typeof(TenantMembershipRecord).GetProperty(nameof(TenantMembershipRecord.Subject))!.GetCustomAttribute<CollationAttribute>()!;

            Assert.Equal(("catalog", "TenantMembershipList"), (type.Schema, type.Name));
            Assert.Equal(DatabaseCollations.Binary, collation.Name);
            Assert.Equal("Latin1_General_100_BIN2", DatabaseCollations.Binary);
        }

        [Fact]
        public void Every_registration_of_a_state_listener_is_kept()
        {
            Assert.NotNull(typeof(ITenantStateListener).GetCustomAttribute<CollectionServiceAttribute>());
        }

        [Fact]
        public void The_telemetry_names_are_pinned()
        {
            Assert.Equal("Tellma.Core", TenancyTelemetryNames.MeterName);
            Assert.Equal(
                [
                    "tellma.tenancy.resolutions",
                    "tellma.tenancy.catalog.refresh.duration",
                    "tellma.tenancy.catalog.snapshot.age",
                    "tellma.tenancy.catalog.tenants",
                    "tellma.tenancy.membership.writes",
                    "tellma.tenancy.scopes",
                ],
                [
                    TenancyTelemetryNames.Resolutions,
                    TenancyTelemetryNames.CatalogRefreshDuration,
                    TenancyTelemetryNames.CatalogSnapshotAge,
                    TenancyTelemetryNames.CatalogTenants,
                    TenancyTelemetryNames.MembershipWrites,
                    TenancyTelemetryNames.Scopes,
                ]);
            Assert.Equal(
                ["outcome", "category", "state", "kind", "source"],
                [TenancyTelemetryNames.OutcomeTag, TenancyTelemetryNames.CategoryTag, TenancyTelemetryNames.StateTag, TenancyTelemetryNames.KindTag, TenancyTelemetryNames.SourceTag]);
        }

        [Fact]
        public void The_stored_names_of_categories_and_states_are_pinned()
        {
            // The catalog stores these names and CHECK constraints list them: renaming one breaks every catalog.
            Assert.Equal(["Live", "Sandbox"], Enum.GetNames<TenantCategory>());
            Assert.Equal(["Provisioning", "Active", "ReadOnly", "Frozen", "Suspended", "Retired"], Enum.GetNames<TenantState>());
        }
    }
}
