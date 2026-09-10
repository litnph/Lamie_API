using Lamie.Application.Common.Exceptions;
using Lamie.API.Models.Orders;
using Lamie.Application.Content;
using Lamie.Application.FeData;
using Lamie.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lamie.API.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (BatchOrderException ex)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                if (ex.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    _logger.LogError(
                        ex.InnerException ?? ex,
                        "Batch order creation failed at index {BatchIndex}; trace {TraceIdentifier}",
                        ex.Index,
                        context.TraceIdentifier);
                }

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    code = "BATCH_ORDER_FAILED",
                    message = ex.Message,
                    errors = ex.Errors,
                    batchError = new
                    {
                        clientDraftId = ex.ClientDraftId,
                        index = ex.Index,
                        code = ex.CauseCode,
                        message = ex.DetailMessage,
                        fieldErrors = ex.Errors
                    }
                });
            }
            catch (BaseException ex)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.ContentType = "application/json";

                context.Response.StatusCode = ex switch
                {
                    NotFoundException => StatusCodes.Status404NotFound,
                    ValidationException => StatusCodes.Status400BadRequest,
                    ConflictException => StatusCodes.Status409Conflict,
                    UnauthorizedException => StatusCodes.Status401Unauthorized,
                    ForbiddenException => StatusCodes.Status403Forbidden,
                    ContentAiNotConfiguredException => StatusCodes.Status503ServiceUnavailable,
                    ContentAiProviderException providerException when providerException.IsTemporary => StatusCodes.Status503ServiceUnavailable,
                    ContentAiProviderException => StatusCodes.Status502BadGateway,
                    FeDataExportException => StatusCodes.Status422UnprocessableEntity,
                    BusinessRuleException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status400BadRequest
                };

                var response = new
                {
                    success = false,
                    code = ex.Code,
                    message = ex.Message,
                    errors = ex switch
                    {
                        ValidationException validation => (object)validation.Errors,
                        FeDataExportException export => (object)new Dictionary<string, IReadOnlyList<string>>
                        {
                            ["export"] = export.Issues
                        },
                        _ => null
                    }
                };

                await context.Response.WriteAsJsonAsync(response);
            }
            catch (DomainException ex)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = StatusCodes.Status400BadRequest;

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    code = "BUSINESS_RULE_VIOLATION",
                    message = ex.Message
                });
            }
            catch (DbUpdateException)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                _logger.LogWarning(
                    "Persistence conflict for {Method} {Path}; trace {TraceIdentifier}",
                    context.Request.Method,
                    context.Request.Path,
                    context.TraceIdentifier);
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    code = "PERSISTENCE_CONFLICT",
                    message = "The requested change conflicts with existing data."
                });
            }
            catch (Exception ex)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                _logger.LogError(
                    ex,
                    "Unhandled request failure for {Method} {Path}; trace {TraceIdentifier}",
                    context.Request.Method,
                    context.Request.Path,
                    context.TraceIdentifier);
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    code = "INTERNAL_SERVER_ERROR",
                    message = "Internal server error"
                });
            }
        }
    }
}
