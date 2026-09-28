using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Formatting;
using Banking_System.ApiService.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

/// <summary>Staff-only directory of bank customers and their accounts, with personal identifiers masked.</summary>
[ApiController]
[Authorize(Policy = "EmployeeOrAdmin")]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private const int MaxPageSize = 50;

    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active", "Inactive", "Locked"
    };

    private readonly ICustomerDirectoryRepository _directory;

    public CustomersController(ICustomerDirectoryRepository directory)
    {
        _directory = directory;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] bool? onlineBanking,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (status is not null && !Statuses.Contains(status))
        {
            return BadRequest(new { error = "InvalidStatus" });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var entries = await _directory.SearchAsync(
            q, status is null ? null : Statuses.First(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)),
            onlineBanking, page, pageSize, cancellationToken);

        var items = entries.Select(e => new CustomerAccountRow(
            $"{e.FirstName} {e.LastName}".Trim(),
            SensitiveMasking.MaskNumber(e.CifNumber),
            SensitiveMasking.MaskNumber(e.PhoneNumber),
            SensitiveMasking.MaskEmail(e.Email),
            e.AccountCount,
            e.TotalBalance,
            e.HasOnlineBanking,
            e.Status)).ToList();

        // Every row carries the overall match count (an out-of-range page has no rows, so 0).
        var total = entries.Count > 0 ? entries[0].TotalCount : 0;

        return Ok(new CustomerAccountPage(items, total, page, pageSize));
    }
}
