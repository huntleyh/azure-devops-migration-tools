using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
//using Microsoft.TeamFoundation.Build.WebApi;
using MigrationTools.DataContracts;
using MigrationTools.DataContracts.Pipelines;
using MigrationTools.Endpoints;
using MigrationTools.Enrichers;

namespace MigrationTools.Processors
{
    /// <summary>
    /// Azure DevOps Processor that migrates Taskgroups, Build- and Release Pipelines.
    /// </summary>
    public partial class AzureDevOpsPipelineProcessor : Processor
    {
        private AzureDevOpsPipelineProcessorOptions _Options;
        private Dictionary<string, string> _serviceConnectionMap;
        private Dictionary<string, string> _taskGroupMap;
        private Dictionary<string, string> _variableGroupMap;
        private IEnumerable<Mapping> _gitRepoMap;

        public AzureDevOpsPipelineProcessor(
                    ProcessorEnricherContainer processorEnrichers,
                    IEndpointFactory endpointFactory,
                    IServiceProvider services,
                    ITelemetryLogger telemetry,
                    ILogger<Processor> logger)
            : base(processorEnrichers, endpointFactory, services, telemetry, logger)
        {
        }

        public new AzureDevOpsEndpoint Source => (AzureDevOpsEndpoint)base.Source;

        public new AzureDevOpsEndpoint Target => (AzureDevOpsEndpoint)base.Target;

        public override void Configure(IProcessorOptions options)
        {
            base.Configure(options);
            Log.LogInformation("AzureDevOpsPipelineProcessor::Configure");
            _Options = (AzureDevOpsPipelineProcessorOptions)options;
        }

        protected override void InternalExecute()
        {
            Log.LogInformation("Processor::InternalExecute::Start");
            EnsureConfigured();
            ProcessorEnrichers.ProcessorExecutionBegin(this);
            MigratePipelinesAsync().GetAwaiter().GetResult();
            ProcessorEnrichers.ProcessorExecutionEnd(this);
            Log.LogInformation("Processor::InternalExecute::End");
        }

        private void EnsureConfigured()
        {
            Log.LogInformation("Processor::EnsureConfigured");
            if (_Options == null)
            {
                throw new Exception("You must call Configure() first");
            }
            if (Source is not AzureDevOpsEndpoint)
            {
                throw new Exception("The Source endpoint configured must be of type AzureDevOpsEndpoint");
            }
            if (Target is not AzureDevOpsEndpoint)
            {
                throw new Exception("The Target endpoint configured must be of type AzureDevOpsEndpoint");
            }
        }

        /// <summary>
        /// Executes Method for migrating Taskgroups, Variablegroups or Pipelines, depinding on what is set in the config.
        /// </summary>
        private async System.Threading.Tasks.Task MigratePipelinesAsync()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            IEnumerable<Mapping<GitRepository>> gitRepositoryMappings = null;
            IEnumerable<Mapping> buildDefinitionMapping = null;
            IEnumerable<Mapping> serviceConnectionMappings = null;
            IEnumerable<Mapping> taskGroupMappings = null;
            IEnumerable<Mapping> variableGroupMappings = null;

            await Target.GetProjectGuid();

            if (_Options.MigrateGitRepositories)
            {
                gitRepositoryMappings = await CreateGitRepositories();
            }
            if (_Options.MigrateServiceConnections)
            {
                serviceConnectionMappings = await CreateServiceConnectionsAsync();
            }
            if (_Options.MigrateTaskGroups)
            {
                taskGroupMappings = await CreateTaskGroupDefinitionsAsync();
            }
            if (_Options.MigrateVariableGroups)
            {
                variableGroupMappings = await CreateVariableGroupDefinitionsAsync();
            }
            if (_Options.MigrateBuildPipelines)
            {
                buildDefinitionMapping = await CreateBuildPipelinesAsync(taskGroupMappings, variableGroupMappings, gitRepositoryMappings);
            }
            if (_Options.MigrateReleasePipelines)
            {
                await CreateReleasePipelinesAsync(taskGroupMappings, variableGroupMappings, gitRepositoryMappings, buildDefinitionMapping);
            }
            stopwatch.Stop();
            Log.LogDebug("DONE in {Elapsed} ", stopwatch.Elapsed.ToString("c"));
        }


