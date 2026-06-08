using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PolicyManagement.API.Models;
using PolicyManagement.Application.Constants;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.API.Validation;

public class PolicyListValidationFilter : IActionFilter
{
    private static readonly HashSet<string> AllowedSortDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc", "desc"
    };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ActionArguments.TryGetValue("request", out var arg) || arg is not PolicyListRequest request)
            return;

        var errors = new Dictionary<string, string[]>();

        if (request.Sort is not null && !SortFields.Allowed.Contains(request.Sort))
            errors["sort"] = [$"Sort field '{request.Sort}' is not allowed. Allowed: {string.Join(", ", SortFields.Allowed)}."];

        if (request.SortDirection is not null && !AllowedSortDirections.Contains(request.SortDirection))
            errors["sortDirection"] = ["sortDirection must be 'asc' or 'desc'."];

        if (request.Status is not null && !Enum.TryParse<PolicyStatus>(request.Status, ignoreCase: true, out _))
            errors["status"] = [$"'{request.Status}' is not a valid status. Allowed: Active, Expired, Pending, Cancelled."];

        if (request.LineOfBusiness is not null && !Enum.TryParse<LineOfBusiness>(request.LineOfBusiness, ignoreCase: true, out _))
            errors["lineOfBusiness"] = [$"'{request.LineOfBusiness}' is not a valid line of business. Allowed: Property, Casualty, AandH, Marine."];

        if (request.EffectiveDateFrom.HasValue && request.EffectiveDateTo.HasValue
            && request.EffectiveDateFrom > request.EffectiveDateTo)
            errors["effectiveDateFrom"] = ["effectiveDateFrom must not be after effectiveDateTo."];

        if (errors.Count > 0)
        {
            var problemDetails = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Instance = context.HttpContext.Request.Path
            };
            context.Result = new BadRequestObjectResult(problemDetails)
            {
                ContentTypes = { "application/problem+json" }
            };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
