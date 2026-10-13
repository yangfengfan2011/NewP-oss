using System.Text;

namespace ICP.Platform.Modules.Identity.Application;

internal static class IdentityInputNormalizer
{
    public static string NormalizeAccountCode(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
        var runes = normalized.EnumerateRunes().ToArray();
        if (runes.Length is < 3 or > 64 || runes.Any(static rune =>
                !Rune.IsLetterOrDigit(rune) && rune.Value is not ('.' or '_' or '-' or '@')))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidAccountCode);
        }

        return normalized;
    }

    public static string NormalizeDisplayName(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.EnumerateRunes().Count() is < 1 or > 128)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidDisplayName);
        }

        return normalized;
    }

    public static string NormalizeReason(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.EnumerateRunes().Count() is < 1 or > 500)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidReason);
        }

        return normalized;
    }
}
