using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Vairas.Scheduling.Configuration;
public sealed class ApiDocumentTransformer:IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument d,OpenApiDocumentTransformerContext context,CancellationToken ct)
    {
        d.Info.Title="Vairas Scheduling API";d.Info.Version="1.0.0";d.Info.Description="School-scoped scheduling, atomic reservations, physical lifecycle and durable owner coordination. Learning credit and vehicle lifecycle belong to their owners.";
        d.Components??=new();d.Components.SecuritySchemes=new Dictionary<string,IOpenApiSecurityScheme>
        { ["bearerAuth"]=new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"},["workloadBearer"]=new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"}};
        foreach(var (path,item) in d.Paths)
        foreach(var operation in item.Operations!.Values)
        {
            var internalRoute=path.StartsWith("/internal/",StringComparison.Ordinal);
            operation.Security=[new(){[new OpenApiSecuritySchemeReference(internalRoute?"workloadBearer":"bearerAuth",d)]=[]}];
            foreach(var parameter in operation.Parameters??[])
                if(parameter.Name=="Idempotency-Key" && parameter is OpenApiParameter key){key.Required=true;key.Description="Stable nonempty UUID; retry the exact command under the same identity.";}
            operation.Responses??=new();
            foreach(var status in new[]{"400","401","403","404","409","422","503"})
                operation.Responses.TryAdd(status,new OpenApiResponse{Description="ProblemDetails with stable code and correlationId; another school's state is never disclosed."});
            if(path.Contains("/delegated/",StringComparison.Ordinal))operation.Description=(operation.Description??"")+" Requires scheduling.mcp.write, an independently validated X-User-Authorization token and an exact X-Confirmation-ID.";
        }
        Describe(d,"CreateLessonRequest","reason","Optional, practical lessons only (US-010 additional lesson). Trimmed; blank is treated as omitted; at most 500 characters after trimming, otherwise 400 VALIDATION_FAILED. Part of the operation intent: retrying the same Idempotency-Key with a different reason is 409 IDEMPOTENCY_KEY_REUSED.",500);
        Describe(d,"LessonResponse","reason","Immutable reason recorded when the practical lesson was scheduled; null when none was given.",null);
        return Task.CompletedTask;
    }
    private static void Describe(OpenApiDocument d,string schema,string property,string description,int? maxLength)
    {
        if(d.Components?.Schemas is {} schemas && schemas.TryGetValue(schema,out var found) && found is OpenApiSchema owner
            && owner.Properties is {} properties && properties.TryGetValue(property,out var target) && target is OpenApiSchema value)
        {value.Description=description;if(maxLength is int max)value.MaxLength=max;}
    }
}
