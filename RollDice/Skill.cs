using Alexa.NET;
using Alexa.NET.LocaleSpeech;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RollDice.Extensions;
using Microsoft.Graph;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace RollDice
{
    public static class Skill
    {
        [FunctionName("RollDice")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
        {
            var json = await req.ReadAsStringAsync();
            var skillRequest = JsonConvert.DeserializeObject<SkillRequest>(json);

            // Verifies that the request is indeed coming from Alexa.
            var isValid = await skillRequest.ValidateRequestAsync(req, log);
            if (!isValid)
            {
                return new BadRequestResult();
            }

            // Setup language resources.
            var store = SetupLanguageResources();
            var locale = skillRequest.CreateLocale(store);

            var request = skillRequest.Request;
            SkillResponse response = null;

            try
            {
                if (request is LaunchRequest launchRequest)
                {
                    log.LogInformation("Session started");

                    var givenName = "Sconosciuto";
                    var accessToken = skillRequest.Session?.User?.AccessToken;
                    if (!string.IsNullOrWhiteSpace(accessToken))
                    {
                        var client = GetAuthenticatedClientForUser(accessToken);
                        var me = await client.Me.Request().GetAsync();
                        givenName = me.GivenName;
                    }

                    var welcomeMessage = await locale.Get(LanguageKeys.Welcome, new string[] { givenName });
                    var welcomeRepromptMessage = await locale.Get(LanguageKeys.WelcomeReprompt, null);
                    response = ResponseBuilder.Ask(welcomeMessage, RepromptBuilder.Create(welcomeRepromptMessage));
                }
                else if (request is IntentRequest intentRequest)
                {
                    // Checks whether to handle system messages defined by Amazon.
                    var systemIntentResponse = await HandleSystemIntentsAsync(intentRequest, locale);
                    if (systemIntentResponse.IsHandled)
                    {
                        response = systemIntentResponse.Response;
                    }
                    else
                    {
                        if (intentRequest.Intent.Name == "rolldice")
                        {
                            var faces = Convert.ToInt32(intentRequest.Intent.Slots["faces"].Value);

                            var random = new Random();
                            var number = random.Next(1, faces);

                            var message = await locale.Get(LanguageKeys.Response, new string[] { faces.ToString(), number.ToString() });
                            response = ResponseBuilder.Tell(message);
                        }

                        // Note: The ResponseBuilder.Tell method automatically sets the
                        // Response.ShouldEndSession property to true, so the session will be
                        // automatically closed at the end of the response.
                    }
                }
                else if (request is SessionEndedRequest sessionEndedRequest)
                {
                    log.LogInformation("Session ended");
                    response = ResponseBuilder.Empty();
                }
            }
            catch
            {
                var message = await locale.Get(LanguageKeys.Error, null);
                response = ResponseBuilder.Tell(message);
                response.Response.ShouldEndSession = false;
            }

            return new OkObjectResult(response);
        }

        private static async Task<(bool IsHandled, SkillResponse Response)> HandleSystemIntentsAsync(IntentRequest request, ILocaleSpeech locale)
        {
            SkillResponse response = null;

            if (request.Intent.Name == IntentNames.Cancel)
            {
                var message = await locale.Get(LanguageKeys.Cancel, null);
                response = ResponseBuilder.Tell(message);
            }
            else if (request.Intent.Name == IntentNames.Help)
            {
                var message = await locale.Get(LanguageKeys.Help, null);
                response = ResponseBuilder.Ask(message, RepromptBuilder.Create(message));
            }
            else if (request.Intent.Name == IntentNames.Stop)
            {
                var message = await locale.Get(LanguageKeys.Stop, null);
                response = ResponseBuilder.Tell(message);
            }

            return (response != null, response);
        }

        private static DictionaryLocaleSpeechStore SetupLanguageResources()
        {
            // Creates the locale speech store for each supported languages.
            var store = new DictionaryLocaleSpeechStore();

            store.AddLanguage("en", new Dictionary<string, object>
            {
                [LanguageKeys.Welcome] = "Hello {0}, welcome to roll the solid!",
                [LanguageKeys.WelcomeReprompt] = "You can ask help if you need instructions on how to interact with the skill",
                [LanguageKeys.Response] = "I rolled a {0} faces dice: {1}",
                [LanguageKeys.Cancel] = "Canceling...",
                [LanguageKeys.Help] = "You can ask to me to roll a dice with a given number of faces",
                [LanguageKeys.Stop] = "Bye bye!",
                [LanguageKeys.Error] = "I'm sorry, there was an unexpected error. Please, try again later."
            });

            store.AddLanguage("it", new Dictionary<string, object>
            {
                [LanguageKeys.Welcome] = "Ciao {0}, benvenuto in tira il solido!",
                [LanguageKeys.WelcomeReprompt] = "Se vuoi informazioni sulle mie funzionalità, prova a chiedermi aiuto",
                [LanguageKeys.Response] = "Ho lanciato un dado con {0} facce: {1}",
                [LanguageKeys.Cancel] = "Sto annullando...",
                [LanguageKeys.Help] = "Mi puoi chiedere di tirare un dado con un numero qualsiasi di facce",
                [LanguageKeys.Stop] = "A presto!",
                [LanguageKeys.Error] = "Mi dispiace, si è verificato un errore imprevisto. Per favore, riprova di nuovo in seguito."
            });

            return store;
        }

        private static GraphServiceClient GetAuthenticatedClientForUser(string token)
        {
            // Create Microsoft Graph client.
            try
            {
                var graphClient = new GraphServiceClient(
                    "https://graph.microsoft.com/v1.0",
                    new DelegateAuthenticationProvider(
                        (requestMessage) =>
                        {
                            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
                            return Task.CompletedTask;
                        }));

                return graphClient;

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not create a graph client: " + ex.Message);
            }

            return null;
        }
    }
}
