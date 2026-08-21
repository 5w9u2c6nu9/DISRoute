using System.Security.Cryptography;
using System.Text;
using DISRoute.Contracts.Operational;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace DISRoute.Server.Operational;

public sealed class MfaService
{
    private readonly FeatureStore _store;
    private readonly byte[] _masterKey;
    private readonly byte[] _leaseSigningKey;
    private readonly byte[] _sessionSigningKey;
    private readonly string _issuer;

    public MfaService(FeatureStore store, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _store = store;
        _issuer = configuration["Security:MfaIssuer"]?.Trim() ?? "DISRoute";
        _masterKey = LoadOrCreateKey(SecretReferenceResolver.ResolveOptional(configuration, "Security:MfaMasterKeyBase64"), configuration["Security:MfaKeyPath"], environment, "mfa.key");
        _leaseSigningKey = LoadOrCreateKey(SecretReferenceResolver.ResolveOptional(configuration, "Security:OfflineLeaseSigningKeyBase64"), configuration["Security:OfflineLeaseKeyPath"], environment, "offline-lease.key");
        _sessionSigningKey = LoadOrCreateKey(SecretReferenceResolver.ResolveOptional(configuration, "Security:MfaSessionSigningKeyBase64"), configuration["Security:MfaSessionKeyPath"], environment, "mfa-session.key");
    }

