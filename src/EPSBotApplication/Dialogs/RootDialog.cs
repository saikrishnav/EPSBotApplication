using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

namespace EPSBotApplication.Dialogs
{
    [Serializable]
    public class RootDialog : IDialog<object>
    {
        public string responseMessage;

        public RootDialog(string message)
        {
            responseMessage = message;
        }

        public Task StartAsync(IDialogContext context)
        {
            context.Wait(MessageReceivedAsync);

            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(IDialogContext context, IAwaitable<object> result)
        {
            var activity = await result as Activity;

            // calculate something for us to return
            int length = (activity.Text ?? string.Empty).Length;

            // return our reply to the user
            await context.PostAsync(responseMessage);

            context.Wait(MessageReceivedAsync);
        }
    }
}