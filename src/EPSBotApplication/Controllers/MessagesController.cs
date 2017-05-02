using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using EpsBotApplication.Api;
using System;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using System.Linq;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Location.Client;
using System.Web.Http.Controllers;
using EPSBotApplication.Dialogs;
using Microsoft.VisualStudio.Services.Identity.Client;
using Microsoft.VisualStudio.Services.Identity;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace EPSBotApplication
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        private const string AssignedToWorkItemsQuery = "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = '{0}' and [System.State] = 'Active'";

        private const string DEFAULT_GREETING = "Hi. This is your personal VSTS Assistant. I am here to help you with your 'work'.";

        private const string PROVIDE_ALIAS_REQUEST_MSG = "Please provide your alias without the domain name.";

        //tjLip2f8bo0Pz4bnswyGxmi
        /// <summary>
        /// Hardcoding VSO Repo to save time in querying all the Repos and filtering by "VSO" name 
        /// TODO: better way to get repo id (there are 1320+ repos now to filter by name)
        /// </summary>
        private readonly Guid VSO_REPO_ID = new Guid("{fb240610-b309-4925-8502-65ff76312c40}");

        private static Guid cachedUserId = Guid.Empty;

        private static ConcurrentDictionary<string, Alias> aliasMap = new ConcurrentDictionary<string, Alias>();
        private static ConcurrentDictionary<string, bool> aliasRequestedMap = new ConcurrentDictionary<string, bool>();

        private static VssClientCredentials clientCredentials;
        private static VssClientCredentials ClientCredentials
        {
            get
            {
                if (clientCredentials == null)
                {
                    var pat = System.Configuration.ConfigurationManager.AppSettings["accesstoken"];
                    var cred = new VssBasicCredential(new NetworkCredential(string.Empty, pat).Password, string.Empty);
                    clientCredentials = new VssClientCredentials(cred);
                    //clientCredentials.Storage = new VssClientCredentialStorage();
                }
                return clientCredentials;
            }
        }

        private static WorkItemTrackingHttpClient workItemClient;
        private static WorkItemTrackingHttpClient WorkItemClient
        {
            get
            {
                if (workItemClient == null)
                {
                    workItemClient = new WorkItemTrackingHttpClient(new Uri("https://mseng.visualstudio.com/defaultCollection"), ClientCredentials);
                }
                return workItemClient;
            }
        }

        private static GitHttpClient gitHttpClient;
        private static GitHttpClient GitHttpClient
        {
            get
            {
                if (gitHttpClient == null)
                {
                    gitHttpClient = new GitHttpClient(new Uri("https://mseng.visualstudio.com/defaultCollection"), ClientCredentials);
                }
                return gitHttpClient;
            }
        }

        private static IdentityHttpClient identityHttpClient;
        private static IdentityHttpClient IdentityClient
        {
            get
            {
                if (identityHttpClient == null)
                {
                    identityHttpClient = new IdentityHttpClient(new Uri(" https://mseng.vssps.visualstudio.com/"), ClientCredentials);
                }
                return identityHttpClient;
            }
        }

        /// <summary>
        /// POST: api/Messages
        /// Receive a message from a user and reply to it
        /// </summary>
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            var intentResponse = await FetchResponseDataAsync(activity);

            ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));
            // Create reply message

            if (activity.Type == ActivityTypes.Message)
            {
                var replyMessage = activity.CreateReply();
                replyMessage.Recipient = activity.From;
                replyMessage.Type = ActivityTypes.Message;
                replyMessage.Text = intentResponse;

                // Post a reply to user
                await connector.Conversations.ReplyToActivityAsync(replyMessage);
            }
            else
            {
                HandleSystemMessage(activity);
            }

            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }

        private async Task<string> FetchResponseDataAsync(Activity activity)
        {
            try
            {
                string userMessage = activity.Text;
                if (!string.IsNullOrEmpty(userMessage))
                {
                    var sessionKey = activity.Conversation.Id;
                    if (aliasRequestedMap.ContainsKey(sessionKey))
                    {
                        string cachedUserAlias = userMessage + "@microsoft.com";

                        // Fetch the identity id 
                        var userIdentities = await IdentityClient.ReadIdentitiesAsync(IdentitySearchFilter.MailAddress, cachedUserAlias);

                        if (userIdentities.Count() > 0)
                        {
                            bool flag;
                            aliasRequestedMap.TryRemove(sessionKey, out flag);
                            cachedUserId = userIdentities.FirstOrDefault().Id;

                            aliasMap[sessionKey] = new Alias() { Id = cachedUserId, MailAddress = cachedUserAlias };
                            return "Thank you for providing the alias. How can I help you?";
                        }
                        else
                        {
                            // user alias is incorrect
                            // Reset
                            cachedUserAlias = null;
                            cachedUserId = Guid.Empty;

                            return GetInvalidAliasSummary(userMessage) + " " + PROVIDE_ALIAS_REQUEST_MSG;
                        }
                    }
                    else if (!aliasMap.ContainsKey(sessionKey))
                    {
                        aliasRequestedMap[sessionKey] = true;
                        return PROVIDE_ALIAS_REQUEST_MSG;
                    }

                    var intentMessage = LuisWebApi.GetIntentType(userMessage);

                    switch (intentMessage.ToLowerInvariant())
                    {
                        case "work":
                            string summary = RequestContext.Principal.Identity.Name + ": " + RequestContext.Principal.Identity.AuthenticationType;
                            try
                            {
                                int x = await FindActiveWorkItemsForCurrentUser(aliasMap[sessionKey].MailAddress);
                                summary = $" You have {x} active work items assigned. \n";

                                int prCount = await FindActivePullRequestsAssignedForCurrentUser(aliasMap[sessionKey].Id);
                                summary += $"You have {prCount} Pull Requests assigned. \n";
                            }
                            catch (Exception)
                            {
                                //summary += ", " + ex;
                            }
                            return summary;
                        case "blacklist":
                            return "Inappropriate message, please try again.";
                        default:
                            return DEFAULT_GREETING;
                    }
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            return null;
        }

        /// <summary>
        /// TODO: Return an set of objects which contains data like - link to the WorkItem 
        /// </summary>
        /// <returns></returns>
        private async Task<int> FindActiveWorkItemsForCurrentUser(string mailAddress)
        {
            var workItemQueryResult = await WorkItemClient.QueryByWiqlAsync(new Wiql
            {
                Query =
                string.Format(AssignedToWorkItemsQuery, mailAddress)
            }, "VSOnline", top: 100).ConfigureAwait(false);
            return workItemQueryResult.WorkItems.Count();
        }

        /// <summary>
        /// TODO: Return an set of objects which contains data like - link to the PR 
        /// </summary>
        /// <returns></returns>
        private async Task<int> FindActivePullRequestsAssignedForCurrentUser(Guid userId)
        {
            // TODO: Disabling this to save time
            //var repos = await GitHttpClient.GetRepositoriesAsync(false, null);
            //var vsoRepoId = repos.Where(r => r.Name.Equals("VSO")).FirstOrDefault().Id;

            var searchCriteria = new GitPullRequestSearchCriteria();
            searchCriteria.RepositoryId = VSO_REPO_ID;
            searchCriteria.ReviewerId = userId;
            var gitClientResult = await GitHttpClient.GetPullRequestsAsync(VSO_REPO_ID, searchCriteria);

            return gitClientResult.Count;
        }

        private string GetInvalidAliasSummary(string alias)
        {
            return $"Invalid Alias {alias} Provided. \n";
        }

        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                // Implement user deletion here
                // If we handle user deletion, return a real message
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }
    }

    public class Alias
    {
        public string MailAddress { get; set; }
        public Guid Id { get; set; }
    }
}