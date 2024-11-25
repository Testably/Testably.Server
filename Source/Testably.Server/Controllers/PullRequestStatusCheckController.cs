using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Testably.Server.Models;

namespace Testably.Server.Controllers;

[ApiController]
[Route("pr-status-check")]
[AllowAnonymous]
public class PullRequestStatusCheckController : ControllerBase
{
	private static readonly string[] RepositoryOwners = ["Testably", "aweXpect"];

	private const string SuccessMessage =
		"The PR title must conform to the conventional commits guideline.";

	private static readonly string[] ValidTypes =
	{
		"fix",
		"feat",
		"release",

		// https://github.com/conventional-changelog/commitlint/tree/master/%40commitlint/config-conventional
		"build",
		"chore",
		"ci",
  		"coverage",
		"docs",
		"perf",
		"refactor",
		"revert",
		"style",
		"test"
	};

	private readonly IHttpClientFactory _clientFactory;
	private readonly IConfiguration _configuration;
	private readonly ILogger<PullRequestStatusCheckController> _logger;

	public PullRequestStatusCheckController(
		IConfiguration configuration,
		IHttpClientFactory clientFactory,
		ILogger<PullRequestStatusCheckController> logger)
	{
		_configuration = configuration;
		_clientFactory = clientFactory;
		_logger = logger;
	}

	[HttpPost]
	public async Task<IActionResult> OnPullRequestChanged(
		[FromBody] PullRequestWebhookModel pullRequestModel,
		CancellationToken cancellationToken)
	{
		if (!HttpContext.Request.Headers.TryGetValue("x-github-event", out var value) ||
		    value != "pull_request")
		{
			return Ok("Ignore all events except 'pull_request'.");
		}

		if (pullRequestModel.Repository.Private ||
		    RepositoryOwners.All(repositoryOwner => pullRequestModel.Repository.Owner.Login != repositoryOwner))
		{
			return BadRequest($"Only public repositories from '{string.Join(", ", RepositoryOwners)}' are supported!");
		}

		var bearerToken = pullRequestModel.Repository.Owner.Login switch
		{
			"Testably" => _configuration.GetValue<string>("testablyToken"),
			"aweXpect" => _configuration.GetValue<string>("aweXpectToken"),
			_ => ""
		}
		if (string.IsNullOrEmpty(bearerToken))
		{
			_logger.LogWarning("Could not find valid bearer token for {Organization}",
				pullRequestModel.Repository.Owner.Login);
			return StatusCode(StatusCodes.Status403Forbidden,
				$"Could not find valid bearer token for {pullRequestModel.Repository.Owner.Login}");
		}

		using var client = _clientFactory.CreateClient("Proxied");
		client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Testably",
			Assembly.GetExecutingAssembly().GetName().Version.ToString()));
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", bearerToken);

		var owner = pullRequestModel.Repository.Owner.Login;
		var repo = pullRequestModel.Repository.Name;
		var prNumber = pullRequestModel.Number;
		var requestUri = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
		_logger.LogInformation("Try reading '{RequestUri}'", requestUri);
		var response = await client
			.GetAsync(requestUri, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var responseContent = await response.Content.ReadAsStringAsync();
			_logger.LogWarning("GitHub API '{RequestUri}' not available: {ResponseContent}", requestUri, responseContent);
			return StatusCode(StatusCodes.Status500InternalServerError,
				$"GitHub API '{requestUri}' not available: {responseContent}");
		}

		var jsonDocument = await JsonDocument.ParseAsync(
			await response.Content.ReadAsStreamAsync(cancellationToken),
			cancellationToken: cancellationToken);

		if (!jsonDocument.RootElement.TryGetProperty("title", out var titleProperty) ||
		    titleProperty.GetString() == null)
		{
			_logger.LogWarning("GitHub API '{RequestUri}' returned an invalid response (missing title).", requestUri);
			return StatusCode(StatusCodes.Status500InternalServerError,
				$"GitHub API '{requestUri}' returned an invalid response (missing title).");
		}

		if (!jsonDocument.RootElement.TryGetProperty("head", out var headProperty) ||
		    !headProperty.TryGetProperty("sha", out var shaProperty) ||
		    shaProperty.GetString() == null)
		{
			_logger.LogWarning("GitHub API '{RequestUri}' returned an invalid response (missing head.sha).", requestUri);
			return StatusCode(StatusCodes.Status500InternalServerError,
				$"GitHub API '{requestUri}' returned an invalid response (missing head.sha).");
		}

		var title = titleProperty.GetString()!;
		_logger.LogInformation("Validate title for PR #{PullRequest}: '{Title}'", prNumber, title);
		var commitSha = shaProperty.GetString();
		var statusUri = $"https://api.github.com/repos/{owner}/{repo}/statuses/{commitSha}";
		var hasValidTitle = ValidateTitle(title);
		// https://docs.github.com/en/rest/commits/statuses?apiVersion=2022-11-28#create-a-commit-status
		var json = JsonSerializer.Serialize(new
		{
			context = "Testably/Conventional-Commits",
			state = hasValidTitle ? "success" : "failure",
			description = SuccessMessage
		});
		using var content = new StringContent(json);
		await client.PostAsync(statusUri, content, cancellationToken);
		return NoContent();
	}

	private bool ValidateTitle(string title)
	{
		foreach (var validType in ValidTypes)
		{
			if (!title.StartsWith(validType))
			{
				continue;
			}

			var index = title.IndexOf(':');
			if (index < 0)
			{
				continue;
			}

			// Check whitespace after first colon
			if (title.Substring(index + 1, 1) != " ")
			{
				continue;
			}

			var scope = title.Substring(0, index).Substring(validType.Length);
			if (scope == "" || scope == "!")
			{
				return true;
			}

			if (scope.StartsWith('(') && scope.EndsWith(')'))
			{
				return true;
			}
		}

		return false;
	}
}
