using System;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using DruzhokBot.Domain.Interfaces;
using Moq;
using Telegram.Bot.Types;
using Xunit;

namespace Tests;

public class UserRiskScorerTests
{
    private static Mock<ITelegramBotClientWrapper> WrapperWithPhotoCount(int totalCount)
    {
        var mock = new Mock<ITelegramBotClientWrapper>();
        mock.Setup(w => w.GetUserProfilePhotosAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfilePhotos { TotalCount = totalCount, Photos = Array.Empty<PhotoSize[]>() });
        return mock;
    }

    private static Mock<ITelegramBotClientWrapper> WrapperWithPhotoFailure()
    {
        var mock = new Mock<ITelegramBotClientWrapper>();
        mock.Setup(w => w.GetUserProfilePhotosAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network"));
        return mock;
    }

    [Fact]
    public async Task NoUsername_Alone_Is_Low()
    {
        var scorer = new UserRiskScorer();
        var user = new User { Id = 1, Username = null, FirstName = "Andriy", LanguageCode = "uk" };
        var wrapper = WrapperWithPhotoCount(1);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.Low, assessment.Level);
    }

    [Fact]
    public async Task AutoUsername_NoPhoto_NullLang_Is_High()
    {
        var scorer = new UserRiskScorer();
        // Username matches ^[A-Za-z][a-z]*\d{4,}$ → suspicious_username
        var user = new User { Id = 1, Username = "John12345", FirstName = "John", LanguageCode = null };
        var wrapper = WrapperWithPhotoCount(0);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.High, assessment.Level);
    }

    [Fact]
    public async Task NoUsername_NoPhoto_NullLang_Without_Veto_Is_Low()
    {
        var scorer = new UserRiskScorer();
        // Score: no-username +2, no-photo +2, null-lang +1 = 5 — BUT no suspicious_username and no weird_first_name.
        var user = new User { Id = 1, Username = null, FirstName = "Andriy", LanguageCode = null };
        var wrapper = WrapperWithPhotoCount(0);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.Low, assessment.Level);
    }

    [Fact]
    public async Task WeirdFirstName_NoPhoto_Is_High()
    {
        var scorer = new UserRiskScorer();
        // FirstName is all digits → weird_first_name (+2) + no-photo (+2) = 4, veto met (boundary case).
        var user = new User { Id = 1, Username = "andrew_w", FirstName = "4242", LanguageCode = "en" };
        var wrapper = WrapperWithPhotoCount(0);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.High, assessment.Level);
    }

    [Fact]
    public async Task Premium_Offsets_Other_Signals()
    {
        var scorer = new UserRiskScorer();
        // no-photo +2, no-username +2, premium −3 → total 1 → Low
        var user = new User { Id = 1, Username = null, FirstName = "Andriy", IsPremium = true, LanguageCode = "uk" };
        var wrapper = WrapperWithPhotoCount(0);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.Low, assessment.Level);
    }

    [Fact]
    public async Task PhotoCheck_Failure_Contributes_Zero_Points()
    {
        var scorer = new UserRiskScorer();
        // Without the -photo signal, user would score: suspicious_username +2 + null-lang +1 = 3 → Low.
        var user = new User { Id = 1, Username = "John12345", FirstName = "John", LanguageCode = null };
        var wrapper = WrapperWithPhotoFailure();

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.Low, assessment.Level);
    }

    [Fact]
    public async Task Normal_Looking_User_Is_Low()
    {
        var scorer = new UserRiskScorer();
        var user = new User { Id = 1, Username = "andrew_w", FirstName = "Andrew", LanguageCode = "uk" };
        var wrapper = WrapperWithPhotoCount(3);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.Low, assessment.Level);
    }

    [Fact]
    public async Task SingleCharFirstName_NoPhoto_Is_High()
    {
        var scorer = new UserRiskScorer();
        // single-char first name → weird (+2), no-photo (+2) = 4, veto met
        var user = new User { Id = 1, Username = "andrew_w", FirstName = "A", LanguageCode = "uk" };
        var wrapper = WrapperWithPhotoCount(0);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.High, assessment.Level);
    }

    [Fact]
    public async Task High_Risk_Reason_Contains_All_Fired_Signals()
    {
        var scorer = new UserRiskScorer();
        // suspicious_username + no_photo + null_language → score 5, veto met
        var user = new User { Id = 1, Username = "John12345", FirstName = "John", LanguageCode = null };
        var wrapper = WrapperWithPhotoCount(0);

        var assessment = await scorer.ScoreAsync(user, wrapper.Object, CancellationToken.None);

        Assert.Equal(UserRiskLevel.High, assessment.Level);
        Assert.Contains("suspicious_username", assessment.Reason);
        Assert.Contains("no_photo", assessment.Reason);
        Assert.Contains("null_language", assessment.Reason);
    }
}
