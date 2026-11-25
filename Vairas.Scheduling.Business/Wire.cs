using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(allowIntegerValues:false) } };
    public static string Save<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw SchedulingErrors.Dependency("INVALID_STORED_OUTCOME");
}
