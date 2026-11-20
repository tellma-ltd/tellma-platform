// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The stack conformance suite closed over the fixture's shipment stack: a long-keyed job
    ///     entity that serves enlisted writes, which the export stack's service enlists from its save.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class ShipmentConformanceTests(ConformanceDatabase fixture) : StackConformanceTests<Shipment, long>, IClassFixture<ConformanceDatabase>
    {
        /// <summary>The exports' resource, whose save hosts the enlisted writes.</summary>
        private const string Exports = "fixture.Export";

        /// <inheritdoc />
        protected override IStackConformanceHarness Harness => fixture;

        /// <inheritdoc />
        protected override IReadOnlyList<StackGrant> AdditionalGrants { get; } = [new(Exports, AccessActions.Read), new(Exports, AccessActions.Save)];

        /// <inheritdoc />
        protected override Shipment NewValidEntity(int ordinal)
        {
            return new Shipment { State = ShipmentState.Pending, Payload = Invariant($"Shipment {ordinal}") };
        }

        /// <inheritdoc />
        protected override async Task EnlistAsync(IServiceProvider scope, Func<IWriteHost, IEnlists<Shipment>, Task> enlist)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(enlist);

            // --- An export's save, whose validation enlists through the export service ---
            fixture.Probe.ValidateExports = (service, context) => enlist(context.Host, service);
            try
            {
                EntityService<Export, int> exports = scope.GetRequiredService<EntityService<Export, int>>();
                _ = await exports.SaveAsync([new Export { Name = "Host" }], new SaveOptions { ReturnEntities = false }, TestContext.Current.CancellationToken);
            }
            finally
            {
                fixture.Probe.Reset();
            }
        }
    }
}
