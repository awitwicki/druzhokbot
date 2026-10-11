namespace DruzhokBot.Domain.Entities;

public class ChatSettings
{
    public long ChatId { get; set; }

    public bool GreetingEnabled { get; set; }

    public string? GreetingText { get; set; }

    public DateTime UpdatedAt { get; set; }
}
