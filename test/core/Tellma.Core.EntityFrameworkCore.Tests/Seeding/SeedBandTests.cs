// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;
using Xunit.Sdk;

namespace Tellma.Core.EntityFrameworkCore.Tests.Seeding
{
    /// <summary>
    ///     Asserts the platform seed convention (spec §4): <c>HasData</c> is restricted to
    ///     well-known rows whose IDs code references, confined to a reserved band disjoint from
    ///     sequence output — a low band with every <c>sq_*</c> sequence starting above it, or
    ///     negative IDs. Distributions reuse this helper against their own models.
    /// </summary>
    public static class SeedBandAssert
    {
        /// <summary>
        ///     Asserts that every seeded numeric key value across the model lies at or below
        ///     <paramref name="bandMaxInclusive" /> (negative IDs are always allowed), and that
        ///     every sequence following the <c>sq_</c> naming convention starts above the band.
        /// </summary>
        /// <param name="model">The model whose seed data is checked.</param>
        /// <param name="bandMaxInclusive">The highest key value reserved for seeds.</param>
        public static void SeedKeysWithinBand(IReadOnlyModel model, long bandMaxInclusive)
        {
            ArgumentNullException.ThrowIfNull(model);

            foreach (IReadOnlyEntityType entityType in model.GetEntityTypes())
            {
                IReadOnlyKey? primaryKey = entityType.FindPrimaryKey();
                if (primaryKey is null)
                {
                    continue;
                }

                foreach (IDictionary<string, object?> seed in entityType.GetSeedData())
                {
                    foreach (IReadOnlyProperty keyProperty in primaryKey.Properties)
                    {
                        if (seed.TryGetValue(keyProperty.Name, out object? value)
                            && value is sbyte or byte or short or ushort or int or uint or long)
                        {
                            long keyValue = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                            Assert.True(
                                keyValue <= bandMaxInclusive,
                                $"Seeded key {entityType.DisplayName()}.{keyProperty.Name} = {keyValue} lies outside the " +
                                $"reserved seed band (<= {bandMaxInclusive}). Seeded IDs must never collide with " +
                                "sequence-assigned IDs.");
                        }
                    }
                }
            }

            foreach (IReadOnlySequence sequence in model.GetSequences())
            {
                if (sequence.Name.StartsWith("sq_", StringComparison.Ordinal))
                {
                    Assert.True(
                        sequence.StartValue > bandMaxInclusive,
                        $"Sequence [{sequence.Schema}].[{sequence.Name}] starts at {sequence.StartValue}, inside the " +
                        $"reserved seed band (<= {bandMaxInclusive}); it must start above the band.");
                }
            }
        }
    }

    /// <summary>Seed-band convention tests over a representative model.</summary>
    public class SeedBandTests
    {
        private const long BandMax = 9_999;

        [Fact]
        public void Seeds_within_the_band_pass()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>(e =>
                {
                    e.ToTable("Plains", "dbo");
                    e.HasData(new Plain { Id = 1, Value = "well-known" }, new Plain { Id = -5, Value = "negative band" });
                });
                mb.HasSequence<int>("sq_Plains", "dbo").StartsAt(10_000);
            });

            SeedBandAssert.SeedKeysWithinBand(TestModel.GetFinalizedModel(context), BandMax);
        }

        [Fact]
        public void Seeds_outside_the_band_fail()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Plain>(e =>
                {
                    e.ToTable("Plains", "dbo");
                    e.HasData(new Plain { Id = 50_000, Value = "collides with sequences" });
                }));

            Assert.Throws<TrueException>(() =>
                SeedBandAssert.SeedKeysWithinBand(TestModel.GetFinalizedModel(context), BandMax));
        }

        [Fact]
        public void Sequences_starting_inside_the_band_fail()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>(e => e.ToTable("Plains", "dbo"));
                mb.HasSequence<int>("sq_Plains", "dbo").StartsAt(100);
            });

            Assert.Throws<TrueException>(() =>
                SeedBandAssert.SeedKeysWithinBand(TestModel.GetFinalizedModel(context), BandMax));
        }
    }
}
