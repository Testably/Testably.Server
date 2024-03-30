using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Testably.Server.Controllers;

[Route("pr-status-check")]
[ApiController]
[AllowAnonymous]
public class PullRequestStatusCheckController : ControllerBase
{
    private readonly ILogger<PullRequestStatusCheckController> _logger;

    public PullRequestStatusCheckController(
        ILogger<PullRequestStatusCheckController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> OnPullRequestChanged(
        [FromBody] PullRequestModel pullRequestModel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received {PullRequestModel}", pullRequestModel);

        return NoContent();
    }

    public class PullRequestModel
    {
        public string Action { get; set; } = "";
        public int Number { get; set; }
    }
}