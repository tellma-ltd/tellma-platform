// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Blobs;
using Tellma.Core.Calendars;
using Tellma.Core.Data;
using Tellma.Core.Localization;

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

            // --- Languages, value sets and locale negotiation ---
            // The catalogue reads the distribution's language declaration, which the composition
            // root registers; the value-set registry composes the platform's four sets with every
            // set a feature contributes.
            _ = contribution
                .Singleton<ILanguageCatalog, LanguageCatalog>()
                .Singleton<IValueSetRegistry, ValueSetRegistry>()
                .Singleton<ILocalizationNegotiator, LocalizationNegotiator>();

            // --- The platform's own tables in every tenant database, mapped for migrations only ---
            _ = contribution
                .Model<TellmaSchemaRowConfiguration>()
                .Model<TellmaProvisioningRowConfiguration>();

            // --- Access: users and roles with their children, each configuration closed over the leaf ---
            // spec 0014 §1.2. The services of the two stacks join these lines when they land.
            _ = contribution
                .Entity<User>()
                .Entity<Role>()
                .Model(typeof(UserConfiguration<>))
                .Model(typeof(RoleConfiguration<>))
                .Model(typeof(RoleMembershipConfiguration<>))
                .Model(typeof(PermissionConfiguration<>))
                .Model<UserStampConfiguration>()
                .Model<UserPreferenceConfiguration>()
                .TableType<UserPreferenceList>();

            // --- Blobs: the table every blob reference resolves to; never a stack ---
            _ = contribution.Model<BlobConfiguration>();

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
