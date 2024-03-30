using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Testably.Server.Controllers;

[ApiController]
[Route("lifetime")]
[AllowAnonymous]
public class LifetimeController(IHostApplicationLifetime applicationLifetime) : ControllerBase
{
	[HttpPost("quit")]
	public IActionResult Quit()
	{
		applicationLifetime.StopApplication();
		return NoContent();
	}
}