        private async Task<IEnumerable<Mapping<GitRepository>>> CreateGitRepositories()
        {
            Log.LogInformation($"Processing Service Connections..");

            var sourceDefinitions = await Source.GetApiDefinitionsAsync<GitRepository>();
            var targetDefinitions = await Target.GetApiDefinitionsAsync<GitRepository>();

            var mappings = await Target.CreateRepositoryImportRequestsAsync(FilterOutExistingDefinitions(sourceDefinitions, targetDefinitions), Source.Options.AccessToken);
            mappings.AddRange(FindExistingMappings(sourceDefinitions, targetDefinitions, mappings));
            return mappings;
        }

        private IEnumerable<Mapping<T>> FindExistingMappings<T>(IEnumerable<T> sourceDefinitions, IEnumerable<T> targetDefinitions, List<Mapping<T>> mappings)
             where T : RestApiDefinition, new()
        {
            // This is not safe, because the target project can have a taskgroup with the same name but with different content
            // To make this save we must add a local storage option for the mappings (sid, tid)
            var alreadyMigratedMappings = new List<Mapping<T>>();
            var alreadyMigratedDefintions = targetDefinitions.Where(t => mappings.Any(m => m.TargetId == t.Id) == false).ToList();
            foreach (var item in alreadyMigratedDefintions)
            {
                var source = sourceDefinitions.FirstOrDefault(d => d.Name == item.Name);
                if (source == null)
                {
                    Log.LogInformation("The {DefinitionType} {DefinitionName}({DefinitionId}) doesn't exist in the source collection.", typeof(T).Name, item.Name, item.Id);
                }
                else
                {
                    alreadyMigratedMappings.Add(new()
                    {
                        SourceId = source.Id,
                        TargetId = item.Id,
                        Name = item.Name,
                        Ref = item
                    });
                }
            }
            return alreadyMigratedMappings;
        }

        /// <summary>
        /// Map the taskgroups that are already migrated
        /// </summary>
        /// <typeparam name="DefintionType"></typeparam>
        /// <param name="sourceDefinitions"></param>
        /// <param name="targetDefinitions"></param>
        /// <param name="newMappings"></param>
        /// <returns>Mapping list</returns>
        private IEnumerable<Mapping> FindExistingMappings<DefintionType>(IEnumerable<DefintionType> sourceDefinitions, IEnumerable<DefintionType> targetDefinitions, List<Mapping> newMappings)
            where DefintionType : RestApiDefinition, new()
        {
            // This is not safe, because the target project can have a taskgroup with the same name but with different content
            // To make this save we must add a local storage option for the mappings (sid, tid)
            var alreadyMigratedMappings = new List<Mapping>();
            var alreadyMigratedDefintions = targetDefinitions.Where(t => newMappings.Any(m => m.TargetId == t.Id) == false).ToList();
            foreach (var item in alreadyMigratedDefintions)
            {
                var source = sourceDefinitions.FirstOrDefault(d => d.Name == item.Name);
                if (source == null)
                {
                    Log.LogInformation("The {DefinitionType} {DefinitionName}({DefinitionId}) doesn't exsist in the source collection.", typeof(DefintionType).Name, item.Name, item.Id);
                }
                else
                {
                    alreadyMigratedMappings.Add(new()
                    {
                        SourceId = source.Id,
                        TargetId = item.Id,
                        Name = item.Name
                    });
                }
            }
            return alreadyMigratedMappings;
        }

