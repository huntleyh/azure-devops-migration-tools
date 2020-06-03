using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VstsSyncMigrator.Core;
using VstsSyncMigrator.Core.Execution.OMatics;

namespace VstsSyncMigrator.Engine
{
    public class WorkItemMigrator
    {
        private WorkItemLinkOMatic workItemLinkOMatic = new WorkItemLinkOMatic();
        private AttachmentOMatic attachmentOMatic;
        private RepoOMatic repoOMatic;
        EmbededImagesRepairOMatic embededImagesRepairOMatic = new EmbededImagesRepairOMatic();
        private IDictionary<string, double> processWorkItemMetrics = null;
        private IDictionary<string, string> processWorkItemParamiters = null;

        private WorkItemMigratorConfig _config;

        public WorkItemMigrator(WorkItemMigratorConfig workItemMigratorConfig)
        {
            this._config = workItemMigratorConfig;

            var workItemServer = this._config.migrationEngine.Source.Collection.GetService<WorkItemServer>();
            attachmentOMatic = new AttachmentOMatic(workItemServer, this._config.AttachmentWorkingPath, this._config.AttachmentMazSize);
            repoOMatic = new RepoOMatic(this._config.migrationEngine);
        }

        public bool ProcessWorkItem(WorkItemStoreContext sourceStore, WorkItemStoreContext targetStore, Project destProject, WorkItem sourceWorkItem, int retryLimit = 5, int retrys = 0)
        {
            var witstopwatch = Stopwatch.StartNew();
            var starttime = DateTime.Now;
            processWorkItemMetrics = new Dictionary<string, double>();
            processWorkItemParamiters = new Dictionary<string, string>();
            AddParameter("SourceURL", processWorkItemParamiters, sourceStore.Store.TeamProjectCollection.Uri.ToString());
            AddParameter("SourceWorkItem", processWorkItemParamiters, sourceWorkItem.Id.ToString());
            AddParameter("TargetURL", processWorkItemParamiters, targetStore.Store.TeamProjectCollection.Uri.ToString());
            AddParameter("TargetProject", processWorkItemParamiters, destProject.Name);
            AddParameter("RetryLimit", processWorkItemParamiters, retryLimit.ToString());
            AddParameter("RetryNumber", processWorkItemParamiters, retrys.ToString());

            try
            {
                var targetWorkItem = targetStore.FindReflectedWorkItem(sourceWorkItem, false);
                TraceWriteLine(sourceWorkItem);
                ///////////////////////////////////////////////
                TraceWriteLine(sourceWorkItem, $"Work Item has {sourceWorkItem.Rev} revisions and revision migration is set to {_config.ReplayRevisions}");

                List<RevisionItem> revisionsToMigrate = RevisionsToMigrate(sourceWorkItem, targetWorkItem);
                if (targetWorkItem == null)
                {
                    targetWorkItem = ReplayRevisions(revisionsToMigrate, sourceWorkItem, null, destProject, sourceStore, _config.currentWorkItemId, targetStore);
                    AddMetric("Revisions", processWorkItemMetrics, revisionsToMigrate.Count);
                }
                else
                {
                    if (revisionsToMigrate.Count == 0)
                    {
                        ProcessWorkItemAttachments(sourceWorkItem, targetWorkItem, false);
                        ProcessWorkItemLinks(sourceStore, targetStore, sourceWorkItem, targetWorkItem, false);
                        TraceWriteLine(sourceWorkItem, "Skipping as work item exists and no revisions to sync detected", ConsoleColor.Yellow);
                        processWorkItemMetrics.Add("Revisions", 0);
                    }
                    else
                    {
                        TraceWriteLine(sourceWorkItem, $"Syncing as there are {revisionsToMigrate.Count} revisons detected", ConsoleColor.Yellow);

                        targetWorkItem = ReplayRevisions(revisionsToMigrate, sourceWorkItem, targetWorkItem, destProject, sourceStore, _config.currentWorkItemId, targetStore);

                        AddMetric("Revisions", processWorkItemMetrics, revisionsToMigrate.Count);
                        AddMetric("SyncRev", processWorkItemMetrics, revisionsToMigrate.Count);
                    }
                }
                AddParameter("TargetWorkItem", processWorkItemParamiters, targetWorkItem.Revisions.Count.ToString());
                ///////////////////////////////////////////////
                ProcessHTMLFieldAttachements(targetWorkItem);
                ///////////////////////////////////////////////
                ///////////////////////////////////////////////////////
                if (targetWorkItem != null && targetWorkItem.IsDirty)
                {
                    SaveWorkItem(targetWorkItem);
                }
                if (targetWorkItem != null)
                {
                    targetWorkItem.Close();
                }
                if (sourceWorkItem != null)
                {
                    sourceWorkItem.Close();
                }
            }
            catch (WebException ex)
            {
                Telemetry.Current.TrackException(ex);

                TraceWriteLine(sourceWorkItem, ex.ToString());
                if (retrys < retryLimit)
                {
                    TraceWriteLine(sourceWorkItem, $"WebException: Will retry in {retrys}s ");
                    System.Threading.Thread.Sleep(new TimeSpan(0, 0, retrys));
                    retrys++;
                    TraceWriteLine(sourceWorkItem, $"RETRY {retrys}/{retrys} ");
                    ProcessWorkItem(sourceStore, targetStore, destProject, sourceWorkItem, retryLimit, retrys);
                }
                else
                {
                    TraceWriteLine(sourceWorkItem, "ERROR: Failed to create work item. Retry Limit reached ");
                }
            }
            catch (Exception ex)
            {
                Telemetry.Current.TrackException(ex);
                TraceWriteLine(sourceWorkItem, ex.ToString());
                Telemetry.Current.TrackRequest("ProcessWorkItem", starttime, witstopwatch.Elapsed, "502", false);
                throw ex;
            }
            witstopwatch.Stop();
            //_config.elapsedms += witstopwatch.ElapsedMilliseconds;
            //processWorkItemMetrics.Add("ElapsedTimeMS", _config.elapsedms);

            //var average = new TimeSpan(0, 0, 0, 0, (int)(_config.elapsedms / _config.current));
            //var remaining = new TimeSpan(0, 0, 0, 0, (int)(average.TotalMilliseconds * _config.count));
            //TraceWriteLine(sourceWorkItem,
            //    string.Format("Average time of {0} per work item and {1} estimated to completion",
            //        string.Format(@"{0:s\:fff} seconds", average),
            //        string.Format(@"{0:%h} hours {0:%m} minutes {0:s\:fff} seconds", remaining))
            //    );
            Trace.Flush();
            Telemetry.Current.TrackEvent("WorkItemMigrated", processWorkItemParamiters, processWorkItemMetrics);
            Telemetry.Current.TrackRequest("ProcessWorkItem", starttime, witstopwatch.Elapsed, "200", true);

            return true;
        }
        internal static void SaveWorkItem(WorkItem workItem)
        {
            workItem.Fields["System.ChangedBy"].Value = "Migration";
            workItem.Save();
        }
        private void ProcessHTMLFieldAttachements(WorkItem targetWorkItem)
        {
            if (targetWorkItem != null && _config.FixHtmlAttachmentLinks)
            {
                embededImagesRepairOMatic.FixHtmlAttachmentLinks(targetWorkItem, _config.migrationEngine.Source.Collection.Uri.ToString(), _config.migrationEngine.Target.Collection.Uri.ToString(), _config.migrationEngine.Source.Config.PersonalAccessToken);
            }
        }
        private void ProcessWorkItemLinks(WorkItemStoreContext sourceStore, WorkItemStoreContext targetStore, WorkItem sourceWorkItem, WorkItem targetWorkItem, bool save)
        {
            if (targetWorkItem != null && _config.LinkMigration && sourceWorkItem.Links.Count > 0)
            {
                TraceWriteLine(sourceWorkItem, $"Links {sourceWorkItem.Links.Count} | LinkMigrator:{_config.LinkMigration}");
                workItemLinkOMatic.MigrateLinks(sourceWorkItem, sourceStore, targetWorkItem, targetStore, save);
                AddMetric("RelatedLinkCount", processWorkItemMetrics, targetWorkItem.Links.Count);
                int fixedLinkCount = repoOMatic.FixExternalLinks(targetWorkItem, targetStore, sourceWorkItem, save);
                AddMetric("FixedGitLinkCount", processWorkItemMetrics, fixedLinkCount);
            }
        }

