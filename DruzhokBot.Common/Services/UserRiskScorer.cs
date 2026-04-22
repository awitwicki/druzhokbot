using System.Text.RegularExpressions;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using Telegram.Bot.Types;

namespace DruzhokBot.Common.Services;

public class UserRiskScorer : IUserRiskScorer
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly Regex AutoUsernamePattern =
        new(@"^([A-Za-z][a-z]*\d{4,}|[a-z]+_[a-z]+_\d+)$", RegexOptions.Compiled);

    public async Task<UserRiskAssessment> ScoreAsync(
        User user,
        ITelegramBotClientWrapper botClient,
        CancellationToken cancellationToken)
    {
        var score = 0;
        var signals = new List<string>();
        var suspiciousUsername = false;
        var weirdFirstName = false;

        if (string.IsNullOrEmpty(user.Username))
        {
            score += 2;
            signals.Add("no_username");
        }
        else if (AutoUsernamePattern.IsMatch(user.Username))
        {
            score += 2;
            suspiciousUsername = true;
            signals.Add("suspicious_username");
        }

        var firstName = user.FirstName ?? string.Empty;
        if (string.IsNullOrEmpty(firstName)
            || firstName.Length == 1
            || firstName.All(char.IsDigit))
        {
            score += 2;
            weirdFirstName = true;
            signals.Add("weird_first_name");
        }
        else if (!firstName.Any(char.IsLetter))
        {
            score += 3;
            weirdFirstName = true;
            signals.Add("weird_first_name");
        }

        if (user.LanguageCode == null)
        {
            score += 1;
            signals.Add("null_language");
        }

        if (user.IsPremium == true)
        {
            score -= 3;
            signals.Add("premium");
        }

        try
        {
            var photos = await botClient.GetUserProfilePhotosAsync(
                user.Id, offset: 0, limit: 1, cancellationToken);
            if (photos.TotalCount == 0)
            {
                score += 2;
                signals.Add("no_photo");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to fetch profile photos for user {0}; contributing 0 to risk score", user.Id);
        }

        var isHigh = score >= 4 && (suspiciousUsername || weirdFirstName);
        var level = isHigh ? UserRiskLevel.High : UserRiskLevel.Low;
        var reason = string.Join(",", signals);
        return new UserRiskAssessment(level, reason);
    }
}
