// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Data.Configurations
{
    /// <summary>Maps <see cref="IdentitySession" /> to the <c>Sessions</c> table.</summary>
    public sealed class IdentitySessionConfiguration : IEntityTypeConfiguration<IdentitySession>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<IdentitySession> builder)
        {
            builder.ToTable("Sessions");
            builder.HasKey(static session => session.Sid);
            builder.Property(static session => session.Sid).HasMaxLength(64);
            builder.Property(static session => session.UserId).HasMaxLength(450).IsRequired();
            builder.Property(static session => session.UserAgent).HasMaxLength(256);
            builder.Property(static session => session.IpAddress).HasMaxLength(64);

            // "Sign out everywhere" and the active-sessions page fan out per user.
            builder.HasIndex(static session => session.UserId);

            builder
                .HasOne<TellmaIdentityUser>()
                .WithMany()
                .HasForeignKey(static session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Maps <see cref="IdentitySessionClient" /> to the <c>SessionClients</c> table.</summary>
    public sealed class IdentitySessionClientConfiguration : IEntityTypeConfiguration<IdentitySessionClient>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<IdentitySessionClient> builder)
        {
            builder.ToTable("SessionClients");
            builder.HasKey(static client => new { client.Sid, client.ClientId });
            builder.Property(static client => client.Sid).HasMaxLength(64);
            builder.Property(static client => client.ClientId).HasMaxLength(100);
            builder.Property(static client => client.AuthorizationId).HasMaxLength(450);

            builder.HasIndex(static client => client.ClientId);

            builder
                .HasOne<IdentitySession>()
                .WithMany(static session => session.Clients)
                .HasForeignKey(static client => client.Sid)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Maps <see cref="SingleUseCode" /> to the <c>SingleUseCodes</c> table.</summary>
    public sealed class SingleUseCodeConfiguration : IEntityTypeConfiguration<SingleUseCode>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<SingleUseCode> builder)
        {
            builder.ToTable("SingleUseCodes");
            builder.HasKey(static code => code.Id);
            builder.Property(static code => code.Id).HasMaxLength(64);
            builder.Property(static code => code.UserId).HasMaxLength(450).IsRequired();
            builder.Property(static code => code.SecretHash).HasMaxLength(64).IsRequired();
            builder.Property(static code => code.FlowBinding).HasMaxLength(64);
            builder.Property(static code => code.ReturnUrl).HasMaxLength(2048);
            builder.Property(static code => code.CreatedByClientId).HasMaxLength(100);

            // Verification looks up the outstanding secret per user and purpose; expiry bounds
            // the scan and supports pruning.
            builder.HasIndex(static code => new { code.UserId, code.Purpose, code.ExpiresUtc });

            builder
                .HasOne<TellmaIdentityUser>()
                .WithMany()
                .HasForeignKey(static code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Maps <see cref="TemporaryAccessPass" /> to the <c>TemporaryAccessPasses</c> table.</summary>
    public sealed class TemporaryAccessPassConfiguration : IEntityTypeConfiguration<TemporaryAccessPass>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TemporaryAccessPass> builder)
        {
            builder.ToTable("TemporaryAccessPasses");
            builder.HasKey(static pass => pass.Id);
            builder.Property(static pass => pass.Id).HasMaxLength(64);
            builder.Property(static pass => pass.UserId).HasMaxLength(450).IsRequired();
            builder.Property(static pass => pass.SecretHash).HasMaxLength(64).IsRequired();
            builder.Property(static pass => pass.IssuedByClientId).HasMaxLength(100);

            builder.HasIndex(static pass => pass.UserId);

            builder
                .HasOne<TellmaIdentityUser>()
                .WithMany()
                .HasForeignKey(static pass => pass.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Maps <see cref="AuditEvent" /> to the append-only <c>AuditEvents</c> table.</summary>
    public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<AuditEvent> builder)
        {
            builder.ToTable("AuditEvents");
            builder.HasKey(static auditEvent => auditEvent.Id);
            builder.Property(static auditEvent => auditEvent.Action).HasMaxLength(100).IsRequired();
            builder.Property(static auditEvent => auditEvent.Subject).HasMaxLength(450);
            builder.Property(static auditEvent => auditEvent.ClientId).HasMaxLength(100);
            builder.Property(static auditEvent => auditEvent.Sid).HasMaxLength(64);
            builder.Property(static auditEvent => auditEvent.TraceId).HasMaxLength(64);
            builder.Property(static auditEvent => auditEvent.IpAddress).HasMaxLength(64);
            builder.Property(static auditEvent => auditEvent.Outcome).HasMaxLength(16);

            // Time-ranged queries and per-subject history; deliberately no foreign keys so audit
            // rows outlive everything they reference.
            builder.HasIndex(static auditEvent => auditEvent.WhenUtc);
            builder.HasIndex(static auditEvent => new { auditEvent.Subject, auditEvent.WhenUtc });
        }
    }

    /// <summary>Maps <see cref="RateLimitCounter" /> to the <c>RateLimitCounters</c> table.</summary>
    public sealed class RateLimitCounterConfiguration : IEntityTypeConfiguration<RateLimitCounter>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<RateLimitCounter> builder)
        {
            builder.ToTable("RateLimitCounters");
            builder.HasKey(static counter => new { counter.Key, counter.WindowStartUtc });
            builder.Property(static counter => counter.Key).HasMaxLength(200);
        }
    }

    /// <summary>Maps <see cref="SsoTicket" /> to the <c>SsoTickets</c> table.</summary>
    public sealed class SsoTicketConfiguration : IEntityTypeConfiguration<SsoTicket>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<SsoTicket> builder)
        {
            builder.ToTable("SsoTickets");
            builder.HasKey(static ticket => ticket.Key);
            builder.Property(static ticket => ticket.Key).HasMaxLength(64);
            builder.Property(static ticket => ticket.UserId).HasMaxLength(450);

            // Per-user termination and expiry pruning.
            builder.HasIndex(static ticket => ticket.UserId);
            builder.HasIndex(static ticket => ticket.ExpiresUtc);
        }
    }
}