        private void ProcessWorkItemAttachments(WorkItem sourceWorkItem, WorkItem targetWorkItem, bool save = true)
        {
            if (targetWorkItem != null && _config.AttachmentMigration && sourceWorkItem.Attachments.Count > 0)
            {
                TraceWriteLine(sourceWorkItem, $"Attachemnts {sourceWorkItem.Attachments.Count} | LinkMigrator:{_config.AttachmentMigration}");
                attachmentOMatic.ProcessAttachemnts(sourceWorkItem, targetWorkItem, save);
                AddMetric("Attachments", processWorkItemMetrics, targetWorkItem.AttachedFileCount);
            }
        }
        private List<RevisionItem> RevisionsToMigrate(WorkItem sourceWorkItem, WorkItem targetWorkItem)
        {
            // just to make sure, we replay the events in the same order as they appeared
            // maybe, the Revisions collection is not sorted according to the actual Revision number
            List<RevisionItem> sortedRevisions = null;
            sortedRevisions = sourceWorkItem.Revisions.Cast<Revision>()
                    .Select(x => new RevisionItem
                    {
                        Index = x.Index,
                        Number = Convert.ToInt32(x.Fields["System.Rev"].Value),
                        ChangedDate = Convert.ToDateTime(x.Fields["System.ChangedDate"].Value)


                    })
                    .ToList();

            if (targetWorkItem != null)
            {
                // Target exists so remove any Changed Date matches bwtween them
                var targetChangedDates = (from Revision x in targetWorkItem.Revisions select Convert.ToDateTime(x.Fields["System.ChangedDate"].Value)).ToList();
                if (_config.ReplayRevisions)
                {
                    sortedRevisions = sortedRevisions.Where(x => !targetChangedDates.Contains(x.ChangedDate)).ToList();
                }
                // Find Max target date and remove all source revisions that are newer
                var targetLatestDate = targetChangedDates.Max();
                sortedRevisions = sortedRevisions.Where(x => x.ChangedDate > targetLatestDate).ToList();
            }

            sortedRevisions = sortedRevisions.OrderBy(x => x.Number).ToList();
            if (!_config.ReplayRevisions && sortedRevisions.Count > 0)
            {
                // Remove all but the latest revision if we are not replaying reviss=ions
                sortedRevisions.RemoveRange(0, sortedRevisions.Count - 1);
            }
            TraceWriteLine(sourceWorkItem, $"Found {sortedRevisions.Count} revisions to migrate on  Work item:{sourceWorkItem.Id}", ConsoleColor.Gray, true);
            return sortedRevisions;
        }
        private class RevisionItem
        {
            public int Index { get; set; }
            public int Number { get; set; }
            public DateTime ChangedDate { get; internal set; }
        }

