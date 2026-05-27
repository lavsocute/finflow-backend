namespace FinFlow.Application.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string name, object key)
        : base($"Entity \"{name}\" ({key}) was not found.")
    {
    }
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}

public class ChatAccessDeniedException : Exception
{
    public ChatAccessDeniedException() : base("Access denied") { }
    public ChatAccessDeniedException(string message) : base(message) { }
    public ChatAccessDeniedException(string message, Exception inner) : base(message, inner) { }
}

public class ChatRateLimitExceededException : Exception
{
    public ChatRateLimitExceededException() : base("Rate limit exceeded. Please wait a moment before sending another message.") { }
    public ChatRateLimitExceededException(string message) : base(message) { }
    public ChatRateLimitExceededException(string message, Exception inner) : base(message, inner) { }
}

public class ChatQuotaExceededException : Exception
{
    public ChatQuotaExceededException() : base("Chat quota exceeded.") { }
    public ChatQuotaExceededException(string message) : base(message) { }
    public ChatQuotaExceededException(string message, Exception inner) : base(message, inner) { }
}

public class ChatSessionNotFoundException : Exception
{
    public ChatSessionNotFoundException() : base("Session was not found for the current membership.") { }
    public ChatSessionNotFoundException(string message) : base(message) { }
    public ChatSessionNotFoundException(string message, Exception inner) : base(message, inner) { }
}

public class LlmServiceException : Exception
{
    public LlmServiceException() : base("LLM service is temporarily unavailable. Please try again later.") { }
    public LlmServiceException(string message) : base(message) { }
    public LlmServiceException(string message, Exception inner) : base(message, inner) { }
}
