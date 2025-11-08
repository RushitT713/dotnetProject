using System;
using System.ComponentModel.DataAnnotations;

namespace dotnetProject.Models
{
    public class Player
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string PlayerId { get; set; } // Cookie-based unique identifier

        [MaxLength(100)]
        public string? DisplayName { get; set; } // Optional display name

        [Required]
        public decimal Balance { get; set; } = 5000m; // Starting balance

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastActive { get; set; } = DateTime.UtcNow;

        // Transaction history (optional for tracking)
        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }

    public class Transaction
    {
        [Key]
        public int Id { get; set; }

        public int PlayerId { get; set; }
        public virtual Player Player { get; set; }

        [Required]
        [MaxLength(50)]
        public string GameType { get; set; } // "Blackjack", "Roulette", "Poker"

        public decimal AmountBefore { get; set; }
        public decimal AmountChange { get; set; } // Positive for wins, negative for losses
        public decimal AmountAfter { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}