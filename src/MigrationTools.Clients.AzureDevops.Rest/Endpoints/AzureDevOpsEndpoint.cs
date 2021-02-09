using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Core.WebApi;
using MigrationTools.DataContracts;
using MigrationTools.DataContracts.Pipelines;
using MigrationTools.EndpointEnrichers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MigrationTools.Endpoints
{
    public class AzureDevOpsEndpoint : Endpoint<AzureDevOpsEndpointOptions>
    {
        public override int Count => 0;

        public AzureDevOpsEndpoint(EndpointEnricherContainer endpointEnrichers, ITelemetryLogger telemetry, ILogger<AzureDevOpsEndpoint> logger)
            : base(endpointEnrichers, telemetry, logger)
        {
        }

        public override void Configure(AzureDevOpsEndpointOptions options)
        {
            base.Configure(options);
            Log.LogDebug("AzureDevOpsEndpoint::Configure");
            if (string.IsNullOrEmpty(Options.Organisation))
            {
                throw new ArgumentNullException(nameof(Options.Organisation));
            }
            if (string.IsNullOrEmpty(Options.Project))
            {
                throw new ArgumentNullException(nameof(Options.Project));
            }
            if (string.IsNullOrEmpty(Options.AccessToken))
            {
                throw new ArgumentNullException(nameof(Options.AccessToken));
            }
        }

        /// <summary>
        /// Create a new instance of HttpClient including Headers
        /// </summary>
        /// <returns>HttpClient</returns>
        internal HttpClient GetHttpClient<DefinitionType>()
            where DefinitionType : RestApiDefinition
        {
            UriBuilder baseUrl = GetUriBuilderBasedOnEndpointAndType<DefinitionType>();

            HttpClientHandler httpClientHandler = new HttpClientHandler();
            var proxy = new WebProxy("127.0.0.1", 8888);

            httpClientHandler.Proxy = proxy;

            HttpClient client = new HttpClient();//(httpClientHandler);//

            client.BaseAddress = baseUrl.Uri;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", Options.AccessToken))));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
            
            return client;
        }

        /// <summary>
        /// Method to get the RESP API URLs right
        /// </summary>
        /// <returns>UriBuilder</returns>
        private UriBuilder GetUriBuilderBasedOnEndpointAndType<DefinitionType>()
            where DefinitionType : RestApiDefinition
        {
            var apiNameAttribute = typeof(DefinitionType).GetCustomAttributes(typeof(ApiNameAttribute), false).OfType<ApiNameAttribute>().FirstOrDefault();
            var apiPathAttribute = typeof(DefinitionType).GetCustomAttributes(typeof(ApiPathAttribute), false).OfType<ApiPathAttribute>().FirstOrDefault();
            var apiVersionAttribute = typeof(DefinitionType).GetCustomAttributes(typeof(ApiVersionAttribute), false).OfType<ApiVersionAttribute>().FirstOrDefault();
            var apiOrgLevelAttribute = typeof(DefinitionType).GetCustomAttributes(typeof(ApiOrgLevelAttribute), false).OfType<ApiOrgLevelAttribute>().FirstOrDefault();
            if (apiPathAttribute == null)
            {
                throw new ArgumentNullException($"On the class defintion of '{typeof(DefinitionType).Name}' is the attribute 'ApiName' misssing. Please add the 'ApiName' Attribute to your class");
            }
            string apiVersion = string.Empty;
            var builder = new UriBuilder(Options.Organisation);
            builder.Path += ((apiOrgLevelAttribute != null && apiOrgLevelAttribute.IsOrgLevel) ? "" : Options.Project) + "/_apis/" + apiPathAttribute.Path + "/";

            if (apiVersionAttribute != null)
            {
                apiVersion = apiVersionAttribute.Version;
            }
            if (apiNameAttribute.Name == "Release Piplines")
            {
                if (builder.Host.Contains("dev.azure.com"))
                {
                    builder.Host = "vsrm." + builder.Host;
                    builder.Query = "api-version=" + (string.IsNullOrEmpty(apiVersion) ? "6.0" : apiVersion);
                }
                else if (builder.Host.Contains("visualstudio.com"))
                {
                    int num = builder.Host.IndexOf(".visualstudio.com");
                    builder.Host = builder.Host.Substring(0, num) + ".vsrm.visualstudio.com";
                    builder.Query = "api-version=" + (string.IsNullOrEmpty(apiVersion) ? "6.0" : apiVersion);
                }
                else
                {
                    builder.Query = "api-version=" + (string.IsNullOrEmpty(apiVersion) ? "5.1" : apiVersion);
                }
            }
            else
            {
                builder.Query = "api-version=" + (string.IsNullOrEmpty(apiVersion) ? "5.1-preview.1" : apiVersion);
            }
            return builder;
        }

        /// <summary>
        /// Generic Method to get API Definitions (Taskgroups, Variablegroups, Build- or Release Pipelines)
        /// </summary>
        /// <typeparam name="DefinitionType">Type of Definition. Can be: Taskgroup, Build- or Release Pipeline</typeparam>
        /// <returns>List of API Definitions </returns>
        public async Task<IEnumerable<DefinitionType>> GetApiDefinitionsAsync<DefinitionType>()
            where DefinitionType : RestApiDefinition, new()
        {
            var initialDefinitions = new List<DefinitionType>();

            HttpClient client = GetHttpClient<DefinitionType>();
            var httpResponse = await client.GetAsync("");

            if (httpResponse != null)
            {
                var definitions = await httpResponse.Content.ReadAsAsync<RestResultDefinition<DefinitionType>>();

                if(typeof(DefinitionType) == typeof(TaskAgentQueue))
                {
                    foreach (RestApiDefinition definition in definitions.Value)
                    {
                        // Nessecary because getting all Pipelines doesn't include all of their properties
                        var response = await client.GetAsync(definition.Id);
                        var fullDefinition = JsonConvert.DeserializeObject<DefinitionType>(await response.Content.ReadAsStringAsync());
                        //var fullDefinition = await response.Content.ReadAsAsync<DefinitionType>();
                        initialDefinitions.Add(fullDefinition);
                    }
                }
                // Taskgroups only have a LIST option, so the following step is not needed
                else if (!typeof(DefinitionType).ToString().Contains("TaskGroup"))
                {
                    foreach (RestApiDefinition definition in definitions.Value)
                    {
                        // Nessecary because getting all Pipelines doesn't include all of their properties
                        var response = await client.GetAsync(definition.Id);
                        var fullDefinition = JsonConvert.DeserializeObject<DefinitionType>(await response.Content.ReadAsStringAsync());
                        //var fullDefinition = await response.Content.ReadAsAsync<DefinitionType>();
                        initialDefinitions.Add(fullDefinition);
                    }
                }
                else
                {
                    initialDefinitions = definitions.Value.ToList();
                }
            }
            return initialDefinitions;
        }

        /// <summary>
        /// Make HTTP Request to create a Definition
        /// </summary>
        /// <typeparam name="DefinitionType"></typeparam>
        /// <param name="definitionsToBeMigrated"></param>
        /// <returns>List of Mappings</returns>
        public async Task<List<Mapping>> CreateApiDefinitionsAsync<DefinitionType>(IEnumerable<DefinitionType> definitionsToBeMigrated)
            where DefinitionType : RestApiDefinition, new()
        {
            var migratedDefinitions = new List<Mapping>();

            foreach (var definitionToBeMigrated in definitionsToBeMigrated)
            {
                var client = GetHttpClient<DefinitionType>();
                definitionToBeMigrated.ResetObject();

                DefaultContractResolver contractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                };
                var jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = contractResolver,
                };
                string body = JsonConvert.SerializeObject(definitionToBeMigrated, jsonSettings);

                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var result = await client.PostAsync("", content);

                var responseContent = await result.Content.ReadAsStringAsync();
                if (result.StatusCode != HttpStatusCode.OK)
                {
                    Log.LogError("Error migrating {DefinitionType} {DefinitionName}. Please migrate it manually. {ErrorText}", typeof(DefinitionType).Name, definitionToBeMigrated.Name, responseContent);
                    continue;
                }
                else
                {
                    var targetObject = JsonConvert.DeserializeObject<DefinitionType>(responseContent);
                    migratedDefinitions.Add(new Mapping()
                    {
                        Name = definitionToBeMigrated.Name,
                        SourceId = definitionToBeMigrated.Id,
                        TargetId = targetObject.Id
                    });

                }
            }
            Log.LogInformation("{MigratedCount} of {TriedToMigrate} {DefinitionType}(s) got migrated..", migratedDefinitions.Count, definitionsToBeMigrated.Count(), typeof(DefinitionType).Name);
            return migratedDefinitions;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="reposToBeImported"></param>
        /// <param name="sourcePAT"></param>
        /// <returns></returns>
        public async Task<List<Mapping<GitRepository>>> CreateRepositoryImportRequestsAsync(IEnumerable<GitRepository> reposToBeImported, string sourcePAT)
        {
            var migratedDefinitions = new List<Mapping<GitRepository>>();
            var client = GetHttpClient<GitRepository>();
            var svcClient = GetHttpClient<ServiceConnection>();
            var importRequestsClient = GetHttpClient<GitRepository>();
            var apiVersionAttribute = typeof(GitRepository).GetCustomAttributes(typeof(ApiVersionAttribute), false).OfType<ApiVersionAttribute>().FirstOrDefault();
            string apiVersion = "5.1-preview.1";
            List<ImportRequest> importRequests = new List<ImportRequest>();

            if (apiVersionAttribute != null && !string.IsNullOrEmpty(apiVersionAttribute.Version))
            {
                apiVersion = apiVersionAttribute.Version;
            }
            foreach (var repo in reposToBeImported)
            {
                var targetRepo = await CreateTargetRepository(repo, client, apiVersion);
                if (targetRepo != null)
                {
                    string serviceConnectionId = await CreateGitImportServiceConnection(svcClient, sourcePAT, repo.url);

                    var body = new
                    {
                        parameters = new
                        {
                            deleteServiceEndpointAfterImportIsDone = true,
                            gitSource = new
                            {
                                overwrite = false,
                                url = repo.remoteUrl
                            },
                            serviceEndpointId = serviceConnectionId
                        }
                    };

                    var content = JsonContent.Create(body);
                    var result = await client.PostAsync($"{repo.Name}/importRequests?api-version=5.0", content);

                    var responseContent = await result.Content.ReadAsStringAsync();
                    if (false == result.IsSuccessStatusCode)
                    {
                        Log.LogError("GitRepository: Error migrating repository {Name}. Please migrate it manually. {ErrorText}", repo.Name, responseContent);
                        continue;
                    }
                    else
                    {
                        importRequests.Add(await result.Content.ReadAsAsync<ImportRequest>());

                        migratedDefinitions.Add(new Mapping<GitRepository>()
                        {
                            Name = repo.Name,
                            SourceId = repo.Id,
                            TargetId = targetRepo.Id,
                            Ref = targetRepo
                        });
                    }
                }
            }

            // Wait and verify all import requests are completed
            string[] completedStatuses = new string[] { "abandoned", "completed", "failed" };

            for (int index = 0; index < importRequests.Count; index++)
            {
                var result = await importRequestsClient.GetAsync($"{importRequests[index].repository.Id}/importRequests/{importRequests[index].importRequestId}");

                var latest = await result.Content.ReadAsAsync<ImportRequest>();
                if(completedStatuses.Contains(latest.status))
                {
                    if (latest.status.Equals("completed", StringComparison.CurrentCultureIgnoreCase))
                    {
                        Log.LogInformation($"GitRepository: Import request for {importRequests[index].repository.Name} completed '{latest.status}'....");
                    }
                    else
                    {
                        Log.LogError($"GitRepository: Import request for {importRequests[index].repository.Name} completed '{latest.status}'....");
                    }

                    importRequests.RemoveAt(index);
                    index--;
                    continue;
                }
                else
                    System.Threading.Thread.Sleep(100);
            }
            Log.LogInformation("{MigratedCount} of {TriedToMigrate} {DefinitionType}(s) got migrated..", migratedDefinitions.Count, reposToBeImported.Count(), typeof(GitRepository).Name);
            return migratedDefinitions;
        }

        private async Task<GitRepository> CreateTargetRepository(GitRepository repo, HttpClient client, string apiVersion)
        {
            var body = new
            {
                name = repo.Name
            };
            var content = JsonContent.Create(body);
            var result = await client.PostAsync("", content);

            var responseContent = await result.Content.ReadAsStringAsync();
            var targetObject = JsonConvert.DeserializeObject<GitRepository>(responseContent);
            if (result.IsSuccessStatusCode)
            {
                return targetObject;
            }
            Log.LogError("Error migrating repository {Name}. Please migrate it manually. {ErrorText}", repo.Name, responseContent);

            return null;
        }

        internal async Task<string> CreateGitImportServiceConnection(HttpClient httpClient, string PAT, string uri)
        {
            string svcName = System.Guid.NewGuid().ToString();

            var body = new
            {
                authorization = new
                {
                    parameters = new
                    {
                        password = PAT,
                        username = ""
                    },
                    scheme = "UsernamePassword"
                },
                name = svcName,
                type = "git",
                url = uri
            };

            var content = JsonContent.Create(body);

            var httpResponse = await httpClient.PostAsync("", content);

            if (httpResponse != null && httpResponse.IsSuccessStatusCode)
            {
                var svcConnections = await httpResponse.Content.ReadAsAsync<ServiceConnection>();

                if (svcConnections != null)
                {
                    return svcConnections.Id;
                }
            }

            Log.LogCritical("GitRepoImport: Could not create new service connection: {Content}", httpResponse.Content.ReadAsStringAsync());

            return null;
        }
    }
}