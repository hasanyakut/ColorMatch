namespace ColorMatch.Application.Common;

/// <summary>Base type for expected, client-facing failures. Mapped to HTTP problem details by the API layer.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message);

public sealed class ConflictException(string message) : AppException(message);

public sealed class ForbiddenException(string message) : AppException(message);

public sealed class UnauthorizedAppException(string message) : AppException(message);

public sealed class ValidationAppException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(string field, string message)
        : base(message) => Errors = new Dictionary<string, string[]> { [field] = [message] };

    public ValidationAppException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = new Dictionary<string, string[]>(errors);
}

/// <summary>Too many attempts / requests for a limited game action (mapped to HTTP 429).</summary>
public sealed class TooManyRequestsException(string message) : AppException(message);
