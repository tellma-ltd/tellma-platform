// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The progress percent the background exports and imports report: the share done, rounded
    ///     down and short of the 100 only the job's completion writes.
    /// </summary>
    public sealed class ExcelJobsTests
    {
        [Theory]
        [InlineData(0L, 7L, (byte)0)]
        [InlineData(1L, 3L, (byte)33)]
        [InlineData(2L, 3L, (byte)66)]
        [InlineData(7L, 7L, (byte)99)]
        [InlineData(14L, 7L, (byte)99)]
        [InlineData(-3L, 7L, (byte)0)]
        public void A_percent_is_the_share_done_rounded_down_and_never_the_completions_100(long done, long total, byte expected)
        {
            Assert.Equal(expected, ExcelJobs.Percent(done, total));
        }

        [Theory]
        [InlineData(5L, null)]
        [InlineData(5L, 0L)]
        [InlineData(5L, -1L)]
        public void A_percent_with_no_whole_to_share_is_none(long done, long? total)
        {
            Assert.Null(ExcelJobs.Percent(done, total));
        }
    }
}