        private WorkItem ReplayRevisions(List<RevisionItem> revisionsToMigrate, WorkItem sourceWorkItem, WorkItem targetWorkItem, Project destProject, WorkItemStoreContext sourceStore,
            int current,
            WorkItemStoreContext targetStore)
        {

            try
            {
                var skipToFinalRevisedWorkItemType = _config.SkipToFinalRevisedWorkItemType;

                var last = sourceStore.GetRevision(sourceWorkItem, revisionsToMigrate.Last().Number);

                //If work item hasn't been created yet, create a shell
                if (targetWorkItem == null)
                {
                    targetWorkItem = CreateWorkItem_Shell(destProject, sourceWorkItem, sourceStore.GetRevision(sourceWorkItem, revisionsToMigrate.First().Number).Type.Name);
                }

                if (_config.CollapseRevisions)
                {
                    var data = revisionsToMigrate.Select(rev => sourceStore.GetRevision(sourceWorkItem, rev.Number)).Select(rev => new
                    {
                        rev.Id,
                        rev.Rev,
                        rev.RevisedDate,
                        Fields = rev.Fields.AsDictionary()
                    });

                    var fileData = JsonConvert.SerializeObject(data, new JsonSerializerSettings { PreserveReferencesHandling = PreserveReferencesHandling.None });
                    var filePath = Path.Combine(Path.GetTempPath(), $"{sourceWorkItem.Id}_PreMigrationHistory.json");

                    File.WriteAllText(filePath, fileData);
                    targetWorkItem.Attachments.Add(new Attachment(filePath, "History has been consolidated into the attached file."));

                    revisionsToMigrate = revisionsToMigrate.GetRange(revisionsToMigrate.Count - 1, 1);

                    TraceWriteLine(targetWorkItem, $" Attached a consolidated set of {data.Count()} revisions.");
                }

                foreach (var revision in revisionsToMigrate)
                {
                    var currentRevisionWorkItem = sourceStore.GetRevision(sourceWorkItem, revision.Number);

                    TraceWriteLine(currentRevisionWorkItem, $" Processing Revision [{revision.Number}]");

                    // Decide on WIT
                    string destType = currentRevisionWorkItem.Type.Name;
                    if (_config.migrationEngine.WorkItemTypeDefinitions.ContainsKey(destType))
                    {
                        destType =
                           _config.migrationEngine.WorkItemTypeDefinitions[destType].Map(currentRevisionWorkItem);
                    }

                    PopulateWorkItem(currentRevisionWorkItem, targetWorkItem, destType);
                    _config.migrationEngine.ApplyFieldMappings(currentRevisionWorkItem, targetWorkItem);

                    targetWorkItem.Fields["System.ChangedBy"].Value =
                        currentRevisionWorkItem.Revisions[revision.Index].Fields["System.ChangedBy"].Value;

                    targetWorkItem.Fields["System.History"].Value =
                        currentRevisionWorkItem.Revisions[revision.Index].Fields["System.History"].Value;

                    var fails = targetWorkItem.Validate();

                    foreach (Field f in fails)
                    {
                        TraceWriteLine(currentRevisionWorkItem,
                            $"{current} - Invalid: {currentRevisionWorkItem.Id}-{currentRevisionWorkItem.Type.Name}-{f.ReferenceName}-{sourceWorkItem.Title} Value: {f.Value}");
                    }

                    targetWorkItem.Save();
                    TraceWriteLine(currentRevisionWorkItem,
                        $" Saved TargetWorkItem {targetWorkItem.Id}. Replayed revision {revision.Number} of {revisionsToMigrate.Count}");

                }

                if (targetWorkItem != null)
                {
                    ProcessWorkItemAttachments(sourceWorkItem, targetWorkItem, false);
                    ProcessWorkItemLinks(sourceStore, targetStore, sourceWorkItem, targetWorkItem, false);
                    string reflectedUri = sourceStore.CreateReflectedWorkItemId(sourceWorkItem);
                    if (targetWorkItem.Fields.Contains(_config.migrationEngine.Target.Config.ReflectedWorkItemIDFieldName))
                    {

                        targetWorkItem.Fields[_config.migrationEngine.Target.Config.ReflectedWorkItemIDFieldName].Value = reflectedUri;
                    }
                    var history = new StringBuilder();
                    history.Append(
                        $"This work item was migrated from a different project or organization. You can find the old version at <a href=\"{reflectedUri}\">{reflectedUri}</a>.");
                    targetWorkItem.History = history.ToString();
                    SaveWorkItem(targetWorkItem);

                    attachmentOMatic.CleanUpAfterSave(targetWorkItem);
                    TraceWriteLine(sourceWorkItem, $"...Saved as {targetWorkItem.Id}");

                    if (_config.UpdateSourceReflectedId && sourceWorkItem.Fields.Contains(_config.migrationEngine.Source.Config.ReflectedWorkItemIDFieldName))
                    {
                        sourceWorkItem.Fields[_config.migrationEngine.Source.Config.ReflectedWorkItemIDFieldName].Value =
                            targetStore.CreateReflectedWorkItemId(targetWorkItem);
                        SaveWorkItem(sourceWorkItem);
                        TraceWriteLine(sourceWorkItem, $"...and Source Updated {sourceWorkItem.Id}");
                    }

                }
            }
            catch (Exception ex)
            {
                TraceWriteLine(sourceWorkItem, "...FAILED to Save");

                if (targetWorkItem != null)
                {
                    foreach (Field f in targetWorkItem.Fields)
                        TraceWriteLine(sourceWorkItem, $"{f.ReferenceName} ({f.Name}) | {f.Value}");
                }
                TraceWriteLine(sourceWorkItem, ex.ToString());
            }

            return targetWorkItem;
        }

