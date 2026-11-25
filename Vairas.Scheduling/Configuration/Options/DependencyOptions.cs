namespace Vairas.Scheduling.Configuration.Options;

/// <summary>Bound from "Dependencies": owner service origins and the workload client used to call them.</summary>
public sealed class DependencyOptions
{
    public string AuthBaseUrl { get; set; } = "https://localhost:7100/";
    public string UserManagementBaseUrl { get; set; } = "https://localhost:7001/";
    public string SchoolBaseUrl { get; set; } = "https://localhost:7200/";
    public string VehicleBaseUrl { get; set; } = "";
    public string LearningBaseUrl { get; set; } = "";
    public string ChatBaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "scheduling";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "identity.validate profiles.status.read profiles.binding.read school.scheduling.read school.coordination.read vehicles.eligibility.read learning.planning.read learning.readiness.read confirmations.validate";
}
