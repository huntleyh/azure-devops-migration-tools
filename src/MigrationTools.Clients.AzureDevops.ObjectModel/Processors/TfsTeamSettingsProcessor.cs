using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.ProcessConfiguration.Client;
using MigrationTools.DataContracts;
using MigrationTools.DataContracts.Pipelines;
using MigrationTools.Endpoints;
using MigrationTools.Enrichers;
using Newtonsoft.Json;

namespace MigrationTools.Processors
{
    /// <summary>
    /// Native TFS Processor, does not work with any other Endpoints.
    /// </summary>
    public class TfsTeamSettingsProcessor : Processor
    {
        private TfsTeamSettingsProcessorOptions _Options;
        protected IMigrationEngine Engine { get; }
        public TfsTeamSettingsProcessor(ProcessorEnricherContainer processorEnrichers,
                                        IMigrationEngine engine,
                                        IEndpointFactory endpointFactory,
                                        IServiceProvider services,
                                        ITelemetryLogger telemetry,
                                        ILogger<Processor> logger)
            : base(processorEnrichers, endpointFactory, services, telemetry, logger)
        {
            Engine = engine;
        }

        public new TfsTeamSettingsEndpoint Source => (TfsTeamSettingsEndpoint)base.Source;

        public new TfsTeamSettingsEndpoint Target => (TfsTeamSettingsEndpoint)base.Target;

        public override void Configure(IProcessorOptions options)
        {
            base.Configure(options);
            Log.LogInformation("TfsTeamSettingsProcessor::Configure");
            _Options = (TfsTeamSettingsProcessorOptions)options;
        }

        protected override void InternalExecute()
        {
            Log.LogInformation("Processor::InternalExecute::Start");
            EnsureConfigured();
            ProcessorEnrichers.ProcessorExecutionBegin(this);
            MigrateTeamSettings();
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
            if (Source is not TfsTeamSettingsEndpoint)
            {
                throw new Exception("The Source endpoint configured must be of type TfsTeamSettingsEndpoint");
            }
            if (Target is not TfsTeamSettingsEndpoint)
            {
                throw new Exception("The Target endpoint configured must be of type TfsTeamSettingsEndpoint");
            }
        }
        private void MigrateTeamSettings()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            //////////////////////////////////////////////////
            List<TeamFoundationTeam> sourceTeams = Source.TfsTeamService.QueryTeams(Source.Project).ToList();
            Log.LogInformation("TfsTeamSettingsProcessor::InternalExecute: Found {0} teams in Source?", sourceTeams.Count);
            //////////////////////////////////////////////////
            List<TeamFoundationTeam> targetTeams = Target.TfsTeamService.QueryTeams(Target.Project).ToList();

            Log.LogDebug("Found {0} teams in Target?", sourceTeams.Count);
            //////////////////////////////////////////////////
            int current = sourceTeams.Count;
            int count = 0;
            long elapsedms = 0;

