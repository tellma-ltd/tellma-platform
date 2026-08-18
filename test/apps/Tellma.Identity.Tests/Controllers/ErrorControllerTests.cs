// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Diagnostics;
using Tellma.Identity.Controllers;
using Tellma.Identity.Controllers.ViewModels;

namespace Tellma.Identity.Tests.Controllers
{
    /// <summary>
    ///     What the error page is given to show. Two things decide it: whether the failure was the
    ///     server's own, and the identifier that ties the response to its log entries.
    /// </summary>
    public sealed class ErrorControllerTests
    {
        [Fact]
        public void A_server_fault_is_named_as_one_and_describes_nothing()
        {
            ErrorViewModel model = Render(serverFault: true);

            // Nothing a fault of ours could say about itself is both safe and useful, so the page
            // is told to say whose fault it is instead of what happened.
            Assert.True(model.IsServerFault);
            Assert.Null(model.Error);
            Assert.Null(model.ErrorDescription);
        }

        [Fact]
        public void A_caller_error_is_not_a_server_fault()
        {
            Assert.False(Render(serverFault: false).IsServerFault);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_failure_carries_a_reference(bool serverFault)
        {
            // The only thing connecting what a user saw to what an operator can search for. It has
            // to be there whoever was at fault: someone reporting a refused request needs it as
            // much as someone reporting a crash.
            Assert.False(string.IsNullOrWhiteSpace(Render(serverFault).Reference));
        }

        [Fact]
        public void The_reference_is_the_trace_id_when_the_request_is_traced()
        {
            // Same id a collector indexed the request under, so the reference read off the screen
            // finds the request rather than merely accompanying it.
            using Activity activity = new(nameof(The_reference_is_the_trace_id_when_the_request_is_traced));
            activity.Start();

            Assert.Equal(activity.Id, Render(serverFault: true).Reference);
        }

        /// <summary>Runs the controller and returns the model it handed the view.</summary>
        /// <param name="serverFault">Whether an exception handler re-executed the request.</param>
        /// <returns>The error page's display data.</returns>
        private static ErrorViewModel Render(bool serverFault)
        {
            DefaultHttpContext context = new() { TraceIdentifier = "0HN7C3TEST:00000001" };
            if (serverFault)
            {
                // The feature's presence is the whole signal — it is what an exception handler
                // leaves behind and nothing else sets.
                context.Features.Set<IExceptionHandlerFeature>(
                    new ExceptionHandlerFeature { Error = new InvalidOperationException("boom") });
            }

            ErrorController controller = new()
            {
                ControllerContext = new ControllerContext { HttpContext = context },
                TempData = new TempDataDictionary(context, new NullTempDataProvider()),
            };

            ViewResult view = Assert.IsType<ViewResult>(controller.Error());
            return Assert.IsType<ErrorViewModel>(view.Model);
        }

        /// <summary>A temp-data provider that keeps nothing, for a controller that stores nothing.</summary>
        private sealed class NullTempDataProvider : ITempDataProvider
        {
            /// <inheritdoc />
            public IDictionary<string, object?> LoadTempData(HttpContext context)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            /// <inheritdoc />
            public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
            {
            }
        }
    }
}
