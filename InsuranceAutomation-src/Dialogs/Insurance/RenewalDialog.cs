// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.BotBuilderSamples
{
    /// <summary>
    /// Demonstrates the following concepts:
    /// - Use a subclass of ComponentDialog to implement a multi-turn conversation
    /// - Use a Waterflow dialog to model multi-turn conversation flow
    /// - Use custom prompts to validate user input
    /// - Store conversation and user state.
    /// </summary>
    public class RenewalDialog : ComponentDialog
    {
        // User state for greeting dialog
        private const string GreetingStateProperty = "greetingState";
        private const string policyNumber = "greetingName";
        private const string DOBValue = "greetingCity";

        // Prompts names
        private const string NamePrompt = "namePrompt";
        private const string CityPrompt = "cityPrompt";

        // Minimum length requirements for city and name
        private const int MobileLengthMinValue = 5;
        private const int DOBLengthMinValue = 4;

        // Dialog IDs
        private const string ProfileDialog = "profileDialog";

        public RenewalDialog(IStatePropertyAccessor<RenewalState> userProfileStateAccessor, ILoggerFactory loggerFactory)
            : base(nameof(RenewalDialog))
        {
            UserProfileAccessor = userProfileStateAccessor ?? throw new ArgumentNullException(nameof(userProfileStateAccessor));

            // Add control flow dialogs
            var waterfallSteps = new WaterfallStep[]
            {
                    InitializeStateStepAsync,
                    PromptForNameStepAsync,
                    PromptForCityStepAsync,
                    DisplayGreetingStateStepAsync,
            };
            AddDialog(new WaterfallDialog(ProfileDialog, waterfallSteps));
            AddDialog(new TextPrompt(NamePrompt, ValidateName));
            AddDialog(new TextPrompt(CityPrompt, ValidateCity));
        }

        public IStatePropertyAccessor<RenewalState> UserProfileAccessor { get; }

        private async Task<DialogTurnResult> InitializeStateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var greetingState = await UserProfileAccessor.GetAsync(stepContext.Context, () => null);
            if (greetingState == null)
            {
                var greetingStateOpt = stepContext.Options as RenewalState;
                if (greetingStateOpt != null)
                {
                    await UserProfileAccessor.SetAsync(stepContext.Context, greetingStateOpt);
                }
                else
                {
                    await UserProfileAccessor.SetAsync(stepContext.Context, new RenewalState());
                }
            }

            return await stepContext.NextAsync();
        }

        private async Task<DialogTurnResult> PromptForNameStepAsync(
                                                WaterfallStepContext stepContext,
                                                CancellationToken cancellationToken)
        {
            var greetingState = await UserProfileAccessor.GetAsync(stepContext.Context);

            // if we have everything we need, greet user and return.
            if (greetingState != null && !string.IsNullOrWhiteSpace(greetingState.Mobile) && !string.IsNullOrWhiteSpace(greetingState.BirthYear))
            {
                return await GetPolicyRenewalDate(stepContext);
            }

            if (string.IsNullOrWhiteSpace(greetingState.Mobile))
            {
                // prompt for name, if missing
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = "Whats your 5 digit policy number?",
                    },
                };
                return await stepContext.PromptAsync(NamePrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> PromptForCityStepAsync(
                                                        WaterfallStepContext stepContext,
                                                        CancellationToken cancellationToken)
        {
            // Save name, if prompted.
            var greetingState = await UserProfileAccessor.GetAsync(stepContext.Context);
            var lowerCaseName = stepContext.Result as string;
            if (string.IsNullOrWhiteSpace(greetingState.Mobile) && lowerCaseName != null)
            {
                // Capitalize and set name.
                greetingState.Mobile = char.ToUpper(lowerCaseName[0]) + lowerCaseName.Substring(1);
                await UserProfileAccessor.SetAsync(stepContext.Context, greetingState);
            }

            if (string.IsNullOrWhiteSpace(greetingState.BirthYear))
            {
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = $"What is your birth year?",
                    },
                };
                return await stepContext.PromptAsync(CityPrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> DisplayGreetingStateStepAsync(
                                                    WaterfallStepContext stepContext,
                                                    CancellationToken cancellationToken)
        {
            // Save city, if prompted.
            var greetingState = await UserProfileAccessor.GetAsync(stepContext.Context);

            var lowerCaseCity = stepContext.Result as string;
            if (string.IsNullOrWhiteSpace(greetingState.BirthYear) &&
                !string.IsNullOrWhiteSpace(lowerCaseCity))
            {
                // capitalize and set city
                greetingState.BirthYear = char.ToUpper(lowerCaseCity[0]) + lowerCaseCity.Substring(1);
                await UserProfileAccessor.SetAsync(stepContext.Context, greetingState);
            }

            return await GetPolicyRenewalDate(stepContext);
        }

        /// <summary>
        /// Validator function to verify if the user name meets required constraints.
        /// </summary>
        /// <param name="promptContext">Context for this prompt.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        private async Task<bool> ValidateName(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            // Validate that the user entered a minimum length for their name.
            var value = promptContext.Recognized.Value?.Trim() ?? string.Empty;
            if (value.Length >= MobileLengthMinValue)
            {
                promptContext.Recognized.Value = value;
                return true;
            }
            else
            {
                await promptContext.Context.SendActivityAsync($"Names needs to be at least `{MobileLengthMinValue}` characters long.");
                return false;
            }
        }

        /// <summary>
        /// Validator function to verify if city meets required constraints.
        /// </summary>
        /// <param name="promptContext">Context for this prompt.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        private async Task<bool> ValidateCity(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            // Validate that the user entered a minimum lenght for their name
            var value = promptContext.Recognized.Value?.Trim() ?? string.Empty;
            if (value.Length >= DOBLengthMinValue)
            {
                promptContext.Recognized.Value = value;
                return true;
            }
            else
            {
                await promptContext.Context.SendActivityAsync($"Birth year needs to be at least `{DOBLengthMinValue}` characters long.");
                return false;
            }
        }

        // Helper function to greet user with information in GreetingState.
        private async Task<DialogTurnResult> GetPolicyRenewalDate(WaterfallStepContext stepContext)
        {
            var context = stepContext.Context;
            var greetingState = await UserProfileAccessor.GetAsync(context);

            // Display their profile information and end dialog.
            await context.SendActivityAsync($"Getting policy renewal date for {greetingState.Mobile} - {greetingState.BirthYear} from CRM.. Please wait...");

            var authBodyValues = new Dictionary<string, string>
            {
              { "tenancyName", "sathish-paripoorna" }, { "usernameOrEmailAddress", "sathish@paripoorna.in" }, { "password", "Psss@2018" },
            };

            var authResult = string.Empty;
            var startJobResult = string.Empty;
            var renewalResultValue = string.Empty;
            var jobCreatedID = string.Empty;
            int[] robitIDs = new int[1];
            robitIDs[0] = 74213;

            int timeoutSec = 90;
            string contentType = "application/json";
            var authKeyValue = string.Empty;
            dynamic startJobResultValues = null;
            string polRenewalDate = string.Empty;

            using (var authHttpClient = new HttpClient())
            {
                authHttpClient.BaseAddress = new Uri("https://platform.uipath.com");
                authHttpClient.Timeout = new TimeSpan(0, 0, timeoutSec);
                authHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));
                var authJson = JsonConvert.SerializeObject(authBodyValues);

                using (var stringContent = new StringContent(authJson, Encoding.UTF8, "application/json"))
                {
                    var authResponse = authHttpClient.PostAsync("api/account/authenticate", stringContent).Result;
                    if (authResponse.IsSuccessStatusCode)
                    {
                        authResult = authResponse.Content.ReadAsStringAsync().Result;
                        dynamic authResultValues = JsonConvert.DeserializeObject(authResult);
                        authKeyValue = authResultValues.result;

                        if (authKeyValue != string.Empty)
                        {
                            object startJobArguments = new
                            {
                                in_cust_id = greetingState.Mobile,
                            };

                            object startJobBody = new
                            {
                                ReleaseKey = "30a75006-fd84-42e0-87ad-0ce347018683",
                                RobotIds = robitIDs,
                                JobsCount = 0,
                                Strategy = "Specific",
                                InputArguments = "{in_cust_id:" + greetingState.Mobile + "}",
                            };

                            object startJobWrapper = new
                            {
                                startInfo = startJobBody,
                            };

                            using (var startJobHttpClient = new HttpClient())
                            {
                                startJobHttpClient.BaseAddress = new Uri("https://platform.uipath.com");
                                startJobHttpClient.Timeout = new TimeSpan(0, 0, timeoutSec);
                                startJobHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));
                                startJobHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authKeyValue);

                                var startJobJson = JsonConvert.SerializeObject(startJobWrapper);

                                using (var startJobContent = new StringContent(startJobJson, Encoding.UTF8, "application/json"))
                                {
                                    var startJobResponse = startJobHttpClient.PostAsync("/odata/Jobs/UiPath.Server.Configuration.OData.StartJobs", startJobContent).Result;
                                    Thread.Sleep(20000);
                                    if (startJobResponse.IsSuccessStatusCode)
                                    {
                                        startJobResult = startJobResponse.Content.ReadAsStringAsync().Result;
                                        startJobResultValues = JsonConvert.DeserializeObject(startJobResult);
                                        jobCreatedID = startJobResultValues.value[0].Id;
                                        if (jobCreatedID != null)
                                        {
                                            using (var getJobHttpClient = new HttpClient())
                                            {
                                                polRenewalDate = await GetRenewalJobStatusResult(getJobHttpClient, greetingState.Mobile, authKeyValue).ConfigureAwait(false);
                                                Thread.Sleep(3000);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            await context.SendActivityAsync($"You renewal date for policy no `{polRenewalDate}`");
            return await stepContext.EndDialogAsync();
        }

        private static async Task<string> GetRenewalJobStatusResult(HttpClient getJobHttpClient, string referenceNumber, string authKeyValue)
        {
            int timeoutSec = 90;
            string contentType = "application/json";
            dynamic getJobResultValues = null;
            var getJobResult = string.Empty;
            string polRenewalDate = string.Empty;

            getJobHttpClient.BaseAddress = new Uri("https://platform.uipath.com");
            getJobHttpClient.Timeout = new TimeSpan(0, 0, timeoutSec);
            getJobHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));
            getJobHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authKeyValue);

            object getJobWrapper = new object();
            var getJobResponse = await getJobHttpClient.GetAsync("/odata/QueueItems?$filter=Reference%20eq%20'A" + referenceNumber + "'");
            if (getJobResponse.IsSuccessStatusCode)
            {
                getJobResult = await getJobResponse.Content.ReadAsStringAsync();
                getJobResultValues = JsonConvert.DeserializeObject(getJobResult);
                polRenewalDate = getJobResultValues.value[1].SpecificContent.output_api;
            }
            else
            {
                getJobResult = getJobResponse.Content.ReadAsStringAsync().Result;
            }

            return polRenewalDate;
        }
    }
}
