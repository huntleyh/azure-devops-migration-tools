using System;
using System.Collections.Generic;
using System.Text;

namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("projects/teams")]
    [ApiName("Project Teams")]
    [ApiVersion("6.1-preview.1")]
    public partial class Teams : RestApiDefinition
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
