// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Every stack, child query surface, routed service and enlistment pair of the host, built once
    ///     at startup with no tenant scope. A singleton.
    /// </summary>
    public interface IStackRegistry
    {
        /// <summary>Every stack, in registration order.</summary>
        IReadOnlyList<StackDescriptor> Stacks { get; }

        /// <summary>Every child query surface, at any depth.</summary>
        IReadOnlyList<ChildQueryDescriptor> ChildQueries { get; }

        /// <summary>Every routed service, in registration order.</summary>
        IReadOnlyList<ApiServiceDescriptor> Services { get; }

        /// <summary>The declared writer-to-target pairs over the stacks.</summary>
        IReadOnlyList<EnlistmentDescriptor> Enlistments { get; }

        /// <summary>Finds a stack by its entity name.</summary>
        /// <param name="resource">The entity name, such as <c>gl.Center</c>; compared ordinally.</param>
        /// <returns>The stack, or null when no stack has the name.</returns>
        StackDescriptor? Find(string resource);

        /// <summary>Finds a child query surface by its child entity name.</summary>
        /// <param name="resource">The child's entity name, such as <c>acme.SalesInvoiceLine</c>; compared ordinally.</param>
        /// <returns>The surface, or null when no surface has the name.</returns>
        ChildQueryDescriptor? FindChildQuery(string resource);

        /// <summary>Finds the stack that owns a table: the stack whose root or child collection, at any depth, is stored in it.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The owning stack, or null when no stack owns the table.</returns>
        StackDescriptor? OwnerOf(TableName table);
    }
}
