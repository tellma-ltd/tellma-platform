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

        /// <summary>The process runs ICU globalization.</summary>
        public const string Globalization = "core.globalization";

        /// <summary>No configuration value under <c>Tellma:</c> is an unresolved Key Vault reference.</summary>
        public const string KeyVaultLiterals = "core.key-vault-literals";

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
    }
}
