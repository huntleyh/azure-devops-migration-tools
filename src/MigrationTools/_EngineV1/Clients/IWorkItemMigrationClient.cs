using System.Collections.Generic;
using MigrationTools._EngineV1.Configuration;
using MigrationTools._EngineV1.DataContracts;
using MigrationTools.DataContracts;

namespace MigrationTools._EngineV1.Clients
{
    public interface IWorkItemMigrationClient
    {
        IMigrationClientConfig Config { get; }
        ProjectData Project { get; }

        void Configure(IMigrationClient migrationClient, bool bypassRules = true);

        ProjectData GetProject();

        List<WorkItemData> GetWorkItems(int retryLimit = 0);

        WorkItemData GetWorkItem(string id);

        WorkItemData GetWorkItem(int id);

        List<WorkItemData> GetWorkItems(string WIQLQuery, int retryLimit = 0);

        List<WorkItemData> GetWorkItems(IWorkItemQueryBuilder queryBuilder, int retryLimit = 0);

        WorkItemData PersistWorkItem(WorkItemData workItem);

        WorkItemData GetRevision(WorkItemData workItem, int revision);

        WorkItemData FindReflectedWorkItem(WorkItemData workItem, bool cache);

        WorkItemData FindReflectedWorkItemByReflectedWorkItemId(string refId);

        WorkItemData FindReflectedWorkItemByReflectedWorkItemId(WorkItemData refWi);

        ReflectedWorkItemId CreateReflectedWorkItemId(WorkItemData workItem);

        ReflectedWorkItemId GetReflectedWorkItemId(WorkItemData workItem);
    }
}