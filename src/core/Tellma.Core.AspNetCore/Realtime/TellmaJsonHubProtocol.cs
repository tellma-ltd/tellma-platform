// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The hub's one protocol: the framework's JSON protocol under its own name, with the
    ///     platform's JSON rules for payloads, counting every client invocation the hub refuses.
    /// </summary>
    /// <remarks>
    ///     The hub declares no method a client can invoke, so the framework fails to bind every
    ///     invocation and answers it with a <see cref="HubException" />; this protocol sees each such
    ///     failure as it parses it and counts it. Everything else is the inner protocol's.
    /// </remarks>
    /// <param name="options">The JSON protocol's options, which carry the platform's serializer options.</param>
    /// <param name="metrics">The realtime instruments.</param>
    internal sealed class TellmaJsonHubProtocol(IOptions<JsonHubProtocolOptions> options, RealtimeMetrics metrics) : IHubProtocol
    {
        /// <summary>The protocol's name, the JSON protocol's: clients ask for it by this name.</summary>
        public const string ProtocolName = "json";

        private readonly JsonHubProtocol _inner = new(options);

        /// <inheritdoc />
        public string Name => _inner.Name;

        /// <inheritdoc />
        public int Version => _inner.Version;

        /// <inheritdoc />
        public TransferFormat TransferFormat => _inner.TransferFormat;

        /// <inheritdoc />
        public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, [NotNullWhen(true)] out HubMessage? message)
        {
            if (!_inner.TryParseMessage(ref input, binder, out message))
            {
                return false;
            }

            // An invocation of a method the hub does not declare parses as a binding failure, which
            // the framework answers with a HubException; each one is a refused invocation.
            if (message is InvocationBindingFailureMessage or StreamBindingFailureMessage)
            {
                metrics.RecordInvocationRefused();
            }

            return true;
        }

        /// <inheritdoc />
        public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
        {
            _inner.WriteMessage(message, output);
        }

        /// <inheritdoc />
        public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
        {
            return _inner.GetMessageBytes(message);
        }

        /// <inheritdoc />
        public bool IsVersionSupported(int version)
        {
            return _inner.IsVersionSupported(version);
        }
    }
}
