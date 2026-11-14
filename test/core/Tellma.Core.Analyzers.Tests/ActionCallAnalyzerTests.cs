// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>TELLMA0004: action methods are reached only through the invoker and the pipeline.</summary>
    public class ActionCallAnalyzerTests
    {
        /// <summary>A service with one action of each kind.</summary>
        private const string Service = """
            using System.Threading.Tasks;
            using Tellma.Core.Abstractions.Crud;

            public class Centers
            {
                [ApiAction("export")]
                public virtual Task<int> ExportAsync(string format) => Task.FromResult(0);

                [EntityAction("close")]
                public virtual Task CloseAsync(int[] ids) => Task.CompletedTask;

                public Task<int> CountAsync() => Task.FromResult(0);

                // The declaring type composes its own actions.
                public Task<int> ExportTwiceAsync() => ExportAsync("csv");

                class Nested
                {
                    Task Run(Centers centers) => centers.CloseAsync([]);
                }
            }

            """;

        /// <summary>Calling an action from another type is refused, whether called or taken as a delegate.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Calling_an_action_from_another_type_is_refused()
        {
            return AnalyzerHarness.VerifyAsync<ActionCallAnalyzer>(Service + """
                class Caller
                {
                    async Task Run(Centers centers)
                    {
                        _ = await {|TELLMA0004:centers.ExportAsync("csv")|};
                        await {|TELLMA0004:centers.CloseAsync([1])|};
                        System.Func<int[], Task> close = {|TELLMA0004:centers.CloseAsync|};
                        _ = await centers.CountAsync();
                        _ = nameof(Centers.ExportAsync);
                    }
                }
                """);
        }

        /// <summary>An override keeps its base's attribute, so calling it from outside is refused too.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task An_override_is_an_action_too()
        {
            return AnalyzerHarness.VerifyAsync<ActionCallAnalyzer>(Service + """
                class MyCenters : Centers
                {
                    public override Task<int> ExportAsync(string format) => base.ExportAsync(format);

                    // The derived type composes with the actions on itself.
                    public Task Both() => CloseAsync([]);
                }

                class Caller
                {
                    Task<int> Run(MyCenters centers) => {|TELLMA0004:centers.ExportAsync("csv")|};
                }
                """);
        }

        /// <summary>A derived type calling an action on another instance is refused: only its own instance composes.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_derived_type_calling_another_instance_is_refused()
        {
            return AnalyzerHarness.VerifyAsync<ActionCallAnalyzer>(Service + """
                class MyCenters : Centers
                {
                    Task Other(Centers other) => {|TELLMA0004:other.CloseAsync([])|};
                }
                """);
        }

        /// <summary>The runtime is the invoker and the pipeline, and calls actions directly.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_runtime_calls_actions()
        {
            return AnalyzerHarness.VerifyAsync<ActionCallAnalyzer>(Service + """
                class Invoker
                {
                    Task<int> Run(Centers centers) => centers.ExportAsync("csv");
                }
                """, "Tellma.Core");
        }
    }
}