        /// <summary>
        /// Filter existing Definitions
        /// </summary>
        /// <typeparam name="DefinitionType"></typeparam>
        /// <param name="sourceDefinitions"></param>
        /// <param name="targetDefinitions"></param>
        /// <returns>List of filtered Definitions</returns>
        private IEnumerable<DefinitionType> FilterOutExistingDefinitions<DefinitionType>(IEnumerable<DefinitionType> sourceDefinitions, IEnumerable<DefinitionType> targetDefinitions)
            where DefinitionType : RestApiDefinition, new()
        {
            var objectsToMigrate = sourceDefinitions.Where(s => !targetDefinitions.Any(t => t.Name == s.Name));

            Log.LogInformation("{ObjectsToBeMigrated} of {TotalObjects} source {DefinitionType}(s) are going to be migrated..", objectsToMigrate.Count(), sourceDefinitions.Count(), typeof(DefinitionType).Name);

            return objectsToMigrate;
        }

        private async Task<IEnumerable<Mapping>> CreateBuildPipelinesAsync(IEnumerable<Mapping> TaskGroupMapping = null, IEnumerable<Mapping> VariableGroupMapping = null, IEnumerable<Mapping<GitRepository>> gitRepoMapping = null)
        {
            Log.LogInformation("Processing Build Pipelines..");

            var sourceDefinitions = await Source.GetApiDefinitionsAsync<BuildDefinition>();
            var targetDefinitions = await Target.GetApiDefinitionsAsync<BuildDefinition>();
            var definitionsToBeMigrated = FilterOutExistingDefinitions(sourceDefinitions, targetDefinitions);

            definitionsToBeMigrated = FilterAwayIfAnyMapsAreMissing(definitionsToBeMigrated, TaskGroupMapping, VariableGroupMapping);

            var agentQueueMappings = await CreateAgentQueueMappingsAsync<TaskAgentQueue>();
            var agentPoolMappings = await CreateAgentPoolMappingsAsync<TaskAgentPool>();

            // Replace taskgroup and variablegroup sIds with tIds
            foreach (var definitionToBeMigrated in definitionsToBeMigrated)
            {
                if (agentQueueMappings != null && definitionToBeMigrated.Queue != null)
                {
                    var mapping = agentQueueMappings.FirstOrDefault(d => string.Equals(d.SourceId, definitionToBeMigrated.Queue.Id.ToString(), StringComparison.CurrentCultureIgnoreCase));
                    if (mapping == null)
                    {
                        Log.LogWarning("Can't find agent queue {MissingQueueId} in the target collection.", definitionToBeMigrated.Queue.Id);
                    }
                    else
                    {
                        definitionToBeMigrated.Queue.Id = long.Parse(mapping.TargetId);
                    }

                    if (definitionToBeMigrated.Queue.Pool != null)
                    {
                        mapping = agentPoolMappings.FirstOrDefault(d => string.Equals(d.SourceId, definitionToBeMigrated.Queue.Pool.Id.ToString(), StringComparison.CurrentCultureIgnoreCase));
                        if (mapping == null)
                        {
                            Log.LogWarning("Can't find agent pool {MissingPoolId} in the target collection.", definitionToBeMigrated.Queue.Pool.Id);
                        }
                        else
                        {
                            definitionToBeMigrated.Queue.Pool.Id = long.Parse(mapping.TargetId);
                        }
                    }
                }

                if (TaskGroupMapping is not null && definitionToBeMigrated.Process.Phases is not null)
                {
                    foreach (var phase in definitionToBeMigrated.Process.Phases)
                    {
                        foreach (var step in phase.Steps)
                        {
                            if (step.Task.DefinitionType.ToLower() != "metaTask".ToLower())
                            {
                                continue;
                            }
                            var mapping = TaskGroupMapping.FirstOrDefault(d => d.SourceId == step.Task.Id);
                            if (mapping == null)
                            {
                                Log.LogWarning("Can't find taskgroup {MissingTaskGroupId} in the target collection.", step.Task.Id);
                            }
                            else
                            {
                                step.Task.Id = mapping.TargetId;
                            }
                        }
                    }
                }

                if (VariableGroupMapping is not null && definitionToBeMigrated.VariableGroups is not null)
                {
                    foreach (var variableGroup in definitionToBeMigrated.VariableGroups)
                    {
                        if (variableGroup == null)
                        {
                            continue;
                        }
                        var mapping = VariableGroupMapping.FirstOrDefault(d => d.SourceId == variableGroup.Id);
                        if (mapping == null)
                        {
                            Log.LogWarning("Can't find variablegroup {MissingVariableGroupId} in the target collection.", variableGroup.Id);
                        }
                        else
                        {
                            variableGroup.Id = mapping.TargetId;
                        }
                    }
                }

                if (gitRepoMapping is not null && definitionToBeMigrated.Repository is not null)
                {
                    var repo = gitRepoMapping.FirstOrDefault(x => x.Name.Equals(definitionToBeMigrated.Repository.Name, StringComparison.CurrentCultureIgnoreCase));
                    if (repo != null)
                    {
                        definitionToBeMigrated.Repository.Id = repo.TargetId;
                        definitionToBeMigrated.Repository.Url = new Uri(repo.Ref.remoteUrl);
                        //definitionToBeMigrated.Repository.Properties.cloneUrl = repo.remoteUrl;
                    }
                    else
                    {
                        Log.LogWarning("Can't find repository {MissingRepoId} in the target project.", definitionToBeMigrated.Repository.Name);
                    }
                }
            }
            var mappings = await Target.CreateApiDefinitionsAsync<BuildDefinition>(definitionsToBeMigrated.ToList());
            mappings.AddRange(FindExistingMappings(sourceDefinitions, targetDefinitions, mappings));
            return mappings;
        }

