// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>An entity's key: the property that holds it and its type.</summary>
    /// <param name="Property">The key property's name, such as <c>Id</c>.</param>
    /// <param name="Type">The key's type, which decides whether ids travel as numbers or as decimal text.</param>
    public sealed record ClientEntityKey(string Property, ClientKeyType Type);
}
