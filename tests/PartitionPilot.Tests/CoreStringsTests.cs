using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PartitionPilot.Tests;

/// <summary>
/// Core builds the confirmations that gate wiping, cloning and overwriting a disk. It is a plain library
/// shared with <c>pp.exe</c>, so it resolves those through <see cref="CoreStrings"/> rather than through the
/// WPF markup extension. These tests pin the English wording, prove the lookup follows the thread's UI
/// culture, and prove the builders no longer carry literals.
/// </summary>
// Changes the process-wide UI culture, so it shares the collection with the other localization tests.
[Collection(LocalizationCollection.Name)]
public class CoreStringsTests
{
    private static readonly DiskIdentitySnapshot Identity = new()
    {
        DiskNumber = 3,
        FriendlyName = "Samsung SSD 990",
        Size = 1_000_204_886_016,
        PartitionStyle = "GPT",
        SerialNumber = "S1A2B3C"
    };

    [Fact]
    public void FullDiskPrompts_ReproduceTheEnglishTextTheyReplaced()
    {
        var prompts = WipeWorkflowService.BuildFullDiskPrompts(Identity, "Quick", 1_000_204_886_016);

        Assert.Equal(3, prompts.Count);
        Assert.Equal("Wipe Disk -- Confirmation 1 of 3", prompts[0].Title);
        Assert.Equal(
            $"WARNING: You are about to wipe:\n{Identity.ConfirmationSummary}\n\n" +
            "ALL DATA ON THIS DISK WILL BE PERMANENTLY DESTROYED.\n\nContinue?",
            prompts[0].Message);

        Assert.Equal("Wipe Disk -- Confirmation 2 of 3", prompts[1].Title);
        Assert.Equal(
            $"Are you absolutely sure you want to wipe Disk 3?\n\nTarget:\n{Identity.ConfirmationSummary}\n" +
            $"Size: {SizeUtil.Format(1_000_204_886_016)}\nMode: Quick\n\nThis CANNOT be undone.",
            prompts[1].Message);

        Assert.Equal("Wipe Disk -- FINAL Confirmation", prompts[2].Title);
        Assert.Equal("FINAL WARNING: Click Yes to begin disk wipe immediately.", prompts[2].Message);
    }

    [Fact]
    public void DodPrompts_ReproduceTheEnglishTextTheyReplaced()
    {
        var prompts = WipeWorkflowService.BuildDodPrompts(Identity, 7, 512_000);

        Assert.Equal("DoD 7-Pass Wipe -- Confirmation 1 of 2", prompts[0].Title);
        Assert.Equal(
            $"DoD 5220.22-M 7-PASS WIPE target:\n{Identity.ConfirmationSummary}\n\n" +
            $"Size: {SizeUtil.Format(512_000)}\n\n" +
            "ALL DATA WILL BE PERMANENTLY DESTROYED with multiple overwrite passes.\n\nContinue?",
            prompts[0].Message);

        Assert.Equal("DoD 7-Pass Wipe -- FINAL Confirmation", prompts[1].Title);
        Assert.Equal(
            "FINAL WARNING: 7-pass wipe will write the entire disk 7 times. " +
            "This may take hours on large drives. Click Yes to begin.",
            prompts[1].Message);
    }

    [Fact]
    public void NvmeSanitizePrompts_ReproduceTheEnglishTextTheyReplaced()
    {
        var prompts = WipeWorkflowService.BuildNvmeSanitizePrompts(
            Identity, SecureEraseService.SanitizeMethod.CryptoErase);

        Assert.Equal("NVMe Sanitize -- Confirmation 1 of 2", prompts[0].Title);
        Assert.Equal(
            $"NVMe FIRMWARE ERASE target:\n{Identity.ConfirmationSummary}\n\nMethod: CryptoErase\n\n" +
            "This sends a firmware-level sanitize command directly to the drive controller. " +
            "ALL DATA WILL BE PERMANENTLY AND IRREVERSIBLY DESTROYED.\n\n" +
            "This operation cannot be cancelled once started.",
            prompts[0].Message);

        Assert.Equal("NVMe Sanitize -- FINAL Confirmation", prompts[1].Title);
        Assert.Equal(
            "FINAL WARNING: NVMe sanitize is a hardware-level operation that erases ALL data " +
            "including data in over-provisioned and remapped sectors.\n\nProceed?",
            prompts[1].Message);
    }

