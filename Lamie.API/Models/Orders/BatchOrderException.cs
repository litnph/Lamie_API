using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Models.Orders;

public sealed class BatchOrderException : Exception
{
    private BatchOrderException(
        int index,
        string clientDraftId,
        int statusCode,
        string causeCode,
        string detailMessage,
        IReadOnlyDictionary<string, string[]>? errors,
        Exception innerException)
        : base($"Đơn #{index + 1}: {detailMessage}", innerException)
    {
        Index = index;
        ClientDraftId = clientDraftId;
        StatusCode = statusCode;
        CauseCode = causeCode;
        DetailMessage = detailMessage;
        Errors = errors;
    }

    public int Index { get; }
    public string ClientDraftId { get; }
    public int StatusCode { get; }
    public string CauseCode { get; }
    public string DetailMessage { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static BatchOrderException From(int index, string clientDraftId, Exception exception)
    {
        var (statusCode, code, message, errors) = exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                validation.Code,
                validation.Errors.Values.SelectMany(value => value).FirstOrDefault() ?? validation.Message,
                (IReadOnlyDictionary<string, string[]>?)validation.Errors),
            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                notFound.Code,
                notFound.Message,
                null),
            ConflictException conflict => (
                StatusCodes.Status409Conflict,
                conflict.Code,
                conflict.Message,
                null),
            BusinessRuleException businessRule => (
                StatusCodes.Status400BadRequest,
                businessRule.Code,
                businessRule.Message,
                null),
            DomainException domain => (
                StatusCodes.Status400BadRequest,
                "BUSINESS_RULE_VIOLATION",
                domain.Message,
                null),
            DbUpdateException => (
                StatusCodes.Status409Conflict,
                "PERSISTENCE_CONFLICT",
                "The order could not be persisted because it conflicts with existing data.",
                null),
            _ => (
                StatusCodes.Status500InternalServerError,
                "INTERNAL_SERVER_ERROR",
                "The order could not be created.",
                null)
        };

        return new BatchOrderException(index, clientDraftId, statusCode, code, message, errors, exception);
    }
}
