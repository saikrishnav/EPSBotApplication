using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace EpsBotApplication.Api
{
    public class LuisWebApi
    {
        public static string GetIntentType(string query)
        {
            try
            {
                var jsonResponse = GetJsonResponseFromLuis(query);
                dynamic parsedJsonResponse = JObject.Parse(jsonResponse);

                if (parsedJsonResponse.entities.Count > 0)
                {
                    return parsedJsonResponse.entities[0].type;
                }
            }
            catch (Exception)
            {
                //  TO-DO Log exception
            }

            return string.Empty;
        }

        // Get Intent type from LUIS
        private static string GetJsonResponseFromLuis(string query)
        {
            var client = new HttpClient();
            //var queryString = HttpUtility.ParseQueryString(string.Empty);

            // Request headers
            //client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", "{subscription key}");

            var uri = "https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/68142023-a3c6-42b0-82b8-9d76d3f8761c?subscription-key=4fd6f675b8a444c598c486c851da6d48&verbose=true&timezoneOffset=0&q=" + query;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            try
            {
                WebResponse response = request.GetResponse();
                using (Stream responseStream = response.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                WebResponse errorResponse = ex.Response;
                using (Stream responseStream = errorResponse.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(responseStream, Encoding.GetEncoding("utf-8"));
                    String errorText = reader.ReadToEnd();
                    // log errorText
                }
                throw;
            }
        }
    }
}

