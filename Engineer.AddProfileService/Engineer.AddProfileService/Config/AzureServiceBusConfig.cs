namespace Engineer.AddProfileService.Config
{
    public class AzureServiceBusConfig
    {
        public string AzureServiceBusConnectionString { get; set; }
        public string TopicNameToPublish { get; set; }
    }
}