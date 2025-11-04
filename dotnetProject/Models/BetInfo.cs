namespace dotnetProject.Models
{
    public class BetInfo
    {
        // We now use PlayerName to track bets, as ConnectionId can change.
        public string PlayerName { get; set; }

        // e.g. "Single", "Red", "Even", "Dozen1", etc.
        public string BetType { get; set; }

        // for single-number bets, value = "17"; for dozens, value = "1" (1st dozen), etc.
        public string BetValue { get; set; }

        public decimal Amount { get; set; }
    }
}