using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.IdGenerators;

namespace HtmlCache.DB
{

    [BsonDiscriminator("Render"), BsonIgnoreExtraElements]
    internal class RenderModel
    {
        [BsonId(IdGenerator = typeof(StringObjectIdGenerator)), BsonRepresentation(BsonType.ObjectId)]
        public string ID { get; set; }

        [BsonElement("hash"), BsonRepresentation(BsonType.String)]
        public string Hash { get; set; }

        [BsonElement("url"), BsonRepresentation(BsonType.String)]
        public string Url { get; set; }

        [BsonElement("renderDate"), BsonRepresentation(BsonType.DateTime)]
        public DateTime RenderDate { get; set; }

        [BsonElement("lastmodDate"), BsonRepresentation(BsonType.DateTime)]
        public DateTime LastmodDate { get; set; }

        [BsonElement("contentHash"), BsonRepresentation(BsonType.String)]
        public string ContentHash { get; set; }

        [BsonElement("content"), BsonRepresentation(BsonType.Binary)]
        public byte[] Content { get; set; }
    }
}
