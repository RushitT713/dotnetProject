using System;
using System.Collections.Generic;
using System.Linq;

namespace dotnetProject.Models
{
    public class BlackjackGame
    {
        private List<string> Deck { get; set; }
        public List<string> PlayerHand { get; set; }
        public List<string> DealerHand { get; set; }
        public int PlayerBalance { get; set; }
        public int CurrentBet { get; set; }
        public bool IsGameOver { get; set; }

        private static readonly Dictionary<string, int> CardValues = new Dictionary<string, int>
        {
            {"2", 2}, {"3", 3}, {"4", 4}, {"5", 5},
            {"6", 6}, {"7", 7}, {"8", 8}, {"9", 9},
            {"10", 10}, {"J", 10}, {"Q", 10}, {"K", 10}, {"A", 11}
        };

        public BlackjackGame()
        {
            PlayerBalance = 5000; // Starting balance
            ResetDeck();
            PlayerHand = new List<string>();
            DealerHand = new List<string>();
            IsGameOver = false;
        }

        public void ResetDeck()
        {
            Deck = new List<string>();
            string[] suits = { "♠", "♥", "♦", "♣" };
            string[] ranks = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };

            foreach (var suit in suits)
            {
                foreach (var rank in ranks)
                {
                    Deck.Add($"{rank}{suit}");
                }
            }

            // Shuffle
            var rng = new Random();
            Deck = Deck.OrderBy(c => rng.Next()).ToList();
        }

        public string DrawCard()
        {
            if (Deck.Count == 0)
            {
                ResetDeck();
            }

            var card = Deck[0];
            Deck.RemoveAt(0);
            return card;
        }

        public void StartNewRound(int betAmount)
        {
            // --- FIX 1: Bet is taken from balance at the START of the round ---
            PlayerBalance -= betAmount;
            CurrentBet = betAmount;
            IsGameOver = false;
            PlayerHand.Clear();
            DealerHand.Clear();
            ResetDeck();

            PlayerHand.Add(DrawCard());
            PlayerHand.Add(DrawCard());
            DealerHand.Add(DrawCard());
            DealerHand.Add(DrawCard());
        }

        public void PlayerHit()
        {
            PlayerHand.Add(DrawCard());
            if (CalculateScore(PlayerHand) > 21)
            {
                IsGameOver = true;
            }
        }

        public void DealerPlay()
        {
            while (CalculateScore(DealerHand) < 17)
            {
                DealerHand.Add(DrawCard());
            }
            IsGameOver = true;
        }

        public int CalculateScore(List<string> hand)
        {
            int score = 0;
            int aceCount = 0;

            foreach (var card in hand)
            {
                // Ensure card is not null or empty
                if (string.IsNullOrEmpty(card)) continue;

                string rank = card.Substring(0, card.Length - 1);
                if (CardValues.ContainsKey(rank))
                {
                    score += CardValues[rank];
                    if (rank == "A") aceCount++;
                }
            }

            // Handle Aces as 1 if needed
            while (score > 21 && aceCount > 0)
            {
                score -= 10;
                aceCount--;
            }

            return score;
        }

        public string GetResult()
        {
            // --- FIX 2: Re-written payout logic ---
            // The bet was already subtracted. We now only add winnings.
            // Win = bet * 2 (original bet back + winnings)
            // Push = bet * 1 (original bet back)
            // Loss = 0 (bet is already gone)

            int playerScore = CalculateScore(PlayerHand);
            int dealerScore = CalculateScore(DealerHand);

            if (playerScore > 21)
            {
                // Player busts. Bet is already lost.
                return "Bust! You lose.";
            }
            else if (dealerScore > 21)
            {
                // Dealer busts. Player wins.
                PlayerBalance += CurrentBet * 2; // Return bet + winnings
                return "Dealer busts! You win!";
            }
            else if (playerScore > dealerScore)
            {
                // Player wins.
                PlayerBalance += CurrentBet * 2; // Return bet + winnings
                return "You win!";
            }
            else if (playerScore < dealerScore)
            {
                // Player loses. Bet is already lost.
                return "You lose.";
            }
            else
            {
                // Push (tie).
                PlayerBalance += CurrentBet; // Return original bet
                return "Push. It's a tie.";
            }
        }
    }
}