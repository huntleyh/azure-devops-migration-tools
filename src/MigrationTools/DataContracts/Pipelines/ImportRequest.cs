namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("git/repositories")]
    [ApiName("Git Import Requests")]
    [ApiVersion("6.1-preview.1")]
    public class ImportRequest : RestApiDefinition
    {
        public int importRequestId { get; set; }
        public string status { get; set; }
        public string url { get; set; }
        public GitRepository repository { get; set; }
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
