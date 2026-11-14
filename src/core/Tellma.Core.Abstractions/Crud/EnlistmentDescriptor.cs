// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     One declared writer-to-target pair: a type that implements <see cref="IEnlists{TTarget}" />
    ///     for a stack that accepts enlisted writes. The registry reads every pair from the types the
    ///     composition already registers; the pairs form a graph that is acyclic apart from
    ///     self-pairs.
    /// </summary>
    /// <param name="WriterKey">The writer's type, the key the pair is found by.</param>
    /// <param name="WriterName">The writer's display name for logs and startup problems.</param>
    /// <param name="TargetResource">The entity name of the stack the writer writes.</param>
    public sealed record EnlistmentDescriptor(Type WriterKey, string WriterName, string TargetResource);
}
