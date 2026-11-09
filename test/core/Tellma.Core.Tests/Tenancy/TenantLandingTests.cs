// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The post-sign-in landing rule over the vectors the client's launch redirect reads too, so
    ///     the two places apply one rule.
    /// </summary>
    public class TenantLandingTests
    {
        private static readonly JsonSerializerOptions VectorJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Every vector of the shared file, by name.</summary>
        public static TheoryData<string> Vectors
        {
            get
            {
                TheoryData<string> names = [];
                foreach (LandingVector vector in Load())
                {
                    names.Add(vector.Name);
                }

                return names;
            }
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void The_landing_matches_every_shared_vector(string name)
        {
            LandingVector vector = Load().Single(candidate => candidate.Name == name);

            IReadOnlyList<TenantMembership> memberships = [.. vector.Tenants.Select(static tenant => new TenantMembership(
                new TenantDescriptor(tenant.Id, tenant.Name, tenant.Name2, tenant.Name3, tenant.Languages, tenant.Category, tenant.State, tenant.LiveTenantId),
                tenant.IsActive,
                DateTimeOffset.UnixEpoch))];

            Assert.Equal(vector.Expected, TenantLanding.SingleLiveTenant(memberships));
        }

        [Fact]
        public void The_vectors_count_a_read_only_and_a_frozen_tenant_and_skip_a_suspended_and_a_provisioning_one()
        {
            // The file must keep the cases the rule's statement names, whatever else it grows.
            List<LandingVector> vectors = Load();
            Assert.Contains(vectors, static v => v.Expected is not null && v.Tenants is [{ State: TenantState.ReadOnly }]);
            Assert.Contains(vectors, static v => v.Expected is not null && v.Tenants is [{ State: TenantState.Frozen }]);
            Assert.Contains(vectors, static v => v.Expected is null && v.Tenants is [{ State: TenantState.Suspended }]);
            Assert.Contains(vectors, static v => v.Expected is null && v.Tenants is [{ State: TenantState.Provisioning }]);
        }

        [Fact]
        public void A_null_list_or_entry_is_refused()
        {
            _ = Assert.Throws<ArgumentNullException>(() => TenantLanding.SingleLiveTenant(null!));
            _ = Assert.Throws<ArgumentNullException>(() => TenantLanding.SingleLiveTenant([null!]));
        }

        /// <summary>Reads the shared vector file copied beside the suite.</summary>
        /// <returns>The vectors.</returns>
        private static List<LandingVector> Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Tenancy", "launch-landing-vectors.json");
            return JsonSerializer.Deserialize<List<LandingVector>>(File.ReadAllText(path), VectorJson)
                ?? throw new InvalidOperationException("The landing vectors are empty.");
        }

        /// <summary>One vector: the memberships and the tenant the landing picks.</summary>
        /// <param name="Name">What the vector shows.</param>
        /// <param name="Tenants">The memberships, as the user document lists them.</param>
        /// <param name="Expected">The tenant landed on, or null for the picker.</param>
        private sealed record LandingVector(string Name, IReadOnlyList<VectorTenant> Tenants, int? Expected);

        /// <summary>One membership of a vector, in the user document's shape.</summary>
        /// <param name="Id">The tenant id.</param>
        /// <param name="Name">The first name.</param>
        /// <param name="Name2">The second name.</param>
        /// <param name="Name3">The third name.</param>
        /// <param name="Languages">The language codes.</param>
        /// <param name="Category">The category.</param>
        /// <param name="State">The state.</param>
        /// <param name="LiveTenantId">A sandbox's live tenant.</param>
        /// <param name="IsActive">Whether the membership is active.</param>
        private sealed record VectorTenant(
            int Id,
            string Name,
            string? Name2,
            string? Name3,
            IReadOnlyList<string> Languages,
            TenantCategory Category,
            TenantState State,
            int? LiveTenantId,
            bool IsActive);
    }
}
