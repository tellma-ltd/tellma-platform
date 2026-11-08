// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Marks a property of an action's arguments record as holding the id of a row of another
    ///     entity, so the client's generated action dialog renders an entity picker for it.
    /// </summary>
    /// <remarks>
    ///     Metadata only: the pipeline loads nothing for it, and a rule that needs the referenced row
    ///     loads it through the validation loader. The resource must be owned by a stack that serves
    ///     queries; the startup checks report one that is not.
    /// </remarks>
    /// <example>
    ///     <code>public sealed record AssignInvoiceArguments([property: EntityRef("core.User")] int AssigneeId, string? Comment);</code>
    /// </example>
    /// <param name="resource">The referenced entity's name, such as <c>core.User</c>.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class EntityRefAttribute(string resource) : Attribute
    {
        /// <summary>The referenced entity's name, such as <c>core.User</c>.</summary>
        public string Resource { get; } = resource;
    }
}
