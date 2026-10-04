namespace M2Server.App.Services;

public interface INotificationService
{
    void Show(string message, bool warning = false);
}