        private async Task<IEnumerable<Mapping>> CreateAgentQueueMappingsAsync<DefinitionType>()
            where DefinitionType : RestApiDefinition, new()
        {
            var sourceQueues = await Source.GetApiDefinitionsAsync<DefinitionType>();
            var targetQueues = await Target.GetApiDefinitionsAsync<DefinitionType>();
            var mappings = new List<Mapping>();
            foreach (var sourceQueue in sourceQueues)
            {
                var targetQueue = targetQueues.FirstOrDefault(t => t.Name == sourceQueue.Name);
                if (targetQueue is not null)
                {
                    mappings.Add(new()
                    {
                        SourceId = sourceQueue.Id,
                        TargetId = targetQueue.Id,
                        Name = targetQueue.Name
                    });
                }
            }
            return mappings;
        }

        private async Task<IEnumerable<Mapping>> CreateAgentPoolMappingsAsync<DefinitionType>()
            where DefinitionType : RestApiDefinition, new()
        {
            var sourcePools = await Source.GetApiDefinitionsAsync<DefinitionType>();
            var targetPools = await Target.GetApiDefinitionsAsync<DefinitionType>();
            var mappings = new List<Mapping>();
            foreach (var sourcePool in sourcePools)
            {
                var targetPool = targetPools.FirstOrDefault(t => t.Name == sourcePool.Name);
                if (targetPool is not null)
                {
                    mappings.Add(new()
                    {
                        SourceId = sourcePool.Id,
                        TargetId = targetPool.Id,
                        Name = targetPool.Name
                    });
                }
            }
            return mappings;
        }

        private void UpdateQueueIdForPhase(DeployPhase phase, IEnumerable<Mapping> mappings)
        {
            var mapping = mappings.FirstOrDefault(a => a.SourceId == phase.DeploymentInput.QueueId.ToString());
            if (mapping is not null)
            {
                phase.DeploymentInput.QueueId = int.Parse(mapping.TargetId);
            }
            else
            {
                phase.DeploymentInput.QueueId = 0;
            }
        }

