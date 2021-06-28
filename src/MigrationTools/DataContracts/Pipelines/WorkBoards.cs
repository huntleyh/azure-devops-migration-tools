using System;
using System.Collections.Generic;
using System.Text;

namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("work/boards")]
    [ApiName("Work Boards")]
    [ApiVersion("6.1-preview.1")]
    public partial class WorkBoards : RestApiDefinition
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
