using FluentValidation;
using FluentValidation.Results;

namespace Portal.Application.Features.Customers;

/// <summary>Upload limits (Spec 003, C7 / CR8).</summary>
public static class AttachmentRules
{
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>Allowed extensions and the content type we serve them with (the client's claimed type is ignored).</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>Validates the upload and returns a safe display name, the extension and the content type.</summary>
    public static (string FileName, string Extension, string ContentType) Validate(UploadedFile file)
    {
        var name = SafeFileName(file.FileName);
        var extension = Path.GetExtension(name);

        string? error = null;
        if (file.Length <= 0) error = "The file is empty.";
        else if (file.Length > MaxBytes) error = $"The file is larger than {MaxBytes / (1024 * 1024)} MB.";
        else if (!AllowedTypes.TryGetValue(extension, out _))
            error = $"Files of type '{extension}' are not allowed. Allowed: {string.Join(", ", AllowedTypes.Keys)}.";

        if (error is not null)
            throw new ValidationException([new ValidationFailure("File", error)]);

        return (name, extension, AllowedTypes[extension]);
    }

    private static string SafeFileName(string raw)
    {
        var name = Path.GetFileName(raw.Replace('\\', '/').Split('/').Last()).Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length > 255) name = name[^255..];
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }
}
