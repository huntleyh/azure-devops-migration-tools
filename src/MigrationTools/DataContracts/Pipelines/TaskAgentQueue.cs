namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("distributedtask/queues")]
    [ApiName("Agent Queues")]
    [ApiVersion("6.1-preview.1")]
    public class TaskAgentQueue : RestApiDefinition
    {
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
            //We are not migrating this, right now only using the names for mapping it over to target
        }
    }
}
