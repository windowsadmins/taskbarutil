using Xunit;
using TaskbarUtil.Core;

namespace TaskbarUtil.Tests;

/// <summary>
/// The shortcut names here are the shapes that broke the old first-match
/// resolver: an uninstaller, a documentation link, a safe-mode variant, a
/// reduced edition, and an alias that appeared inside an unrelated word.
/// </summary>
public class ShortcutRankerTests
{
    /// <summary>The winner among a real set of competing shortcut names.</summary>
    static string Best(string term, params (string Name, string? Target)[] shortcuts)
    {
        var scored = shortcuts
            .Select(s => (s.Name, Score: ShortcutRanker.Score(s.Name, term, s.Target)))
            .ToList();

        var ranked = ShortcutRanker.Rank(scored, s => s.Score, s => s.Name).ToList();
        Assert.NotEmpty(ranked);
        return ranked[0].Name;
    }

    // --- The five reported mis-resolutions ---

    [Fact]
    public void ZBrush_PrefersLauncherOverUninstaller()
    {
        // "Uninstall ZBrush..." used to win because U sorts before Z.
        Assert.Equal("ZBrush 9", Best("ZBrush",
            ("Uninstall ZBrush 9 ZBrush 9", @"C:\Program Files\Maxon ZBrush 9\Uninstall Maxon ZBrush.exe"),
            ("ZBrush 9", @"C:\Program Files\Maxon ZBrush 9\ZBrush.exe")));
    }

    [Fact]
    public void Rhino_PrefersLauncherOverSafeMode()
    {
        // "Rhino 9 in Safe Mode" used to win: it continues with a space (0x20)
        // where "Rhino 9.lnk" continues with the dot (0x2E) of its extension.
        Assert.Equal("Rhino 9", Best("Rhino",
            ("Rhino 9 in Safe Mode", @"C:\Program Files\Rhino 9\System\Rhino.exe"),
            ("Rhino 9", @"C:\Program Files\Rhino 9\System\Rhino.exe")));
    }

    [Fact]
    public void Nuke_PrefersLauncherOverDocumentation()
    {
        Assert.Equal("Nuke 9.0v1", Best("Nuke",
            ("Nuke 9.0v1 Documentation", @"C:\Program Files\Nuke9.0v1\Documentation\index.html"),
            ("Nuke 9.0v1", @"C:\Program Files\Nuke9.0v1\Nuke17.1.exe")));
    }

    [Fact]
    public void Houdini_PrefersFullEditionOverApprentice()
    {
        // Apprentice used to win on A sorting before E and F.
        var winner = Best("Houdini",
            ("Houdini Apprentice 9.0.1", @"C:\Program Files\Side Effects Software\Houdini 9.0.1\bin\happrentice.exe"),
            ("Houdini FX 9.0.1", @"C:\Program Files\Side Effects Software\Houdini 9.0.1\bin\houdinifx.exe"),
            ("Houdini Education 9.0.1", @"C:\Program Files\Side Effects Software\Houdini 9.0.1\bin\houdinied.exe"));

        Assert.NotEqual("Houdini Apprentice 9.0.1", winner);
    }

    [Fact]
    public void Firefox_PrefersBrowserOverPrivateBrowsingShortcut()
    {
        Assert.Equal("Firefox", Best("Firefox",
            ("Firefox Private Browsing", @"C:\Program Files\Mozilla Firefox\private_browsing.exe"),
            ("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe")));
    }

    // --- The substring-alias defect ---

    [Fact]
    public void CodeAlias_DoesNotMatchInsideEncoder()
    {
        // "Adobe Media Encoder 9" contains "code" but not as a word, and
        // this is the whole reason Visual Studio Code pinned an Adobe app.
        Assert.Equal(ShortcutRanker.NoMatch, ShortcutRanker.Score("Adobe Media Encoder 9", "Code"));
    }

    [Fact]
    public void CodeAlias_StillMatchesVisualStudioCode()
    {
        Assert.NotEqual(ShortcutRanker.NoMatch, ShortcutRanker.Score("Visual Studio Code", "Code"));
    }

    [Fact]
    public void VisualStudioCode_WinsAgainstEncoder()
    {
        Assert.Equal("Visual Studio Code", Best("Code",
            ("Adobe Media Encoder 9", @"C:\Program Files\Adobe\Adobe Media Encoder 9\Adobe Media Encoder.exe"),
            ("Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe")));
    }

    // --- Boundary rules ---

    [Fact]
    public void MatchesWhenAVersionNumberRunsStraightOn()
    {
        // Trailing edge only rejects a following letter, not a digit.
        Assert.NotEqual(ShortcutRanker.NoMatch, ShortcutRanker.Score("Photoshop9", "Photoshop"));
    }

    [Fact]
    public void DoesNotMatchInsideALongerWord()
    {
        Assert.Equal(ShortcutRanker.NoMatch, ShortcutRanker.Score("Blenderella", "Blender"));
    }

    [Fact]
    public void PunctuatedNamesStillMatch()
    {
        Assert.NotEqual(ShortcutRanker.NoMatch, ShortcutRanker.Score("7-Zip File Manager", "7-Zip"));
    }

