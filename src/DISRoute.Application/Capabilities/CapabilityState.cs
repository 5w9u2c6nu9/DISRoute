namespace DISRoute.Application.Capabilities;
public enum CapabilityState { NotConfigured, Available, Unavailable }
public sealed record CapabilityStatus(string Key,CapabilityState State,string? Detail=null);
public static class CapabilityKeys
{
 public const string Mapping="gis.mapping"; public const string StreetImagery="gis.street-imagery"; public const string Hydrants="gis.hydrants";
 public const string ApparatusGps="gis.apparatus-gps"; public const string Cameras="video.cameras"; public const string Onvif="video.onvif";
 public const string Weather="integration.weather"; public const string Hospitals="integration.hospitals"; public const string Preplans="preplans";
}
