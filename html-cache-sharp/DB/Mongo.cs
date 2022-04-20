using HtmlCache.Config;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace HtmlCache.DB
{
    internal class Mongo : IDB
    {
        internal MongoClient dbClient;
        internal IMongoDatabase db;
        internal IMongoCollection<BsonDocument> collection;

        const int PORT = 27017;
        const string COLLECTION_NAME = "renders";

        public Mongo()
        {
            var verbose = AppConfig.Instance.Verbose;

            var dbConfig = AppConfig.Instance.Db;

            string collectionName = !string.IsNullOrEmpty(dbConfig.Collection) ? dbConfig.Collection : COLLECTION_NAME;
            int dbPort = (int)(dbConfig.Port != null ? dbConfig.Port : PORT);

            string authPart = !string.IsNullOrEmpty(dbConfig.User) && !string.IsNullOrEmpty(dbConfig.Passwd) ? $"{dbConfig.User}:{dbConfig.Passwd}@" : "";
            string connStr = $"mongodb://{authPart}{dbConfig.Host}:{dbPort}/{dbConfig.Db}{(!string.IsNullOrEmpty(authPart) ? "?authSource=admin" : "")}";

            string maskedAuthPart = !string.IsNullOrEmpty(dbConfig.User) && !string.IsNullOrEmpty(dbConfig.Passwd) ? $"{dbConfig.User}:{{PWD: {dbConfig.Passwd.Length} symbols}}@" : "";
            string maskedConnStr = $"mongodb://{maskedAuthPart}{dbConfig.Host}:{dbPort}/{dbConfig.Db}{(!string.IsNullOrEmpty(maskedAuthPart) ? "?authSource=admin" : "")}";

            dbClient = new(connStr);

            db = dbClient.GetDatabase(dbConfig.Db);

            var filter = new BsonDocument("name", collectionName);
            var options = new ListCollectionNamesOptions { Filter = filter };

            if (!db.ListCollectionNames(options).Any())
            {
                db.CreateCollection(collectionName);
            }

            collection = db.GetCollection<BsonDocument>(collectionName);

            Console.WriteLine($">> Connect to MongoDB successfully initiated [{maskedConnStr}]");

            if (verbose)
            {
                Console.WriteLine();

                var command = new BsonDocument { { "dbstats", 1 } };
                var result = db.RunCommand<BsonDocument>(command);
                Console.WriteLine("DB stats:\n" + result.ToJson(new() { Indent = true }));

                Console.WriteLine();
            }
        }

        public RenderModel? FindByHash(string hash)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("hash", hash);

            BsonDocument? doc = collection.Find(filter).FirstOrDefault();

            return doc == null ? null : BsonSerializer.Deserialize<RenderModel>(doc);
        }

        public bool Save(RenderModel model)
        {
            var filter = new BsonDocument();
            var update = new BsonDocument("$set", model.ToBsonDocument());
            var options = new UpdateOptions { IsUpsert = true };

            var result = collection.UpdateOne(filter, update, options);

            return result.IsAcknowledged;
        }
    }
}
