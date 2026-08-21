using DISRoute.Application.Security;

namespace DISRoute.Security;

public static class AdGroupRoleMapper
{
    private static readonly Dictionary<string, string> Default = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DISRoute-Dispatchers"] = Roles.Dispatcher,
        ["DISRoute-Police"] = Roles.Police,
        ["DISRoute-Fire"] = Roles.Fire,
        ["DISRoute-EMS"] = Roles.Ems,
        ["DISRoute-Supervisors"] = Roles.Supervisor,
        ["DISRoute-Administrators"] = Roles.Administrator
    };

    public static string[] Map(IEnumerable<string> groupNames) =>
        groupNames
            .Where(Default.ContainsKey)
            .Select(groupName => Default[groupName])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
