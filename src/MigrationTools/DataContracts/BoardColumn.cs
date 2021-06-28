using System.Collections.Generic;

namespace MigrationTools.DataContracts
{
    public class BoardColumn
    {
        public string id { get; set; }
        public string name { get; set; }
        public string itemLimit { get; set; }
        public Dictionary<string, string> stateMappings { get; set; }
        public bool isSplit { get; set; }
        public string description { get; set; }
        public string columnType { get; set; }
    }
    public class BoardColumnsResponse
    {
        public int count{ get; set; }
        public List<BoardColumn> value{ get; set; }
    }
}
