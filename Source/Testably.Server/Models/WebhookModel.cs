namespace Testably.Server.Models;

public class WebhookModel<T>
{
	public string Event { get; set; } = "";
	public T Payload { get; set; } = default!;
}