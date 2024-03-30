namespace Testably.Server.Models;

public class RepositoryModel
{
	public string Name { get; set; } = "";
	public RepositoryOwnerModel Owner { get; set; } = default!;
    public bool Private { get; set; }
}