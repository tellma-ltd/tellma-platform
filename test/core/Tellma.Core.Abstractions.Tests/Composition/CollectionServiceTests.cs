// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.Composition
{
    /// <summary>The extension points every registration of which must be kept are marked as collection services.</summary>
    public class CollectionServiceTests
    {
        [Theory]
        [InlineData(typeof(IStartupCheck))]
        [InlineData(typeof(IClientAssetContributor))]
        [InlineData(typeof(ICalendarSystem))]
        public void The_contract_marks_its_collection_extension_points(Type serviceType)
        {
            Assert.True(serviceType.IsDefined(typeof(CollectionServiceAttribute), inherit: false));
        }
    }
}
