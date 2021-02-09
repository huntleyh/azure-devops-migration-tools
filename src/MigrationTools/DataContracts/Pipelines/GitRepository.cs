namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("git/repositories")]
    [ApiName("Git Repositories")]
    [ApiVersion("6.1-preview.1")]
    public partial class GitRepository : RestApiDefinition
    {
        public string remoteUrl { get; set; }
        public string url { get; set; }
        public override bool HasTaskGroups()
        {
            return false;
        }

        public override bool HasVariableGroups()
        {
            return false;
        }

        public override void ResetObject()
        {
            return;
        }
    }
}
