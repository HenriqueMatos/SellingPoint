namespace SellingPoint.Printing;

/// <summary>
/// Turns the framework's exceptions into something the person at the till can act
/// on. "Access to the port 'COM3' is denied" is accurate, English, and tells an
/// operator nothing about what to do next.
/// </summary>
public static class PrinterErrors
{
    public static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "A porta está a ser usada por outro programa",
        FileNotFoundException => "A porta já não existe",
        TimeoutException => "A impressora não respondeu a tempo",
        PlatformNotSupportedException e => e.Message,

        // Raised by this project's own code, already in Portuguese.
        InvalidOperationException e => e.Message,

        IOException => "Falha na ligação à impressora",
        _ => exception.Message
    };
}
