using System;
using System.Collections.Generic;
using System.Text;

namespace MigrationTools.DataContracts
{
    public class Board
    {
        public string id { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }
    public class BoardResponse
    {
        public int count { get; set; }
        public List<Board> value { get; set; }
    }
}
