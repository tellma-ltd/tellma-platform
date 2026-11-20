// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.2: the partitions of the rate and concurrency policies. Every key is derived from
    // what routing and authentication established, never from a header a caller could vary to land
    // in a fresh bucket, and every variant spelling of one tenant or one address maps to one key.

    /// <summary>The report lane a request takes; <see cref="None" /> off the report policy.</summary>
    internal enum RateLane
    {
        /// <summary>A policy without lanes.</summary>
        None,

        /// <summary>The lane of a report the user is looking at: every request without a low priority.</summary>
        Interactive,

        /// <summary>The lane of a low-priority run, such as a dashboard tile's.</summary>
        Batch,
    }

    /// <summary>One partition of a policy: its key and, on the report policy, its lane.</summary>
    /// <param name="Key">The partition's key.</param>
    /// <param name="Lane">The lane; <see cref="RateLane.None" /> off the report policy.</param>
    internal readonly record struct RatePartition(string Key, RateLane Lane);

    /// <summary>Derives the partition keys of the rate and concurrency policies from a request.</summary>
    internal static class RatePartitionKeys
    {
        /// <summary>The key of a value the request does not establish: one shared bucket, never one per caller.</summary>
        public const string Unknown = "-";

        /// <summary>The value of <c>Tellma-Priority</c> that takes the batch lane.</summary>
        public const string LowPriority = "low";

        /// <summary>The tag value of the interactive lane.</summary>
        public const string InteractiveLane = "interactive";

        /// <summary>The tag value of the batch lane.</summary>
        public const string BatchLane = "batch";

        /// <summary>The bits of an IPv6 address one client is assumed to hold: a /64, the smallest prefix a network assigns.</summary>
        private const int Ipv6PrefixBytes = 8;

        /// <summary>
        ///     The tenant a request's route names, as its id in digits alone, so <c>01</c> and <c>1</c>
        ///     share a bucket. A segment the tenant middleware refuses as no tenant (a sign, white space)
        ///     shares one bucket with every other such segment.
        /// </summary>
        /// <param name="context">The request, past routing.</param>
        /// <returns>The key.</returns>
        public static string Tenant(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.GetRouteValue(TellmaEndpointAudit.TenantIdParameter) is { } value
                && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                && id > 0
                ? id.ToString(CultureInfo.InvariantCulture)
                : Unknown;
        }

        /// <summary>
        ///     The caller a request authenticated: a person by their subject, a service account by its
        ///     client, each in its own namespace so neither can take the other's bucket.
        /// </summary>
        /// <param name="user">The request's principal, past authentication.</param>
        /// <returns>The key.</returns>
        public static string Subject(ClaimsPrincipal user)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (user.Identity?.IsAuthenticated != true)
            {
                return Unknown;
            }

            if (NonEmpty(user.FindFirst(TellmaClaimTypes.Subject)?.Value) is string subject)
            {
                return "u:" + subject;
            }

            string? client = NonEmpty(user.FindFirst(TellmaClaimTypes.AuthorizedParty)?.Value) ?? NonEmpty(user.FindFirst(TellmaClaimTypes.ClientId)?.Value);
            return client is null ? Unknown : "c:" + client;
        }

        /// <summary>The caller within the tenant: <c>{tenantId}:{subject}</c>.</summary>
        /// <param name="context">The request, past routing and authentication.</param>
        /// <returns>The key.</returns>
        public static string TenantSubject(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The tenant id is digits alone or the unknown key, so the first ':' always ends it.
            return Tenant(context) + ":" + Subject(context.User);
        }

        /// <summary>
        ///     The client's address, as the forwarded-headers stage established it. An IPv4 address
        ///     mapped into IPv6 is its IPv4 form, and an IPv6 address is its /64 prefix: one client
        ///     holds a whole /64, so a per-address bucket would be no bound at all.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>The key.</returns>
        public static string ClientAddress(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IPAddress? address = context.Connection.RemoteIpAddress;
            if (address is null)
            {
                return Unknown;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return address.ToString();
            }

            // --- The /64 prefix: the host half zeroed, the scope dropped ---
            byte[] bytes = address.GetAddressBytes();
            Array.Clear(bytes, Ipv6PrefixBytes, bytes.Length - Ipv6PrefixBytes);
            return new IPAddress(bytes).ToString() + "/64";
        }

        /// <summary>
        ///     The report lane of a request: the batch lane when the endpoint honors the priority and
        ///     the request carries <c>Tellma-Priority: low</c> exactly once; any other value, or none,
        ///     is the interactive lane.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="honorsPriority">Whether the endpoint lets the priority choose the lane.</param>
        /// <returns>The lane.</returns>
        public static RateLane Lane(HttpContext context, bool honorsPriority)
        {
            ArgumentNullException.ThrowIfNull(context);
            return honorsPriority && IsLowPriority(context.Request.Headers[TellmaHeaders.Priority]) ? RateLane.Batch : RateLane.Interactive;
        }

        /// <summary>Whether a <c>Tellma-Priority</c> header asks for the batch lane: one value, <c>low</c>.</summary>
        /// <param name="values">The header's values.</param>
        /// <returns>True when it does.</returns>
        public static bool IsLowPriority(StringValues values)
        {
            return values.Count == 1 && string.Equals(values[0]?.Trim(' ', '\t'), LowPriority, StringComparison.Ordinal);
        }

        /// <summary>The tag value of a lane; null for a policy without lanes.</summary>
        /// <param name="lane">The lane.</param>
        /// <returns>The tag value, or null.</returns>
        public static string? TagOf(RateLane lane)
        {
            return lane switch
            {
                RateLane.Interactive => InteractiveLane,
                RateLane.Batch => BatchLane,
                RateLane.None => null,
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown rate lane."),
            };
        }

        /// <summary>A value, or null when it is empty.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The value, or null.</returns>
        private static string? NonEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