            /////////
            if (_Options.Teams != null)
            {
                sourceTeams = sourceTeams.Where(t => _Options.Teams.Contains(t.Name)).ToList();
            }
            // Create teams
            foreach (TeamFoundationTeam sourceTeam in sourceTeams)
            {
                Stopwatch witstopwatch = Stopwatch.StartNew();
                var foundTargetTeam = (from x in targetTeams where x.Name == sourceTeam.Name select x).SingleOrDefault();
                if (foundTargetTeam == null || _Options.UpdateTeamSettings)
                {
                    Log.LogDebug("Processing team '{0}':", sourceTeam.Name);
                    TeamFoundationTeam newTeam = foundTargetTeam ?? Target.TfsTeamService.CreateTeam(Target.TfsProjectUri.ToString(), sourceTeam.Name, sourceTeam.Description, null);
                    Log.LogDebug("-> Team '{0}' created", sourceTeam.Name);

                    if (_Options.MigrateTeamSettings)
                    {
                        // Duplicate settings
                        Log.LogDebug("-> Processing team '{0}' settings:", sourceTeam.Name);
                        var sourceConfigurations = Source.TfsTeamSettingsService.GetTeamConfigurations(new List<Guid> { sourceTeam.Identity.TeamFoundationId });
                        var targetConfigurations = Target.TfsTeamSettingsService.GetTeamConfigurations(new List<Guid> { newTeam.Identity.TeamFoundationId });

                        foreach (var sourceConfig in sourceConfigurations)
                        {
                            if (sourceConfig.TeamSettings.BacklogIterationPath != null &&
                                sourceConfig.TeamSettings.TeamFieldValues.Length > 0)
                            {
                                var targetConfig = targetConfigurations.FirstOrDefault(t => t.TeamName == sourceConfig.TeamName);
                                if (targetConfig == null)
                                {
                                    Log.LogDebug("-> Settings for team '{sourceTeamName}'.. not found", sourceTeam.Name);
                                    continue;
                                }

                                Log.LogInformation("-> Settings found for team '{sourceTeamName}'..", sourceTeam.Name);
                                if (_Options.PrefixProjectToNodes)
                                {
                                    targetConfig.TeamSettings.BacklogIterationPath =
                                        string.Format("{0}\\{1}", Target.Project, sourceConfig.TeamSettings.BacklogIterationPath);
                                    targetConfig.TeamSettings.IterationPaths = sourceConfig.TeamSettings.IterationPaths
                                        .Select(path => string.Format("{0}\\{1}", Target.Project, path))
                                        .ToArray();
                                    targetConfig.TeamSettings.TeamFieldValues = sourceConfig.TeamSettings.TeamFieldValues
                                        .Select(field => new TeamFieldValue
                                        {
                                            IncludeChildren = field.IncludeChildren,
                                            Value = string.Format("{0}\\{1}", Target.Project, field.Value)
                                        })
                                        .ToArray();
                                }
                                else
                                {
                                    targetConfig.TeamSettings.BacklogIterationPath = sourceConfig.TeamSettings.BacklogIterationPath.Replace(Source.Project, Target.Project);
                                    Log.LogDebug("targetConfig.TeamSettings.BacklogIterationPath={BacklogIterationPath}", targetConfig.TeamSettings.BacklogIterationPath);
                                    targetConfig.TeamSettings.IterationPaths = sourceConfig.TeamSettings.IterationPaths.Select(ip => ip.Replace(Source.Project, Target.Project)).ToArray();
                                    Log.LogDebug("targetConfig.TeamSettings.IterationPaths={@IterationPaths}", targetConfig.TeamSettings.IterationPaths);
                                    targetConfig.TeamSettings.TeamFieldValues = sourceConfig.TeamSettings.TeamFieldValues;
                                    foreach (var item in targetConfig.TeamSettings.TeamFieldValues)
                                    {
                                        item.Value = item.Value.Replace(Source.Project, Target.Project);
                                    }
                                    Log.LogDebug("targetConfig.TeamSettings.TeamFieldValues={@TeamFieldValues}", targetConfig.TeamSettings.TeamFieldValues);
                                }

                                Target.TfsTeamSettingsService.SetTeamSettings(targetConfig.TeamId, targetConfig.TeamSettings);
                                Log.LogDebug("-> Team '{0}' settings... applied", targetConfig.TeamName);
                            }
                            else
                            {
                                Log.LogWarning("-> Settings for team '{sourceTeamName}'.. not configured", sourceTeam.Name);
                            }
                        }

                        if (_Options.MigrateBoards == true)
                        {
                            if (MigrateTeamBoardSettings(sourceTeam, newTeam).Result)
                            {
                                Log.LogDebug("-> Team '{0}' Board settings... applied", newTeam.Name);
                            }
                            else
                            {
                                Log.LogWarning("-> Settings for team '{sourceTeamName}'.. Could not migrate board settings", sourceTeam.Name);
                            }
                        }
                        if (_Options.MigrateTeamSettings == true)
                        {
                            if (MigrateTeamMembers(sourceTeam, newTeam).Result)
                            {
                                Log.LogDebug("-> Team '{0}' Members migrated", newTeam.Name);
                            }
                            else
                            {
                                Log.LogWarning("-> Members for team '{sourceTeamName}'.. Could not migrate team members", sourceTeam.Name);
                            }
                        }
                    }
                }
                else
                {
                    Log.LogDebug("Team '{0}' found.. skipping", sourceTeam.Name);
                }

                witstopwatch.Stop();
                elapsedms = elapsedms + witstopwatch.ElapsedMilliseconds;
                current--;
                count++;
                TimeSpan average = new TimeSpan(0, 0, 0, 0, (int)(elapsedms / count));
                TimeSpan remaining = new TimeSpan(0, 0, 0, 0, (int)(average.TotalMilliseconds * current));
                //Log.LogInformation("Average time of {0} per work item and {1} estimated to completion", string.Format(@"{0:s\:fff} seconds", average), string.Format(@"{0:%h} hours {0:%m} minutes {0:s\:fff} seconds", remaining)));
            }
            // Set Team Settings
            //foreach (TeamFoundationTeam sourceTeam in sourceTL)
            //{
            //    Stopwatch witstopwatch = new Stopwatch();
            //    witstopwatch.Start();
            //    var foundTargetTeam = (from x in targetTL where x.Name == sourceTeam.Name select x).SingleOrDefault();
            //    if (foundTargetTeam == null)
            //    {
            //        TLog.LogInformation("Processing team {0}", sourceTeam.Name));
            //        var sourceTCfU = sourceTSCS.GetTeamConfigurations((new[] { sourceTeam.Identity.TeamFoundationId })).SingleOrDefault();
            //        TeamSettings newTeamSettings = CreateTargetTeamSettings(sourceTCfU);
            //        TeamFoundationTeam newTeam = targetTS.CreateTeam(targetProject.Uri.ToString(), sourceTeam.Name, sourceTeam.Description, null);
            //        targetTSCS.SetTeamSettings(newTeam.Identity.TeamFoundationId, newTeamSettings);
            //    }
            //    else
            //    {
            //        Log.LogInformation("Team found.. skipping"));
            //    }

