using System;
using System.Collections.Generic;
using System.Text;

namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("projects")]
    [ApiName("Projects")]
    [ApiVersion("6.1-preview.4")]
    [ApiOrgLevel(true)]
    public class TeamProject : RestApiDefinition
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
