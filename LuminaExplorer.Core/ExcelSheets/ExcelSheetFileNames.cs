using System;
using System.Globalization;
using System.IO;
using Lumina.Data;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Helpers for the file naming scheme of Excel sheets.</summary>
/// <remarks>
/// A sheet <c>Name</c> consists of a header file <c>exd/Name.exh</c>, and data pages named
/// <c>exd/Name_StartRowId.exd</c> (language-neutral) or <c>exd/Name_StartRowId_LanguageCode.exd</c>.
/// Sheet names may contain path separators (<c>quest/000/ClsArc001_00001</c>) and underscores.
/// </remarks>
public static class ExcelSheetFileNames {
    public const string HeaderExtension = ".exh";
    public const string DataExtension = ".exd";

    public static string GetLanguageCode(Language language) => LanguageUtil.GetLanguageStr(language);

    public static bool TryParseLanguageCode(string code, out Language language)
    {
        foreach (var l in Enum.GetValues<Language>()) {
            if (l == Language.None)
                continue;
            if (!string.Equals(GetLanguageCode(l), code, StringComparison.OrdinalIgnoreCase))
                continue;
            language = l;
            return true;
        }

        language = Language.None;
        return false;
    }

    public static string GetLanguageDisplayName(Language language) => language switch {
        Language.None => "(none)",
        Language.Japanese => "Japanese (ja)",
        Language.English => "English (en)",
        Language.German => "German (de)",
        Language.French => "French (fr)",
        Language.ChineseSimplified => "Chinese Simplified (chs)",
        Language.ChineseTraditional => "Chinese Traditional (cht)",
        Language.Korean => "Korean (ko)",
        Language.TraditionalChinese => "Traditional Chinese (tc)",
        _ => $"{language} ({GetLanguageCode(language)})",
    };

    /// <summary>Gets the file name of a data page.</summary>
    /// <param name="sheetBaseName">Sheet name without directories (e.g. <c>Item</c>).</param>
    /// <param name="startRowId">First row ID of the page.</param>
    /// <param name="language">Language of the page.</param>
    public static string GetDataFileName(string sheetBaseName, uint startRowId, Language language) =>
        language == Language.None
            ? $"{sheetBaseName}_{startRowId}{DataExtension}"
            : $"{sheetBaseName}_{startRowId}_{GetLanguageCode(language)}{DataExtension}";

    public static string GetHeaderFileName(string sheetBaseName) => sheetBaseName + HeaderExtension;

    /// <summary>Parses the file name of a data page.</summary>
    /// <param name="fileName">File name, optionally with directories.</param>
    /// <param name="sheetBaseName">Sheet name without directories.</param>
    /// <param name="startRowId">First row ID of the page.</param>
    /// <param name="language">Language of the page, or <see cref="Language.None"/>.</param>
    /// <returns><c>true</c> if parsed.</returns>
    public static bool TryParseDataFileName(
        string fileName,
        out string sheetBaseName,
        out uint startRowId,
        out Language language)
    {
        sheetBaseName = string.Empty;
        startRowId = 0;
        language = Language.None;

        fileName = Path.GetFileName(fileName.Replace('\\', '/').TrimEnd('/'));
        if (!fileName.EndsWith(DataExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = fileName[..^DataExtension.Length];
        var sep = stem.LastIndexOf('_');
        if (sep <= 0)
            return false;

        if (TryParseLanguageCode(stem[(sep + 1)..], out var lang)) {
            language = lang;
            stem = stem[..sep];
            sep = stem.LastIndexOf('_');
            if (sep <= 0)
                return false;
        }

        if (!uint.TryParse(stem[(sep + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out startRowId))
            return false;

        sheetBaseName = stem[..sep];
        return true;
    }

    /// <summary>Gets the directory part of a sheet name (e.g. <c>quest/000</c>), or an empty string.</summary>
    public static string GetSheetDirectory(string sheetName)
    {
        var sep = sheetName.LastIndexOf('/');
        return sep == -1 ? string.Empty : sheetName[..sep];
    }

    /// <summary>Gets the sheet name (as in <c>root.exl</c>) from a full path to a header or data file.</summary>
    /// <param name="fullPath">Full path, such as <c>exd/quest/000/ClsArc001_00001.exh</c>.</param>
    /// <returns>Sheet name, such as <c>quest/000/ClsArc001_00001</c>, or <c>null</c> if not applicable.</returns>
    public static string? GetSheetNameFromPath(string fullPath)
    {
        fullPath = fullPath.Replace('\\', '/').Trim('/');
        var exdIndex = fullPath.StartsWith("exd/", StringComparison.OrdinalIgnoreCase)
            ? 0
            : fullPath.LastIndexOf("/exd/", StringComparison.OrdinalIgnoreCase) is var i and >= 0
                ? i + 1
                : -1;
        var dir = Path.GetDirectoryName(fullPath)?.Replace('\\', '/') ?? string.Empty;
        var name = Path.GetFileName(fullPath);
        string baseName;
        if (name.EndsWith(HeaderExtension, StringComparison.OrdinalIgnoreCase))
            baseName = name[..^HeaderExtension.Length];
        else if (TryParseDataFileName(name, out var b, out _, out _))
            baseName = b;
        else
            return null;

        if (exdIndex == -1)
            return baseName;

        dir = dir.Length <= exdIndex + 4 ? string.Empty : dir[(exdIndex + 4)..];
        return dir.Length == 0 ? baseName : $"{dir}/{baseName}";
    }
}
