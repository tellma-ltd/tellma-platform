// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tests.Errors
{
    /// <summary>The stale-context outcome: its code, the stale inputs it names, and the guard's nameless form.</summary>
    public class StaleContextExceptionTests
    {
        [Fact]
        public void A_stale_context_names_its_inputs_in_order_and_copies_them()
        {
            VersionTagDependency settings = new(VersionTagNames.Settings, VersionTag.New(), VersionTagMismatchPolicy.Rerun);
            VersionTagDependency permissions = new(VersionTagNames.Permissions, VersionTag.New(), VersionTagMismatchPolicy.Rerun);
            List<VersionTagDependency> stale = [settings, permissions];

            StaleContextException exception = new(stale);
            stale.Clear();

            Assert.Equal("stale-context", exception.Code);
            Assert.Empty(exception.Arguments);
            Assert.Equal([settings, permissions], exception.Dependencies);
            Assert.Equal("Version tags moved again after the request re-ran once: settings, permissions.", exception.Message);
        }

        [Fact]
        public void The_write_guards_refusal_names_no_input()
        {
            StaleContextException exception = new([]);

            Assert.Empty(exception.Dependencies);
            Assert.Equal("The write guard found a version tag moved after the request re-ran once.", exception.Message);
        }

        [Fact]
        public void A_null_list_or_entry_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => new StaleContextException(null!));
            Assert.Throws<ArgumentException>(() => new StaleContextException([null!]));
        }
    }
}
