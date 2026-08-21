namespace DISRoute.Integrations;
public enum IntegrationState { NotConfigured,Available,Unavailable }
public sealed record IntegrationDescriptor(string Key,string DisplayName,IntegrationState State,string? Provider,string? Detail);
