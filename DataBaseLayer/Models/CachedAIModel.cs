using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(Id))]
    public class CachedAIModel
    {
        public long Id { get; set; }

        [MaxLength(128)]
        public required string ModelId { get; set; }

        [MaxLength(128)]
        public required string ProviderName { get; set; }

        public bool IsAvailable { get; set; }

        public required DateTime LastChecked { get; set; }

        [MaxLength(256)]
        public string? FriendlyName { get; set; }
    }
}
