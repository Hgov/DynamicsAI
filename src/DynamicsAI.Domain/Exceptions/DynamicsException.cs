namespace DynamicsAI.Domain.Exceptions;

public class DynamicsException : Exception
{
    public int? StatusCode { get; }

    public DynamicsException(string message) : base(message) { }

    public DynamicsException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public DynamicsException(string message, Exception inner) : base(message, inner) { }
}
