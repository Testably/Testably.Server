using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Testably.Server.Controllers;

[Route("pr-status-check")]
[ApiController]
[AllowAnonymous]
public class PullRequestStatusCheckController : ControllerBase
{
	private const string RepositoryOwner = "Testably";

	private const string SuccessMessage =
		"Title must conform to the conventional commits naming convention";

	private static readonly string[] ValidTypes =
	{
		"fix",
		"feat",
		"release",

		// https://github.com/conventional-changelog/commitlint/tree/master/%40commitlint/config-conventional
		"build",
		"chore",
		"ci",
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
		[FromBody] WebhookModel<PullRequestWebhookModel> pullRequestModel,
		CancellationToken cancellationToken)
	{
		_logger.LogInformation("Received {PullRequestWebhookModel}", pullRequestModel);

		if (pullRequestModel.Payload.Repository.Private ||
		    pullRequestModel.Payload.Repository.Owner.Login != RepositoryOwner)
		{
			return BadRequest($"Only public repositories from '{RepositoryOwner}' are supported!");
		}
		var bearerToken = _configuration.GetValue<string>("GithubBearerToken");
		using var client = _clientFactory.CreateClient();
		client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Testably", Assembly.GetExecutingAssembly().GetName().Version.ToString()));
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", bearerToken);

		var owner = pullRequestModel.Payload.Repository.Owner.Login;
		var repo = pullRequestModel.Payload.Repository.Name;
		var prNumber = pullRequestModel.Payload.Number;
		var requestUri = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
		var response = await client
			.GetAsync(requestUri, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var responseContent = await response.Content.ReadAsStringAsync();
			return StatusCode(StatusCodes.Status500InternalServerError,
				$"GitHub API '{requestUri}' not available: {responseContent}");
		}

		var jsonDocument = await JsonDocument.ParseAsync(
			await response.Content.ReadAsStreamAsync(cancellationToken),
			cancellationToken: cancellationToken);

		if (!jsonDocument.RootElement.TryGetProperty("title", out var titleProperty) ||
		    titleProperty.GetString() == null)
		{
			return StatusCode(StatusCodes.Status500InternalServerError,
				$"GitHub API '{requestUri}' returned an invalid response (missing title).");
		}

		if (!jsonDocument.RootElement.TryGetProperty("head", out var headProperty) ||
		    !headProperty.TryGetProperty("sha", out var shaProperty) ||
		    shaProperty.GetString() == null)
		{
			return StatusCode(StatusCodes.Status500InternalServerError,
				$"GitHub API '{requestUri}' returned an invalid response (missing head.sha).");
		}

		var title = titleProperty.GetString()!;
		var commitSha = shaProperty.GetString();
		var statusUri = $"https://api.github.com/repos/{owner}/{repo}/statuses/{commitSha}";
		var hasValidTitle = ValidateTitle(title);
		// https://docs.github.com/en/rest/commits/statuses?apiVersion=2022-11-28#create-a-commit-status
		var json = JsonSerializer.Serialize(new
		{
			context = "Testably/Conventional-Commits",
			state = hasValidTitle ? "success" : "failure",
			description = "The PR title must conform to the conventional commits guideline."
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

	public class WebhookModel<T>
	{
		public string Event { get; set; }
		public T Payload { get; set; }
	}

	public class PullRequestWebhookModel
	{
		public string Action { get; set; } = "";
		public int Number { get; set; }

		public PullRequestModel PullRequest { get; set; }

		public RepositoryModel Repository { get; set; }
	}

	public class PullRequestModel
	{
		public string MergeCommitSha { get; set; }
	}

	public class RepositoryModel
	{
		public string Name { get; set; }
		public RepositoryOwnerModel Owner { get; set; }
		public bool Private { get; set; }
	}

	public class RepositoryOwnerModel
	{
		public string Login { get; set; }
	}
}