    [Fact]
    public void SectorClonePrompts_ReproduceTheEnglishTextTheyReplaced()
    {
        var destination = new DiskIdentitySnapshot { DiskNumber = 4, FriendlyName = "WD Blue", Size = 500, PartitionStyle = "MBR" };
        var prompts = CloneWorkflowService.BuildSectorClonePrompts(Identity, destination);

        Assert.Equal("Confirm Sector Clone", prompts[0].Title);
        Assert.Equal(
            "WARNING: This will overwrite ALL data on the destination disk with a sector-by-sector copy.\n\n" +
            $"Source:\n{Identity.ConfirmationSummary}\n\nDestination:\n{destination.ConfirmationSummary}\n\n" +
            "This operation cannot be undone. Continue?",
            prompts[0].Message);

        Assert.Equal("Confirm Clone", prompts[1].Title);
        Assert.Equal(
            "FINAL CONFIRMATION: All data on the destination disk will be permanently overwritten " +
            "with a raw sector copy.",
            prompts[1].Message);
    }

    [Theory]
    // The encryption line carries its own leading and trailing newline, so a protected volume gets a blank
    // line on both sides of it. That is what the concatenation this replaced produced.
    [InlineData("Encrypted", "Wipe free space on E:?\n\nEncryption: Encrypted\n\nExisting files remain in place. Previously deleted data in free space will be overwritten.")]
    [InlineData("", "Wipe free space on E:?\n\nExisting files remain in place. Previously deleted data in free space will be overwritten.")]
    public void FreeSpacePrompt_ReproducesTheEnglishTextItReplaced(string encryptionStatus, string expected)
    {
        var prompt = WipeWorkflowService.BuildFreeSpacePrompt('e', encryptionStatus);

        Assert.Equal("Confirm Free-Space Wipe", prompt.Title);
        Assert.Equal(expected, prompt.Message);
    }

    [Fact]
    public void BitLockerDestructiveConfirmation_ReproducesTheEnglishTextItReplaced()
    {
        Assert.Equal(
            "Wipe Disk 3 will target BitLocker-protected data:\n  - C: Encrypted\n  - D: Encrypted\n\n" +
            "This can permanently destroy encrypted contents, recovery metadata, and any data protected " +
            "by recovery keys. Continue only if backups and recovery keys are available.",
            BitLockerPreflight.BuildDestructiveConfirmation("Wipe Disk 3", ["C: Encrypted", "D: Encrypted"]));

        Assert.Equal(
            "Wipe Disk 3 will target BitLocker-protected data:\nBitLocker-protected data\n\n" +
            "This can permanently destroy encrypted contents, recovery metadata, and any data protected " +
            "by recovery keys. Continue only if backups and recovery keys are available.",
            BitLockerPreflight.BuildDestructiveConfirmation("Wipe Disk 3", []));
    }

    /// <summary>
    /// These two take an operation name the caller has already translated, so leaving the sentence around it
    /// in English produced a message half in each language.
    /// </summary>
    [Fact]
    public void BitLockerBlockedMessages_ReproduceTheEnglishTextTheyReplaced()
    {
        Assert.Equal(
            "Extend partition 2 is blocked for D: because BitLocker protection is active or unknown.\n\n" +
            "Encryption state: Encrypted\n\n" +
            "Suspend BitLocker protection, unlock the volume if needed, refresh PartitionPilot, then retry.",
            BitLockerPreflight.BuildMutationBlockedMessage("Extend partition 2", "D:", "Encrypted"));

        Assert.Equal(
            "Wipe free space on E: requires E: to be unlocked first.\n\n" +
            "Encryption state: BitLocker: Not reported\n\n" +
            "Unlock the volume in Windows, refresh PartitionPilot, then retry.",
            BitLockerPreflight.BuildUnlockRequiredMessage("Wipe free space on E:", "E:", null));
    }

    /// <summary>
    /// The whole message has to change language together. A translated operation name spliced into an
    /// English sentence is the defect this replaced.
    /// </summary>
    [Fact]
    public void BitLockerBlockedMessage_TranslatesTheSentenceAroundTheOperation()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");

            var message = BitLockerPreflight.BuildMutationBlockedMessage("Op", "D:", "Encrypted");

