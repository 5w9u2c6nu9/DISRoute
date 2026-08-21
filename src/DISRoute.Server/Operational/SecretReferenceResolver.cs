using System.Runtime.InteropServices;

namespace DISRoute.Server.Operational;

/// <summary>
/// Resolves deployment secrets without placing secret material in source-controlled configuration.
/// Supported forms are secret://env/VARIABLE and secret://file/absolute/or/relative/path.
/// Relative secret files are resolved beneath Security:Secrets:Directory when configured.
/// </summary>
public static class SecretReferenceResolver
{
    private const string Prefix = "secret://";

    public static string? ResolveOptional(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return ResolveValue(configuration, value, key);
    }

    public static string ResolveRequired(IConfiguration configuration, string key)
    {
        return ResolveOptional(configuration, key)
            ?? throw new InvalidOperationException($"Required configuration value '{key}' is missing.");
    }

    public static string ResolveConnectionString(IConfiguration configuration, string name = "DISRoute")
    {
        var key = $"ConnectionStrings:{name}";
        var value = configuration.GetConnectionString(name)?.Trim();
        if (string.IsNullOrWhiteSpace(value) && name.Equals("DISRoute", StringComparison.OrdinalIgnoreCase))
        {
            key = "Persistence:ConnectionString";
            value = configuration[key]?.Trim();
        }
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required PostgreSQL connection string '{name}' is missing.");
        return ResolveValue(configuration, value, key);
    }

    public static bool IsSecretReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string ResolveValue(IConfiguration configuration, string value, string logicalName)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return trimmed;

        var spec = trimmed[Prefix.Length..];
        if (spec.StartsWith("env/", StringComparison.OrdinalIgnoreCase))
        {
            var variable = spec[4..].Trim();
            if (string.IsNullOrWhiteSpace(variable) || variable.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
                throw new InvalidOperationException($"Secret reference '{logicalName}' has an invalid environment-variable name.");
            var resolved = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(resolved))
                throw new InvalidOperationException($"Secret reference '{logicalName}' could not resolve environment variable '{variable}'.");
            return resolved.TrimEnd('\r', '\n');
        }

        if (spec.StartsWith("file/", StringComparison.OrdinalIgnoreCase))
        {
            var configuredPath = Uri.UnescapeDataString(spec[5..]).Trim();
            if (string.IsNullOrWhiteSpace(configuredPath))
                throw new InvalidOperationException($"Secret reference '{logicalName}' has an empty file path.");

            var root = configuration["Security:Secrets:Directory"]?.Trim();
            var path = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(root) ? AppContext.BaseDirectory : root, configuredPath));

            if (!string.IsNullOrWhiteSpace(root))
            {
                var fullRoot = Path.GetFullPath(root);
                var relative = Path.GetRelativePath(fullRoot, path);
                if (relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Secret file for '{logicalName}' escapes Security:Secrets:Directory.");
            }

            if (!File.Exists(path)) throw new InvalidOperationException($"Secret file for '{logicalName}' does not exist: {path}");
            ValidateUnixSecretPermissions(path, logicalName);
            var resolved = File.ReadAllText(path).TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(resolved)) throw new InvalidOperationException($"Secret file for '{logicalName}' is empty.");
            return resolved;
        }

        throw new InvalidOperationException($"Secret reference '{logicalName}' uses unsupported scheme '{spec.Split('/', 2)[0]}'. Use secret://env/... or secret://file/....");
    }

    public static void RequireReferenceWhenPresent(IConfiguration configuration, string key, bool production)
    {
        var value = configuration[key]?.Trim();
        if (!production || string.IsNullOrWhiteSpace(value)) return;
        if (!IsSecretReference(value))
            throw new InvalidOperationException($"Production sensitive setting '{key}' must use a secret://env/... or secret://file/... reference.");
        _ = ResolveValue(configuration, value, key);
    }

    private static void ValidateUnixSecretPermissions(string path, string logicalName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            var forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((mode & forbidden) != 0)
                throw new InvalidOperationException($"Secret file for '{logicalName}' must not grant group/other permissions: {path}");
        }
        catch (PlatformNotSupportedException)
        {
            // Filesystem does not expose POSIX mode bits; caller still gets path containment and no logging of the value.
        }
    }
}
