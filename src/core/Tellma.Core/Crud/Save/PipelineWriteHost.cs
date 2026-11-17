// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Save
{
    /// <summary>The frame the pipeline opens around one of its operations, as the operation's participants see it.</summary>
    /// <param name="resource">The stack's resource.</param>
    /// <param name="operation">The operation's name, such as <c>save</c>.</param>
    /// <param name="confirmations">The frame's view of the scope's confirmations.</param>
    internal sealed class PipelineWriteHost(string resource, string operation, IConfirmations confirmations) : IWriteHost
    {
        /// <inheritdoc />
        public WriteHostKind Kind => WriteHostKind.Pipeline;

        /// <inheritdoc />
        public string Operation { get; } = $"{resource}:{operation}";

        /// <inheritdoc />
        public IConfirmations Confirmations { get; } = confirmations;
    }
}
