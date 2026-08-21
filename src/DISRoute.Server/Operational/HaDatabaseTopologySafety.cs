namespace DISRoute.Server.Operational;

public sealed record HaDatabaseTopologyPolicy(
    string DatabaseTopology,
    string DatabaseFencingMode,
    bool RequiresExternalWitness,
    bool RequiresExternalDatabaseFencing);

/// <summary>
/// Defines the safety contract between DISRoute application fencing and the PostgreSQL topology.
/// A shared writable endpoint may use the database session advisory lock because every application
/// contender sees the same lock namespace. Once physical failover can create independently writable
/// PostgreSQL timelines/endpoints, that assumption is no longer true: application authority must be
/// fenced by an independent witness and the database layer must provide its own external STONITH /
/// quorum mechanism. DISRoute deliberately refuses to pretend application fencing replaces it.
/// </summary>
public static class HaDatabaseTopologySafety
{
    public const string SharedPrimary = "SharedPrimary";
    public const string PhysicalFailover = "PhysicalFailover";
    public const string NoExternalFencing = "None";
    public const string ExternalFencing = "External";

    public static HaDatabaseTopologyPolicy Resolve(IConfiguration configuration, bool externalWitnessConfigured)
    {
        var topology = NormalizeTopology(configuration["HighAvailability:DatabaseTopology"]);
        var fencing = NormalizeFencingMode(configuration["HighAvailability:DatabaseFencingMode"]);

        if (topology.Equals(PhysicalFailover, StringComparison.Ordinal))
        {
            if (!externalWitnessConfigured)
            {
                throw new InvalidOperationException(
                    "HighAvailability:DatabaseTopology=PhysicalFailover requires an independent HighAvailability:WitnessConnectionString. " +
                    "A PostgreSQL advisory lock on either failover member is not a shared fence after timelines diverge.");
            }

            if (!fencing.Equals(ExternalFencing, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "HighAvailability:DatabaseTopology=PhysicalFailover requires HighAvailability:DatabaseFencingMode=External. " +
                    "The PostgreSQL HA layer must provide external STONITH/quorum fencing before a replica is promoted.");
            }
        }

        return new HaDatabaseTopologyPolicy(
            topology,
            fencing,
            topology.Equals(PhysicalFailover, StringComparison.Ordinal),
            topology.Equals(PhysicalFailover, StringComparison.Ordinal));
    }

    public static string NormalizeTopology(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "shared" or "sharedprimary" or "shared-primary" or "singleendpoint" or "single-endpoint" => SharedPrimary,
        "physical" or "physicalfailover" or "physical-failover" or "streamingreplication" or "streaming-replication" => PhysicalFailover,
        _ => throw new InvalidOperationException(
            $"Unsupported HighAvailability:DatabaseTopology '{value}'. Supported values are {SharedPrimary} and {PhysicalFailover}.")
    };

    public static string NormalizeFencingMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "none" => NoExternalFencing,
        "external" or "stonith" or "quorum" or "externalstonith" or "external-stonith" => ExternalFencing,
        _ => throw new InvalidOperationException(
            $"Unsupported HighAvailability:DatabaseFencingMode '{value}'. Supported values are {NoExternalFencing} and {ExternalFencing}.")
    };
}
