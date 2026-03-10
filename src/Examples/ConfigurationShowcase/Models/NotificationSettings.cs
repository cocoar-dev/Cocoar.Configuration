namespace ConfigurationShowcase.Models;

public interface INotificationSettings
{
    string SmtpServer { get; }
    int SmtpPort { get; }
    string FromAddress { get; }
    bool EnableSlack { get; }
    string SlackWebhookUrl { get; }
}

public class NotificationSettings : INotificationSettings
{
    public string SmtpServer { get; set; } = "smtp.example.com";
    public int SmtpPort { get; set; } = 587;
    public string FromAddress { get; set; } = "noreply@example.com";
    public bool EnableSlack { get; set; }
    public string SlackWebhookUrl { get; set; } = "";
}
