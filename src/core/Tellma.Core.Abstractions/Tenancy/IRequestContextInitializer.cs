// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     One step that completes a request context after the platform bound its tenant and
    ///     principal: it takes the context so far and returns a copy with its own members set. The
    ///     platform's user connection runs at <see cref="Order" /> 100 and binds
    ///     <see cref="RequestContext.UserId" />; its locale negotiation runs at 200.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The steps run in ascending <see cref="Order" />, steps of equal order in registration
    ///         order, for every tenant request before its handler and for every background tenant
    ///         scope before its caller receives it. Each step is resolved from the scope it completes,
    ///         and the scope's <see cref="IRequestContextAccessor" /> already answers the context the
    ///         step is given, so a step may use tenant-scoped services.
    ///     </para>
    ///     <para>
    ///         A step never changes whom the context is bound to: the tenant, the principal's kind and
    ///         its subject come back as they went in, or the platform refuses the result. A step that
    ///         finds the principal is not an active member of the tenant throws
    ///         <see cref="TenantNotFoundException" />.
    ///     </para>
    ///     <para>A collection service: every registration runs.</para>
    /// </remarks>
    [CollectionService]
    public interface IRequestContextInitializer
    {
        /// <summary>Where the step runs among the others: ascending.</summary>
        int Order { get; }

        /// <summary>Completes the context.</summary>
        /// <param name="context">The context so far: the platform's binding and the result of every earlier step.</param>
        /// <param name="inputs">What the request asked for; <see cref="RequestContextInputs.Empty" /> in a background scope.</param>
        /// <param name="cancellationToken">Cancels the step.</param>
        /// <returns>The context with this step's members set; never null.</returns>
        Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken);
    }
}