        private WorkItem CreateWorkItem_Shell(Project destProject, WorkItem currentRevisionWorkItem, string destType)
        {
            WorkItem newwit;
            var newWorkItemstartTime = DateTime.UtcNow;
            var newWorkItemTimer = Stopwatch.StartNew();
            if (destProject.WorkItemTypes.Contains(destType))
            {
                newwit = destProject.WorkItemTypes[destType].NewWorkItem();
            }
            else
            {
                throw new Exception(string.Format("WARNING: Unable to find '{0}' in the target project. Most likley this is due to a typo in the .json configuration under WorkItemTypeDefinition! ", destType));
            }
            newWorkItemTimer.Stop();
            Telemetry.Current.TrackDependency("TeamService", "NewWorkItem", newWorkItemstartTime, newWorkItemTimer.Elapsed, true);
            if (_config.UpdateCreatedBy) { newwit.Fields["System.CreatedBy"].Value = currentRevisionWorkItem.Revisions[0].Fields["System.CreatedBy"].Value; }
            if (_config.UpdateCreatedDate) { newwit.Fields["System.CreatedDate"].Value = currentRevisionWorkItem.Revisions[0].Fields["System.CreatedDate"].Value; }

            return newwit;
        }

        private void PopulateWorkItem(WorkItem oldWi, WorkItem newwit, string destType)
        {
            var newWorkItemstartTime = DateTime.UtcNow;
            var fieldMappingTimer = Stopwatch.StartNew();

            if (newwit.IsPartialOpen || !newwit.IsOpen)
            {
                newwit.Open();
            }

            newwit.Title = oldWi.Title;
            newwit.State = oldWi.State;
            newwit.Reason = oldWi.Reason;

            foreach (Field f in oldWi.Fields)
            {
                if (newwit.Fields.Contains(f.ReferenceName) && !_config.ignoreFieldsList.Contains(f.ReferenceName) && (!newwit.Fields[f.ReferenceName].IsChangedInRevision || newwit.Fields[f.ReferenceName].IsEditable))
                {
                    newwit.Fields[f.ReferenceName].Value = oldWi.Fields[f.ReferenceName].Value;
                }
            }

            newwit.AreaPath = GetNewNodeName(oldWi.AreaPath, oldWi.Project.Name, newwit.Project.Name, newwit.Store, "Area");
            newwit.IterationPath = GetNewNodeName(oldWi.IterationPath, oldWi.Project.Name, newwit.Project.Name, newwit.Store, "Iteration");
            switch (destType)
            {
                case "Test Case":
                    newwit.Fields["Microsoft.VSTS.TCM.Steps"].Value = oldWi.Fields["Microsoft.VSTS.TCM.Steps"].Value;
                    newwit.Fields["Microsoft.VSTS.Common.Priority"].Value =
                        oldWi.Fields["Microsoft.VSTS.Common.Priority"].Value;
                    break;
            }

            if (newwit.Fields.Contains("Microsoft.VSTS.Common.BacklogPriority")
                && newwit.Fields["Microsoft.VSTS.Common.BacklogPriority"].Value != null
                && !IsNumeric(newwit.Fields["Microsoft.VSTS.Common.BacklogPriority"].Value.ToString(),
                    NumberStyles.Any))
                newwit.Fields["Microsoft.VSTS.Common.BacklogPriority"].Value = 10;

            var description = new StringBuilder();
            description.Append(oldWi.Description);
            newwit.Description = description.ToString();
            fieldMappingTimer.Stop();
            // Trace.WriteLine(
            //    $"FieldMapOnNewWorkItem: {newWorkItemstartTime} - {fieldMappingTimer.Elapsed.ToString("c")}", Name);
        }
        NodeDetecomatic _nodeOMatic;