    public TotpEnrollmentStartDto BeginEnrollment(string userId, string displayName, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID is required.");
        var secret = RandomNumberGenerator.GetBytes(32);
        var encoded = Base32Encode(secret);
        _store.BeginMfaEnrollment(userId.Trim(), Protect(secret), actor);
        var label = Uri.EscapeDataString($"{_issuer}:{(string.IsNullOrWhiteSpace(displayName) ? userId : displayName)}");
        var issuer = Uri.EscapeDataString(_issuer);
        var uri = $"otpauth://totp/{label}?secret={encoded}&issuer={issuer}&algorithm=SHA256&digits=6&period=30";
        CryptographicOperations.ZeroMemory(secret);
        return new TotpEnrollmentStartDto(userId.Trim(), encoded, uri, DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public RecoveryCodesDto ConfirmEnrollment(string userId, ConfirmTotpEnrollmentRequest request)
    {
        var state = _store.GetMfaCredential(userId) ?? throw new KeyNotFoundException("TOTP enrollment not found.");
        if (!state.Pending) throw new InvalidOperationException("TOTP is already enrolled.");
        if (DateTimeOffset.UtcNow - state.CreatedAtUtc > TimeSpan.FromMinutes(10)) throw new InvalidOperationException("TOTP enrollment challenge expired. Start enrollment again.");
        var secret = Unprotect(state.EncryptedSecret);
        try
        {
            if (!VerifyTotp(secret, request.Code, DateTimeOffset.UtcNow)) throw new InvalidOperationException("TOTP code is invalid.");
            var codes = Enumerable.Range(0, 10).Select(_ => GenerateRecoveryCode()).ToArray();
            _store.ConfirmMfaEnrollment(userId, codes.Select(HashRecoveryCode).ToArray(), request.Actor);
            return new RecoveryCodesDto(codes);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public TotpVerificationResultDto Verify(string userId, VerifyTotpRequest request, string actor)
    {
        var state = _store.GetMfaCredential(userId);
        if (state is null || state.Pending) return new TotpVerificationResultDto(false, false, DateTimeOffset.UtcNow);
        var normalized = NormalizeRecoveryCode(request.Code);
        if (normalized.Length >= 12 && _store.ConsumeRecoveryCode(userId, HashRecoveryCode(normalized), actor))
            return new TotpVerificationResultDto(true, true, DateTimeOffset.UtcNow);
        var secret = Unprotect(state.EncryptedSecret);
        try { return new TotpVerificationResultDto(VerifyTotp(secret, request.Code, DateTimeOffset.UtcNow), false, DateTimeOffset.UtcNow); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }


    public MfaSessionDto IssueSession(string userId, int validMinutes = 30)
    {
        var state = _store.GetMfaCredential(userId);
        if (state is null || state.Pending) throw new InvalidOperationException("TOTP MFA is not enrolled.");
        var issued = DateTimeOffset.UtcNow;
        var expires = issued.AddMinutes(Math.Clamp(validMinutes, 5, 480));
        var payload = new MfaSessionPayload(1, userId.Trim(), issued.ToUnixTimeSeconds(), expires.ToUnixTimeSeconds(), Guid.NewGuid().ToString("N"));
        var body = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Base64UrlEncode(HMACSHA256.HashData(_sessionSigningKey, Encoding.ASCII.GetBytes(body)));
        return new MfaSessionDto(body + "." + signature, payload.UserId, issued, expires);
    }

    public MfaSessionValidationDto VerifySession(string token, string expectedUserId)
    {
        try
        {
            var parts = (token ?? string.Empty).Split('.', 2);
            if (parts.Length != 2) return new(false, null, null, "Malformed MFA session.");
            var expected = HMACSHA256.HashData(_sessionSigningKey, Encoding.ASCII.GetBytes(parts[0]));
            var provided = Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return new(false, null, null, "Invalid MFA session signature.");
            var payload = System.Text.Json.JsonSerializer.Deserialize<MfaSessionPayload>(Base64UrlDecode(parts[0]));
            if (payload is null || payload.Version != 1) return new(false, null, null, "Unsupported MFA session.");
            if (!payload.UserId.Equals(expectedUserId, StringComparison.OrdinalIgnoreCase)) return new(false, null, null, "MFA session user mismatch.");
            var expires = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresUnix);
            if (DateTimeOffset.UtcNow >= expires) return new(false, payload.UserId, expires, "MFA session expired.");
            var state = _store.GetMfaCredential(payload.UserId);
            if (state is null || state.Pending) return new(false, payload.UserId, expires, "MFA enrollment is no longer active.");
            return new(true, payload.UserId, expires, null);
        }
        catch (Exception ex) { return new(false, null, null, ex.Message); }
    }
    public void Disable(string userId, DisableTotpRequest request) => _store.DisableTotp(userId, request.Actor);

    public OfflineAuthorizationLeaseDto IssueLease(IssueOfflineAuthorizationLeaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.DeviceId)) throw new ArgumentException("User ID and device ID are required.");
        var device = _store.Devices.FirstOrDefault(x => x.DeviceId.Equals(request.DeviceId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Registered device not found.");
        if (!device.State.Equals("Active", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Device is not active.");
        var user = _store.Users.FirstOrDefault(x => x.UserId.Equals(request.UserId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("User not found.");
        if (!user.State.Equals("Active", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("User is not active.");
        if (user.AgencyId != request.AgencyId || device.AgencyId != request.AgencyId) throw new InvalidOperationException("User/device agency scope mismatch.");
        var allowed = DISRoute.Security.RolePermissionMap.ForRoles(user.Roles);
        var requested = request.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Any(x => !allowed.Contains(x))) throw new InvalidOperationException("Offline lease requested permissions outside the user's current role scope.");
        var minutes = Math.Clamp(request.ValidMinutes, 1, 60);
        var issued = DateTimeOffset.UtcNow;
        var expires = issued.AddMinutes(minutes);
        var payload = new LeasePayload(1, request.UserId.Trim(), request.DeviceId.Trim(), request.AgencyId, requested, issued.ToUnixTimeSeconds(), expires.ToUnixTimeSeconds(), Guid.NewGuid().ToString("N"));
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
        var body = Base64UrlEncode(json);
        var signature = Base64UrlEncode(HMACSHA256.HashData(_leaseSigningKey, Encoding.ASCII.GetBytes(body)));
        return new OfflineAuthorizationLeaseDto(body + "." + signature, payload.UserId, payload.DeviceId, payload.AgencyId, payload.Permissions, issued, expires);
    }

    public OfflineAuthorizationLeaseValidationDto VerifyLease(VerifyOfflineAuthorizationLeaseRequest request)
    {
        try
        {
            var parts = request.Token.Split('.', 2);
            if (parts.Length != 2) return InvalidLease("Malformed token.");
            var expected = HMACSHA256.HashData(_leaseSigningKey, Encoding.ASCII.GetBytes(parts[0]));
            var provided = Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return InvalidLease("Invalid signature.");
            var payload = System.Text.Json.JsonSerializer.Deserialize<LeasePayload>(Base64UrlDecode(parts[0])) ?? throw new InvalidOperationException("Invalid payload.");
            if (payload.Version != 1) return InvalidLease("Unsupported lease version.");
            if (!payload.DeviceId.Equals(request.DeviceId, StringComparison.OrdinalIgnoreCase)) return InvalidLease("Device mismatch.");
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= payload.ExpiresUnix) return InvalidLease("Lease expired.");
            var device = _store.Devices.FirstOrDefault(x => x.DeviceId.Equals(payload.DeviceId, StringComparison.OrdinalIgnoreCase));
            var user = _store.Users.FirstOrDefault(x => x.UserId.Equals(payload.UserId, StringComparison.OrdinalIgnoreCase));
            if (device is null || !device.State.Equals("Active", StringComparison.OrdinalIgnoreCase)) return InvalidLease("Device revoked or missing.");
            if (user is null || !user.State.Equals("Active", StringComparison.OrdinalIgnoreCase)) return InvalidLease("User revoked or missing.");
            if (device.AgencyId != payload.AgencyId || user.AgencyId != payload.AgencyId) return InvalidLease("Agency scope changed.");
            var currentPermissions = DISRoute.Security.RolePermissionMap.ForRoles(user.Roles);
            if (payload.Permissions.Any(x => !currentPermissions.Contains(x))) return InvalidLease("Authorization scope changed.");
            return new OfflineAuthorizationLeaseValidationDto(true, payload.UserId, payload.AgencyId, payload.Permissions, DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresUnix), null);
        }
        catch (Exception ex) { return InvalidLease(ex.Message); }
    }

    private static OfflineAuthorizationLeaseValidationDto InvalidLease(string error) => new(false, null, null, [], null, error);

    private string Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_masterKey, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes("DISRoute:MFA:v1"));
        var output = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        output[0] = 1; nonce.CopyTo(output, 1); tag.CopyTo(output, 13); ciphertext.CopyTo(output, 29);
        return Convert.ToBase64String(output);
    }

    private byte[] Unprotect(string protectedValue)
    {
        var input = Convert.FromBase64String(protectedValue);
        if (input.Length < 30 || input[0] != 1) throw new CryptographicException("Unsupported MFA secret format.");
        var plaintext = new byte[input.Length - 29];
        using var aes = new AesGcm(_masterKey, 16);
        aes.Decrypt(input.AsSpan(1, 12), input.AsSpan(29), input.AsSpan(13, 16), plaintext, Encoding.UTF8.GetBytes("DISRoute:MFA:v1"));
        return plaintext;
    }

    private static bool VerifyTotp(byte[] secret, string code, DateTimeOffset now)
    {
        var normalized = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length != 6) return false;
        var counter = now.ToUnixTimeSeconds() / 30;
        for (long drift = -1; drift <= 1; drift++)
            if (string.Equals(ComputeTotp(secret, counter + drift), normalized, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string ComputeTotp(byte[] secret, long counter)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, counter);
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(bytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GenerateRecoveryCode()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..12]}-{raw[12..16]}";
    }

    private static string NormalizeRecoveryCode(string value) => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string HashRecoveryCode(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(value))));

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0; var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; output.Append(alphabet[(buffer >> bits) & 31]); }
        }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - s.Length % 4) % 4), '=');
        return Convert.FromBase64String(s);
    }

    private static byte[] LoadOrCreateKey(string? configuredBase64, string? configuredPath, IWebHostEnvironment environment, string defaultName)
    {
        if (!string.IsNullOrWhiteSpace(configuredBase64))
        {
            var key = Convert.FromBase64String(configuredBase64);
            if (key.Length != 32) throw new InvalidOperationException($"Configured {defaultName} key must be 32 bytes.");
            return key;
        }
        var path = string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine(AppContext.BaseDirectory, "data", defaultName) : Path.GetFullPath(configuredPath);
        if (File.Exists(path))
        {
            var key = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (key.Length != 32) throw new InvalidOperationException($"Stored {defaultName} key must be 32 bytes.");
            return key;
        }
        if (!environment.IsDevelopment()) throw new InvalidOperationException($"Production requires a configured or pre-provisioned {defaultName} key.");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        var created = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(path, Convert.ToBase64String(created));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return created;
    }

    private sealed record MfaSessionPayload(int Version, string UserId, long IssuedUnix, long ExpiresUnix, string Nonce);
    private sealed record LeasePayload(int Version, string UserId, string DeviceId, Guid AgencyId, IReadOnlyList<string> Permissions, long IssuedUnix, long ExpiresUnix, string Nonce);
}
