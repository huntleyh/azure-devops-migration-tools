using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Git.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using MigrationTools;
using MigrationTools._EngineV1.Clients;
using MigrationTools._EngineV1.Configuration;
using MigrationTools._EngineV1.Configuration.Processing;
using MigrationTools._EngineV1.DataContracts;
using MigrationTools._EngineV1.Enrichers;
using MigrationTools._EngineV1.Processors;
using MigrationTools.DataContracts;
using MigrationTools.Enrichers;
using MigrationTools.ProcessorEnrichers;
using Newtonsoft.Json;
using Serilog.Context;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace VstsSyncMigrator.Engine
{
    public class GitRepositoryMigrationContext : MigrationProcessorBase
    {
        /// <summary>
        /// The processor configuration
        /// </summary>
        private GitRepositoryMigrationConfig _config;
        private ILogger<GitRepositoryMigrationContext> _logger;

        public GitRepositoryMigrationContext(IMigrationEngine engine, IServiceProvider services, ITelemetryLogger telemetry, ILogger<GitRepositoryMigrationContext> logger) : base(engine, services, telemetry, logger)
        {
            this._logger = logger;
        }

        public override string Name
        {
            get
            {
                return "GitRepositoryMigrationContext";
            }
        }

        public override void Configure(IProcessorConfig config)
        {
            this._config = (GitRepositoryMigrationConfig)config;
        }

        protected override void InternalExecute()
        {
            string serviceConnectionName = this._config.ServiceConnectionName;

            if(string.IsNullOrEmpty(this._config.ServiceConnectionName))
            {
                serviceConnectionName = CreateTemporaryServiceConnection();
            }

            var sourceGitRepositoryService = Engine.Source.GetService<GitRepositoryService>();
            var targetGitRepositoryService = Engine.Target.GetService<GitRepositoryService>();

        }

        private string CreateTemporaryServiceConnection()
        {
            return "";
        }
    }
}
