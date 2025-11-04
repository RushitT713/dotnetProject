namespace dotnetProject.Models
{
    public class PokerPlayer
    {
        public string ConnectionId { get; set; }
        public string Name { get; set; }
        public decimal Balance { get; set; }
        public List<Card> Hand { get; set; } = new();
        public decimal CurrentBet { get; set; }
        public bool HasFolded { get; set; }
        public bool IsAllIn { get; set; }
        public int SeatPosition { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class Card
    {
        public string Rank { get; set; } // 2-9, T, J, Q, K, A
        public string Suit { get; set; } // H, D, C, S

        public override string ToString() => $"{Rank}{Suit}";

        public int GetValue()
        {
            return Rank switch
            {
                "A" => 14,
                "K" => 13,
                "Q" => 12,
                "J" => 11,
                "T" => 10,
                _ => int.Parse(Rank)
            };
        }
    }

    public class PokerGameState
    {
        public List<Card> CommunityCards { get; set; } = new();
        public List<Card> Deck { get; set; } = new();
        public decimal Pot { get; set; }
        public decimal CurrentBet { get; set; }
        public int DealerPosition { get; set; }
        public int CurrentPlayerIndex { get; set; }
        public int LastRaiserIndex { get; set; } = -1;
        public GamePhase Phase { get; set; } = GamePhase.Waiting;
        public decimal SmallBlind { get; set; } = 10;
        public decimal BigBlind { get; set; } = 20;
        public List<string> GameLog { get; set; } = new();
    }

    public enum GamePhase
    {
        Waiting,
        PreFlop,
        Flop,
        Turn,
        River,
        Showdown
    }

    public class HandResult
    {
        public string PlayerName { get; set; }
        public HandRank Rank { get; set; }
        public string Description { get; set; }
        public List<Card> BestHand { get; set; }
        public List<int> Values { get; set; } // For tie-breaking
    }

    public enum HandRank
    {
        HighCard = 1,
        OnePair = 2,
        TwoPair = 3,
        ThreeOfKind = 4,
        Straight = 5,
        Flush = 6,
        FullHouse = 7,
        FourOfKind = 8,
        StraightFlush = 9,
        RoyalFlush = 10
    }

    public class PlayerAction
    {
        public string Action { get; set; } // Fold, Check, Call, Raise, AllIn
        public decimal Amount { get; set; }
    }
}