    [Fact]
    public void ExactRequestIsHonouredEvenWhenItLooksSecondary()
    {
        // Asking for a demoted shortcut by its full name should still get it,
        // otherwise a marker word makes an app permanently unpinnable.
        Assert.Equal(95, ShortcutRanker.Score("Houdini Apprentice 9.0.1", "Houdini Apprentice 9.0.1"));
    }

    [Fact]
    public void PrimaryAlwaysOutranksDemoted()
    {
        // The property the penalty sizes depend on: the worst primary score
        // beats the best demoted score, so no combination of penalties can
        // ever let an uninstaller through.
        var worstPrimary = ShortcutRanker.Score("Some App Nuke Edition", "Nuke", @"C:\app.exe");
        var bestDemoted = ShortcutRanker.Score("Nuke Uninstall", "Nuke", @"C:\uninstall.exe");

        Assert.True(worstPrimary > bestDemoted, $"primary {worstPrimary} should beat demoted {bestDemoted}");
    }

    // --- Ranking behaviour ---

    [Fact]
    public void ShorterNameWinsWhenNothingElseSeparatesThem()
    {
        Assert.Equal("Maya 9", Best("Maya",
            ("Maya 9 Command Line", @"C:\Program Files\Autodesk\Maya9\bin\mayabatch.exe"),
            ("Maya 9", @"C:\Program Files\Autodesk\Maya9\bin\maya.exe")));
    }

    [Fact]
    public void NonExecutableTargetIsDemotedWithoutAMarkerWord()
    {
        // A documentation shortcut whose name gives nothing away is still
        // caught, because its target opens a web page rather than a program.
        var doc = ShortcutRanker.Score("Blender Guide", "Blender", @"C:\Program Files\Blender\guide.html");
        var app = ShortcutRanker.Score("Blender Studio", "Blender", @"C:\Program Files\Blender\blender.exe");

        Assert.True(app > doc, $"exe {app} should beat html {doc}");
    }

    // --- Newest release wins ---

    [Fact]
    public void PrefersTheNewerYearWhenBothGenerationsAreInstalled()
    {
        // Apps that ship one shortcut per yearly release leave every installed
        // generation behind. Both score the same and are the same length, so the
        // ordinal tie-break used to hand back last year's.
        Assert.Equal("Adobe Photoshop 2026", Best("Adobe Photoshop",
            ("Adobe Photoshop 2024", @"C:\Program Files\Adobe\Adobe Photoshop 2024\Photoshop.exe"),
            ("Adobe Photoshop 2026", @"C:\Program Files\Adobe\Adobe Photoshop 2026\Photoshop.exe")));
    }

    [Fact]
    public void ComparesVersionPartsAsNumbersNotText()
    {
        // 17.1v1 beats 17.0v3, which an ordinal comparison gets backwards
        // because "0" sorts before "1" at the second part.
        Assert.Equal("Nuke 17.1v1", Best("Nuke",
            ("Nuke 17.0v3", @"C:\Program Files\Nuke17.0v3\Nuke17.0.exe"),
            ("Nuke 17.1v1", @"C:\Program Files\Nuke17.1v1\Nuke17.1.exe")));
    }

    [Fact]
    public void DoubleDigitVersionBeatsSingleDigit()
    {
        // Text ordering puts "9" after "10"; numeric ordering does not.
        Assert.Equal("Studio 10", Best("Studio",
            ("Studio 10", @"C:\Program Files\Studio10\studio.exe"),
            ("Studio 9", @"C:\Program Files\Studio9\studio.exe")));
    }

    [Fact]
    public void UnversionedNameStillWinsOnLength()
    {
        // The version rule only applies when *both* names carry one. Treating
        // "no version" as version zero would rank the ESR build above plain
        // Firefox, undoing the shortest-name rule.
        Assert.Equal("Firefox", Best("Firefox",
            ("Firefox 115 ESR", @"C:\Program Files\Mozilla Firefox ESR\firefox.exe"),
            ("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe")));
    }

    [Fact]
    public void VersionRuleDoesNotOutrankScore()
    {
        // An older primary launcher still beats a newer uninstaller: score is
        // compared before the version.
        Assert.Equal("ZBrush 2024", Best("ZBrush",
            ("Uninstall ZBrush 2026", @"C:\Program Files\ZBrush 2026\Uninstall.exe"),
            ("ZBrush 2024", @"C:\Program Files\ZBrush 2024\ZBrush.exe")));
    }

    [Fact]
    public void TrailingDigitsInsideAWordAreNotAVersion()
    {
        // "Photoshop9" is one word, so there is no version to compare and the
        // existing tie-breaks decide it.
        Assert.Equal("Photoshop9", Best("Photoshop",
            ("Photoshop9 Extended", @"C:\Program Files\Photoshop9\photoshop.exe"),
            ("Photoshop9", @"C:\Program Files\Photoshop9\photoshop.exe")));
    }

    [Fact]
    public void NoMatchIsDiscardedByRank()
    {
        var scored = new[] { "Adobe Media Encoder 9" }
            .Select(n => (Name: n, Score: ShortcutRanker.Score(n, "Code")))
            .ToList();

        // Better to pin nothing than to pin the wrong application.
        Assert.Empty(ShortcutRanker.Rank(scored, s => s.Score, s => s.Name));
    }
}
