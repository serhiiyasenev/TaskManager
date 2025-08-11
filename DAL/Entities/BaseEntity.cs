using Newtonsoft.Json;

namespace DAL.Entities
{
    public abstract class BaseEntity
    {
        [JsonProperty(Order = -1)]
        public int Id { get; set; }
    }
}
