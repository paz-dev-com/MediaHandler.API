namespace MediaHandler.Domain.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string name, object key)
        : base($"{name} with key '{key}' was not found.")
    {
    }
}