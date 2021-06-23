using System;
using System.Collections.Generic;
using System.Text;

namespace MigrationTools.DataContracts.Pipelines
{
    [ApiPath("extensionmanagement/installedextensions")]
    [ApiName("AzDo Extension")]
    [ApiVersion("6.1-preview.1")]
    [ApiUriDomainPrefix("extmgmt")]
    [ApiOrgLevel(true)]
    public class AzDoExtensions : RestApiDefinition
    {
        public override bool HasTaskGroups()
        {
            throw new NotImplementedException();
        }

        public override bool HasVariableGroups()
        {
            throw new NotImplementedException();
        }

        public override void ResetObject()
        {
            throw new NotImplementedException();
        }
    }
}
