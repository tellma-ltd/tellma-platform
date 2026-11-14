// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;

namespace Tellma.Core.Abstractions.Tests.Client
{
    /// <summary>The job handlers the client metadata lists: their members under the platform's JSON rules.</summary>
    public class ClientJobHandlerTests
    {
        [Fact]
        public void A_handler_with_a_result_and_a_label_resource_omits_the_fallback()
        {
            ClientJobHandler handler = new("acme.export", Schedulable: true, CommitsInSteps: false, "JobHandler_acme_export", null, new ClientJobResult("acme.Export", "JobId"));

            Assert.Equal(
                /*lang=json,strict*/ """{"key":"acme.export","schedulable":true,"commitsInSteps":false,"labelKey":"JobHandler_acme_export","result":{"entity":"acme.Export","via":"JobId"}}""",
                JsonSerializer.Serialize(handler, TellmaJson.Options));
        }

        [Fact]
        public void A_handler_without_a_label_resource_carries_its_key_as_the_fallback_and_omits_the_result()
        {
            ClientJobHandler handler = new("core.blob-sweep", Schedulable: false, CommitsInSteps: true, "JobHandler_core_blob-sweep", "core.blob-sweep", null);

            Assert.Equal(
                /*lang=json,strict*/ """{"key":"core.blob-sweep","schedulable":false,"commitsInSteps":true,"labelKey":"JobHandler_core_blob-sweep","labelFallback":"core.blob-sweep"}""",
                JsonSerializer.Serialize(handler, TellmaJson.Options));
        }
    }
}