        private async Task<IEnumerable<Mapping>> CreateReleasePipelinesAsync(IEnumerable<Mapping> TaskGroupMapping = null, IEnumerable<Mapping> VariableGroupMapping = null,
                                                                            IEnumerable<Mapping> gitRepoMapping = null, IEnumerable<Mapping> buildPipelinesMapping = null)
        {
            Log.LogInformation($"Processing Release Pipelines..");

            var sourceDefinitions = await Source.GetApiDefinitionsAsync<ReleaseDefinition>();
            var targetDefinitions = await Target.GetApiDefinitionsAsync<ReleaseDefinition>();

            var agentPoolMappings = await CreateAgentQueueMappingsAsync<TaskAgentQueue>();
            var deploymentGroupMappings = await CreateAgentQueueMappingsAsync<DeploymentGroup>();

            var definitionsToBeMigrated = FilterOutExistingDefinitions(sourceDefinitions, targetDefinitions);
            if (_Options.ReleasePipelines is not null)
            {
                definitionsToBeMigrated = definitionsToBeMigrated.Where(d => _Options.ReleasePipelines.Contains(d.Name));
            }

            definitionsToBeMigrated = FilterAwayIfAnyMapsAreMissing(definitionsToBeMigrated, TaskGroupMapping, VariableGroupMapping);

            // Replace queue, taskgroup and variablegroup sourceIds with targetIds
            foreach (var definitionToBeMigrated in definitionsToBeMigrated)
            {
                UpdateQueueIdOnPhases(definitionToBeMigrated, agentPoolMappings, deploymentGroupMappings);

                UpdateTaskGroupId(definitionToBeMigrated, TaskGroupMapping);

                UpdateRepositoryId(definitionToBeMigrated, gitRepoMapping);

                UpdateBuildId(definitionToBeMigrated, buildPipelinesMapping);

                if (VariableGroupMapping is not null)
                {
                    UpdateVariableGroupId(definitionToBeMigrated.VariableGroups, VariableGroupMapping);

                    //foreach (var environment in definitionToBeMigrated.Environments)
                    //{
                    //    UpdateVariableGroupId(environment.VariableGroups, VariableGroupMapping);
                    //}
                }
                foreach (var environment in definitionToBeMigrated.Environments)
                {
                    UpdateAutomatedPrePostApprovals(environment.PreDeployApprovals);
                    UpdateAutomatedPrePostApprovals(environment.PostDeployApprovals);

                    if (VariableGroupMapping is not null)
                    {
                        UpdateVariableGroupId(environment.VariableGroups, VariableGroupMapping);
                    }
                }
                
            }

            var mappings = await Target.CreateApiDefinitionsAsync<ReleaseDefinition>(definitionsToBeMigrated);
            mappings.AddRange(FindExistingMappings(sourceDefinitions, targetDefinitions, mappings));
            return mappings;
        }

        private void UpdateAutomatedPrePostApprovals(DeployApprovals deployApprovals)
        {
            if(deployApprovals != null && deployApprovals.Approvals != null)
            {
                bool automatedApproval = false;
                foreach(var approval in deployApprovals.Approvals)
                {
                    if(approval.IsAutomated && automatedApproval == false)
                    {
                        automatedApproval = true;
                    }
                    else if (approval.IsAutomated && automatedApproval == true)
                    {
                        // We can have only a single automated approval so we set every other approval as false
                        approval.IsAutomated = false;
                    }
                }
            }
        }

        private void UpdateBuildId(ReleaseDefinition definitionToBeMigrated, IEnumerable<Mapping> buildPipelinesMapping)
        {
            if (buildPipelinesMapping is null) return;

            if (definitionToBeMigrated.Artifacts != null)
            {
                foreach (var artifact in definitionToBeMigrated.Artifacts)
                {
                    if (artifact.Type.Equals("Build", StringComparison.CurrentCultureIgnoreCase) &&
                        artifact.DefinitionReference != null &&
                        artifact.DefinitionReference.Definition != null)
                    {
                        var mapping = buildPipelinesMapping.FirstOrDefault(d => d.SourceId == artifact.DefinitionReference.Definition.Id.ToString());
                        if (mapping == null)
                        {
                            Log.LogWarning("Can't find definition ID {Id} in the target collection.", artifact.DefinitionReference.Definition.Id);
                        }
                        else
                        {
                            if (artifact.DefinitionReference.Project != null)
                            {
                                artifact.DefinitionReference.Project.Id = Target.ProjectGuid;
                                artifact.DefinitionReference.Project.Name = Target.Options.Project;
                            }
                            artifact.DefinitionReference.Definition.Id = mapping.TargetId;
                            artifact.SourceId = string.Format("{0}:{1}", Target.ProjectGuid, mapping.TargetId);
                        }
                    }
                }
            }
        }

