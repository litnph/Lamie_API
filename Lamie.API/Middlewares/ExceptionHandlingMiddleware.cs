using Lamie.Application.Common.Exceptions;
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
                    BusinessRuleException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status400BadRequest
                };

                var response = new
                {
                    success = false,
                    code = ex.Code,
                    message = ex.Message,
                    errors = ex is ValidationException ve ? ve.Errors : null
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
