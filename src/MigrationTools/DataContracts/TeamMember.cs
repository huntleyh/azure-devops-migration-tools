using System.Collections.Generic;

namespace MigrationTools.DataContracts
{
    public class TeamMember
    {
        public bool isTeamAdmin { get; set; }
        public TeamMemberIdentity identity { get; set; }
    }
    public class TeamMemberIdentity
    {
        public string id { get; set; }
        public string displayName { get; set; }
        public string uniqueName { get; set; }
        public string url { get; set; }
    }
    public class TeamMembersResponse
    {
        public int count{ get; set; }
        public List<TeamMember> value{ get; set; }
    }
}
