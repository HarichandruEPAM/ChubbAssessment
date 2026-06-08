using Microsoft.AspNetCore.Mvc;
using PolicyManagement.API.Models;
using PolicyManagement.API.Validation;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Interfaces;

namespace PolicyManagement.API.Controllers;

[ApiController]
[Route("api/v1/policies")]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policyService;
    private readonly ILogger<PoliciesController> _logger;

    public PoliciesController(IPolicyService policyService, ILogger<PoliciesController> logger)
    {
        _policyService = policyService;
        _logger = logger;
    }

    /// <summary>Returns a paginated, filtered list of policies.</summary>
    [HttpGet]
    [ServiceFilter(typeof(PolicyListValidationFilter))]
    [ProducesResponseType(typeof(PaginatedResult<PolicyListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListPolicies([FromQuery] PolicyListRequest request, CancellationToken ct)
    {
        var query = request.ToQuery();
        var result = await _policyService.ListAsync(query, ct);
        return Ok(result);
    }

    /// <summary>Returns aggregate summary statistics across all policies.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(PolicySummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var result = await _policyService.GetSummaryAsync(ct);
        return Ok(result);
    }

    /// <summary>Bulk flags a list of policies for review.</summary>
    [HttpPatch("flag")]
    [ProducesResponseType(typeof(BulkFlagResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkFlag([FromBody] BulkFlagRequest request, CancellationToken ct)
    {
        var result = await _policyService.BulkFlagAsync(request.PolicyIds, ct);
        return Ok(result);
    }

    /// <summary>Returns a single policy by its UUID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PolicyDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _policyService.GetByIdAsync(id, ct);
        if (result is null)
        {
            _logger.LogWarning("Policy {PolicyId} not found", id);
            return Problem(detail: $"Policy with ID '{id}' was not found.",
                           statusCode: StatusCodes.Status404NotFound,
                           title: "Policy Not Found");
        }
        return Ok(result);
    }
}
