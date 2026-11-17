// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Tenancy
{
    /// <summary>
    ///     What a request asks of its context about the activity stamp: none for an endpoint that
    ///     says so or a request that says it is background, and a mutation's header ignored.
    /// </summary>
    public sealed class RequestInputsTests
    {
        /// <summary>The empty services every request resolves its options from.</summary>
        private static readonly ServiceProvider Services = new ServiceCollection().BuildServiceProvider();

        [Fact]
        public void An_ordinary_request_stamps_activity()
        {
            Assert.False(RequestInputs.Of(Request(HttpMethods.Get, metadata: [])).SuppressActivityStamp);
        }

        [Fact]
        public void An_endpoint_that_asks_for_no_stamp_suppresses_it()
        {
            Assert.True(RequestInputs.Of(Request(HttpMethods.Get, metadata: [new NoActivityStampMetadata()])).SuppressActivityStamp);
        }

        [Fact]
        public void A_read_that_says_it_is_background_suppresses_the_stamp()
        {
            HttpContext context = Request(HttpMethods.Get, metadata: [new TenantEndpointMetadata(TenantSurface.Api, IsMutation: false, AllowWhenReadOnly: true)]);
            context.Request.Headers[TellmaHeaders.Activity] = "background";

            Assert.True(RequestInputs.Of(context).SuppressActivityStamp);
        }

        [Fact]
        public void A_mutation_that_says_it_is_background_still_stamps()
        {
            HttpContext context = Request(HttpMethods.Post, metadata: [new TenantEndpointMetadata(TenantSurface.Api, IsMutation: true, AllowWhenReadOnly: false)]);
            context.Request.Headers[TellmaHeaders.Activity] = "background";

            Assert.False(RequestInputs.Of(context).SuppressActivityStamp);
        }

        /// <summary>A request to an endpoint carrying the metadata.</summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="metadata">The endpoint's metadata.</param>
        /// <returns>The request's context.</returns>
        private static DefaultHttpContext Request(string method, object[] metadata)
        {
            DefaultHttpContext context = new() { RequestServices = Services };
            context.Request.Method = method;
            context.Request.Path = "/api/things";
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "things"));
            return context;
        }
    }
}
