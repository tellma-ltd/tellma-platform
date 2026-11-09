// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Calendars;
using Tellma.Core.Data;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The platform's own feature, named <c>core</c>. The composition root adds it first and
    ///     unconditionally, so every other feature is realised after it without declaring it.
    /// </summary>
    /// <remarks>
    ///     It carries the users, roles, permissions, settings and cache content of the platform as
    ///     those areas land; each adds its items to <see cref="Contribute" />. A distribution never
    ///     adds it: <c>AddFeature&lt;CoreFeature&gt;()</c> is accepted as the idempotent no-op it is.
    /// </remarks>
    public sealed class CoreFeature : ITellmaFeature
    {
        /// <summary>The feature's name.</summary>
        public const string FeatureName = "core";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            // Core requires nothing and binds its options as platform options, not through here.
            ArgumentNullException.ThrowIfNull(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            // --- Calendars: the platform's three systems, first in the registry, and the registry ---
            _ = contribution
                .Calendar<GregorianCalendarSystem>()
                .Calendar<UmAlQuraCalendarSystem>()
                .Calendar<EthiopianCalendarSystem>()
                .Singleton<ICalendarRegistry, CalendarRegistry>();

            // --- The platform's own tables in every tenant database, mapped for migrations only ---
            _ = contribution
                .Model<TellmaSchemaRowConfiguration>()
                .Model<TellmaProvisioningRowConfiguration>();

            // --- The standalone table types: the canonical bulk-operation shapes, and the version-tag guard's ---
            _ = contribution
                .TableType<IdList>()
                .TableType<BigIdList>()
                .TableType<GuidList>()
                .TableType<StringList>()
                .TableType<DateList>()
                .TableType<IdStampList>()
                .TableType<VersionTagList>();
        }
    }
}
