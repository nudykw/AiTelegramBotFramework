using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(Id))]
    public class CachedTranslation
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [MaxLength(10)]
        public required string LanguageCode { get; set; }

        [MaxLength(128)]
        public required string ResourceKey { get; set; }

        [MaxLength(2048)]
        public required string OriginalText { get; set; }

        [MaxLength(2048)]
        public required string TranslatedText { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
