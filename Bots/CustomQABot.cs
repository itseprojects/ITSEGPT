// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Builder.AI.QnA.Models;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.BotBuilderSamples.Bots
{
    public class CustomQABot : ActivityHandler
    {
        private readonly ILogger<CustomQABot> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _endpointKey;
        private readonly string _hostname;
        private readonly string _knowledgeBaseId;
        private readonly string _defaultWelcome = "Hello and Welcome";
        private readonly bool _enablePreciseAnswer;
        private readonly bool _displayPreciseAnswerOnly;
        private readonly string _openai;
        private readonly string _openKey;
        public CustomQABot(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<CustomQABot> logger)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            const string missingConfigError = "{0} is missing or empty in configuration.";

            _hostname = configuration["LanguageEndpointHostName"];
            if (string.IsNullOrEmpty(_hostname))
            {
                throw new ArgumentException(string.Format(missingConfigError, "LanguageEndpointHostName"));
            }

            // added by HG
            _openai = configuration["OpenIA:CompletionEndpoint"];
            _openKey = configuration["OpenIA:APIKey"];

            _endpointKey = configuration["LanguageEndpointKey"];
            if (string.IsNullOrEmpty(_endpointKey))
            {
                throw new ArgumentException(string.Format(missingConfigError, "LanguageEndpointKey"));
            }

            _knowledgeBaseId = configuration["ProjectName"];
            if (string.IsNullOrEmpty(_knowledgeBaseId))
            {
                throw new ArgumentException(string.Format(missingConfigError, "ProjectName"));
            }

            var welcomeMsg = configuration["DefaultWelcomeMessage"];
            if (!string.IsNullOrWhiteSpace(welcomeMsg))
            {
                _defaultWelcome = welcomeMsg;
            }

            _enablePreciseAnswer = bool.Parse(configuration["EnablePreciseAnswer"]);
            _displayPreciseAnswerOnly = bool.Parse(configuration["DisplayPreciseAnswerOnly"]);
        }

        protected override async Task<object> OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var customQuestionAnswering = CreateCustomQuestionAnsweringClient(httpClient);

            // Call Custom Question Answering service to get a response.
            _logger.LogInformation("Calling Custom Question Answering");
            var options = new QnAMakerOptions { Top = 1, EnablePreciseAnswer = _enablePreciseAnswer };
            var response = await customQuestionAnswering.GetAnswersAsync(turnContext, options);

            if (response.Length > 0)
            {
                var activities = new List<IActivity>();

                // Create answer activity.
                var answerText = response[0].Answer;
                var answer = MessageFactory.Text(answerText, answerText);

                // Answer span text has precise answer.
                var preciseAnswerText = response[0].AnswerSpan?.Text;
                if(response[0].Score<0.7 )//|| string.IsNullOrEmpty(preciseAnswerText))
                {
                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri(_openai);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openKey}");

                    HttpRequestMessage reques = new HttpRequestMessage(HttpMethod.Post, client.BaseAddress);
                    var body = $"{{\"model\":\"text-davinci-003\",\"prompt\":\"{turnContext.Activity.Text}\",\"temperature\":1,\"max_tokens\":2048}}";
                    var content = new StringContent(body,System.Text.Encoding.UTF8,"application/json");
                    reques.Content = content;

                    try
                    {
                        var airesponse = await client.SendAsync(reques).ConfigureAwait(false);
                        airesponse.EnsureSuccessStatusCode();
                        string responseString = await airesponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        JObject jsonObject = JObject.Parse(responseString);

                        if (jsonObject != null)
                        {
                            if (jsonObject["choices"][0]!=null)
                            {
                                answer = MessageFactory.Text((string)jsonObject["choices"][0]["text"],(string)jsonObject["choices"][0]["text"]);
                                // Add answer to the reply when it is configured.
                                activities.Add(answer);
                            }
                        }
                        

                    }
                    catch{
                        activities.Add(answer);
                    }
                }

                else {
                    activities.Add(answer);
                    // Create precise answer activity.
                    // var preciseAnswer = MessageFactory.Text(preciseAnswerText, preciseAnswerText);
                    // activities.Add(preciseAnswer);

                    // if (!_displayPreciseAnswerOnly)
                    // {
                    //     // Add answer to the reply when it is configured.
                    //     activities.Add(answer);
                    // }
                    // else{}
                }

                return await turnContext.SendActivitiesAsync(activities.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return await turnContext.SendActivityAsync(MessageFactory.Text("No answers were found.", "No answers were found."), cancellationToken);
            }
        }

        private CustomQuestionAnswering CreateCustomQuestionAnsweringClient(HttpClient httpClient)
        {
            // Create a new Custom Question Answering instance initialized with QnAMakerEndpoint.
            return new CustomQuestionAnswering(new QnAMakerEndpoint
            {
                KnowledgeBaseId = _knowledgeBaseId,
                EndpointKey = _endpointKey,
                Host = _hostname,
                QnAServiceType = ServiceType.Language
            },
           null,
           httpClient);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(_defaultWelcome, _defaultWelcome), cancellationToken);
                }
            }
        }
    }
}
