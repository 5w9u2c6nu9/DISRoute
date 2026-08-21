using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed record MfaCredentialState(string UserId, string EncryptedSecret, bool Pending, IReadOnlyList<string> RecoveryCodeHashes, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed partial class FeatureStore
{
    private readonly Dictionary<string, MfaCredentialState> _mfaCredentials = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MfaCredentialState> MfaCredentials
    {
        get { lock (_gate) return _mfaCredentials.Values.ToArray(); }
    }

    public MfaCredentialState? GetMfaCredential(string userId)
    {
        lock (_gate) return _mfaCredentials.TryGetValue(userId, out var state) ? state : null;
    }

    public MfaCredentialState BeginMfaEnrollment(string userId, string encryptedSecret, string actor)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var item = new MfaCredentialState(userId, encryptedSecret, true, [], now, now);
            _mfaCredentials[userId] = item;
            if (!_users.ContainsKey(userId))
                _users[userId] = new UserSecurityDto(userId, userId, OperationalStore.DefaultAgencyId, [], false, false, false, "Active", now);
            AuditMutation(_users[userId].AgencyId, actor, "TotpEnrollmentStarted", "User", userId, "TOTP enrollment challenge issued");
            return item;
        }
    }

    public MfaCredentialState ConfirmMfaEnrollment(string userId, IReadOnlyList<string> recoveryCodeHashes, string actor)
    {
        lock (_gate)
        {
            if (!_mfaCredentials.TryGetValue(userId, out var current)) throw new KeyNotFoundException("TOTP enrollment not found.");
            var now = DateTimeOffset.UtcNow;
            var updated = current with { Pending = false, RecoveryCodeHashes = recoveryCodeHashes, UpdatedAtUtc = now };
            _mfaCredentials[userId] = updated;
            var user = _users.TryGetValue(userId, out var existing) ? existing : new UserSecurityDto(userId, userId, OperationalStore.DefaultAgencyId, [], false, false, false, "Active", now);
            _users[userId] = user with { TotpEnrolled = true, UpdatedAtUtc = now };
            AuditMutation(_users[userId].AgencyId, actor, "TotpEnrolled", "User", userId, "TOTP MFA enabled; recovery codes issued");
            return updated;
        }
    }

    public bool ConsumeRecoveryCode(string userId, string recoveryHash, string actor)
    {
        lock (_gate)
        {
            if (!_mfaCredentials.TryGetValue(userId, out var current) || current.Pending) return false;
            var hashes = current.RecoveryCodeHashes.ToList();
            var index = hashes.FindIndex(x => string.Equals(x, recoveryHash, StringComparison.Ordinal));
            if (index < 0) return false;
            hashes.RemoveAt(index);
            _mfaCredentials[userId] = current with { RecoveryCodeHashes = hashes, UpdatedAtUtc = DateTimeOffset.UtcNow };
            var agencyId = _users.TryGetValue(userId, out var user) ? user.AgencyId : OperationalStore.DefaultAgencyId;
            AuditMutation(agencyId, actor, "RecoveryCodeUsed", "User", userId, $"Remaining={hashes.Count}");
            return true;
        }
    }

    public void DisableTotp(string userId, string actor)
    {
        lock (_gate)
        {
            _mfaCredentials.Remove(userId);
            if (_users.TryGetValue(userId, out var user))
            {
                _users[userId] = user with { TotpEnrolled = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
                AuditMutation(user.AgencyId, actor, "TotpDisabled", "User", userId, "TOTP MFA disabled");
            }
        }
    }
}
