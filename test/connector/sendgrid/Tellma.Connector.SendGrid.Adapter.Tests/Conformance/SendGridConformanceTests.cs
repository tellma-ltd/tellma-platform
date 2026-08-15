// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Connector.SendGrid.Adapter.Tests.Infrastructure;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.SendGrid.Adapter.Tests.Conformance
{
    /// <summary>The SendGrid transport answering the shared <see cref="Core.Abstractions.Email.IEmailSender" /> contract.</summary>
    public class SendGridConformanceTests : EmailSenderConformanceTests
    {
        /// <inheritdoc />
        protected override ValueTask<IEmailSenderHarness> CreateHarnessAsync()
        {
            return ValueTask.FromResult<IEmailSenderHarness>(new SendGridSenderHarness());
        }
    }
}
