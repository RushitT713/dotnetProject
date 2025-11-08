namespace dotnetProject.Models
{
    public class BetInfo
    {
        // Player identification
        public string PlayerName { get; set; }
        public string PlayerId { get; set; } // Cookie-based player ID

        // Bet details
        public string BetType { get; set; } // e.g. "Single", "Red", "Even", "Dozen1", etc.
        public string BetValue { get; set; } // for single-number bets, value = "17"; for dozens, value = "1" (1st dozen), etc.
        public decimal Amount { get; set; }
    }
}