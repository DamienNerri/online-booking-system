namespace OnlineBooking.Api.Common;

/// <summary>Exception applicative avec code HTTP, mappée par le middleware d'erreurs.</summary>
public class AppException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public AppException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

public sealed class ValidationException : AppException
{
    public ValidationException(string message) : base(400, "VALIDATION_ERROR", message) { }
}

public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message = "Authentification requise.")
        : base(401, "UNAUTHORIZED", message) { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message = "Accès refusé.")
        : base(403, "FORBIDDEN", message) { }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message = "Ressource introuvable.")
        : base(404, "NOT_FOUND", message) { }
}

/// <summary>Conflit de concurrence : ressource déjà réservée/indisponible (Req 3.2).</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string message = "Ressource indisponible.")
        : base(409, "CONFLICT", message) { }
}
