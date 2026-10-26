namespace HelpDesk.Domain;

/// <summary>
/// Raised when an operation would break a business rule (for example, resolving a segment that is already closed).
/// The API translates it into a 409/422 response with the message shown to the user.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
