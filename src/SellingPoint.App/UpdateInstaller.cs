using System.Net.Http;

namespace SellingPoint.App;

/// <summary>
/// Downloads a new executable and puts it in place.
///
/// Windows will not let a running program be overwritten, but it will let it be
/// renamed. So the swap is: rename the running file out of the way, move the
/// downloaded one into its place. Done as the app closes, so the next time it
/// opens it is the new one - no relaunching, no second copy running, nothing to
/// go wrong halfway.
/// </summary>
public sealed class UpdateInstaller(string folder)
{
    private const string PendingName = "SellingPoint.pending.exe";

    /// <summary>Where a downloaded update waits until the app closes.</summary>
    public string PendingPath => Path.Combine(folder, PendingName);

    public bool HasPendingUpdate => File.Exists(PendingPath);

    public async Task<string> DownloadAsync(HttpClient http, ReleaseInfo release, CancellationToken token = default)
    {
        Directory.CreateDirectory(folder);

        // Download beside the target and rename on completion, so an interrupted
        // download can never be mistaken for a finished one.
        var partial = PendingPath + ".part";

        await using (var source = await http.GetStreamAsync(release.DownloadUrl, token))
        await using (var destination = File.Create(partial))
        {
            await source.CopyToAsync(destination, token);
        }

        File.Move(partial, PendingPath, overwrite: true);
        return PendingPath;
    }

    public void DiscardPending()
    {
        Delete(PendingPath);
        Delete(PendingPath + ".part");
    }

    /// <summary>
    /// Puts a downloaded update in place. Called as the app closes, when the file
    /// is still locked against being overwritten but not against being renamed.
    /// Returns false when there was nothing to do or the swap could not be made,
    /// in which case the running copy is left exactly as it was.
    /// </summary>
    public bool ApplyPending(string? runningExecutablePath)
    {
        if (!HasPendingUpdate) return false;
        if (string.IsNullOrWhiteSpace(runningExecutablePath) || !File.Exists(runningExecutablePath)) return false;

        var retired = runningExecutablePath + ".old";

        try
        {
            Delete(retired);
            File.Move(runningExecutablePath, retired);

            try
            {
                File.Move(PendingPath, runningExecutablePath);
            }
            catch (Exception)
            {
                // Put the working copy back rather than leaving no executable at all.
                File.Move(retired, runningExecutablePath);
                throw;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Clears the previous version left behind by the last swap.</summary>
    public static void CleanUp(string? runningExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(runningExecutablePath)) return;

        Delete(runningExecutablePath + ".old");
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Still locked, or gone already. Neither is worth interrupting anyone for.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
