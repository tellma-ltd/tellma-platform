// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Composition.Checks
{
    /// <summary>The names of the platform's built-in startup checks.</summary>
    internal static class CoreCheckNames
    {
        /// <summary>Every bound options type passes validation.</summary>
        public const string Options = "core.options";

        /// <summary>The registered deployment identity names this distribution, and a sandbox context resolves.</summary>
        public const string DeploymentIdentity = "core.deployment-identity";

        /// <summary>Every provisioning step can be created and has a valid, unique name, an order and a version.</summary>
        public const string ProvisioningSteps = "core.provisioning-steps";

        /// <summary>The process runs ICU globalization.</summary>
        public const string Globalization = "core.globalization";

        /// <summary>No configuration value under <c>Tellma:</c> is an unresolved Key Vault reference.</summary>
        public const string KeyVaultLiterals = "core.key-vault-literals";

        /// <summary>The credential profiles and the catalog connection follow the connection rules, and the allowed servers are set.</summary>
        public const string SqlProfiles = "core.sql-profiles";

        /// <summary><c>Tellma:PublicOrigin</c> is an absolute, path-less origin, https outside Development.</summary>
        public const string PublicOrigin = "core.public-origin";

        /// <summary>In Development, the repository's <c>taxonomy.json</c> lists the slug.</summary>
        public const string Taxonomy = "core.taxonomy";

        /// <summary>Every leaf carries the shapes other features require of its entity.</summary>
        public const string EntityShapes = "core.entity-shapes";

        /// <summary>Every model configuration Core contributes is an entity type configuration the model can apply.</summary>
        public const string ModelContributors = "core.model-contributors";

        /// <summary>The tenant model builds, and every entity leaf in it satisfies the entity contract.</summary>
        public const string EntityMetadata = "core.entity-metadata";

        /// <summary>
        ///     Every version tag the entities declare is well formed, entity tags come from cacheable
        ///     entities alone, every user-level tag names an int property, and no two names share a row.
        /// </summary>
        public const string VersionTags = "core.version-tags";

        /// <summary>The declared languages are described, their regions are theirs, their strings ship, and symbols are unique.</summary>
        public const string Languages = "core.languages";

        /// <summary>Calendar codes are well formed and unique, and the platform's three calendars are composed.</summary>
        public const string Calendars = "core.calendars";

        /// <summary>Value sets are well formed and uniquely named, and every property that draws from one names a composed set.</summary>
        public const string ValueSets = "core.value-sets";

        /// <summary>
        ///     Notification type keys and categories are well formed, keys are unique, every target
        ///     names a routable entity with an integer key or a registered securable resource, and
        ///     channel-only types follow their rule.
        /// </summary>
        public const string NotificationTypes = "core.notification-types";

        /// <summary>
        ///     Channel keys are well formed and unique, the two baseline channels are composed, and every
        ///     channel sender names a registered channel other than the inbox, at most one per channel.
        /// </summary>
        public const string NotificationChannels = "core.notification-channels";

        /// <summary>Client event names are well formed and unique.</summary>
        public const string ClientEvents = "core.client-events";

        /// <summary>
        ///     The securables compose without conflict, their names follow the grammar, their filter
        ///     roots are query entities, a child's owner is reached through its navigation, owner
        ///     chains end, and every securable belongs to a composed feature.
        /// </summary>
        public const string Securables = "core.securables";

        /// <summary>Warns about each zone a user can pick that this host's time-zone data cannot resolve.</summary>
        public const string TimeZones = "core.time-zones";
    }
}
