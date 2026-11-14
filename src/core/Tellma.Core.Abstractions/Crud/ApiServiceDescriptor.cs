// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>A routed service that is not an entity's (<c>inbox</c>, <c>settings</c>, <c>access</c>) and the API actions it declares.</summary>
    /// <param name="Route">The service's route segment, from its route attribute.</param>
    /// <param name="ServiceType">The service's type.</param>
    /// <param name="Actions">The service's API actions, in declaration order.</param>
    /// <param name="Feature">The name of the feature that registered the service; every securable registered for it carries it.</param>
    public sealed record ApiServiceDescriptor(string Route, Type ServiceType, IReadOnlyList<ApiActionDescriptor> Actions, string Feature);
}
