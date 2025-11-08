using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using dotnetProject.Models;
using dotnetProject.Services;

namespace dotnetProject.Hubs
{
    public class PokerHub : Hub
    {
        private static readonly ConcurrentDictionary<string, PokerLobby> PokerLobbies = new();
        private readonly IWalletService _walletService;
        private static readonly string[] Ranks = { "2", "3", "4", "5", "6", "7", "8", "9", "T", "J", "Q", "K", "A" };
        private static readonly string[] Suits = { "H", "D", "C", "S" };
        public PokerHub(IWalletService walletService)
        {
            _walletService = walletService;
        }

        public async Task JoinPokerLobby(string lobbyCode, string playerName)
        {
            var httpContext = Context.GetHttpContext();
            var playerId = httpContext?.Items["PlayerId"]?.ToString() ?? Guid.NewGuid().ToString("N");

            var lobby = PokerLobbies.GetOrAdd(lobbyCode, _ => new PokerLobby
            {
                LobbyCode = lobbyCode,
                CreatorConnectionId = Context.ConnectionId
            });

            var existingPlayer = lobby.Players.FirstOrDefault(p =>
                p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));

            if (existingPlayer != null)
            {
                // Reconnecting player
                existingPlayer.ConnectionId = Context.ConnectionId;
                existingPlayer.Name = playerName;
                existingPlayer.IsActive = true;

                // Sync balance from wallet
                var balance = await _walletService.GetBalanceAsync(playerId);
                existingPlayer.Balance = balance;
            }
            else
            {
                // New player
                if (lobby.Players.Count >= 7)
                {
                    await Clients.Caller.SendAsync("Error", "Table is full (max 7 players)");
                    return;
                }

                // Get balance from wallet
                var balance = await _walletService.GetBalanceAsync(playerId);

                var newPlayer = new PokerPlayer
                {
                    ConnectionId = Context.ConnectionId,
                    Name = playerName,
                    PlayerId = playerId,
                    Balance = balance,
                    SeatPosition = lobby.Players.Count
                };
                lobby.Players.Add(newPlayer);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyCode);
            await Clients.Caller.SendAsync("SetConnectionId", Context.ConnectionId);

            var playerNames = lobby.Players.Select(p => p.Name).ToList();
            await Clients.Group(lobbyCode).SendAsync("UpdatePlayerList", playerNames, lobby.CreatorConnectionId);

            if (lobby.GameState.Phase != GamePhase.Waiting)
            {
                await Clients.Caller.SendAsync("GameStarted", lobbyCode);
            }

            await BroadcastGameState(lobbyCode);
        }

        public async Task StartPokerGame(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;

            if (Context.ConnectionId != lobby.CreatorConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Only the host can start the game");
                return;
            }

            if (lobby.Players.Count < 2)
            {
                await Clients.Caller.SendAsync("Error", "Need at least 2 players to start");
                return;
            }

            lobby.IsGameStarted = true;
            await Clients.Group(lobbyCode).SendAsync("GameStarted", lobbyCode);
            await StartNewRound(lobbyCode);
        }

        private async Task StartNewRound(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;

            var game = lobby.GameState;

            // Remove players with no balance
            lobby.Players.RemoveAll(p => p.Balance <= 0);

            if (lobby.Players.Count < 2)
            {
                await Clients.Group(lobbyCode).SendAsync("GameEnded", "Not enough players to continue");
                return;
            }

            // Reset round state
            game.Deck = CreateShuffledDeck();
            game.CommunityCards.Clear();
            game.Pot = 0;
            game.CurrentBet = 0;
            game.Phase = GamePhase.PreFlop;
            game.GameLog.Clear();

            foreach (var player in lobby.Players)
            {
                player.Hand.Clear();
                player.CurrentBet = 0;
                player.HasFolded = false;
                player.IsAllIn = false;
            }

            // Move dealer button
            game.DealerPosition = (game.DealerPosition + 1) % lobby.Players.Count;

            // Post blinds
            await PostBlinds(lobbyCode);

            // Deal hole cards
            foreach (var player in lobby.Players)
            {
                player.Hand.Add(game.Deck[0]);
                game.Deck.RemoveAt(0);
                player.Hand.Add(game.Deck[0]);
                game.Deck.RemoveAt(0);
            }

            // Set first player to act (after big blind)
            game.CurrentPlayerIndex = (game.DealerPosition + 3) % lobby.Players.Count;
            game.LastRaiserIndex = (game.DealerPosition + 2) % lobby.Players.Count; // Big blind is last raiser

            await BroadcastGameState(lobbyCode);
            await NotifyCurrentPlayer(lobbyCode);
        }

