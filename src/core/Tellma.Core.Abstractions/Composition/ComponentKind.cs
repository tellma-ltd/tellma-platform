// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>The kind of a stack component a <see cref="ValidatorContributionItem" /> registers: the contract it implements.</summary>
    public enum ComponentKind
    {
        /// <summary>An <c>IEntityValidator&lt;TEntity&gt;</c>: rules over saves, deletes and actions.</summary>
        Validator,

        /// <summary>An <c>IPersistEffect&lt;TEntity&gt;</c>: statements in the persist batch and work after the commit.</summary>
        PersistEffect,

        /// <summary>An <c>IDetailsContributor&lt;TEntity&gt;</c>: the details extras its class declares.</summary>
        DetailsContributor,

        /// <summary>An <c>INamedFilterContributor&lt;TEntity&gt;</c>: named filters, built once at startup.</summary>
        NamedFilterContributor,
    }
}