        private void UpdateRepositoryId(ReleaseDefinition definitionToBeMigrated, IEnumerable<Mapping> gitRepoMapping)
        {
            if (gitRepoMapping is null) return;

            if (definitionToBeMigrated.Artifacts != null)
            {
                foreach (var artifact in definitionToBeMigrated.Artifacts)
                {
                    if (artifact.Type.Equals("Git", StringComparison.CurrentCultureIgnoreCase) &&
                        artifact.DefinitionReference != null &&
                        artifact.DefinitionReference.Definition != null)
                    {
                        var mapping = gitRepoMapping.FirstOrDefault(d => d.SourceId == artifact.DefinitionReference.Definition.Id.ToString());
                        if (mapping == null)
                        {
                            Log.LogWarning("Can't find definition ID {Id} in the target collection.", artifact.DefinitionReference.Definition.Id);
                        }
                        else
                        {
                            if (artifact.DefinitionReference.Project != null)
                            {
                                artifact.DefinitionReference.Project.Id = Target.ProjectGuid;
                                artifact.DefinitionReference.Project.Name = Target.Options.Project;
                            }
                            artifact.DefinitionReference.Definition.Id = mapping.TargetId;
                            artifact.SourceId = string.Format("{0}:{1}", Target.ProjectGuid, mapping.TargetId);
                        }
                    }
                }
            }
        }

        private IEnumerable<DefinitionType> FilterAwayIfAnyMapsAreMissing<DefinitionType>(
                                                IEnumerable<DefinitionType> definitionsToBeMigrated,
                                                IEnumerable<Mapping> TaskGroupMapping,
                                                IEnumerable<Mapping> VariableGroupMapping)
            where DefinitionType : RestApiDefinition
        {
            //filter away definitions that contains task or variable groups if we dont have those mappings
            if (TaskGroupMapping is null)
            {
                var containsTaskGroup = definitionsToBeMigrated.Any(d => d.HasTaskGroups());
                if (containsTaskGroup)
                {
                    Log.LogWarning("You can't migrate pipelines that uses taskgroups if you didn't migrate taskgroups");
                    definitionsToBeMigrated = definitionsToBeMigrated.Where(d => d.HasTaskGroups() == false);
                }
            }
            if (VariableGroupMapping is null)
            {
                var containsVariableGroup = definitionsToBeMigrated.Any(d => d.HasVariableGroups());
                if (containsVariableGroup)
                {
                    Log.LogWarning("You can't migrate pipelines that uses variablegroups if you didn't migrate variablegroups");
                    definitionsToBeMigrated = definitionsToBeMigrated.Where(d => d.HasTaskGroups() == false);
                }
            }

            return definitionsToBeMigrated;
        }

        private void UpdateVariableGroupId(int[] variableGroupIds, IEnumerable<Mapping> VariableGroupMapping)
        {
            for (int i = 0; i < variableGroupIds.Length; i++)
            {
                var oldId = variableGroupIds[i].ToString();
                var mapping = VariableGroupMapping.FirstOrDefault(d => d.SourceId == oldId);
                if (mapping is not null)
                {
                    variableGroupIds[i] = int.Parse(mapping.TargetId);
                }
                else
                {
                    //Not sure if we should exit hard in this case?
                    Log.LogWarning("Can't find variablegroups {OldVariableGroupId} in the target collection.", oldId);
                }
            }
        }

