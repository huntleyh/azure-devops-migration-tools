using System.Collections.Generic;

namespace MigrationTools._EngineV1.Configuration.Processing
{
    public class GitRepositoryMigrationConfig : IProcessorConfig
    {
        /// <inheritdoc />
        public bool Enabled { get; set; }
        public string ServiceConnectionName { get; set; }

        /// <inheritdoc />
        public string Processor => "GitRepositoryMigrationContext"; 
        
        /// <inheritdoc />
        public bool IsProcessorCompatible(IReadOnlyList<IProcessorConfig> otherProcessors)
        {
            return true;
        }

        public GitRepositoryMigrationConfig()
        {
            Enabled = false;
        }
    }
}
