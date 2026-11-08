// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Declares, on an entity type, the Queryex select a query uses when the request names none:
    ///     the columns pickers and the agent query tool read by default. Without it the platform's
    ///     default applies: the id, the natural keys, the multilingual name group, the code, the
    ///     active flag and the audit columns. Inherited.
    /// </summary>
    /// <remarks>The text is validated at startup against the tenant-independent schema.</remarks>
    /// <example>
    ///     <code>[DefaultSelect("Id,Message,CreatedAt,CreatedById")]</code>
    /// </example>
    /// <param name="select">The Queryex select text, comma-separated.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class DefaultSelectAttribute(string select) : Attribute
    {
        /// <summary>The Queryex select text, comma-separated.</summary>
        public string Select { get; } = select;
    }
}
