namespace SellingPoint.Printing;

public interface IPrintTransport
{
    /// <param name="data">The ESC/POS bytes.</param>
    /// <param name="preview">
    /// The same slip as readable text. Real printers ignore it; the file transport
    /// writes it alongside the bytes so a slip can be checked on a machine that has
    /// no printer attached.
    /// </param>
    void Send(byte[] data, string preview);

    /// <summary>Shown in Settings so the operator can see what is configured.</summary>
    string Describe();
}

/// <summary>
/// Writes each slip to a folder instead of to paper: the .txt is the readable
/// slip, the .bin is exactly what would have gone down the wire. This is how the
/// app is developed and how ticket layout is checked without hardware.
/// </summary>
public sealed class FileTransport(string folder) : IPrintTransport
{
    private int _counter;

    public string Folder { get; } = folder;

    public void Send(byte[] data, string preview)
    {
        Directory.CreateDirectory(Folder);

        var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}-{++_counter:000}";
        File.WriteAllBytes(Path.Combine(Folder, $"ticket-{stamp}.bin"), data);
        File.WriteAllText(Path.Combine(Folder, $"ticket-{stamp}.txt"), preview);
    }

    public string Describe() => $"Ficheiros em {Folder}";
}