            //    witstopwatch.Stop();
            //    elapsedms = elapsedms + witstopwatch.ElapsedMilliseconds;
            //    current--;
            //    count++;
            //    TimeSpan average = new TimeSpan(0, 0, 0, 0, (int)(elapsedms / count));
            //    TimeSpan remaining = new TimeSpan(0, 0, 0, 0, (int)(average.TotalMilliseconds * current));
            //    //Log.LogInformation("Average time of {0} per work item and {1} estimated to completion", string.Format(@"{0:s\:fff} seconds", average), string.Format(@"{0:%h} hours {0:%m} minutes {0:s\:fff} seconds", remaining)));

            //}
            //////////////////////////////////////////////////
            stopwatch.Stop();
            Log.LogDebug("DONE in {Elapsed} ", stopwatch.Elapsed.ToString("c"));
        }

        private async Task<bool> MigrateTeamMembers(TeamFoundationTeam sourceTeam, TeamFoundationTeam newTeam)
        {
            var sourceHttpClient = GetHttpClient(Source.Options.Organisation, Source.Options.AccessToken);
            var targetHttpClient = GetHttpClient(Target.Options.Organisation, Target.Options.AccessToken);

            var sourceMembers = await GetTeamMembers(sourceHttpClient, Source.Options.Project, sourceTeam.Name);
            var targetMembers = await GetTeamMembers(targetHttpClient, Target.Options.Project, newTeam.Name);

            var targetTeam = await GetTeam(targetHttpClient, Target.Options.Project, newTeam.Name);

            foreach (var member in sourceMembers)
            {
                var existing = targetMembers.FirstOrDefault((m) => m.identity.uniqueName == member.identity.uniqueName);

                if (existing == null)
                {
                    if (false == await addTeamMember(targetHttpClient, Target.Options.Organisation, targetTeam.id, member.identity.id))
                    {
                        Log.LogWarning("-> Team '{0}' ... Could not add member: {1}", sourceTeam.Name, member.identity.uniqueName);
                    }
                }
            }

            return true;
        }

        private async Task<bool> addTeamMember(HttpClient httpClient, string organization, string groupId, string memberId)
        {
            string url = string.Format("https://vsaex.dev.azure.com/{0}/_apis/GroupEntitlements/{1}/members/{2}?api-version=6.1-preview.1"
                                    , GetOrganization(organization), groupId, memberId);

            var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

            var res = await httpClient.PutAsync(url, content);

            if (res.IsSuccessStatusCode == false)
            {
                Log.LogWarning("-> FAILED to add team member: {0}", await res.Content.ReadAsStringAsync());
                return false;
            }
            return true;
        }

