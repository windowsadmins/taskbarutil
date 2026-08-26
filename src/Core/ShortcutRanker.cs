namespace TaskbarUtil.Core;

/// <summary>
/// Scores a Start Menu shortcut against a search term so that the app's primary
/// launcher wins over its uninstaller, documentation and alternate editions.
///
/// The resolver used to take the first match in directory enumeration order,
/// which is alphabetical, and that is wrong surprisingly often:
///
///   Uninstall &lt;app&gt;.lnk            beat  &lt;app&gt;.lnk                 (U &lt; the app's own initial)
///   &lt;app&gt; Apprentice.lnk           beat  &lt;app&gt; Education.lnk       (A &lt; E)
///   &lt;app&gt; in Safe Mode.lnk         beat  &lt;app&gt;.lnk
///   &lt;app&gt; Documentation.lnk        beat  &lt;app&gt;.lnk
///   Firefox Private Browsing.lnk   beat  Firefox.lnk
///
/// The last three are the same comparison: the unwanted name continues with a
/// space (0x20) where the real launcher continues with the dot (0x2E) of its
/// extension, and space sorts first.
///
/// Separately, matching was a bare case-insensitive substring test, so the
/// "Code" alias of Visual Studio Code matched Adobe Media En-code-r and
/// pinned an unrelated application.
/// </summary>
public static class ShortcutRanker
{
    /// <summary>No match at all -- the caller must discard this candidate.</summary>
    public const int NoMatch = int.MinValue;

    // Kept on the same scale the resolver already used, so a Start Menu hit
    // still ranks below a KnownApps entry that resolves straight to an AUMID.
    const int ExactScore = 95;
    const int PrefixScore = 80;
    const int WordScore = 70;

    // Sized so a demoted shortcut can never outrank a primary one (lowest
    // primary score is 70, highest demoted score is 80 - 40 = 40) while every
    // score a caller sees stays positive.
    const int SecondaryPenalty = 40;
    const int NonExecutablePenalty = 25;

    /// <summary>
    /// Words that mark a shortcut as something other than the app's main
    /// launcher. Matched as whole words, so "Assist" does not fire on
    /// "Assistant" and "Demo" does not fire on "Democracy".
    ///
    /// Deliberately conservative: every entry here was observed on a lab
    /// machine shadowing a real launcher. A word that merely sounds secondary
    /// does not belong here -- the cost of a wrong entry is an app that can
    /// never be pinned.
    /// </summary>
    static readonly string[] SecondaryMarkers =
    {
        "uninstall", "uninstaller", "remove", "repair", "modify", "setup", "installer",
        "documentation", "docs", "help", "manual", "readme", "release notes",
        "user guide", "getting started", "tutorial", "samples", "examples",
        "website", "web site", "changelog", "license", "licensing",
        "safe mode", "troubleshoot", "private browsing",
        "apprentice", "non-commercial", "noncommercial", "indie", "assist", "demo", "trial",
    };

    /// <summary>
    /// Shortcut targets that open a document or a web page rather than running
    /// the program. This catches a documentation shortcut whose name gives
    /// nothing away, and needs no vocabulary to do it.
    /// </summary>
    static readonly string[] NonExecutableTargets =
    {
        ".html", ".htm", ".chm", ".url", ".pdf", ".txt", ".md", ".rtf", ".doc", ".docx",
    };

    /// <summary>
    /// Score <paramref name="shortcutName"/> (no extension) against
    /// <paramref name="searchTerm"/>. Higher is better;
    /// <see cref="NoMatch"/> means it does not match at all.
    /// </summary>
    public static int Score(string shortcutName, string searchTerm, string? targetPath = null)
    {
        if (string.IsNullOrWhiteSpace(shortcutName) || string.IsNullOrWhiteSpace(searchTerm))
            return NoMatch;

        var name = shortcutName.Trim();
        var term = searchTerm.Trim();

        // An exact request is honoured as-is. If someone asks for "Houdini
        // Apprentice" by that name they get it, markers and all.
        if (name.Equals(term, StringComparison.OrdinalIgnoreCase))
            return ExactScore;

        var at = IndexOfWord(name, term);
        if (at < 0)
            return NoMatch;

        var score = at == 0 ? PrefixScore : WordScore;

        if (HasSecondaryMarker(name))
            score -= SecondaryPenalty;

        if (IsNonExecutableTarget(targetPath))
            score -= NonExecutablePenalty;

        return score;
    }

    /// <summary>
    /// Orders candidates best-first: score, then the shortest name, then
    /// ordinally by name so the result never depends on enumeration order.
    /// Shortest-name is what separates "&lt;app&gt; 9.0" from
    /// "&lt;app&gt; 9.0 Documentation" when no marker word applies.
    /// </summary>
    public static IEnumerable<T> Rank<T>(IEnumerable<T> candidates, Func<T, int> score, Func<T, string> name)
    {
        return candidates
            .Where(c => score(c) != NoMatch)
            .OrderByDescending(score)
            .ThenBy(c => name(c).Length)
            .ThenBy(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds <paramref name="term"/> in <paramref name="name"/> at a word
    /// boundary, or -1.
    ///
    /// The leading edge must not be preceded by a letter or digit, which is
    /// what stops "Code" matching inside "Encoder". The trailing edge only has
    /// to not be followed by a *letter*, so "Photoshop" still matches
    /// "Photoshop9" where a version number runs straight on.
    /// </summary>
    static int IndexOfWord(string name, string term)
    {
        var from = 0;
        while (from <= name.Length - term.Length)
        {
            var at = name.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return -1;

            var startsClean = at == 0 || !char.IsLetterOrDigit(name[at - 1]);
            var end = at + term.Length;
            var endsClean = end == name.Length || !char.IsLetter(name[end]);

            if (startsClean && endsClean)
                return at;

            from = at + 1;
        }

        return -1;
    }

    static bool HasSecondaryMarker(string name)
    {
        foreach (var marker in SecondaryMarkers)
        {
            if (IndexOfWord(name, marker) >= 0)
                return true;
        }
        return false;
    }

    static bool IsNonExecutableTarget(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        var ext = Path.GetExtension(targetPath);
        if (string.IsNullOrEmpty(ext))
            return false;

        return NonExecutableTargets.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
