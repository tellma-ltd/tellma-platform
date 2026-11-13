// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Testing.Entities
{
    // Stand-ins for the core tables the fixture schema references that no feature contributes yet,
    // under their production names, so the job foreign keys resolve to the table production has.
    // The stand-in is replaced, and the fixture migrations regenerated, when the job runtime
    // contributes core.Jobs. Core itself contributes core.Users, the other access tables,
    // core.VersionTags and core.Blobs to every composition.

    /// <summary>A stand-in for <c>core.Jobs</c>, which every job entity's <c>JobId</c> references.</summary>
    [Table("Jobs", Schema = PlatformSchema.Name)]
    public class FixtureJob : Entity<int>;

    /// <summary>The platform schema's name.</summary>
    public static class PlatformSchema
    {
        /// <summary>The schema of the platform's core tables.</summary>
        public const string Name = "core";
    }
}
