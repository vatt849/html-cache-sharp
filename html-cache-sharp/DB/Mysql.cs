using HtmlCache.Config;
using MySql.Data.MySqlClient;

namespace HtmlCache.DB
{
    internal class Mysql : IDB
    {
        internal MySqlConnection dbClient;
        internal string tableName;

        const int PORT = 3306;
        const string TABLE_NAME = "renders";

        public Mysql()
        {
            var verbose = AppConfig.Instance.Verbose;

            var dbConfig = AppConfig.Instance.Db;

            tableName = !string.IsNullOrEmpty(dbConfig.Table) ? dbConfig.Table : TABLE_NAME;

            int dbPort = (int)(dbConfig.Port != null ? dbConfig.Port : PORT);

            var connStrBuilder = new MySqlConnectionStringBuilder
            {
                Server = dbConfig.Host,
                Port = (uint)dbPort,
                Database = dbConfig.Db
            };

            if (!string.IsNullOrEmpty(dbConfig.User) && !string.IsNullOrEmpty(dbConfig.Passwd))
            {
                connStrBuilder.UserID = dbConfig.User;
                connStrBuilder.Password = dbConfig.Passwd;
            }

            dbClient = new(connStrBuilder.GetConnectionString(true));

            dbClient.Open();

            string maskedConnStr = connStrBuilder.GetConnectionString(false);

            if (!dbClient.Ping())
            {
                throw new Exception($"Unable to connect to MySQL db by connection string `{maskedConnStr}`");
            }

            Console.WriteLine($">> Connect to Mysql successfully initiated [{maskedConnStr}]");

            var commandTableCheck = dbClient.CreateCommand();
            commandTableCheck.CommandText = $"SHOW TABLES LIKE '{tableName}'";

            var tableCheck = commandTableCheck.ExecuteScalar();

            if (tableCheck == null)
            {
                Console.WriteLine($">> Table `{tableName}` not found in DB. Create new one");

                var commandTableCreate = dbClient.CreateCommand();
                commandTableCreate.CommandText = $@"
                    CREATE TABLE `{tableName}` 
                    (
                        `id` INT(11) NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        `hash` VARCHAR(32) NOT NULL,
                        `url` VARCHAR(255) NOT NULL,
                        `renderDate` DATETIME NOT NULL,
                        `lastmodDate` DATETIME NOT NULL,
                        `contentHash` VARCHAR(32) NOT NULL,
                        `content` MEDIUMBLOB NOT NULL
                    ) COLLATE='utf8_general_ci' ENGINE=InnoDB;";

                commandTableCreate.ExecuteNonQuery();
            }
        }

        public RenderModel? FindByHash(string hash)
        {
            var select = dbClient.CreateCommand();
            select.CommandText = $"SELECT * FROM `{tableName}` WHERE `hash` = '{hash}' LIMIT 1";

            using var reader = select.ExecuteReader();

            RenderModel? model = null;

            while (reader.Read())
            {
                if (model == null)
                {
                    model = new RenderModel();
                }

                model.ID = reader.GetString("id");
                model.Hash = reader.GetString("hash");
                model.Url = reader.GetString("url");
                model.RenderDate = reader.GetDateTime("renderDate");
                model.LastmodDate = reader.GetDateTime("lastmodDate");
                model.Url = reader.GetString("url");
                model.ContentHash = reader.GetString("contentHash");
                model.Content = (byte[])reader["content"];
            }

            return model;
        }

        public bool Save(RenderModel model)
        {
            var update = dbClient.CreateCommand();

            var fieldNames = new List<string>();
            var fieldValues = new List<string>();

            if (!string.IsNullOrEmpty(model.ID))
            {
                fieldNames.Add("`id`");
                fieldValues.Add(model.ID);
            }

            fieldNames.Add("`hash`");
            fieldValues.Add($"'{model.Hash}'");

            fieldNames.Add("`url`");
            fieldValues.Add($"'{model.Url}'");

            fieldNames.Add("`renderDate`");
            fieldValues.Add($"'{model.RenderDate:o}'");

            fieldNames.Add("`lastmodDate`");
            fieldValues.Add($"'{model.LastmodDate:o}'");

            fieldNames.Add("`contentHash`");
            fieldValues.Add($"'{model.ContentHash}'");

            fieldNames.Add("`content`");
            fieldValues.Add("?content");

            var contentData = new MySqlParameter();
            contentData.ParameterName = "?content";
            contentData.MySqlDbType = MySqlDbType.Blob;
            contentData.Value = model.Content;

            update.CommandText = $"REPLACE INTO `{tableName}` ({string.Join(',', fieldNames)}) VALUES ({string.Join(',', fieldValues)}) ";

            update.Parameters.Add(contentData);

            return update.ExecuteNonQuery() > 0;
        }
    }
}
