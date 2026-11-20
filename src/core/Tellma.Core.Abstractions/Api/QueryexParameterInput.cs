// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>A parameter type the author confirmed, which wins over the type the clauses imply.</summary>
    /// <param name="Name">The parameter's name, without its <c>@</c>; matched without regard to case.</param>
    /// <param name="Type">The confirmed type; neither a hierarchy nor a geography, which have no parameter form.</param>
    public sealed record QueryexParameterInput(string Name, QueryexType Type);
}
