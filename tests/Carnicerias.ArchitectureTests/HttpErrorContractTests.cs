using System.Text.Json;
using Carnicerias.Api.Contracts;

namespace Carnicerias.ArchitectureTests;

public sealed class HttpErrorContractTests
{
    [Fact]
    public void SerializesUsingTheUniformErrorEnvelope()
    {
        var response = new ErrorResponse(
            new ApiError("VALIDATION_ERROR", "No se pudo completar la solicitud", []));

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        Assert.Equal(
            "{\"error\":{\"code\":\"VALIDATION_ERROR\",\"message\":\"No se pudo completar la solicitud\",\"details\":[]}}",
            json);
    }
}
