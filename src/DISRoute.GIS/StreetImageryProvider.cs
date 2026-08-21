namespace DISRoute.GIS;
public enum ProviderHealth { NotConfigured,Available,Unavailable }
public sealed record StreetImageRequest(string Address,double? Latitude,double? Longitude,double? PreferredHeading=null,double? PreferredFov=null);
public sealed record StreetImageResult(bool Found,string? ImageUri,string? Attribution,string? Provider);
public interface IStreetImageryProvider
{
 string Name { get; }
 ProviderHealth Health { get; }
 ValueTask<StreetImageResult> TryGetAsync(StreetImageRequest request,CancellationToken cancellationToken=default);
}
