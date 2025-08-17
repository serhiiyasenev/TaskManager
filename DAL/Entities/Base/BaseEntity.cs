using Newtonsoft.Json;

namespace DAL.Entities.Base
{
    public abstract class BaseEntity
    {
        [JsonProperty(Order = -1)]
        public int Id { get; set; }
    }
}
