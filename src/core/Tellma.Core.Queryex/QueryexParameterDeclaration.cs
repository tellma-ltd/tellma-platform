// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A declared parameter: its name, its type, and whether a value is guaranteed present.
    /// </summary>
    /// <remarks>
    ///     <paramref name="IsNotNull" /> is a truthfulness obligation on the host, exactly like a
    ///     column's. Null guards are omitted on its strength, so a host that declares it and then
    ///     binds an absent value forfeits the guarantees the language makes about how absence
    ///     compares.
    /// </remarks>
    /// <param name="Name">The name, without the leading marker. Matched case-insensitively.</param>
    /// <param name="Type">
    ///     The parameter's type. Neither hierarchy nodes nor spatial values have a parameter
    ///     representation, so declaring one of those is a caller error.
    /// </param>
    /// <param name="IsNotNull">Whether a value is guaranteed present at execution.</param>
    public sealed record QueryexParameterDeclaration(string Name, QueryexType Type, bool IsNotNull);
}
