using HotelBooking.Api.Dtos.TestData;
using HotelBooking.Services.TestData;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(ITestDataService testDataService) : ControllerBase
{
    [HttpPost("seed")]
    [ProducesResponseType<SeedResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SeedResponse>> Seed(CancellationToken cancellationToken)
    {
        var result = await testDataService.SeedAsync(cancellationToken);

        return Ok(new SeedResponse(
            result.HotelId,
            result.HotelName,
            result.RoomsCreated));
    }

    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        await testDataService.ResetAsync(cancellationToken);

        return NoContent();
    }
}
