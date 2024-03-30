﻿using System.Net.Http.Headers;
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
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<PullRequestStatusCheckController> _logger;


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
        if (pullRequestModel.Payload.Repository.Private ||
            pullRequestModel.Payload.Repository.Owner.Login != RepositoryOwner)
        {
            return BadRequest($"Only public repositories from '{RepositoryOwner}' are supported!");
        }

        _logger.LogInformation("Received {PullRequestWebhookModel}", pullRequestModel);

        using var client = _clientFactory.CreateClient();

        var owner = pullRequestModel.Payload.Repository.Owner.Login;
        var repo = pullRequestModel.Payload.Repository.Name;
        var prNumber = pullRequestModel.Payload.Number;
        var requestUri = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
        var response = await client
            .GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"GitHub API '{requestUri}' not available");
        }

        var jsonDocument = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!jsonDocument.RootElement.TryGetProperty("title", out var titleProperty) ||
            titleProperty.GetString() == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"GitHub API '{requestUri}' returned an invalid response");
        }

        var bearerToken = _configuration.GetValue<string>("GithubBearerToken");
        var title = titleProperty.GetString()!;
        string commitSha = pullRequestModel.Payload.PullRequest.MergeCommitSha;
        var statusUri = $"https://api.github.com/repos/{owner}/{repo}/statuses/{commitSha}";
        bool hasValidTitle = ValidateTitle(title);
        // https://docs.github.com/en/rest/commits/statuses?apiVersion=2022-11-28#create-a-commit-status
        var json = JsonSerializer.Serialize(new
        {
            context = "Testably/Conventional-Commits",
            state = hasValidTitle ? "success" : "failure",
            description = "The PR title must conform to the conventional commits guideline."
        });
        using var content = new StringContent(json);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);
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