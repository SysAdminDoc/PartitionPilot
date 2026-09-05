using System.Text;
using System.Text.RegularExpressions;

namespace PartitionPilot.Tests;

/// <summary>
/// A ratchet over the view models. Every user-visible message a view model hands to a dialog or assigns to a
/// bound status property has to come from a resource lookup, not from an English literal in the source. The
/// view models still waiting for conversion are named in <see cref="PendingConversion"/>, and that list can
/// only shrink: a file on it that has been cleaned up fails the test until it is removed.
/// </summary>
public class ViewModelMessageLocalizationTests
{
    /// <summary>
    /// View models whose messages are still English literals. Empty, and it stays that way: a name added
    /// here has to come back off before the file it names can be called done.
    /// </summary>
    private static readonly string[] PendingConversion = [];

    /// <summary>
    /// Calls whose arguments are shown to the operator verbatim, including the helpers that write the shell
    /// status line and the benchmark progress channel.
    /// </summary>
    private static readonly Regex DialogCallPattern = new(
        @"(?<!\w)(ShowError|ShowInfo|ShowWarning|Confirm|ConfirmDanger|ConfirmWarning|WorkflowPrompt|SetStatus)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Properties whose value is rendered in the window. Matched by suffix rather than by name so that a new
    /// status or summary property is covered the day it is written, and including the object-initializer
    /// members that end up inside a rendered report.
    /// </summary>
    private static readonly Regex BoundTextAssignmentPattern = new(
        @"(?<!\w)(?:this\.|_?[A-Za-z]\w*\.)?" +
        @"(_?\w*(?:Text|Summary|Results|Status|Detail|Remediation|Title|Filter|Message))\s*=(?!=)",
        RegexOptions.Compiled);

    /// <summary>Text appended to a report or pushed through a progress channel before it reaches a binding.</summary>
    private static readonly Regex ReportBuilderPattern = new(
        @"(?<!\w)(AppendLine|AppendFormat|Report)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex InterpolationHole = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Public string members. Every one of these is a candidate binding source, and several render straight
    /// into the window without ever passing through a status property.
    /// </summary>
    private static readonly Regex PublicTextMemberPattern = new(
        @"public\s+string\??\s+(?<name>[A-Z]\w*)\s*(?<body>=>|\{)",
        RegexOptions.Compiled);

    [Fact]
    public void ConvertedViewModels_AssignNoEnglishLiterals()
    {
        var offenders = ScanViewModels()
            .Where(v => !PendingConversion.Contains(v.File))
            .ToList();

        Assert.True(offenders.Count == 0,
            "View model message(s) must resolve through LocExtension rather than an English literal: " +
            string.Join("; ", offenders.Take(10).Select(v => $"{v.File}:{v.Line}:{v.Literal}")));
    }

    [Fact]
    public void PendingConversionList_NamesOnlyViewModelsThatStillHaveLiterals()
    {
        var findings = ScanViewModels();
        var stale = PendingConversion
            .Where(file => findings.All(v => v.File != file))
            .ToList();

        Assert.True(stale.Count == 0,
            "These view models are localized and must be removed from PendingConversion so the gate covers them: " +
            string.Join(", ", stale));
    }

    [Fact]
    public void PendingConversionList_NamesRealFiles()
    {
        var dir = ViewModelDirectory();
        var missing = PendingConversion.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();

        Assert.True(missing.Count == 0,
            "PendingConversion names file(s) that no longer exist: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Proves the scanner reacts to the shape of literal it is meant to catch, so a green run means the source
    /// is clean rather than the regexes having stopped matching.
    /// </summary>
    [Theory]
    [InlineData("_dialog.ShowError($\"Restore failed: {ex.Message}\", \"Restore\");")]
    [InlineData("_dialog.ShowInfo(\n    \"Snapshot exported to disk.\",\n    \"Exported\");")]
    [InlineData("StatusText = ready ? \"All good here.\" : \"Something went wrong.\";")]
    [InlineData("SummaryText = \"Disk usage scan cancelled.\";")]
    [InlineData("CloneProgressText = \"Starting sector clone...\";")]
    [InlineData("ImagePreflightSummary = \"Choose a destination path.\";")]
    [InlineData("public string DiskCapacityText => disk is null ? \"No disk selected\" : disk.Name;")]
    [InlineData("public string Summary\n{\n    get { return \"Select a disk first.\"; }\n}")]
    [InlineData("if (!_dialog.Confirm(\"Are you sure about this?\", \"Confirm\")) return;")]
    [InlineData("SetStatus(\"Refreshing the workspace...\");")]
    [InlineData("sb.AppendLine($\"Read {rate} MB/s on disk {n}\");")]
    [InlineData("progress.Report(\"Sequential Write in progress...\");")]
    [InlineData("var dlg = new SaveFileDialog { Title = \"Export Support Bundle\" };")]
    [InlineData("var dlg = new SaveFileDialog { Filter = \"ZIP Archive (*.zip)|*.zip\" };")]
    [InlineData("new Capability { Detail = \"Checking smartctl support...\" };")]
    [InlineData("new Audit { Remediation = \"Refresh disks and rerun the audit.\" };")]
    [InlineData("this.StatusText = \"Loading disk health data...\";")]
    public void Scanner_FlagsAnEnglishLiteral(string source)
    {
        Assert.NotEmpty(FindLiterals(source, "Sample.cs"));
    }

    [Theory]
    [InlineData("_dialog.ShowError(LocExtension.Format(\"RestoreBlocked\", ex.Message), LocExtension.Get(\"RestoreSnapshotTitle\"));")]
    [InlineData("StatusText = LocExtension.Get(\"UsageScanCancelled\");")]
    [InlineData("_dialog.ShowError(string.Join(\"\\n\", errors), LocExtension.Get(\"ExportErrorTitle\"));")]
    [InlineData("_dialog.ShowInfo(string.Join(\", \", names), LocExtension.Get(\"ExportErrorTitle\"));")]
    [InlineData("public string Caption => $\"{Size} | {FileSystem} | {Details}\";")]
    // Text that stays English on purpose.
    [InlineData("StatusText = LocExtension.Get(\"Ready\");\n_log.Log(\"Disk usage scan cancelled.\");")]
    [InlineData("var cmd = $\"Clear-Disk -Number {n} -RemoveData -Confirm:$false\";\nStatusText = LocExtension.Get(\"Ready\");")]
    [InlineData("throw new InvalidOperationException(\"Could not locate the EFI System Partition.\");")]
    public void Scanner_AcceptsAResourceLookup(string source)
    {
        Assert.Empty(FindLiterals(source, "Sample.cs"));
    }

    private static List<(string File, int Line, string Literal)> ScanViewModels()
    {
        var findings = new List<(string, int, string)>();

        foreach (var file in Directory.GetFiles(ViewModelDirectory(), "*.cs"))
            findings.AddRange(FindLiterals(File.ReadAllText(file), Path.GetFileName(file)));

        return findings;
    }

    private static List<(string File, int Line, string Literal)> FindLiterals(string source, string fileName)
    {
        var findings = new List<(string, int, string)>();
        var excluded = ExcludedSpans(source);

        foreach (Match match in DialogCallPattern.Matches(source))
        {
            var open = source.IndexOf('(', match.Index);
            findings.AddRange(LiteralsIn(source, open + 1, EndOfCall(source, open), fileName, excluded));
        }

        foreach (Match match in BoundTextAssignmentPattern.Matches(source))
        {
            var start = match.Index + match.Length;
            findings.AddRange(LiteralsIn(source, start, EndOfStatement(source, start), fileName, excluded));
        }

        // Reports assembled a piece at a time before being assigned. Scanning only the assignment would miss
        // every line of a StringBuilder-built summary.
        foreach (Match match in ReportBuilderPattern.Matches(source))
        {
            var open = source.IndexOf('(', match.Index);
            findings.AddRange(LiteralsIn(source, open + 1, EndOfCall(source, open), fileName, excluded));
        }

        foreach (Match match in PublicTextMemberPattern.Matches(source))
        {
            var start = match.Groups["body"].Index;
            var end = match.Groups["body"].Value == "=>"
                ? EndOfStatement(source, start + 2)
                : EndOfBlock(source, start);

            findings.AddRange(LiteralsIn(source, start, end, fileName, excluded));
        }

        return findings
            .GroupBy(f => (f.Item1, f.Item2, f.Item3))
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>
    /// A literal is a message when it holds a letter and a space. Resource keys are single words and
    /// separators such as "\n" or ", " carry no letters, so neither is mistaken for prose.
    /// </summary>
    private static IEnumerable<(string File, int Line, string Literal)> LiteralsIn(
        string source, int start, int end, string fileName, List<(int Start, int End)> excluded)
    {
        for (var i = start; i < end; i++)
        {
            if (source[i] != '"')
                continue;

            if (excluded.Any(span => i >= span.Start && i < span.End))
            {
                i = SkipLiteral(source, i);
                continue;
            }

            var text = new StringBuilder();
            var cursor = i + 1;
            while (cursor < source.Length && source[cursor] != '"')
            {
                if (source[cursor] == '\\' && cursor + 1 < source.Length)
                {
                    text.Append(source[cursor + 1] switch { 'n' => '\n', 't' => '\t', var c => c });
                    cursor += 2;
                    continue;
                }

                text.Append(source[cursor]);
                cursor++;
            }

            var value = text.ToString();

            // Placeholders carry expression names, not prose, so "{Name} | {Tab}" is a separator rather
            // than a message. Strip them before deciding whether any English is left.
            var prose = InterpolationHole.Replace(value, "");
            if (prose.Any(char.IsLetter) && prose.Contains(' '))
                yield return (fileName, LineOf(source, i), Truncate(value));

            i = cursor;
        }
    }

    private static int EndOfCall(string source, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < source.Length; i++)
        {
            if (source[i] == '"')
            {
                i = SkipLiteral(source, i);
                continue;
            }

            if (source[i] == '(') depth++;
            else if (source[i] == ')' && --depth == 0) return i;
        }

        return source.Length;
    }

    private static int EndOfStatement(string source, int start)
    {
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '"')
            {
                i = SkipLiteral(source, i);
                continue;
            }

            if (source[i] is '(' or '[' or '{') depth++;
            else if (source[i] is ')' or ']' or '}') depth--;
            // A comma ends an object-initializer member the same way a semicolon ends a statement, so one
            // member's value cannot drag the next member's text into scope.
            else if (source[i] is ';' or ',' && depth <= 0) return i;
        }

        return source.Length;
    }

    /// <summary>
    /// Text that stays English on purpose: activity log lines, which travel in support bundles; exception
    /// messages, which are for developers; and the shell commands the app runs.
    /// </summary>
    private static readonly Regex EnglishOnPurpose = new(
        @"(?<!\w)(Log|LogError|throw new \w+|RunPowerShellAsync|RunExeAsync|RunDiskpartAsync|" +
        @"SaveSnapshotForDestructiveOperationAsync|Register|EscapePowerShellString)\s*\(",
        RegexOptions.Compiled);

    private static List<(int Start, int End)> ExcludedSpans(string source) =>
        EnglishOnPurpose.Matches(source)
            .Select(m => source.IndexOf('(', m.Index))
            .Where(open => open >= 0)
            .Select(open => (open, EndOfCall(source, open)))
            .ToList();

    private static int EndOfBlock(string source, int openBrace)
    {
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '"')
            {
                i = SkipLiteral(source, i);
                continue;
            }

            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return i;
        }

        return source.Length;
    }

    private static int SkipLiteral(string source, int quote)
    {
        for (var i = quote + 1; i < source.Length; i++)
        {
            if (source[i] == '\\') { i++; continue; }
            if (source[i] == '"') return i;
        }

        return source.Length - 1;
    }

    private static int LineOf(string source, int index) =>
        source.Take(index).Count(c => c == '\n') + 1;

    private static string Truncate(string value) =>
        value.Length <= 40 ? value : value[..40] + "...";

    private static string ViewModelDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PartitionPilot", "ViewModels");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the PartitionPilot ViewModels directory.");
    }
}
