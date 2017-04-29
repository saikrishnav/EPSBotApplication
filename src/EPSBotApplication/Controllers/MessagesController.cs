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

namespace EPSBotApplication
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        private const string AssignedToWorkItemsQuery = "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @me and [System.State] = 'Active'";

        private const string DEFAULT_GREETING = "Hi. This is your personal VSTS Assistant. I am here to help you with your 'work'.";
        //tjLip2f8bo0Pz4bnswyGxmi
        /// <summary>
        /// Hardcoding VSO Repo to save time in querying all the Repos and filtering by "VSO" name 
        /// TODO: better way to get repo id (there are 1320+ repos now to filter by name)
        /// </summary>
        private readonly Guid VSO_REPO_ID = new Guid("{fb240610-b309-4925-8502-65ff76312c40}");
        
        private static VssClientCredentials clientCredentials;
        private static VssClientCredentials ClientCredentials
        {
            get
            {
                if (clientCredentials == null)
                {
                    clientCredentials = new VssClientCredentials();
                    clientCredentials.Storage = new VssClientCredentialStorage();
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

        private static LocationHttpClient locationHttpClient;
        private static LocationHttpClient LocationHttpClient
        {
            get
            {
                if (locationHttpClient == null)
                {
                    locationHttpClient = new LocationHttpClient(new Uri("https://mseng.visualstudio.com/defaultCollection"), ClientCredentials);
                }
                return locationHttpClient;
            }
        }

        /// <summary>
        /// POST: api/Messages
        /// Receive a message from a user and reply to it
        /// </summary>
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {           
            var intentMessage = LuisWebApi.GetIntentType(activity.Text);
            var intentResponse = await FetchResponseDataAsync(intentMessage).ConfigureAwait(false);
            //Populate response message with intent response data

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

        private async Task<string> FetchResponseDataAsync(string intentMessage)
        {
            if (!string.IsNullOrEmpty(intentMessage))
            {
                switch (intentMessage.ToLowerInvariant())
                {
                    case "work":
                        string summary = RequestContext.Principal.Identity.Name + ": " + RequestContext.Principal.Identity.AuthenticationType;
                        try
                        {
                            int x = await FindActiveWorkItemsForCurrentUser();
                            summary = $" You have {x} active work items assigned. \n";

                            int y = await FindActivePullRequestsAssignedForCurrentUser();
                            summary += $"You have {y} Pull Requests assigned. \n";
                        }
                        catch(Exception ex)
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

            return null;
        }

        /// <summary>
        /// TODO: Return an set of objects which contains data like - link to the WorkItem 
        /// </summary>
        /// <returns></returns>
        private async Task<int> FindActiveWorkItemsForCurrentUser()
        {
            var workItemQueryResult = await WorkItemClient.QueryByWiqlAsync(new Wiql { Query = AssignedToWorkItemsQuery }, "VSOnline", top: 100).ConfigureAwait(false);
            return workItemQueryResult.WorkItems.Count();
        }

        /// <summary>
        /// TODO: Return an set of objects which contains data like - link to the PR 
        /// </summary>
        /// <returns></returns>
        private async Task<int> FindActivePullRequestsAssignedForCurrentUser()
        {
            // TODO: Disabling this to save time
            //var repos = await GitHttpClient.GetRepositoriesAsync(false, null);
            //var vsoRepoId = repos.Where(r => r.Name.Equals("VSO")).FirstOrDefault().Id;

            var x = await LocationHttpClient.GetConnectionDataAsync(Microsoft.VisualStudio.Services.WebApi.ConnectOptions.None, 0);
            var id = x.AuthenticatedUser.Id;

            var searchCriteria = new GitPullRequestSearchCriteria();
            searchCriteria.RepositoryId = VSO_REPO_ID;
            searchCriteria.ReviewerId = id;
            var gitClientResult = await GitHttpClient.GetPullRequestsAsync(VSO_REPO_ID, searchCriteria);

            return gitClientResult.Count;
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
}