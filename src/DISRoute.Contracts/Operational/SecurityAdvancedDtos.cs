namespace DISRoute.Contracts.Operational;

public sealed record TotpEnrollmentStartDto(string UserId, string SecretBase32, string OtpauthUri, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmTotpEnrollmentRequest(string Code, string Actor);
public sealed record VerifyTotpRequest(string Code);
public sealed record TotpVerificationResultDto(bool Success, bool UsedRecoveryCode, DateTimeOffset VerifiedAtUtc);
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);
public sealed record DisableTotpRequest(string Actor);

public sealed record IssueOfflineAuthorizationLeaseRequest(Guid AgencyId, string UserId, string DeviceId, IReadOnlyList<string> Permissions, int ValidMinutes);
public sealed record OfflineAuthorizationLeaseDto(string Token, string UserId, string DeviceId, Guid AgencyId, IReadOnlyList<string> Permissions, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record VerifyOfflineAuthorizationLeaseRequest(string Token, string DeviceId);
public sealed record OfflineAuthorizationLeaseValidationDto(bool Valid, string? UserId, Guid? AgencyId, IReadOnlyList<string> Permissions, DateTimeOffset? ExpiresAtUtc, string? Error);

public sealed record AuditIntegrityRecordDto(Guid AuditRecordId, long Sequence, string PreviousHash, string Hash, DateTimeOffset SealedAtUtc);
public sealed record AuditIntegrityValidationDto(bool Valid, int RecordsChecked, string? Error);
