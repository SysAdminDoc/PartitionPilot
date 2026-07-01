using System.Globalization;

namespace PartitionPilot;

public sealed record PartitionPlanRequest(
    string Operation,
    int? DiskNumber,
    int? PartitionNumber,
    string? FileSystem,
    string? Label,
    string? Letter,
    string? Size,
    bool Apply);

public sealed record PartitionPlanResult(
    string Operation,
    int? DiskNumber,
    int? PartitionNumber,
    string Description,
    string RiskLevel,
    string DiskpartScript,
    bool WillApply);

public static class PartitionPlanService
{
    public static PartitionPlanResult Build(PartitionPlanRequest request)
    {
        var op = (request.Operation ?? "").Trim().ToLowerInvariant();
        return op switch
        {
            "delete" => BuildDelete(request),
            "format" => BuildFormat(request),
            "change-letter" => BuildChangeLetter(request),
            "create" => BuildCreate(request),
            _ => throw new ArgumentException($"Unknown plan operation: {request.Operation}. Supported: delete, format, change-letter, create.")
        };
    }

    public static long ParseSizeMB(string sizeText)
    {
        var normalized = (sizeText ?? "").Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("Size is required.");

        long result;
        if (normalized.EndsWith("TB", StringComparison.Ordinal))
            result = checked((long)(ParsePositiveDouble(normalized.Replace("TB", "")) * 1024 * 1024));
        else if (normalized.EndsWith("GB", StringComparison.Ordinal))
            result = checked((long)(ParsePositiveDouble(normalized.Replace("GB", "")) * 1024));
        else if (normalized.EndsWith("MB", StringComparison.Ordinal))
            result = checked((long)ParsePositiveDouble(normalized.Replace("MB", "")));
        else
            result = long.Parse(normalized, NumberStyles.None, CultureInfo.InvariantCulture);

        if (result <= 0)
            throw new ArgumentException($"Size must be positive: {sizeText}");
        return result;
    }

    private static double ParsePositiveDouble(string text)
    {
        var value = double.Parse(text, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentException($"Size must be a positive finite number: {text}");
        return value;
    }

    private static PartitionPlanResult BuildDelete(PartitionPlanRequest request)
    {
        RequireDiskAndPartition(request, "delete");
        return new PartitionPlanResult(
            "delete",
            request.DiskNumber,
            request.PartitionNumber,
            $"Delete partition {request.PartitionNumber!.Value} on disk {request.DiskNumber!.Value}",
            "High",
            $"select disk {request.DiskNumber.Value}\nselect partition {request.PartitionNumber.Value}\ndelete partition override",
            request.Apply);
    }

    private static PartitionPlanResult BuildFormat(PartitionPlanRequest request)
    {
        RequireDiskAndPartition(request, "format");
        var fs = ProcessRunner.ValidateFileSystem(request.FileSystem ?? "NTFS");
        var formatCapability = FilesystemCapabilityService.Evaluate(fs, FilesystemOperation.Format);
        if (!formatCapability.IsAllowed)
            throw new ArgumentException(formatCapability.Reason);
        var label = ProcessRunner.SanitizeLabel(request.Label ?? "");
        var description = $"Format partition {request.PartitionNumber!.Value} on disk {request.DiskNumber!.Value} as {fs}" +
                          (label.Length > 0 ? $" (label: {label})" : "");
        var script = $"select disk {request.DiskNumber.Value}\nselect partition {request.PartitionNumber.Value}\nformat fs={fs}{(label.Length > 0 ? $" label=\"{label}\"" : "")} quick";

        return new PartitionPlanResult("format", request.DiskNumber, request.PartitionNumber, description, "High", script, request.Apply);
    }

    private static PartitionPlanResult BuildChangeLetter(PartitionPlanRequest request)
    {
        RequireDiskAndPartition(request, "change-letter");
        var letter = ValidateLetter(request.Letter);
        return new PartitionPlanResult(
            "change-letter",
            request.DiskNumber,
            request.PartitionNumber,
            $"Assign letter {letter}: to partition {request.PartitionNumber!.Value} on disk {request.DiskNumber!.Value}",
            "Normal",
            $"select disk {request.DiskNumber.Value}\nselect partition {request.PartitionNumber.Value}\nassign letter={letter}",
            request.Apply);
    }

    private static PartitionPlanResult BuildCreate(PartitionPlanRequest request)
    {
        if (!request.DiskNumber.HasValue)
            throw new ArgumentException("--disk N required for create.");

        var sizeText = request.Size ?? "max";
        var sizeClause = sizeText.Equals("max", StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" size={ParseSizeMB(sizeText)}";
        var fs = ProcessRunner.ValidateFileSystem(request.FileSystem ?? "NTFS");
        var createCapability = FilesystemCapabilityService.Evaluate(fs, FilesystemOperation.Create);
        if (!createCapability.IsAllowed)
            throw new ArgumentException(createCapability.Reason);
        var label = ProcessRunner.SanitizeLabel(request.Label ?? "");
        var description = $"Create{(sizeClause.Length > 0 ? sizeClause.Trim() + " MB" : " max-size")} {fs} partition on disk {request.DiskNumber.Value}";
        var script = $"select disk {request.DiskNumber.Value}\ncreate partition primary{sizeClause}\nformat fs={fs}{(label.Length > 0 ? $" label=\"{label}\"" : "")} quick\nassign";

        return new PartitionPlanResult("create", request.DiskNumber, null, description, "Normal", script, request.Apply);
    }

    private static void RequireDiskAndPartition(PartitionPlanRequest request, string operation)
    {
        if (!request.DiskNumber.HasValue || !request.PartitionNumber.HasValue)
            throw new ArgumentException($"--disk N and --partition P required for {operation}.");
    }

    private static string ValidateLetter(string? letterText)
    {
        var letter = letterText?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(letter) || letter.Length != 1 || !char.IsLetter(letter[0]))
            throw new ArgumentException("--letter X required (single letter A-Z).");
        return letter;
    }
}