        private async Task PostBlinds(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            var sbIndex = (game.DealerPosition + 1) % lobby.Players.Count;
            var bbIndex = (game.DealerPosition + 2) % lobby.Players.Count;

            var sbPlayer = lobby.Players[sbIndex];
            var bbPlayer = lobby.Players[bbIndex];

            var sbAmount = Math.Min(game.SmallBlind, sbPlayer.Balance);
            var bbAmount = Math.Min(game.BigBlind, bbPlayer.Balance);

            sbPlayer.Balance -= sbAmount;
            sbPlayer.CurrentBet = sbAmount;
            game.Pot += sbAmount;

            bbPlayer.Balance -= bbAmount;
            bbPlayer.CurrentBet = bbAmount;
            game.Pot += bbAmount;
            game.CurrentBet = bbAmount;

            if (sbPlayer.Balance == 0) sbPlayer.IsAllIn = true;
            if (bbPlayer.Balance == 0) bbPlayer.IsAllIn = true;

            game.GameLog.Add($"{sbPlayer.Name} posts small blind ₹{sbAmount}");
            game.GameLog.Add($"{bbPlayer.Name} posts big blind ₹{bbAmount}");
        }

        public async Task PlayerAction(string lobbyCode, PlayerAction action)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            var player = lobby.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null || lobby.Players[game.CurrentPlayerIndex] != player) return;

            switch (action.Action.ToLower())
            {
                case "fold":
                    player.HasFolded = true;
                    game.GameLog.Add($"{player.Name} folds");
                    break;

                case "check":
                    if (player.CurrentBet < game.CurrentBet)
                    {
                        await Clients.Caller.SendAsync("Error", "Cannot check, must call or raise");
                        return;
                    }
                    game.GameLog.Add($"{player.Name} checks");
                    break;

                case "call":
                    var callAmount = Math.Min(game.CurrentBet - player.CurrentBet, player.Balance);
                    player.Balance -= callAmount;
                    player.CurrentBet += callAmount;
                    game.Pot += callAmount;
                    if (player.Balance == 0) player.IsAllIn = true;
                    game.GameLog.Add($"{player.Name} calls ₹{callAmount}");
                    break;

                case "raise":
                    var raiseAmount = Math.Min(action.Amount, player.Balance + player.CurrentBet);
                    if (raiseAmount <= game.CurrentBet)
                    {
                        await Clients.Caller.SendAsync("Error", "Raise must be higher than current bet");
                        return;
                    }
                    var raiseToAdd = raiseAmount - player.CurrentBet;
                    player.Balance -= raiseToAdd;
                    player.CurrentBet = raiseAmount;
                    game.Pot += raiseToAdd;
                    game.CurrentBet = raiseAmount;
                    game.LastRaiserIndex = game.CurrentPlayerIndex;
                    if (player.Balance == 0) player.IsAllIn = true;
                    game.GameLog.Add($"{player.Name} raises to ₹{raiseAmount}");
                    break;

                case "allin":
                    var allInAmount = player.Balance;
                    player.CurrentBet += allInAmount;
                    game.Pot += allInAmount;
                    player.Balance = 0;
                    player.IsAllIn = true;
                    if (player.CurrentBet > game.CurrentBet)
                    {
                        game.CurrentBet = player.CurrentBet;
                        game.LastRaiserIndex = game.CurrentPlayerIndex;
                    }
                    game.GameLog.Add($"{player.Name} goes all-in with ₹{allInAmount}");
                    break;
            }

            await AdvanceGame(lobbyCode);
        }

        private async Task AdvanceGame(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            // Check if only one player remains
            var activePlayers = lobby.Players.Where(p => !p.HasFolded).ToList();
            if (activePlayers.Count == 1)
            {
                await EndRound(lobbyCode, activePlayers[0]);
                return;
            }

            // Move to next active player
            int startIndex = game.CurrentPlayerIndex;
            do
            {
                game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % lobby.Players.Count;
            } while ((lobby.Players[game.CurrentPlayerIndex].HasFolded ||
                     lobby.Players[game.CurrentPlayerIndex].IsAllIn) &&
                     game.CurrentPlayerIndex != startIndex);

            // Check if betting round is complete
            var playersToAct = lobby.Players.Where(p => !p.HasFolded && !p.IsAllIn).ToList();

            // Betting round complete when:
            // 1. All active players have equal bets OR
            // 2. We've returned to the last raiser OR
            // 3. Everyone has acted at least once
            bool allBetsEqual = playersToAct.All(p => p.CurrentBet == game.CurrentBet);
            bool backToLastRaiser = game.CurrentPlayerIndex == game.LastRaiserIndex && playersToAct.Count > 0;

            if ((allBetsEqual || backToLastRaiser) && playersToAct.Count <= 1)
            {
                await AdvancePhase(lobbyCode);
            }
            else if (allBetsEqual && playersToAct.Count > 0)
            {
                // Check if everyone has acted
                bool everyoneActed = true;
                foreach (var p in playersToAct)
                {
                    if (p.CurrentBet < game.CurrentBet) everyoneActed = false;
                }

                if (everyoneActed)
                {
                    await AdvancePhase(lobbyCode);
                }
                else
                {
                    await BroadcastGameState(lobbyCode);
                    await NotifyCurrentPlayer(lobbyCode);
                }
            }
            else
            {
                await BroadcastGameState(lobbyCode);
                await NotifyCurrentPlayer(lobbyCode);
            }
        }

        private async Task AdvancePhase(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            // Reset bets for next round
            foreach (var player in lobby.Players)
            {
                player.CurrentBet = 0;
            }
            game.CurrentBet = 0;
            game.LastRaiserIndex = -1;

            // Check if everyone is all-in except one
            var activePlayers = lobby.Players.Where(p => !p.HasFolded).ToList();
            var playersNotAllIn = activePlayers.Where(p => !p.IsAllIn).Count();

            if (playersNotAllIn <= 1)
            {
                // Skip to showdown
                await RevealAllCards(lobbyCode);
                return;
            }

            switch (game.Phase)
            {
                case GamePhase.PreFlop:
                    game.Deck.RemoveAt(0); // Burn card
                    for (int i = 0; i < 3; i++)
                    {
                        game.CommunityCards.Add(game.Deck[0]);
                        game.Deck.RemoveAt(0);
                    }
                    game.Phase = GamePhase.Flop;
                    game.GameLog.Add("Flop dealt");
                    break;

                case GamePhase.Flop:
                    game.Deck.RemoveAt(0); // Burn card
                    game.CommunityCards.Add(game.Deck[0]);
                    game.Deck.RemoveAt(0);
                    game.Phase = GamePhase.Turn;
                    game.GameLog.Add("Turn dealt");
                    break;

                case GamePhase.Turn:
                    game.Deck.RemoveAt(0); // Burn card
                    game.CommunityCards.Add(game.Deck[0]);
                    game.Deck.RemoveAt(0);
                    game.Phase = GamePhase.River;
                    game.GameLog.Add("River dealt");
                    break;

                case GamePhase.River:
                    await Showdown(lobbyCode);
                    return;
            }

            // First to act is after dealer (or first active player)
            game.CurrentPlayerIndex = (game.DealerPosition + 1) % lobby.Players.Count;
            while (lobby.Players[game.CurrentPlayerIndex].HasFolded ||
                   lobby.Players[game.CurrentPlayerIndex].IsAllIn)
            {
                game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % lobby.Players.Count;
            }

            await BroadcastGameState(lobbyCode);
            await NotifyCurrentPlayer(lobbyCode);
        }

        private async Task RevealAllCards(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            // Deal remaining community cards
            while (game.CommunityCards.Count < 5 && game.Deck.Count > 0)
            {
                game.Deck.RemoveAt(0); // Burn
                if (game.Deck.Count > 0)
                {
                    game.CommunityCards.Add(game.Deck[0]);
                    game.Deck.RemoveAt(0);
                }
            }

            await Showdown(lobbyCode);
        }

        private async Task Showdown(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            game.Phase = GamePhase.Showdown;
            var activePlayers = lobby.Players.Where(p => !p.HasFolded).ToList();
            var results = new List<HandResult>();

            foreach (var player in activePlayers)
            {
                var result = EvaluateHand(player.Hand, game.CommunityCards);
                result.PlayerName = player.Name;
                results.Add(result);
            }

            results = results.OrderByDescending(r => (int)r.Rank)
                           .ThenByDescending(r => r.Values[0])
                           .ThenByDescending(r => r.Values.Count > 1 ? r.Values[1] : 0)
                           .ThenByDescending(r => r.Values.Count > 2 ? r.Values[2] : 0)
                           .ToList();

            var winner = results[0];
            var winningPlayer = activePlayers.First(p => p.Name == winner.PlayerName);

            // Update wallet with winnings
            await _walletService.AddBalanceAsync(
                winningPlayer.PlayerId,
                game.Pot,
                "Poker",
                $"Won pot: ₹{game.Pot}"
            );

            // Sync balance from wallet
            var newBalance = await _walletService.GetBalanceAsync(winningPlayer.PlayerId);
            winningPlayer.Balance = newBalance;

            game.GameLog.Add($"{winner.PlayerName} wins ₹{game.Pot} with {winner.Description}!");

            await Clients.Group(lobbyCode).SendAsync("ShowdownResult", new
            {
                winner = winner.PlayerName,
                amount = game.Pot,
                hand = winner.Description,
                results = results.Select(r => new
                {
                    playerName = r.PlayerName,
                    hand = r.Description,
                    cards = r.BestHand.Select(c => c.ToString()).ToList()
                }).ToList()
            });

            await Task.Delay(5000);
            await StartNewRound(lobbyCode);
        }

        private async Task EndRound(string lobbyCode, PokerPlayer winner)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            // Update wallet with winnings
            await _walletService.AddBalanceAsync(
                winner.PlayerId,
                game.Pot,
                "Poker",
                "Won pot (all others folded)"
            );

            // Sync balance from wallet
            var newBalance = await _walletService.GetBalanceAsync(winner.PlayerId);
            winner.Balance = newBalance;

            game.GameLog.Add($"{winner.Name} wins ₹{game.Pot} (all others folded)");

            await Clients.Group(lobbyCode).SendAsync("RoundWinner", new
            {
                winner = winner.Name,
                amount = game.Pot
            });

            await Task.Delay(3000);
            await StartNewRound(lobbyCode);
        }

        private async Task BroadcastGameState(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;
            var game = lobby.GameState;

            foreach (var player in lobby.Players)
            {
                await Clients.Client(player.ConnectionId).SendAsync("GameState", new
                {
                    players = lobby.Players.Select(p => new
                    {
                        name = p.Name,
                        balance = p.Balance,
                        currentBet = p.CurrentBet,
                        hasFolded = p.HasFolded,
                        isAllIn = p.IsAllIn,
                        seatPosition = p.SeatPosition,
                        isDealer = p.SeatPosition == game.DealerPosition,
                        cards = p.ConnectionId == player.ConnectionId ?
                                p.Hand.Select(c => c.ToString()).ToList() :
                                new List<string> { "??", "??" }
                    }).ToList(),
                    communityCards = game.CommunityCards.Select(c => c.ToString()).ToList(),
                    pot = game.Pot,
                    currentBet = game.CurrentBet,
                    phase = game.Phase.ToString(),
                    currentPlayerIndex = game.CurrentPlayerIndex,
                    gameLog = game.GameLog.TakeLast(10).ToList(),
                    isCreator = player.ConnectionId == lobby.CreatorConnectionId
                });
            }
        }

        private async Task NotifyCurrentPlayer(string lobbyCode)
        {
            if (!PokerLobbies.TryGetValue(lobbyCode, out var lobby)) return;

            var currentPlayer = lobby.Players[lobby.GameState.CurrentPlayerIndex];

            // --- START FIX ---
            // Check if the player is disconnected.
            if (!currentPlayer.IsActive)
            {
                // Player is disconnected, auto-fold them
                currentPlayer.HasFolded = true;
                lobby.GameState.GameLog.Add($"{currentPlayer.Name} folds (disconnected)");

                // Immediately advance the game to the next player
                // This will also broadcast the new game state
                await AdvanceGame(lobbyCode);
                return; // Stop and don't send "YourTurn"
            }
            // --- END FIX ---

            await Clients.Client(currentPlayer.ConnectionId).SendAsync("YourTurn");
        }

        private List<Card> CreateShuffledDeck()
        {
            var deck = new List<Card>();
            foreach (var suit in Suits)
            {
                foreach (var rank in Ranks)
                {
                    deck.Add(new Card { Rank = rank, Suit = suit });
                }
            }
            return deck.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        private HandResult EvaluateHand(List<Card> holeCards, List<Card> communityCards)
        {
            var allCards = holeCards.Concat(communityCards).ToList();
            var bestHand = GetBestFiveCardHand(allCards);

            if (IsRoyalFlush(bestHand, out var values))
                return new HandResult { Rank = HandRank.RoyalFlush, Description = "Royal Flush", BestHand = bestHand, Values = values };
            if (IsStraightFlush(bestHand, out values))
                return new HandResult { Rank = HandRank.StraightFlush, Description = "Straight Flush", BestHand = bestHand, Values = values };
            if (IsFourOfKind(bestHand, out values))
                return new HandResult { Rank = HandRank.FourOfKind, Description = "Four of a Kind", BestHand = bestHand, Values = values };
            if (IsFullHouse(bestHand, out values))
                return new HandResult { Rank = HandRank.FullHouse, Description = "Full House", BestHand = bestHand, Values = values };
            if (IsFlush(bestHand, out values))
                return new HandResult { Rank = HandRank.Flush, Description = "Flush", BestHand = bestHand, Values = values };
            if (IsStraight(bestHand, out values))
                return new HandResult { Rank = HandRank.Straight, Description = "Straight", BestHand = bestHand, Values = values };
            if (IsThreeOfKind(bestHand, out values))
                return new HandResult { Rank = HandRank.ThreeOfKind, Description = "Three of a Kind", BestHand = bestHand, Values = values };
            if (IsTwoPair(bestHand, out values))
                return new HandResult { Rank = HandRank.TwoPair, Description = "Two Pair", BestHand = bestHand, Values = values };
            if (IsOnePair(bestHand, out values))
                return new HandResult { Rank = HandRank.OnePair, Description = "One Pair", BestHand = bestHand, Values = values };

            return new HandResult
            {
                Rank = HandRank.HighCard,
                Description = "High Card",
                BestHand = bestHand,
                Values = bestHand.OrderByDescending(c => c.GetValue()).Select(c => c.GetValue()).ToList()
            };
        }

        private List<Card> GetBestFiveCardHand(List<Card> cards)
        {
            return cards.OrderByDescending(c => c.GetValue()).Take(5).ToList();
        }

        private bool IsRoyalFlush(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            if (!IsFlush(cards, out _)) return false;
            var sortedValues = cards.Select(c => c.GetValue()).OrderBy(v => v).ToList();
            if (sortedValues.SequenceEqual(new[] { 10, 11, 12, 13, 14 }))
            {
                values = new List<int> { 14 };
                return true;
            }
            return false;
        }

        private bool IsStraightFlush(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            if (IsFlush(cards, out _) && IsStraight(cards, out values))
                return true;
            return false;
        }

        private bool IsFourOfKind(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            var groups = cards.GroupBy(c => c.GetValue()).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key);
            if (groups.First().Count() == 4)
            {
                values = new List<int> { groups.First().Key, groups.Last().Key };
                return true;
            }
            return false;
        }

        private bool IsFullHouse(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            var groups = cards.GroupBy(c => c.GetValue()).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToList();
            if (groups.Count == 2 && groups[0].Count() == 3 && groups[1].Count() == 2)
            {
                values = new List<int> { groups[0].Key, groups[1].Key };
                return true;
            }
            return false;
        }

        private bool IsFlush(List<Card> cards, out List<int> values)
        {
            values = cards.OrderByDescending(c => c.GetValue()).Select(c => c.GetValue()).ToList();
            return cards.GroupBy(c => c.Suit).Any(g => g.Count() >= 5);
        }

        private bool IsStraight(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            var sortedValues = cards.Select(c => c.GetValue()).Distinct().OrderBy(v => v).ToList();
            for (int i = 0; i <= sortedValues.Count - 5; i++)
            {
                if (sortedValues[i + 4] - sortedValues[i] == 4)
                {
                    values = new List<int> { sortedValues[i + 4] };
                    return true;
                }
            }
            if (sortedValues.Contains(14) && sortedValues.Take(4).SequenceEqual(new[] { 2, 3, 4, 5 }))
            {
                values = new List<int> { 5 };
                return true;
            }
            return false;
        }

        private bool IsThreeOfKind(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            var groups = cards.GroupBy(c => c.GetValue()).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToList();
            if (groups.First().Count() == 3)
            {
                values = new List<int> { groups[0].Key };
                values.AddRange(groups.Skip(1).Select(g => g.Key));
                return true;
            }
            return false;
        }

        private bool IsTwoPair(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            var pairs = cards.GroupBy(c => c.GetValue()).Where(g => g.Count() == 2).OrderByDescending(g => g.Key).ToList();
            if (pairs.Count >= 2)
            {
                values = new List<int> { pairs[0].Key, pairs[1].Key };
                values.Add(cards.Where(c => c.GetValue() != pairs[0].Key && c.GetValue() != pairs[1].Key)
                               .OrderByDescending(c => c.GetValue()).First().GetValue());
                return true;
            }
            return false;
        }

        private bool IsOnePair(List<Card> cards, out List<int> values)
        {
            values = new List<int>();
            var groups = cards.GroupBy(c => c.GetValue()).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToList();
            if (groups.First().Count() == 2)
            {
                values = new List<int> { groups[0].Key };
                values.AddRange(groups.Skip(1).OrderByDescending(g => g.Key).Select(g => g.Key));
                return true;
            }
            return false;
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            foreach (var lobby in PokerLobbies.Values)
            {
                var player = lobby.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    player.IsActive = false;
                    await Clients.Group(lobby.LobbyCode).SendAsync("PlayerDisconnected", player.Name);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
    }

    public class PokerLobby
    {
        public string LobbyCode { get; set; }
        public List<PokerPlayer> Players { get; set; } = new();
        public PokerGameState GameState { get; set; } = new();
        public string CreatorConnectionId { get; set; }
        public bool IsGameStarted { get; set; } = false;
    }
}