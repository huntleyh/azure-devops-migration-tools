using System.Collections.Generic;

namespace MigrationTools.DataContracts
{
    public class BoardRow
    {
        public string id { get; set; }
        public string name { get; set; }
    }
    public class BoardRowsResponse
    {
        public int count{ get; set; }
        public List<BoardRow> value{ get; set; }
    }
}