        private string GetNewNodeName(string oldNodeName, string oldProjectName, string newProjectName, WorkItemStore newStore, string nodePath)
        {
            if (_nodeOMatic == null)
            {
                _nodeOMatic = new NodeDetecomatic(newStore);
            }

            // Replace project name with new name (if necessary) and inject nodePath (Area or Iteration) into path for node validation
            string newNodeName = "";
            if (_config.PrefixProjectToNodes)
            {
                newNodeName = $@"{newProjectName}\{nodePath}\{oldNodeName}";
            }
            else
            {
                var regex = new Regex(Regex.Escape(oldProjectName));
                if (oldNodeName.StartsWith($@"{oldProjectName}\{nodePath}\"))
                {
                    newNodeName = regex.Replace(oldNodeName, newProjectName, 1);
                }
                else
                {
                    newNodeName = regex.Replace(oldNodeName, $@"{newProjectName}\{nodePath}", 1);
                }
            }

            // Validate the node exists
            if (!_nodeOMatic.NodeExists(newNodeName))
            {
                Trace.WriteLine(string.Format("The Node '{0}' does not exist, leaving as '{1}'. This may be because it has been renamed or moved and no longer exists, or that you have not migrateed the Node Structure yet.", newNodeName, newProjectName));
                newNodeName = newProjectName;
            }

            // Remove nodePath (Area or Iteration) from path for correct population in work item
            if (newNodeName.StartsWith(newProjectName + '\\' + nodePath + '\\'))
            {
                return newNodeName.Remove(newNodeName.IndexOf($@"{nodePath}\"), $@"{nodePath}\".Length);
            }
            else if (newNodeName.StartsWith(newProjectName + '\\' + nodePath))
            {
                return newNodeName.Remove(newNodeName.IndexOf($@"{nodePath}"), $@"{nodePath}".Length);
            }
            else
            {
                return newNodeName;
            }
        }
        private static bool IsNumeric(string val, NumberStyles numberStyle)
        {
            double result;
            return double.TryParse(val, numberStyle,
                CultureInfo.CurrentCulture, out result);
        }
        internal void TraceWriteLine(WorkItem sourceWorkItem, string message = "", ConsoleColor colour = ConsoleColor.Green, bool header = false)
        {
            if (header)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Trace.WriteLine("===============================================================================================");
            }
            Console.ForegroundColor = colour;
            Trace.WriteLine($"{TraceWriteLineTags(sourceWorkItem)} | {message}");
            if (header)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Trace.WriteLine("===============================================================================================");
            }
            Console.ForegroundColor = ConsoleColor.White;
        }

        private string TraceWriteLineTags(WorkItem sourceWorkItem, WorkItem targetWorkItem = null)
        {
            string totalWorkItems = _config.totalWorkItem.ToString();
            string currentWorkITem = _config.currentWorkItemId.ToString();
            string sourceWorkItemId = sourceWorkItem.Id.ToString();
            string sourceRevisionInt = sourceWorkItem.Revision.ToString();
            string targetWorkItemId = "null";
            return $"[{sourceWorkItem.Type.Name.PadLeft(20)}][Complete:{currentWorkITem.PadLeft(totalWorkItems.Length)}/{totalWorkItems}][sid:{sourceWorkItemId.PadRight(6)}|Rev:{sourceRevisionInt.PadRight(3)}][tid:{targetWorkItemId.PadRight(6)}";
        }
        internal static void AddParameter(string name, IDictionary<string, string> store, string value)
        {
            if (!store.ContainsKey(name)) store.Add(name, value);
        }

        internal static void AddMetric(string name, IDictionary<string, double> store, double value)
        {
            if (!store.ContainsKey(name)) store.Add(name, value);
        }
    }

    public class WorkItemMigratorConfig
    {
        public List<String> ignoreFieldsList { get; internal set; }
        public int currentWorkItemId { get; set; }
        public bool ReplayRevisions { get; set; }
        public bool FixHtmlAttachmentLinks { get; set; }
        public bool LinkMigration { get; set; }
        public bool AttachmentMigration { get; set; }
        public string AttachmentWorkingPath { get; set; }
        public int AttachmentMazSize { get; set; }
        public bool SkipToFinalRevisedWorkItemType { get; set; }
        public bool CollapseRevisions { get; set; }
        public bool UpdateSourceReflectedId { get; set; }
        public bool UpdateCreatedBy { get; set; }
        public bool UpdateCreatedDate { get; set; }
        public bool PrefixProjectToNodes { get; set; }
        public int totalWorkItem { get; set; }
        public MigrationEngine migrationEngine { get; internal set; }

        public WorkItemMigratorConfig(MigrationEngine me)
        {
            this.migrationEngine = me;
            ignoreFieldsList = new List<string>
            {
                "System.Rev",
                "System.AreaId",
                "System.IterationId",
                "System.Id",
                "System.RevisedDate",
                "System.AuthorizedAs",
                "System.AttachedFileCount",
                "System.TeamProject",
                "System.NodeName",
                "System.RelatedLinkCount",
                "System.WorkItemType",
                "Microsoft.VSTS.Common.StateChangeDate",
                "System.ExternalLinkCount",
                "System.HyperLinkCount",
                "System.Watermark",
                "System.AuthorizedDate",
                "System.BoardColumn",
                "System.BoardColumnDone",
                "System.BoardLane",
                "SLB.SWT.DateOfClientFeedback",
                "System.CommentCount",
                "System.RemoteLinkCount"
            };

            ReplayRevisions = true;
            LinkMigration = true;
            AttachmentMigration = true;
            FixHtmlAttachmentLinks = false;
            AttachmentWorkingPath = "c:\\temp\\WorkItemAttachmentWorkingFolder\\";
            AttachmentMazSize = 480000000;
            UpdateCreatedBy = true;
            PrefixProjectToNodes = false;
            UpdateCreatedDate = true;
            UpdateSourceReflectedId = false;
            //public int current { get; set; }
            SkipToFinalRevisedWorkItemType = false;
            CollapseRevisions = false;
            //public int totalWorkItem { get; set; }
        }
    }
}