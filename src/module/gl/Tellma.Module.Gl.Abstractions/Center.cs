// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Module.Gl
{
    // Spec 0018 §5.2-§5.3: the annotations alone produce gl.Centers through the platform's
    // conventions (sequence, enum as string, tree, natural key, table index); GL writes no model
    // configuration. Spec 0018 §5.5 projects delete-by-query too, but the stack registry refuses a
    // stack that judges its deletes and still serves one (centers judge theirs), so it is left out.
    /// <summary>
    ///     A responsibility center: a node of the tenant's tree of business units, cost centers and
    ///     revenue centers that postings are analysed by. The module's default: a distribution extends
    ///     it by inheritance and registers its class as the leaf.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only an <see cref="CenterType.Abstract" /> or a <see cref="CenterType.BusinessUnit" />
    ///         center has children, and a center with children is not deleted on its own: a delete
    ///         names its descendants too, or deletes it with its descendants.
    ///     </para>
    ///     <para>
    ///         The stack serves no delete by query: centers are deleted by their ids or with their
    ///         descendants, so every deletion meets the rule that keeps the tree whole.
    ///     </para>
    /// </remarks>
    [Table("Centers", Schema = GlModule.Schema)]
    [TableType]
    [Tree(MaxDepth = 32)]
    [TableIndex(nameof(IsActive), nameof(Name))]
    [ApiResource(Description = "Responsibility centers")]
    [Stack(Operations = StackOperations.All & ~StackOperations.DeleteByQuery)]
    public class Center : ActivatableTreeEntity
    {
        /// <summary>
        ///     What the center stands for, <c>varchar(12)</c>; only an abstract center or a business
        ///     unit may have children.
        /// </summary>
        public CenterType CenterType { get; set; }

        /// <summary>The name in the tenant's primary language, <c>nvarchar(255)</c>.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the tenant's secondary language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the tenant's tertiary language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The natural key seeds and imports find the center by, <c>nvarchar(50)</c>, unique.</summary>
        [MaxLength(50)]
        [NaturalKey]
        [Searchable(SearchKind.Prefix)]
        public string Code { get; set; } = null!;
    }
}
