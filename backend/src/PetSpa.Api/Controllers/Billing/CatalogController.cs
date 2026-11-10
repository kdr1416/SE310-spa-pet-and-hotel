using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    /// <summary>
    /// E02: Lấy danh mục dịch vụ, combo và khoảng size
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CatalogResponseDto>>> GetCatalog()
    {
        var result = await _catalogService.GetActiveCatalogAsync();
        return Ok(ApiResponse<CatalogResponseDto>.Ok(result, "Lấy danh mục dịch vụ thành công"));
    }
}