            // The pseudo-locale pads rather than translating words, so the proof is that the padding wraps
            // the whole message: the closing marker is only there if the sentence after the operation name
            // came from the resource rather than from a literal in the builder.
            Assert.StartsWith("[", message);
            Assert.EndsWith("!!!]", message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// The point of the whole change: Core's text follows the thread's UI culture, which is what the app sets
    /// when a language is chosen and what the CLI inherits from the operating system.
    /// </summary>
    [Fact]
    public void Prompts_FollowTheThreadUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var english = WipeWorkflowService.BuildFullDiskPrompts(Identity, "Quick", 512).Last().Message;

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");
            var translated = WipeWorkflowService.BuildFullDiskPrompts(Identity, "Quick", 512).Last().Message;

            Assert.NotEqual(english, translated);
            Assert.Contains(english, translated);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Get_FallsBackToEnglishForACultureWithNoTranslation()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");

            Assert.Equal("Confirm Clone", CoreStrings.Get("SectorCloneFinalTitle"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Get_ReturnsTheBracketedKeyForAMissingKey()
    {
        Assert.Equal("[NoSuchCoreKey]", CoreStrings.Get("NoSuchCoreKey"));
    }

    [Fact]
    public void Format_FallsBackToTheTemplateRatherThanThrowingOnAMismatch()
    {
        Assert.Equal(CoreStrings.Get("WipeDisk2Body"), CoreStrings.Format("WipeDisk2Body"));
    }

    [Fact]
    public void PseudoLocale_HasEveryEnglishKeyWithMatchingPlaceholders()
    {
        var english = ReadResources("CoreStrings.resx");
        var pseudo = ReadResources("CoreStrings.qps-ploc.resx");

        var missing = english.Keys.Where(k => !pseudo.ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "Pseudo-locale is missing: " + string.Join(", ", missing));

        var extra = pseudo.Keys.Where(k => !english.ContainsKey(k)).Order().ToList();
        Assert.True(extra.Count == 0, "Pseudo-locale has keys English does not: " + string.Join(", ", extra));

        var mismatched = english
            .Where(pair => !Placeholders(pair.Value).SetEquals(Placeholders(pseudo[pair.Key])))
            .Select(pair => pair.Key)
            .ToList();

        Assert.True(mismatched.Count == 0, "Placeholder mismatch on: " + string.Join(", ", mismatched));
    }

    /// <summary>
    /// Guards the builders themselves rather than whole files: the same classes also hold robocopy switches
    /// and log text, which stay English on purpose.
    /// </summary>
    [Theory]
    [InlineData("WipeWorkflowService.cs")]
    [InlineData("CloneWorkflowService.cs")]
    public void PromptBuilders_ContainNoEnglishLiterals(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(CoreRoot(), "Services", fileName));
        var violations = new List<string>();

        foreach (Match builder in BuilderSignature.Matches(source))
        {
            var body = MethodBody(source, builder.Index);
            foreach (Match literal in ProseLiteral.Matches(body))
            {
                var value = Regex.Replace(literal.Groups["value"].Value, @"\{[^{}]*\}", "");
                if (value.Any(char.IsLetter) && value.Contains(' '))
                    violations.Add($"{fileName}:{builder.Groups["name"].Value}:{literal.Groups["value"].Value}");
            }
        }

        Assert.True(violations.Count == 0,
            "Core prompt text must resolve through CoreStrings: " + string.Join("; ", violations.Take(10)));
    }

    /// <summary>
    /// Proves the guard above reacts, so a green run means the builders are clean rather than the regex
    /// having stopped matching them.
    /// </summary>
    [Fact]
    public void PromptBuilderGuard_MatchesEveryBuilder()
    {
        var wipe = File.ReadAllText(Path.Combine(CoreRoot(), "Services", "WipeWorkflowService.cs"));
        var names = BuilderSignature.Matches(wipe).Select(m => m.Groups["name"].Value).ToList();

        Assert.Contains("BuildFreeSpacePrompt", names);
        Assert.Contains("BuildFullDiskPrompts", names);
        Assert.Contains("BuildDodPrompts", names);
        Assert.Contains("BuildNvmeSanitizePrompts", names);

        var planted = MethodBody("void BuildXPrompts() { var a = \"all data will be destroyed\"; }", 0);
        Assert.Contains("all data", planted);
    }

    private static readonly Regex BuilderSignature = new(
        @"(?<name>Build\w*(?:Prompt|Prompts|Summary|Confirmation))\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ProseLiteral = new("\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    /// <summary>Text from the signature to the closing brace of the method that follows it.</summary>
    private static string MethodBody(string source, int signatureIndex)
    {
        var open = source.IndexOf('{', signatureIndex);
        if (open < 0)
            return "";

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..i];
        }

        return source[open..];
    }

    private static HashSet<int> Placeholders(string value) =>
        Regex.Matches(value, @"\{(?<index>\d+)(?:[,:][^}]*)?\}")
            .Select(m => int.Parse(m.Groups["index"].Value))
            .ToHashSet();

    private static Dictionary<string, string> ReadResources(string fileName) =>
        XDocument.Load(Path.Combine(CoreRoot(), "Properties", fileName)).Root!
            .Elements("data")
            .Where(d => d.Attribute("name") is not null)
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")?.Value ?? "");

    private static string CoreRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PartitionPilot.Core");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PartitionPilot.Core.");
    }
}
