using System.Text.RegularExpressions;

namespace DruzhokBot.Common.Helpers;

public static class SpamChecker
{
    public static bool IsSpam(string text)
    {
        const string regexPattern = @"opensea\.io|(@\w+bot(\b))|(t\.me\/[^\s]+bot\b)";
        return Regex.IsMatch(text, regexPattern, RegexOptions.Compiled);
    }
}
