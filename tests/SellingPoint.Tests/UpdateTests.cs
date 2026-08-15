using SellingPoint.App;

namespace SellingPoint.Tests;

public class UpdateCheckerTests
{
    private const string Sample = """
    {
      "tag_name": "v1.2.0",
      "body": "  Poupa papel e traz teclado próprio.  ",
      "assets": [
        { "name": "source.zip", "browser_download_url": "https://example/source.zip", "size": 100 },
        { "name": "SellingPoint.exe", "browser_download_url": "https://example/SellingPoint.exe", "size": 52428800 }
      ]
    }
    """;

    [Fact]
    public void A_release_yields_its_version_notes_and_download()
    {
        var release = UpdateChecker.Parse(Sample);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release!.Version);
        Assert.Equal("Poupa papel e traz teclado próprio.", release.Notes);
        Assert.Equal("https://example/SellingPoint.exe", release.DownloadUrl);
        Assert.Equal("50,0 MB", release.SizeText.Replace('.', ','));
    }

    [Fact]
    public void The_source_archive_is_not_an_update()
    {
        // GitHub attaches source zips to every release; only the executable is one.
        var release = UpdateChecker.Parse(Sample);

        Assert.DoesNotContain("source", release!.DownloadUrl);
    }

    [Fact]
    public void A_release_with_no_executable_offers_nothing()
    {
        var json = """
        { "tag_name": "v9.9.9", "body": "", "assets": [ { "name": "notes.txt",
          "browser_download_url": "https://example/notes.txt", "size": 10 } ] }
        """;

        Assert.Null(UpdateChecker.Parse(json));
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V2.0", 2, 0, 0)]
    [InlineData("  v1.0.0  ", 1, 0, 0)]
    public void Tags_are_accepted_with_or_without_the_v(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void Anything_that_is_not_a_version_is_refused(string? tag)
        => Assert.False(UpdateChecker.TryParseVersion(tag, out _));

    [Fact]
    public void Only_a_higher_version_counts_as_newer()
    {
        var current = new Version(1, 2, 0);

        Assert.True(UpdateChecker.IsNewer(new Version(1, 2, 1), current));
        Assert.True(UpdateChecker.IsNewer(new Version(2, 0, 0), current));
        Assert.False(UpdateChecker.IsNewer(current, current));
        Assert.False(UpdateChecker.IsNewer(new Version(1, 1, 9), current));
    }

    [Fact]
    public void Rubbish_from_the_network_is_refused_rather_than_thrown()
        => Assert.Null(UpdateChecker.Parse("<html>404</html>"));

    [Fact]
    public void This_copy_knows_its_own_version()
        => Assert.True(UpdateChecker.Current >= new Version(1, 0, 0));
}

public class UpdateInstallerTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"sp-update-{Guid.NewGuid():N}");
        public UpdateInstaller Installer { get; }
        public string Running { get; }

        public Sandbox()
        {
            Directory.CreateDirectory(Root);
            Running = Path.Combine(Root, "SellingPoint.exe");
            File.WriteAllText(Running, "versão a correr");

            Installer = new UpdateInstaller(Path.Combine(Root, "atualizacao"));
        }

        public void StagePending(string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Installer.PendingPath)!);
            File.WriteAllText(Installer.PendingPath, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void With_nothing_downloaded_there_is_nothing_to_apply()
    {
        using var s = new Sandbox();

        Assert.False(s.Installer.HasPendingUpdate);
        Assert.False(s.Installer.ApplyPending(s.Running));
        Assert.Equal("versão a correr", File.ReadAllText(s.Running));
    }

    [Fact]
    public void Applying_puts_the_new_one_in_place_and_keeps_the_old_beside_it()
    {
        using var s = new Sandbox();
        s.StagePending("versão nova");

        Assert.True(s.Installer.ApplyPending(s.Running));

        Assert.Equal("versão nova", File.ReadAllText(s.Running));
        Assert.Equal("versão a correr", File.ReadAllText(s.Running + ".old"));
        Assert.False(s.Installer.HasPendingUpdate);
    }

    [Fact]
    public void The_old_copy_is_cleared_on_the_next_start()
    {
        using var s = new Sandbox();
        s.StagePending("versão nova");
        s.Installer.ApplyPending(s.Running);

        UpdateInstaller.CleanUp(s.Running);

        Assert.False(File.Exists(s.Running + ".old"));
        Assert.Equal("versão nova", File.ReadAllText(s.Running));
    }

    [Fact]
    public void A_second_update_over_a_first_still_works()
    {
        using var s = new Sandbox();
        s.StagePending("versão dois");
        s.Installer.ApplyPending(s.Running);

        // Without clearing the previous .old first, this would fail.
        s.StagePending("versão três");
        Assert.True(s.Installer.ApplyPending(s.Running));

        Assert.Equal("versão três", File.ReadAllText(s.Running));
    }

    [Fact]
    public void Discarding_leaves_the_running_copy_alone()
    {
        using var s = new Sandbox();
        s.StagePending("versão nova");

        s.Installer.DiscardPending();

        Assert.False(s.Installer.HasPendingUpdate);
        Assert.Equal("versão a correr", File.ReadAllText(s.Running));
    }

    [Fact]
    public void A_missing_executable_is_refused_rather_than_leaving_none_at_all()
    {
        using var s = new Sandbox();
        s.StagePending("versão nova");

        Assert.False(s.Installer.ApplyPending(Path.Combine(s.Root, "não-existe.exe")));

        // The download is kept, so the next attempt can still use it.
        Assert.True(s.Installer.HasPendingUpdate);
    }

    [Fact]
    public void An_interrupted_download_is_never_mistaken_for_a_finished_one()
    {
        using var s = new Sandbox();
        Directory.CreateDirectory(Path.GetDirectoryName(s.Installer.PendingPath)!);
        File.WriteAllText(s.Installer.PendingPath + ".part", "metade de um ficheiro");

        Assert.False(s.Installer.HasPendingUpdate);
        Assert.False(s.Installer.ApplyPending(s.Running));
    }
}
