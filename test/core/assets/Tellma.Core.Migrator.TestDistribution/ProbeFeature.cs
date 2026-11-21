// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Migrator.TestDistribution
{
    /// <summary>
    ///     The test distribution's one feature: a table in a schema of its own, so the tenant model
    ///     has a schema and a sequence beyond the core feature's for the grants to cover.
    /// </summary>
    public sealed class ProbeFeature : ITellmaFeature
    {
        /// <summary>The feature's name.</summary>
        public const string FeatureName = "probe";

        /// <summary>The feature's schema.</summary>
        public const string Schema = "probe";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            _ = contribution.Entity<Gadget>();
        }
    }

    /// <summary>The probe feature's entity: a top-level table with its own key sequence and table type.</summary>
    [Table("Gadgets", Schema = ProbeFeature.Schema)]
    [TableType]
    public class Gadget : TopLevelEntity
    {
        /// <summary>The gadget's name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }
}
