using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Xunit;

namespace Lumen.Tests.Game;

/// <summary>
/// Guards on the release scripts (spec §72–73).
///
/// These are text files rather than code, which is exactly why they are worth a test: an
/// installer script is edited rarely, by hand, usually in a hurry before a release, and
/// the two mistakes it invites — changing the AppId, or hardcoding a version — are both
/// invisible until somebody's install is already broken.
/// </summary>
public class InstallerScriptTests
{
    /// <summary>
    /// Walks up from the test binary to the repository root. The scripts are not copied
    /// to the output directory, and nor should they be — they belong to the repository,
    /// not to the build.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LUMEN.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to find the repository root");
        return dir!.FullName;
    }

    private static string InstallerScript() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tools", "installer", "LUMEN.iss"));

    private static string ReleaseScript() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tools", "release.ps1"));

    /// <summary>
    /// The GUID an installed copy is known by. Changing it turns the next release from an
    /// upgrade into a second, parallel installation — with the old one still in Add/Remove
    /// Programs and its shortcuts still pointing at the old build. It is written out here
    /// so that changing it takes a deliberate edit in two places.
    /// </summary>
    private const string AppId = "8E3C6F14-2B7A-4E59-9C41-6A0D5B8F7E22";

    [Fact]
    public void The_installer_keeps_the_app_id_that_makes_upgrades_upgrades()
    {
        InstallerScript().Should().Contain(AppId);
    }

    /// <summary>
    /// The version has one home — Directory.Build.props — and the installer is handed it
    /// on the command line. A literal in the script is a second home, and the two would
    /// drift the first time somebody bumped only one.
    /// </summary>
    [Fact]
    public void The_installer_takes_its_version_from_the_build_rather_than_a_literal()
    {
        string script = InstallerScript();

        script.Should().Contain("AppVersion={#AppVersion}");
        script.Should().MatchRegex(@"VersionInfoVersion=\{#AppVersion\}");

        // The only version literal allowed is the fallback for a hand-run compile, which
        // is deliberately not a real version so a build made that way is obvious.
        foreach (string line in Lines(script).Where(l => l.Contains("define AppVersion")))
        {
            line.Should().Contain("0.0.0");
        }
    }

    [Fact]
    public void The_installer_puts_the_game_in_program_files_and_the_data_elsewhere()
    {
        string script = InstallerScript();

        script.Should().Contain("DefaultDirName={autopf}");
        script.Should().Contain("{localappdata}\\{#AppName}");
    }

    /// <summary>
    /// Uninstalling must not take a player's rating and chart library with it by default.
    /// The prompt defaults to No, which is what MB_DEFBUTTON2 means here.
    /// </summary>
    [Fact]
    public void Uninstalling_does_not_delete_player_data_without_asking()
    {
        string script = InstallerScript();

        script.Should().Contain("MB_DEFBUTTON2",
            "the prompt to delete player data must default to keeping it");
        script.Should().Contain("DelTree",
            "there must still be a way to remove it for somebody who wants that");
    }

    /// <summary>
    /// The one that was found the hard way. Under <c>/VERYSILENT /SUPPRESSMSGBOXES</c> —
    /// which is how every package manager and every deployment tool uninstalls — the
    /// confirmation box is answered Yes regardless of MB_DEFBUTTON2, and a silent
    /// uninstall took a whole data folder with it. A prompt nobody can see is not consent,
    /// so the deletion has to be skipped outright when there is nobody to ask.
    /// </summary>
    [Fact]
    public void A_silent_uninstall_never_deletes_player_data()
    {
        string script = InstallerScript();
        string[] lines = Lines(script);

        int guard = Array.FindIndex(lines, l => l.Contains("UninstallSilent"));
        int delete = Array.FindIndex(lines, l => l.Contains("DelTree"));

        guard.Should().BeGreaterThan(-1,
            "the uninstaller must check whether it is running silently");
        delete.Should().BeGreaterThan(guard,
            "the silent check must come before anything is deleted");

        // The guard has to bail out, not merely note the situation and carry on. Read as a
        // span so the assertion survives the statement being wrapped onto the next line.
        string guardBody = string.Join("\n", lines[guard..Math.Min(guard + 3, lines.Length)]);
        guardBody.Should().Contain("Exit",
            "a silent uninstall must leave the data alone entirely, not fall through");
    }

    [Fact]
    public void The_installer_matches_the_publish_architecture()
    {
        InstallerScript().Should().Contain("x64compatible");
    }

    /// <summary>
    /// The three native libraries are the difference between a game that starts and one
    /// that dies on the SDL load, so the verification pass has to keep checking for them.
    /// </summary>
    [Fact]
    public void The_release_script_checks_for_the_native_libraries_the_game_cannot_start_without()
    {
        string script = ReleaseScript();

        foreach (string native in new[] { "SDL2.dll", "soft_oal.dll", "e_sqlite3.dll" })
        {
            script.Should().Contain(native);
        }
    }

    [Fact]
    public void The_release_script_reads_the_version_from_the_one_place_it_lives()
    {
        string script = ReleaseScript();

        script.Should().Contain("Directory.Build.props");
        script.Should().NotContain($"\"{GameIdentity.Version}\"",
            "the release script must not carry its own copy of the version");
    }

    /// <summary>
    /// The build file is where the version lives, and it has to look like one, because the
    /// release script names every artifact after it.
    /// </summary>
    [Fact]
    public void The_build_file_carries_the_version_the_game_reports()
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));

        props.Should().Contain($"<Version>{GameIdentity.Version}</Version>",
            "GameIdentity reads the version off the assembly, which is stamped from here");
    }

    private static string[] Lines(string text) =>
        text.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);
}