        /// <summary>
        /// Create a new instance of HttpClient including Headers
        /// </summary>
        /// <returns>HttpClient</returns>
        internal HttpClient GetHttpClient(string organization, string token)
        {
            HttpClient client = new HttpClient();

            client.BaseAddress = new Uri(organization);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", token))));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

            return client;
        }
        private async Task<bool> MigrateTeamBoardSettings(TeamFoundationTeam sourceTeam, TeamFoundationTeam newTeam)
        {
            var sourceHttpClient = GetHttpClient(Source.Options.Organisation, Source.Options.AccessToken);
            var targetHttpClient = GetHttpClient(Target.Options.Organisation, Target.Options.AccessToken);

            var sourceBoards = await GetBoards(sourceHttpClient, Source.Options.Project, sourceTeam);
            var targetBoards = await GetBoards(targetHttpClient, Target.Options.Project, newTeam);

            var targetBoardColumnsMappedToBoards = await GetTargetBoardColumnMapping(targetHttpClient, Target.Options.Project, newTeam, targetBoards);

            var fieldValueMappings = Engine.FieldMaps.Items.First().Value.FindAll(m =>
                        m.Name.Equals("FieldValueMap") &&
                        (
                            ((MigrationTools.FieldMaps.AzureDevops.ObjectModel.FieldValueMap)m).Config.sourceField.Equals("System.State")
                        ));

            foreach (var board in sourceBoards)
            {
                var sourceBoardColumns = await GetBoardColumns(sourceHttpClient, Source.Options.Project, sourceTeam, board);
                var sourceBoardRows = await GetBoardRows(sourceHttpClient, Source.Options.Project, sourceTeam, board);

                var targetBoard = FindTargetBoard(targetBoards, targetBoardColumnsMappedToBoards, sourceBoardColumns);
                if (targetBoard != null)
                {
                    List<BoardColumn> newColumns = new List<BoardColumn>();
                    List<BoardRow> newRows = new List<BoardRow>();

                    var targetBoardRows = await GetBoardRows(targetHttpClient, Target.Options.Project, newTeam, targetBoard);

                    newColumns = BuildBoardColumns(sourceBoardColumns, targetBoard, targetBoardColumnsMappedToBoards, fieldValueMappings);
                    newRows = BuildBoardRows(sourceBoardRows, targetBoardRows);

                    if (await UpdateTeamBoard(targetHttpClient, Target.Options.Project, newTeam, targetBoard, newColumns, newRows))
                    {
                        Log.LogDebug("-> Team '{0}' ... {1} Board settings... applied", newTeam.Name, targetBoard.name);
                    }
                    else
                    {
                        Log.LogWarning("-> Team '{0}' ... {1} Board settings... applied.. Could not assign board settings", sourceTeam.Name, targetBoard.name);
                    }
                }
            }

            return true;
        }

        private List<BoardRow> BuildBoardRows(List<BoardRow> sourceBoardRows, List<BoardRow> targetBoardRows)
        {
            const string defaultRowId = "00000000-0000-0000-0000-000000000000";
            List<BoardRow> newRows = new List<BoardRow>();
            foreach (BoardRow row in sourceBoardRows)
            {
                if (row.id.Equals(defaultRowId))
                    newRows.Add(row);
                else
                    newRows.Add(new BoardRow()
                    {
                        id = null,
                        name = row.name
                    });
            }

            return newRows;
        }

        private List<BoardColumn> BuildBoardColumns(List<BoardColumn> sourceBoardColumns, Board targetBoard, Dictionary<string, List<BoardColumn>> targetBoardColumnsMappedToBoards, List<_EngineV1.Containers.IFieldMap> fieldValueMappings)
        {
            List<BoardColumn> newColumns = new List<BoardColumn>();
            BoardColumn sourceIncomingColumn = sourceBoardColumns.Find(c => c.columnType.Equals("incoming"));
            BoardColumn targetIncomingColumn = targetBoardColumnsMappedToBoards[targetBoard.id].Find(c => c.columnType.Equals("incoming"));

            BoardColumn templateTargetIncomingColumn = targetBoardColumnsMappedToBoards[targetBoard.id].Find(c => c.columnType.Equals("incoming"));
            BoardColumn templateTargetInProgressColumn = targetBoardColumnsMappedToBoards[targetBoard.id].Find(c => c.columnType.Equals("inProgress"));
            BoardColumn templateTargetOutgoingColumn = targetBoardColumnsMappedToBoards[targetBoard.id].Find(c => c.columnType.Equals("outgoing"));

            var column = MapExistingBoardColumnProperties(targetBoardColumnsMappedToBoards[targetBoard.id], sourceIncomingColumn, targetIncomingColumn, fieldValueMappings, templateTargetIncomingColumn.stateMappings);
            newColumns.Add(column);

            foreach (var inprogressColumn in sourceBoardColumns.Where(c => c.columnType.Equals("inProgress")))
            {
                BoardColumn newInProgressColumn = new BoardColumn();

                column = MapExistingBoardColumnProperties(targetBoardColumnsMappedToBoards[targetBoard.id], inprogressColumn, newInProgressColumn, fieldValueMappings, templateTargetInProgressColumn.stateMappings);

                newColumns.Add(column);
            }

            BoardColumn sourceOutgoingColumn = sourceBoardColumns.Find(c => c.columnType.Equals("outgoing"));
            BoardColumn targetOutgoingColumn = targetBoardColumnsMappedToBoards[targetBoard.id].Find(c => c.columnType.Equals("outgoing"));

            column = MapExistingBoardColumnProperties(targetBoardColumnsMappedToBoards[targetBoard.id], sourceOutgoingColumn, targetOutgoingColumn, fieldValueMappings, templateTargetOutgoingColumn.stateMappings);

            newColumns.Add(column);

            return newColumns;
        }

