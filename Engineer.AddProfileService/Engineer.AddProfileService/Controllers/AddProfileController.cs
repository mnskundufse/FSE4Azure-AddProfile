using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Engineer.AddProfileService.Business.Contracts;
using Engineer.AddProfileService.Config;
using Engineer.AddProfileService.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Engineer.AddProfileService.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("skill-tracker/api/v{version:apiVersion}/engineer")]
    [Produces("application/json")]
    public class AddProfileController: ControllerBase
    {
        private readonly ILogger<AddProfileController> _logger;
        private readonly AzureServiceBusConfig _azureServiceBusConfig;
        private readonly IAddProfileBusiness _addProfileBC;
        private readonly Cache _cache;

        public AddProfileController(ILogger<AddProfileController> logger, IDistributedCache cacheProvider ,IOptions<AzureServiceBusConfig> azureServiceBusConfig, IAddProfileBusiness addProfileBC)
        {
            _cache = new Cache(cacheProvider);
            _addProfileBC = addProfileBC;
            _logger = logger;
            _azureServiceBusConfig = azureServiceBusConfig.Value;
        }

        /// <summary>
        /// Insert User Profile
        /// </summary>
        /// <param name="userProfile"></param>
        /// <returns></returns>
        [MapToApiVersion("1.0")]
        [Authorize]
        [HttpPost("add-profile")]
        public async Task<IActionResult> AddUserProfile(UserProfile userProfile)
        {
            ApiResponse response = new ApiResponse();
            try
            {
                bool insertOperationFlag = false;
                if (ModelState.IsValid)
                {
                    userProfile.CreatedDate = DateTime.Now;
                    userProfile.UpdatedDate = DateTime.Now;
                    response = await _addProfileBC.AddUserProfileBusiness(userProfile);

                    long result = 0;
                    long.TryParse(Convert.ToString(response.Result ?? 0), out result);
                    if (result > 0)
                    {
                        userProfile.UserId = result;
                        PushToCache(userProfile);
                        PublishTopicToAzureServiceBus(userProfile);
                        insertOperationFlag = true;
                    }
                }
                else
                {
                    response.Status.Message = "Invalid Input";
                    response.Status.Status = "FAIL";
                    response.Status.IsValid = false;
                }

                if (insertOperationFlag)
                {
                    _logger.LogInformation("{date} : AddUserProfile of the AddProfileController executed.", DateTime.UtcNow);
                    return StatusCode(200, response);
                }
                else
                {
                    _logger.LogInformation("{date} : AddUserProfile of the AddProfileController Failed : Message {message} ", DateTime.UtcNow, response.Status.Message);
                    return StatusCode(405, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unknown error occurred on the AddUserProfile of the AddProfileController.");
                throw ex;
            }
        }

        #region Publish Event to Service Bus Topic
        /// <summary>
        /// Publish Add UserProfile event to profileforadminaddtopic (Azure Service Bus Code)
        /// </summary>
        /// <param name="userProfileForAdmin"></param>
        private async void PublishTopicToAzureServiceBus(UserProfile userProfileForAdmin)
        {
            // Create the clients that we'll use for sending and processing messages.
            ServiceBusClient client = new ServiceBusClient(_azureServiceBusConfig.AzureServiceBusConnectionString);
            ServiceBusSender sender = client.CreateSender(_azureServiceBusConfig.TopicNameToPublish);

            // create a batch 
            using ServiceBusMessageBatch messageBatch = await sender.CreateMessageBatchAsync();

            // try adding a message to the batch
            var encryptedMessageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(userProfileForAdmin));
            if (!messageBatch.TryAddMessage(new ServiceBusMessage(encryptedMessageBody)))
            {
                // if it is too large for the batch
                _logger.LogInformation("{date} : PublishTopicToAzureServiceBus of the AddProfileController Failed. Message: The message is too large to fit in the batch. ", DateTime.UtcNow);
                throw new Exception($"The message {encryptedMessageBody} is too large to fit in the batch.");
            }

            try
            {
                // Use the producer client to send the batch of messages to the Service Bus topic
                await sender.SendMessagesAsync(messageBatch);
                _logger.LogInformation("{date} : PublishTopicToAzureServiceBus of the AddProfileController completed. Message: Message has been published to the topic.", DateTime.UtcNow);
                Console.WriteLine($"Messages has been published to the topic.");
            }
            finally
            {
                // Calling DisposeAsync on client types is required to ensure that network
                // resources and other unmanaged objects are properly cleaned up.
                await sender.DisposeAsync();
                await client.DisposeAsync();
            }
        }
        #endregion

        #region Queing to Existing Cache
        /// <summary>
        /// Queue the newly added profile with ex if "Key" exist in the cache
        /// </summary>
        /// <param name="userProfile"></param>
        private async void PushToCache(UserProfile userProfile)
        {
            List<UserProfileForCache> userProfileList = await _cache.Get<List<UserProfileForCache>>("UserProfiles");
            if(userProfileList != null)
            {
                List<SkillDetails> techSkillDetails = null, nonTechSkillDetails = null;
                ConvertExpertiseToListOfExpertise(ref techSkillDetails, ref nonTechSkillDetails, userProfile);


                UserProfileForCache userProfileForCache = new UserProfileForCache
                {
                    UserId = userProfile.UserId,
                    Name = userProfile.Name,
                    AssociateId = userProfile.AssociateId,
                    Mobile = userProfile.Mobile,
                    Email = userProfile.Email,
                    TechnicalSkillDetails = techSkillDetails.ToList(),
                    NonTechnicalSkillDetails = nonTechSkillDetails.ToList(),
                    CreatedDate = userProfile.CreatedDate,
                    UpdatedDate = userProfile.UpdatedDate
                };

                userProfileList.Add(userProfileForCache);
                await _cache.Clear("UserProfiles");
                await _cache.Set<List<UserProfileForCache>>("UserProfiles", userProfileList, new DistributedCacheEntryOptions());
                _logger.LogInformation("{date} : PushToCache of the AddProfileController executed.", DateTime.UtcNow);
            }
            else
            {
                _logger.LogInformation("{date} : Added profile is not added in cache as the key is not present.", DateTime.UtcNow);
            }
        }

        private void ConvertExpertiseToListOfExpertise(ref List<SkillDetails> techSkillDetails, ref List<SkillDetails> nonTechSkillDetails, UserProfile userProfile)
        {
            techSkillDetails = new List<SkillDetails>
            {
                new SkillDetails { SkillName = "HTML-CSS-JAVASCRIPT", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.HTMLCSSJavaScriptExpertiseLevel) },
                new SkillDetails { SkillName = "ANGULAR", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.AngularExpertiseLevel) },
                new SkillDetails { SkillName = "REACT", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.ReactExpertiseLevel) },
                new SkillDetails { SkillName = "ASP.NET CORE", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.AspNetCoreExpertiseLevel) },
                new SkillDetails { SkillName = "RESTFUL", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.RestfulExpertiseLevel) },
                new SkillDetails { SkillName = "ENTITY FRAMEWORK", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.EntityFrameworkExpertiseLevel) },
                new SkillDetails { SkillName = "GIT", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.GitExpertiseLevel) },
                new SkillDetails { SkillName = "DOCKER", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.DockerExpertiseLevel) },
                new SkillDetails { SkillName = "JENKINS", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.JenkinsExpertiseLevel) },
                new SkillDetails { SkillName = "AZURE", SkillValue = Convert.ToInt32(userProfile.TechnicalSkillExpertiseLevel.AzureExpertiseLevel) }
            };

            nonTechSkillDetails = new List<SkillDetails>()
            {
                new SkillDetails { SkillName = "SPOKEN", SkillValue = Convert.ToInt32(userProfile.NonTechnicalSkillExpertiseLevel.SpokenExpertiseLevel) },
                new SkillDetails { SkillName = "COMMUNICATION", SkillValue = Convert.ToInt32(userProfile.NonTechnicalSkillExpertiseLevel.CommunicationExpertiseLevel) },
                new SkillDetails { SkillName = "APTITUDE", SkillValue = Convert.ToInt32(userProfile.NonTechnicalSkillExpertiseLevel.AptitudeExpertiseLevel) }
            };
        }
        #endregion
    }
}