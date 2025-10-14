namespace dotnetProject.Models
{
    public class BetInfo
    {
        public string ConnectionId { get; set; }
        // e.g. "Single", "Red", "Even", "Dozen1", etc.
        public string BetType { get; set; }
        // for single-number bets, value = "17"; for dozens, value = "1" (1st dozen), etc.
        public string BetValue { get; set; }
        public decimal Amount { get; set; }
    }
}
