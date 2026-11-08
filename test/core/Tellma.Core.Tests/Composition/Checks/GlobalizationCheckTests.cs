// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.globalization</c>: the process runs ICU.</summary>
    public class GlobalizationCheckTests
    {
        private const string Check = "core.globalization";

        [Fact]
        public async Task A_process_without_ICU_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, icuProbe: static () => false);

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Contains("InvariantGlobalization", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_process_with_ICU_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, icuProbe: static () => true);

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public void The_test_host_itself_runs_ICU()
        {
            // The suites run on Windows (ICU since Windows 10 1903) and on Linux with libicu, the
            // configurations every deployment must have; a false here means the probe misreads them.
            Assert.True(IcuProbe.IsRunningIcu());
        }
    }
}
