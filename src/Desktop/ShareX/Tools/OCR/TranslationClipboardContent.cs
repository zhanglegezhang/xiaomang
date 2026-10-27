using System;

namespace ShareX;

internal static class TranslationClipboardContent
{
    // Use the service's language metadata, never guess from OCR mode or strip characters.
    internal static string Compose(string original, BaiduTranslationResult result, TranslationCopyContent content)
    {
        if (result == null || string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(result.Text)) return null;
        string chinese = result.TargetLanguage == "zh" ? result.Text : result.SourceLanguage == "zh" ? original : null;
        string english = result.TargetLanguage == "en" ? result.Text : result.SourceLanguage == "en" ? original : null;
        return content switch
        {
            TranslationCopyContent.Chinese => chinese,
            TranslationCopyContent.English => english,
            TranslationCopyContent.ChineseAndEnglish when chinese != null && english != null =>
                chinese.Trim() + Environment.NewLine + Environment.NewLine + english.Trim(),
            _ => null
        };
    }
}
