using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBaseLayer.Models
{
    /// <summary>
    /// History of balance modifications (replenishments, manual adjustments, etc.).
    /// </summary>
    public class BalanceHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// ID of the user whose balance was changed.
        /// </summary>
        [Required]
        public long UserId { get; set; }

        /// <summary>
        /// The amount of the change (delta).
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// ID of the user who performed the modification (null if system/payment).
        /// </summary>
        public long? ModifiedById { get; set; }

        /// <summary>
        /// When the modification occurred.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Source of the modification (e.g., "Admin", "PaymentSystem").
        /// </summary>
        [MaxLength(64)]
        public string? Source { get; set; }
    }
}
