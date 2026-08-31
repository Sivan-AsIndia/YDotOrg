using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// Sessions, tokens, MFA, devices and sign-in history.
///
/// ONE PATTERN RUNS THROUGH EVERY TABLE HERE: the secret is stored as a HASH and the hash
/// column carries a unique index. That combination is what makes a lookup both safe and fast —
/// the caller hashes what it was given and does an index seek, and a stolen database yields
/// nothing usable because the plaintext was never written.
/// </summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("iam_user_sessions");

        builder.HasKey(session => session.Id);

        builder.HasIndex(session => session.SessionTokenHash)
            .HasDatabaseName("ix_iam_user_sessions_token_hash")
            .IsUnique();

        // Filtered to the live rows. The security screen only ever lists active sessions, and
        // a closed session from last year should not sit in that index.
        builder.HasIndex(session => new { session.UserId, session.ExpiresAtUtc })
            .HasDatabaseName("ix_iam_user_sessions_user_active")
            .HasFilter("revoked_at_utc IS NULL");

        builder.Property(session => session.SessionTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(session => session.DeviceName).HasMaxLength(160);
        builder.Property(session => session.DeviceIdentifier).HasMaxLength(200);
        builder.Property(session => session.UserAgent).HasMaxLength(400);
        builder.Property(session => session.Browser).HasMaxLength(80);
        builder.Property(session => session.OperatingSystem).HasMaxLength(80);
        builder.Property(session => session.IpAddress).HasMaxLength(64);
        builder.Property(session => session.Location).HasMaxLength(160);
        builder.Property(session => session.RevocationReason).HasMaxLength(300);

        builder.Property(session => session.ClientType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(session => session.AccessScope).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(session => session.Version).IsConcurrencyToken();

        builder.HasOne(session => session.User)
            .WithMany(user => user.Sessions)
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Refresh tokens, rotated on every use.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("iam_refresh_tokens");

        builder.HasKey(token => token.Id);

        builder.HasIndex(token => token.TokenHash)
            .HasDatabaseName("ix_iam_refresh_tokens_hash")
            .IsUnique();

        // The reuse check walks the chain by session, so this index is what makes detecting
        // a replayed token cheap enough to do on every refresh.
        builder.HasIndex(token => token.SessionId)
            .HasDatabaseName("ix_iam_refresh_tokens_session");

        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.RevocationReason).HasMaxLength(300);
        builder.Property(token => token.CreatedFromIpAddress).HasMaxLength(64);
        builder.Property(token => token.CreatedByUserAgent).HasMaxLength(400);
        builder.Property(token => token.Version).IsConcurrencyToken();

        builder.HasOne(token => token.Session)
            .WithMany(session => session.RefreshTokens)
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction: the session cascade above already removes these, and a second cascade
        // path from iam_users would be an ambiguous multiple-cascade that PostgreSQL rejects.
        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>Invitations. The Tenant-specific front door for a new account.</summary>
public sealed class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable("iam_user_invitations");

        builder.HasKey(invitation => invitation.Id);

        // Looked up ACROSS Organisations by token, because the person clicking has no session
        // and therefore no ambient Organisation. The row itself names the Organisation.
        builder.HasIndex(invitation => invitation.TokenHash)
            .HasDatabaseName("ix_iam_user_invitations_token_hash")
            .IsUnique();

        builder.HasIndex(invitation => invitation.Reference)
            .HasDatabaseName("ix_iam_user_invitations_reference")
            .IsUnique();

        // One outstanding invitation per user. Filtered, so an accepted one does not block a
        // later re-invitation after a withdrawal.
        builder.HasIndex(invitation => invitation.UserId)
            .HasDatabaseName("ix_iam_user_invitations_pending")
            .IsUnique()
            .HasFilter("status IN ('Pending', 'Resent')");

        builder.Property(invitation => invitation.Email).HasMaxLength(320).IsRequired();
        builder.Property(invitation => invitation.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(invitation => invitation.Reference).HasMaxLength(40).IsRequired();
        builder.Property(invitation => invitation.AcceptedFromIpAddress).HasMaxLength(64);
        builder.Property(invitation => invitation.AcceptedUserAgent).HasMaxLength(400);
        builder.Property(invitation => invitation.RevocationReason).HasMaxLength(500);
        builder.Property(invitation => invitation.InvitationHostName).HasMaxLength(253);
        builder.Property(invitation => invitation.Message).HasMaxLength(1000);

        builder.Property(invitation => invitation.InvitationType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(invitation => invitation.Version).IsConcurrencyToken();

        builder.HasOne(invitation => invitation.User)
            .WithMany()
            .HasForeignKey(invitation => invitation.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(invitation => invitation.Tenant)
            .WithMany()
            .HasForeignKey(invitation => invitation.TenantId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(invitation => invitation.InitialRole)
            .WithMany()
            .HasForeignKey(invitation => invitation.InitialRoleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Password reset and e-mail confirmation tokens.</summary>
public sealed class RecoveryTokenConfiguration : IEntityTypeConfiguration<RecoveryToken>
{
    public void Configure(EntityTypeBuilder<RecoveryToken> builder)
    {
        builder.ToTable("iam_recovery_tokens");

        builder.HasKey(token => token.Id);

        builder.HasIndex(token => token.TokenHash)
            .HasDatabaseName("ix_iam_recovery_tokens_hash")
            .IsUnique();

        builder.HasIndex(token => new { token.UserId, token.Purpose })
            .HasDatabaseName("ix_iam_recovery_tokens_user_purpose");

        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.TargetValue).HasMaxLength(320);
        builder.Property(token => token.ConsumedFromIpAddress).HasMaxLength(64);
        builder.Property(token => token.InvalidationReason).HasMaxLength(300);
        builder.Property(token => token.RequestedFromIpAddress).HasMaxLength(64);
        builder.Property(token => token.RequestedUserAgent).HasMaxLength(400);

        builder.Property(token => token.Purpose).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(token => token.Version).IsConcurrencyToken();

        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Enrolled second factors.</summary>
public sealed class MfaMethodConfiguration : IEntityTypeConfiguration<MfaMethod>
{
    public void Configure(EntityTypeBuilder<MfaMethod> builder)
    {
        builder.ToTable("iam_mfa_methods");

        builder.HasKey(method => method.Id);

        builder.HasIndex(method => new { method.UserId, method.MethodType })
            .HasDatabaseName("ix_iam_mfa_methods_user_type");

        // One primary method per user, so the sign-in path never has to choose.
        builder.HasIndex(method => method.UserId)
            .HasDatabaseName("ix_iam_mfa_methods_primary")
            .IsUnique()
            .HasFilter("is_primary = TRUE AND status = 'Active'");

        builder.Property(method => method.Label).HasMaxLength(80);
        builder.Property(method => method.MaskedDestination).HasMaxLength(120);
        builder.Property(method => method.SecretHash).HasMaxLength(500);
        builder.Property(method => method.RevocationReason).HasMaxLength(300);

        builder.Property(method => method.MethodType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(method => method.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(method => method.Version).IsConcurrencyToken();

        builder.HasOne(method => method.User)
            .WithMany(user => user.MfaMethods)
            .HasForeignKey(method => method.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Outstanding one-time codes.</summary>
public sealed class MfaChallengeConfiguration : IEntityTypeConfiguration<MfaChallenge>
{
    public void Configure(EntityTypeBuilder<MfaChallenge> builder)
    {
        builder.ToTable("iam_mfa_challenges");

        builder.HasKey(challenge => challenge.Id);

        // The client echoes this opaque handle back, so it is the lookup key. It is NOT the
        // code - the code is hashed separately - which is why it can be indexed and returned.
        builder.HasIndex(challenge => challenge.ChallengeToken)
            .HasDatabaseName("ix_iam_mfa_challenges_token")
            .IsUnique();

        builder.HasIndex(challenge => new { challenge.UserId, challenge.Purpose })
            .HasDatabaseName("ix_iam_mfa_challenges_user_purpose")
            .HasFilter("is_consumed = FALSE");

        builder.Property(challenge => challenge.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(challenge => challenge.ChallengeToken).HasMaxLength(128).IsRequired();
        builder.Property(challenge => challenge.MaskedDestination).HasMaxLength(120);
        builder.Property(challenge => challenge.IpAddress).HasMaxLength(64);
        builder.Property(challenge => challenge.UserAgent).HasMaxLength(400);

        builder.Property(challenge => challenge.MethodType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(challenge => challenge.Purpose).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(challenge => challenge.Version).IsConcurrencyToken();

        builder.HasOne(challenge => challenge.User)
            .WithMany()
            .HasForeignKey(challenge => challenge.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(challenge => challenge.MfaMethod)
            .WithMany()
            .HasForeignKey(challenge => challenge.MfaMethodId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Single-use backup codes.</summary>
public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("iam_recovery_codes");

        builder.HasKey(code => code.Id);

        // Not unique: two users could theoretically hash to the same value, and the lookup is
        // always scoped to one user anyway.
        builder.HasIndex(code => new { code.UserId, code.CodeHash })
            .HasDatabaseName("ix_iam_recovery_codes_user_hash");

        builder.Property(code => code.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(code => code.RedeemedFromIpAddress).HasMaxLength(64);
        builder.Property(code => code.Version).IsConcurrencyToken();

        builder.HasOne(code => code.User)
            .WithMany()
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Devices the person asked the platform to remember.</summary>
public sealed class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("iam_trusted_devices");

        builder.HasKey(device => device.Id);

        builder.HasIndex(device => device.DeviceTokenHash)
            .HasDatabaseName("ix_iam_trusted_devices_token_hash")
            .IsUnique();

        builder.HasIndex(device => device.UserId)
            .HasDatabaseName("ix_iam_trusted_devices_user")
            .HasFilter("revoked_at_utc IS NULL");

        builder.Property(device => device.DeviceTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(device => device.DeviceName).HasMaxLength(160);
        builder.Property(device => device.DeviceIdentifier).HasMaxLength(200);
        builder.Property(device => device.UserAgent).HasMaxLength(400);
        builder.Property(device => device.Browser).HasMaxLength(80);
        builder.Property(device => device.OperatingSystem).HasMaxLength(80);
        builder.Property(device => device.IpAddress).HasMaxLength(64);
        builder.Property(device => device.Location).HasMaxLength(160);
        builder.Property(device => device.RevocationReason).HasMaxLength(300);

        builder.Property(device => device.ClientType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(device => device.Version).IsConcurrencyToken();

        builder.HasOne(device => device.User)
            .WithMany()
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Every sign-in attempt, successful or not. Append-only, and the table that answers "who has
/// been trying to get into my account?".
/// </summary>
public sealed class SignInAttemptConfiguration : IEntityTypeConfiguration<SignInAttempt>
{
    public void Configure(EntityTypeBuilder<SignInAttempt> builder)
    {
        builder.ToTable("iam_sign_in_attempts");

        builder.HasKey(attempt => attempt.Id);

        // Descending on time, because every read of this table is "most recent first".
        builder.HasIndex(attempt => new { attempt.UserId, attempt.AttemptedAtUtc })
            .HasDatabaseName("ix_iam_sign_in_attempts_user_time")
            .IsDescending(false, true);

        // Supports the per-IP rate limit, which runs BEFORE any account lookup on every
        // single sign-in - so it has to be an index seek.
        builder.HasIndex(attempt => new { attempt.IpAddress, attempt.AttemptedAtUtc })
            .HasDatabaseName("ix_iam_sign_in_attempts_ip_time")
            .IsDescending(false, true);

        builder.HasIndex(attempt => new { attempt.TenantId, attempt.AttemptedAtUtc })
            .HasDatabaseName("ix_iam_sign_in_attempts_tenant_time")
            .IsDescending(false, true);

        builder.Property(attempt => attempt.AttemptedIdentifier).HasMaxLength(320).IsRequired();
        builder.Property(attempt => attempt.HostName).HasMaxLength(253);
        builder.Property(attempt => attempt.FailureDetail).HasMaxLength(500);
        builder.Property(attempt => attempt.IpAddress).HasMaxLength(64);
        builder.Property(attempt => attempt.UserAgent).HasMaxLength(400);
        builder.Property(attempt => attempt.Browser).HasMaxLength(80);
        builder.Property(attempt => attempt.OperatingSystem).HasMaxLength(80);
        builder.Property(attempt => attempt.DeviceIdentifier).HasMaxLength(200);
        builder.Property(attempt => attempt.Location).HasMaxLength(160);
        builder.Property(attempt => attempt.CorrelationId).HasMaxLength(80);

        builder.Property(attempt => attempt.Outcome).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(attempt => attempt.ClientType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(attempt => attempt.Version).IsConcurrencyToken();

        // SetNull rather than Cascade: a deleted user must not take the record of attempts
        // against their account with them. That history is exactly what an investigation needs.
        builder.HasOne(attempt => attempt.User)
            .WithMany()
            .HasForeignKey(attempt => attempt.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Half-finished sensitive actions, parked during a step-up.</summary>
public sealed class ProtectedActionDraftConfiguration : IEntityTypeConfiguration<ProtectedActionDraft>
{
    public void Configure(EntityTypeBuilder<ProtectedActionDraft> builder)
    {
        builder.ToTable("iam_protected_action_drafts");

        builder.HasKey(draft => draft.Id);

        builder.HasIndex(draft => draft.DraftToken)
            .HasDatabaseName("ix_iam_protected_action_drafts_token")
            .IsUnique();

        builder.Property(draft => draft.ActionCode).HasMaxLength(100).IsRequired();
        builder.Property(draft => draft.Payload).HasMaxLength(8000).IsRequired();
        builder.Property(draft => draft.DraftToken).HasMaxLength(128).IsRequired();
        builder.Property(draft => draft.Version).IsConcurrencyToken();
    }
}
