// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A name a batch emits into statement text rather than binding as a value: an object name,
    ///     which the batch bracket-quotes part by part (<c>fixture.Widgets</c> becomes
    ///     <c>[fixture].[Widgets]</c>), or a table variable or table-valued parameter the batch
    ///     declared (<see cref="IDataBatch.Tvp{TRow}" />, <see cref="IDataBatch.DeclareIdTable" />),
    ///     emitted as is. Never a value.
    /// </summary>
    public sealed record SqlIdentifier
    {
        /// <summary>Creates an identifier.</summary>
        /// <param name="name">The object name, or the <c>@</c> name the batch declared.</param>
        /// <exception cref="ArgumentException">The name is empty or white space.</exception>
        public SqlIdentifier(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name;
        }

        /// <summary>The object name, or the <c>@</c> name the batch declared.</summary>
        public string Name { get; }

        /// <summary>Whether the identifier names a variable or parameter (it starts with <c>@</c>).</summary>
        public bool IsVariable => Name.StartsWith('@');

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }
    }
}
