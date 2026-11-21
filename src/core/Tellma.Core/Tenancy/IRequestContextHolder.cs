// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The write side of a scope's request context. Scoped, and the platform's own: only its two
    ///     writers set it, the tenant front door of a request (the tenant middleware, then the access
    ///     guard) and the tenant scope factory for background work. Everything else reads through
    ///     <see cref="IRequestContextAccessor" />, which resolves to the same instance.
    /// </summary>
    /// <remarks>
    ///     The context lives in the scope alone: it is never copied into an ambient, a static or a
    ///     singleton. Until a writer sets one, the scope's context is the tenantless context, stamped
    ///     with the host's clock the first time it is read. A writer binds the tenant and the
    ///     principal in one write; from then on the scope keeps both, and later writes only complete
    ///     the context.
    /// </remarks>
    public interface IRequestContextHolder : IRequestContextAccessor
    {
        /// <summary>Sets the scope's context.</summary>
        /// <param name="context">The context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The scope is already bound to a tenant and <paramref name="context" /> names another
        ///     tenant or none, another principal (its kind or its subject), or another user than the
        ///     one already bound, or none: a scope serves one tenant, one principal and one user for
        ///     its whole lifetime. Binding the user of a context that names none is allowed.
        /// </exception>
        [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Set is the holder's one write, named for what it does; the holder is written in C#, where Set is not a keyword.")]
        void Set(RequestContext context);
    }
}
