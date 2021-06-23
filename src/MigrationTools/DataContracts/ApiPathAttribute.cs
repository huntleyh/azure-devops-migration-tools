using System;

namespace MigrationTools.DataContracts
{
    public class ApiPathAttribute : Attribute
    {
        public ApiPathAttribute(string Path)
        {
            this.Path = Path;
        }

        public string Path { get; }
    }

    public class ApiNameAttribute : Attribute
    {
        public ApiNameAttribute(string Name)
        {
            this.Name = Name;
        }

        public string Name { get; }
    }

    public class ApiVersionAttribute : Attribute
    {
        public ApiVersionAttribute(string version)
        {
            this.Version = version;
        }

        public string Version { get; }
    }
    public class ApiUriDomainPrefixAttribute : Attribute
    {
        public ApiUriDomainPrefixAttribute(string version)
        {
            this.UriDomainPrefix = version;
        }

        public string UriDomainPrefix { get; }
        
    }
    public class ApiOrgLevelAttribute : Attribute
    {
        public bool IsOrgLevel { get; set; }
        public ApiOrgLevelAttribute(bool orgLevel = true)
        {
            IsOrgLevel = orgLevel;
        }
    }
}