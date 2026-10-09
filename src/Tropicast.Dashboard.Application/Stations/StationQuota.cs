using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Stations;

/// <summary>How many stations a tenant may have on its plan.</summary>
public static class StationQuota
{
    public static bool AllowsAnother(Plan plan, int existingStations)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return existingStations < plan.MaxStations;
    }

    public static string LimitMessage(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return $"The {plan.Name} plan allows {plan.MaxStations} station{(plan.MaxStations == 1 ? "" : "s")}. Upgrade to add more.";
    }
}
