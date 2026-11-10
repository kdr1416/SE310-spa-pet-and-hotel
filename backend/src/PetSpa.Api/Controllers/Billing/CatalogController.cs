using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class CatalogController(ICatalogService catalogService) : ControllerBase
{
    /// <summary>
    /// E02: Lấy danh mục dịch vụ, combo và khoảng size
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CatalogDto>>> GetCatalog(CancellationToken cancellationToken)
    {
        var result = await catalogService.GetActiveCatalogAsync(cancellationToken);
        return Ok(ApiResponse<CatalogDto>.Ok(result, "Lấy danh mục dịch vụ thành công."));
    }
}