        private void UpdateTaskGroupId(ReleaseDefinition definitionToBeMigrated, IEnumerable<Mapping> TaskGroupMapping)
        {
            if (TaskGroupMapping is null)
            {
                return;
            }
            foreach (var environment in definitionToBeMigrated.Environments)
            {
                foreach (var deployPhase in environment.DeployPhases)
                {
                    foreach (var WorkflowTask in deployPhase.WorkflowTasks)
                    {
                        if (WorkflowTask.DefinitionType == null || WorkflowTask.DefinitionType.ToLower() != "metaTask".ToLower())
                        {
                            continue;
                        }
                        var mapping = TaskGroupMapping.FirstOrDefault(d => d.SourceId == WorkflowTask.TaskId.ToString());
                        if (mapping == null)
                        {
                            Log.LogWarning("Can't find taskgroup {TaskGroupId} in the target collection.", WorkflowTask.TaskId);
                        }
                        else
                        {
                            WorkflowTask.TaskId = Guid.Parse(mapping.TargetId);
                        }
                    }
                }
            }
        }

        private void UpdateQueueIdOnPhases(ReleaseDefinition definitionToBeMigrated, IEnumerable<Mapping> agentPoolMappings, IEnumerable<Mapping> deploymentGroupMappings)
        {
            foreach (var environment in definitionToBeMigrated.Environments)
            {
                foreach (var phase in environment.DeployPhases)
                {
                    if (phase.PhaseType == "agentBasedDeployment")
                    {
                        UpdateQueueIdForPhase(phase, agentPoolMappings);
                    }
                    else if (phase.PhaseType == "machineGroupBasedDeployment")
                    {
                        UpdateQueueIdForPhase(phase, deploymentGroupMappings);
                    }
                }
            }
        }

        private async Task<IEnumerable<Mapping>> CreateServiceConnectionsAsync()
        {
            Log.LogInformation($"Processing Service Connections..");

            var sourceDefinitions = await Source.GetApiDefinitionsAsync<ServiceConnection>();
            var targetDefinitions = await Target.GetApiDefinitionsAsync<ServiceConnection>();
            var mappings = await Target.CreateApiDefinitionsAsync(FilterOutExistingDefinitions(sourceDefinitions, targetDefinitions));
            mappings.AddRange(FindExistingMappings(sourceDefinitions, targetDefinitions, mappings));
            return mappings;
        }

        private async Task<IEnumerable<Mapping>> CreateTaskGroupDefinitionsAsync()
        {
            Log.LogInformation($"Processing Taskgroups..");

            var sourceDefinitions = await Source.GetApiDefinitionsAsync<TaskGroup>();
            var targetDefinitions = await Target.GetApiDefinitionsAsync<TaskGroup>();
            var mappings = await Target.CreateApiDefinitionsAsync(FilterOutExistingDefinitions(sourceDefinitions, targetDefinitions));
            mappings.AddRange(FindExistingMappings(sourceDefinitions, targetDefinitions, mappings));
            return mappings;
        }

        private async Task<IEnumerable<Mapping>> CreateVariableGroupDefinitionsAsync()
        {
            Log.LogInformation($"Processing Variablegroups..");

            var sourceDefinitions = await Source.GetApiDefinitionsAsync<VariableGroups>();
            var targetDefinitions = await Target.GetApiDefinitionsAsync<VariableGroups>();
            var filteredDefinition = FilterOutExistingDefinitions(sourceDefinitions, targetDefinitions);
            foreach (var variableGroup in filteredDefinition)
            {
                //was needed when now trying to migrated to azure devops services
                variableGroup.VariableGroupProjectReferences = new VariableGroupProjectReference[1];
                variableGroup.VariableGroupProjectReferences[0] = new VariableGroupProjectReference
                {
                    Name = variableGroup.Name,
                    ProjectReference = new ProjectReference
                    {
                        Name = Target.Options.Project
                    }
                };
            }
            var mappings = await Target.CreateApiDefinitionsAsync(filteredDefinition);
            mappings.AddRange(FindExistingMappings(sourceDefinitions, targetDefinitions, mappings));
            return mappings;
        }
    }
}