// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A named-filter component: constant filters a stack's queries select by name. Instantiated
    ///     once at startup from the root service provider, so its constructor takes singletons only.
    /// </summary>
    /// <remarks>
    ///     A name matches <c>^[a-z][a-z0-9-]*$</c> and is declared once across a stack's contributors.
    ///     A tree takes no value and depends on the caller only through the context functions
    ///     (<c>me()</c>, <c>now()</c>, <c>today()</c>); it is trusted code, so a query may reach a table
    ///     through it that the traversal rule would refuse in user text.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    [CollectionService]
    public interface INamedFilterContributor<TEntity>
    {
        /// <summary>The contributor's named filters, by name.</summary>
        IReadOnlyDictionary<string, FilterTree> NamedFilters { get; }
    }
}
