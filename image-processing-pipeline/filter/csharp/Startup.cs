// Copyright 2020 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Threading.Tasks;
using Common;
using Google.Cloud.Vision.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Filter
{
    public class Startup
    {
        private const string PubSubTopicId = "fileuploaded";

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            logger.LogInformation("Service is starting...");

            app.UseRouting();

            var projectId = Environment.GetEnvironmentVariable("PROJECT_ID");
            logger.LogInformation($"Event Adapter: pubsub with projectId '{projectId}' and topicId '{PubSubTopicId}'");
            var eventAdapter = new PubSubEventAdapter(projectId, PubSubTopicId, logger);

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPost("/", async context =>
                {
                    var cloudEvent = await eventAdapter.ReadEvent(context);

                    dynamic data = JValue.Parse((string)cloudEvent.Data);

                    //"protoPayload" : {"resourceName":"projects/_/buckets/events-atamel-images-input/objects/atamel.jpg}";
                    var tokens = ((string)data.protoPayload.resourceName).Split('/');
                    var bucket = tokens[3];
                    var name = tokens[5];
                    var storageUrl = $"gs://{bucket}/{name}";

                    logger.LogInformation($"Storage url: {storageUrl}");

                    var safe = await DetectSafeSearch(storageUrl);
                    logger.LogInformation($"Is the picture safe? {safe}");

                    if (!safe) {
                        return;
                    }

                    var replyData = JsonConvert.SerializeObject(new {bucket = data.bucket, name = data.name});
                    await eventAdapter.WriteEvent(replyData, context);
                });
            });
        }

        private async Task<bool> DetectSafeSearch(string storageUrl)
        {
            var visionClient = ImageAnnotatorClient.Create();
            var response = await visionClient.DetectSafeSearchAsync(Image.FromUri(storageUrl));
            return response.Adult < Likelihood.Possible
                || response.Medical < Likelihood.Possible
                || response.Racy < Likelihood.Possible
                || response.Spoof < Likelihood.Possible
                || response.Violence < Likelihood.Possible;
        }
    }
}
