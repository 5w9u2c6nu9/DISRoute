namespace DISRoute.Video;
public sealed record CameraConfiguration(Guid Id,Guid StationId,string Name,string Host,int OnvifPort,bool UseTls,bool Enabled,string? LogicalSlot,int DisplayOrder,string? ManualRtspUrl);
public interface IOnvifCameraService
{
 ValueTask<OnvifProbeResult> ProbeAsync(string host,int port,bool useTls,string credentialReference,CancellationToken cancellationToken=default);
}
public sealed record OnvifProbeResult(bool Success,string? Manufacturer,string? Model,string? Firmware,IReadOnlyList<OnvifMediaProfile> Profiles,string? Error);
public sealed record OnvifMediaProfile(string Token,string Name,string? Resolution,string? Encoding);
