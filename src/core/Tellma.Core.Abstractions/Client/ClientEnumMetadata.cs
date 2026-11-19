// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     A named enum, written once and named by every property, action field and setting key that
    ///     takes its values: a C# enum, named by the schema of the first entity, setting key or action
    ///     that carries it and its type's name (<c>gl.CenterType</c>), or a value set
    ///     (<see cref="ValueSetDescriptor" />), named by its set (<c>core.TimeZone</c>).
    /// </summary>
    /// <param name="Name">The enum's name.</param>
    /// <param name="Values">The enum's values, in order: a C# enum's in its value order, a value set's in its own.</param>
    /// <param name="Aliases">A value set's other spellings, each mapped to its value, in ordinal order; null, and omitted, for none.</param>
    public sealed record ClientEnumMetadata(
        string Name,
        IReadOnlyList<ClientEnumValue> Values,
        IReadOnlyDictionary<string, string>? Aliases);
}
