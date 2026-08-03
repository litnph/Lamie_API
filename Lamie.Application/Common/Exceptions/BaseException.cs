using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamie.Application.Common.Exceptions
{
    public abstract class BaseException : Exception
    {
        public string Code { get; }
        protected BaseException(string message, string code)
            : base(message)
        {
            Code = code;
        }
    }


    #region Common Exceptions
    public class NotFoundException : BaseException
    {
        public NotFoundException(string entity, object key)
            : base($"{entity} not found", "NOT_FOUND")
        {
        }
    }

    public class ValidationException : BaseException
    {
        public IReadOnlyDictionary<string, string[]> Errors { get; }

        public ValidationException(Dictionary<string, string[]> errors)
            : base("Validation failed", "VALIDATION_ERROR")
        {
            Errors = errors;
        }
    }

    public class ConflictException : BaseException
    {
        public ConflictException(string message)
            : base(message, "CONFLICT")
        {
        }
    }

    public class UnauthorizedException : BaseException
    {
        public UnauthorizedException(string message = "Unauthorized")
            : base(message, "UNAUTHORIZED")
        {
        }
    }

    public class ForbiddenException : BaseException
    {
        public ForbiddenException(string message = "Forbidden")
            : base(message, "FORBIDDEN")
        {
        }
    }

    public class BusinessRuleException : BaseException
    {
        public BusinessRuleException(string message)
            : base(message, "BUSINESS_RULE_VIOLATION")
        {
        }
    }

    public class InternalException : BaseException
    {
        public InternalException(string message)
            : base(message, "INTERNAL_ERROR")
        {
        }
    }
    #endregion
}
