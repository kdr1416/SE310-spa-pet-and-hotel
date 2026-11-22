using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Foundation;

public sealed class ShiftQueryBindingTests
{
    [Theory]
    [InlineData("?maCa=1", HttpStatusCode.BadRequest)]
    [InlineData("?maCa=1&ngay=2035-01-01", HttpStatusCode.OK)]
    public async Task ShiftQuery_GetBindingRequiresDate(string query, HttpStatusCode expected)
    {
        using var host = AuthApplicationFactory.Create().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ShiftQueryProbeController).Assembly)));
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/test-only/shift-query" + query);
        Assert.Equal(expected, response.StatusCode);
    }
}

[ApiController]
[Route("test-only/shift-query")]
public sealed class ShiftQueryProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] ShiftQuery query) => Ok(query);
}
