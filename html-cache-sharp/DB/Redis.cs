using HtmlCache.Config;
using StackExchange.Redis;

namespace HtmlCache.DB
{
    internal class Redis : IDB
    {
        internal ConnectionMultiplexer dbClient;
        internal IDatabase db;

        public Redis()
        {
            var verbose = AppConfig.Instance.Verbose;

            var dbConfig = AppConfig.Instance.Db;

            string connStr = $"{dbConfig.Host}:{dbConfig.Port}";

            dbClient = ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { connStr }
            });

            db = dbClient.GetDatabase(Convert.ToInt32(dbConfig.Db));

            var pong = db.Ping();

            Console.WriteLine($">> Connect to Redis successfully initiated [{connStr}:{dbConfig.Db} - ping: {pong.Milliseconds}ms]");
        }

        public RenderModel? FindByHash(string hash)
        {
            RenderModel? result = null;

            var hashes = db.HashGetAll(hash);

            if (hashes == null || !hashes.Any())
            {
                return result;
            }

            result = new RenderModel()
            {
                ID = hash,
            };

            foreach (var hashEntry in hashes)
            {
                switch (hashEntry.Name)
                {
                    case "hash":
                        result.Hash = hashEntry.Value;
                        break;
                    case "url":
                        result.Url = hashEntry.Value;
                        break;
                    case "renderDate":
                        result.RenderDate = DateTime.Parse(hashEntry.Value);
                        break;
                    case "lastmodDate":
                        result.LastmodDate = DateTime.Parse(hashEntry.Value);
                        break;
                    case "contentHash":
                        result.ContentHash = hashEntry.Value;
                        break;
                    case "content":
                        result.Content = hashEntry.Value;
                        break;
                }
            }

            return result;
        }

        public bool Save(RenderModel model)
        {
            var transaction = db.CreateTransaction();

            //transaction.AddCondition(Condition.HashNotExists(model.Hash, "hash"));

            transaction.HashSetAsync(model.Hash, "hash", model.Hash);
            transaction.HashSetAsync(model.Hash, "url", model.Url);
            transaction.HashSetAsync(model.Hash, "renderDate", model.RenderDate.ToString("o"));
            transaction.HashSetAsync(model.Hash, "lastmodDate", model.LastmodDate.ToString("o"));
            transaction.HashSetAsync(model.Hash, "contentHash", model.ContentHash);
            transaction.HashSetAsync(model.Hash, "content", model.Content);

            return transaction.Execute();
        }
    }
}