        private async Task<bool> UpdateTeamBoard(HttpClient httpClient, string project, TeamFoundationTeam team, Board board, List<BoardColumn> newColumns, List<BoardRow> newRows)
        {
            string url = string.Format("{0}/{1}/{2}/_apis/work/boards/{3}/columns?api-version=6.1-preview.1", httpClient.BaseAddress, project, team.Identity.TeamFoundationId, board.id);

            var content = new StringContent(JsonConvert.SerializeObject(newColumns), Encoding.UTF8, "application/json");

            var res = await httpClient.PutAsync(url, content);

            if (res.IsSuccessStatusCode == false)
            {
                Log.LogWarning("-> FAILED to update Board columns: ", await res.Content.ReadAsStringAsync());
                return false;
            }
            else
            {
                url = string.Format("{0}/{1}/{2}/_apis/work/boards/{3}/rows?api-version=6.1-preview.1", httpClient.BaseAddress, project, team.Identity.TeamFoundationId, board.id);

                content = new StringContent(JsonConvert.SerializeObject(newRows), Encoding.UTF8, "application/json");

                res = await httpClient.PutAsync(url, content);

                if (res.IsSuccessStatusCode == false)
                {
                    Log.LogWarning("-> FAILED to update Board Rows: ", await res.Content.ReadAsStringAsync());
                    return false;
                }
            }
            return true;
        }

        private BoardColumn MapExistingBoardColumnProperties(List<BoardColumn> targetBoardColumns, BoardColumn sourceColumn,
                                                    BoardColumn targetColumn, List<_EngineV1.Containers.IFieldMap> fieldValueMappings,
                                                    Dictionary<string, string> templateStateMappings)
        {
            BoardColumn column = new BoardColumn();
            column.id = targetColumn.id;

            column.name = sourceColumn.name;
            column.isSplit = sourceColumn.isSplit;
            column.description = sourceColumn.description ?? "";
            column.itemLimit = sourceColumn.itemLimit.ToString();
            column.columnType = sourceColumn.columnType;
            column.stateMappings = new Dictionary<string, string>();

            foreach (string key in sourceColumn.stateMappings.Keys)
            {
                string targetWitName = key;

                if (Engine.TypeDefinitionMaps.Items.ContainsKey(key))
                {
                    targetWitName =
                       Engine.TypeDefinitionMaps.Items[key].Map();
                }

                // Verify if we need to provide a state mapping for this work item type on this
                // new board
                if (templateStateMappings.ContainsKey(targetWitName) == false)
                    continue;

                var mappings = fieldValueMappings.FindAll(m => (
                                                    ((MigrationTools.FieldMaps.AzureDevops.ObjectModel.FieldValueMap)m).Config.WorkItemTypeName == "*"
                                                                ||
                                                    ((MigrationTools.FieldMaps.AzureDevops.ObjectModel.FieldValueMap)m).Config.WorkItemTypeName == targetWitName));

                string newState = sourceColumn.stateMappings[key];
                if (mappings != null)
                    foreach (var m in mappings)
                    {
                        _EngineV1.Configuration.FieldMap.FieldValueMapConfig mapper = ((MigrationTools.FieldMaps.AzureDevops.ObjectModel.FieldValueMap)m).Config;
                        string state = sourceColumn.stateMappings[key];

                        if (mapper.valueMapping.ContainsKey(newState))
                            newState = mapper.valueMapping[newState];
                    }

                column.stateMappings.Add(targetWitName, newState);
            }
            // Ensure we have all required work item types specified for this column
            // using the template column mappings provided.
            foreach (var key in templateStateMappings.Keys)
            {
                if (column.stateMappings.ContainsKey(key) == false)
                {
                    column.stateMappings.Add(key, templateStateMappings[key]);
                }
            }
            return column;
        }

