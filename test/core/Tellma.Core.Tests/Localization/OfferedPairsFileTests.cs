// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The offered pairs the client's engine check reads are the language catalogue's own: every
    ///     (language, region) pair, in catalogue order.
    /// </summary>
    public class OfferedPairsFileTests
    {
        [Fact]
        public void The_client_pairs_file_lists_exactly_the_catalogue_pairs_in_order()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Localization", "offered-pairs.json");
            using var file = JsonDocument.Parse(File.ReadAllText(path));

            string[] pairs = [.. file.RootElement.GetProperty("pairs").EnumerateArray().Select(static pair => pair.GetString()!)];

            Assert.Equal(CultureDataSource.CataloguePairs(), pairs);
        }
    }
}
