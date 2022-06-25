using Engineer.AddProfileService.Config;
using Engineer.AddProfileService.Model;
using Engineer.AddProfileService.Repository.Contracts;
using MongoDB.Driver;

namespace Engineer.AddProfileService.Repository.Implementation
{
    public class AddProfileContext: IAddProfileContext
    {
        private readonly IMongoDatabase _db;

        public AddProfileContext(MongoDbConfig config)
        {
            var client = new MongoClient(config.ConnectionString);
            _db = client.GetDatabase(config.Database);
        }
        public IMongoCollection<UserProfile> UserProfile => _db.GetCollection<UserProfile>("UserProfile");
    }
}