        private Board FindTargetBoard(List<Board> targetBoards, Dictionary<string, List<BoardColumn>> targetBoardColumnsMappedToBoards, List<BoardColumn> sourceBoardColumns)
        {
            BoardColumn sourceColumn = sourceBoardColumns[0];

            foreach (string sourceWitName in sourceColumn.stateMappings.Keys)
            {
                string targetWitName = sourceWitName;

                if (Engine.TypeDefinitionMaps.Items.ContainsKey(sourceWitName))
                {
                    targetWitName =
                       Engine.TypeDefinitionMaps.Items[sourceWitName].Map();
                }

                foreach (var board in targetBoards)
                {
                    var mapping = targetBoardColumnsMappedToBoards[board.id];

                    if (mapping.Any(m => m.stateMappings.ContainsKey(targetWitName)))
                        return board;
                }
            }
            return null;
        }

        private async Task<Dictionary<string, List<BoardColumn>>> GetTargetBoardColumnMapping(HttpClient httpClient, string project, TeamFoundationTeam team, List<Board> boards)
        {
            Dictionary<string, List<BoardColumn>> mapping = new Dictionary<string, List<BoardColumn>>();

            foreach (var board in boards)
            {
                var columns = await GetBoardColumns(httpClient, project, team, board);

                mapping.Add(board.id, columns);
            }
            return mapping;
        }

        private async Task<List<TeamMember>> GetTeamMembers(HttpClient httpClient, string project, string teamName)
        {
            string url = string.Format("{0}/_apis/projects/{1}/teams/{2}/members?api-version=6.1-preview.2", httpClient.BaseAddress, project, teamName);

            var res = await httpClient.GetAsync(url);

            var members = JsonConvert.DeserializeObject<TeamMembersResponse>(await res.Content.ReadAsStringAsync());

            return members.value;
        }

        private async Task<Team> GetTeam(HttpClient httpClient, string project, string teamName)
        {
            string url = string.Format("{0}/_apis/projects/{1}/teams/{2}?api-version=6.1-preview.3", httpClient.BaseAddress, project, teamName);

            var res = await httpClient.GetAsync(url);

            var team = JsonConvert.DeserializeObject<Team>(await res.Content.ReadAsStringAsync());

            return team;
        }

        private string GetOrganization(string organizationUri)
        {
            string newUri = "dev.azure.com";
            string oldUri = "visualstudio.com";

            if (organizationUri.Contains(newUri))
            {
                return organizationUri.Substring(organizationUri.IndexOf(newUri) + (newUri.Length + 1)).Replace("/", "");
            }
            else if (organizationUri.Contains(oldUri))
            {
                return organizationUri.Substring(0, organizationUri.IndexOf(oldUri)).Replace("https://", "").Replace("http://", "");
            }
            return organizationUri;
        }

        private async Task<List<Board>> GetBoards(HttpClient httpClient, string project, TeamFoundationTeam team)
        {
            string url = string.Format("{0}/{1}/{2}/_apis/work/boards?api-version=6.1-preview.1", httpClient.BaseAddress, project, team.Identity.TeamFoundationId);

            var res = await httpClient.GetAsync(url);

            var boards = JsonConvert.DeserializeObject<BoardResponse>(await res.Content.ReadAsStringAsync());

            return boards.value;
        }
        private async Task<List<BoardColumn>> GetBoardColumns(HttpClient httpClient, string project, TeamFoundationTeam team, Board board)
        {
            string url = string.Format("{0}/{1}/{2}/_apis/work/boards/{3}/columns?api-version=6.1-preview.1", httpClient.BaseAddress, project, team.Identity.TeamFoundationId, board.id);

            var res = await httpClient.GetAsync(url);

            var columns = JsonConvert.DeserializeObject<BoardColumnsResponse>(await res.Content.ReadAsStringAsync());

            return columns.value;
        }
        private async Task<List<BoardRow>> GetBoardRows(HttpClient httpClient, string project, TeamFoundationTeam team, Board board)
        {
            string url = string.Format("{0}/{1}/{2}/_apis/work/boards/{3}/rows?api-version=6.1-preview.1", httpClient.BaseAddress, project, team.Identity.TeamFoundationId, board.id);

            var res = await httpClient.GetAsync(url);

            var rowsResponse = JsonConvert.DeserializeObject<BoardRowsResponse>(await res.Content.ReadAsStringAsync());

            return rowsResponse.value;
        }
    }
}
